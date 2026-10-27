using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Caregiver side of address proof: one utility bill (PDF/JPG/PNG) for the CURRENT address, reviewed by staff. The proof lives on the
    /// <see cref="CaregiverAddressHistory"/> record itself. Files go to the same private (authenticated) store as police clearances.
    /// </summary>
    public class AddressProofService : IAddressProofService
    {
        private readonly CareProDbContext _db;
        private readonly IPoliceClearanceFileStore _store;
        private readonly IAddressProofNotifier _notifier;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AddressProofService> _logger;
        private readonly TimeProvider _time;

        public AddressProofService(CareProDbContext db, IPoliceClearanceFileStore store, IAddressProofNotifier notifier,
            IConfiguration configuration, ILogger<AddressProofService> logger, TimeProvider? time = null)
        {
            _db = db; _store = store; _notifier = notifier; _configuration = configuration; _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        public async Task<AddressProofInfoDTO> SubmitAsync(string caregiverId, string addressId, SubmitAddressProofRequest request)
        {
            if (!ObjectId.TryParse(caregiverId, out var caregiverOid))
                throw new PoliceClearanceValidationException("Caregiver identity is invalid.");
            if (!ObjectId.TryParse(addressId, out var addressOid))
                throw new KeyNotFoundException("Address record not found.");

            var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == caregiverOid)
                ?? throw new KeyNotFoundException("Caregiver not found.");
            var record = await _db.CaregiverAddressHistories.FirstOrDefaultAsync(a => a.Id == addressOid)
                ?? throw new KeyNotFoundException("Address record not found.");
            if (!string.Equals(record.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this address record.");

            if (record.MovedOut != null)
                throw new PoliceClearanceValidationException("A bill is only needed for the address you currently live at.");
            if (record.ProofStatus == AddressProofStatuses.Approved)
                throw new PoliceClearanceConflictException("Your bill for this address is already approved — there is nothing more to submit.");
            if (record.ProofStatus == AddressProofStatuses.Pending)
                throw new PoliceClearanceConflictException("Your bill is already waiting for review. We'll notify you once it has been checked.");

            var settings = AddressProofSettings.Load(_configuration);
            var now = NowUtc;

            var name = (request.NameOnBill ?? string.Empty).Trim();
            if (name.Length < 2 || name.Length > 150)
                throw new PoliceClearanceValidationException("Please enter the account holder's name exactly as it appears on the bill.");
            if (!DateTime.TryParse(request.BillDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var billDate))
                throw new PoliceClearanceValidationException("Please enter the date on the bill.");
            var dateError = AddressProofSettings.CheckBillDate(billDate, now, settings.MaxBillAgeMonths);
            if (dateError != null) throw new PoliceClearanceValidationException(dateError);

            if (request.File == null)
                throw new PoliceClearanceValidationException("Please attach your power-holding (NEPA) bill as a PDF, JPG or PNG.");
            var validation = PoliceClearanceFileValidator.Validate(
                new List<IncomingClearanceFile> { new(request.File.FileName, request.File.ContentType, request.File.Bytes) },
                settings.ToFileSettings());
            if (!validation.IsValid) throw new PoliceClearanceValidationException(validation.Error!);

            var f = validation.Files[0];
            StoredClearanceFile s;
            try { s = await _store.UploadAsync(f.Bytes, f.Extension); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Address proof upload to storage failed for caregiver {CaregiverId}", caregiverId);
                throw new InvalidOperationException("We couldn't store your file just now. Please try again in a moment.");
            }

            var previous = record.ProofFiles?.ToList() ?? new List<PoliceClearanceFile>();
            record.ProofStatus = AddressProofStatuses.Pending;
            record.ProofFiles = new List<PoliceClearanceFile>
            {
                new() { PublicId = s.PublicId, ResourceType = s.ResourceType, OriginalFileName = f.SafeFileName,
                        ContentType = f.ContentType, SizeBytes = f.Bytes.LongLength }
            };
            record.ProofNameOnBill = name;
            record.ProofBillDate = billDate.Date;
            record.ProofSubmittedAt = now;
            record.ProofReviewedAt = null;
            record.ProofReviewedByAdminId = null;
            record.ProofReviewedByAdminEmail = null;
            record.ProofReviewNotes = null;
            record.ProofRejectionReason = null;
            record.ProofChecklist = null;
            record.UpdatedAt = now;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                await DeleteFilesAsync(new[] { record.ProofFiles[0] });
                throw new PoliceClearanceConflictException("Your address was just updated. Please refresh the page and try again.");
            }

            // The superseded (rejected) file is no longer referenced by anything.
            await DeleteFilesAsync(previous);
            await _notifier.NotifySubmittedAsync(record, $"{caregiver.FirstName} {caregiver.LastName}".Trim());
            return AddressProofMapper.Info(record, settings);
        }

        private async Task DeleteFilesAsync(IEnumerable<PoliceClearanceFile> files)
        {
            foreach (var f in files)
            {
                if (string.IsNullOrWhiteSpace(f.PublicId)) continue;
                var outcome = await _store.DeleteAsync(f.PublicId, f.ResourceType);
                if (outcome == ClearanceFileDeleteOutcome.Failed)
                    _logger.LogError("Orphaned address proof file (public id {PublicId}) — needs manual cleanup", f.PublicId);
            }
        }

        public async Task<ClearanceFileContent> GetOwnFileAsync(string caregiverId, string addressId)
        {
            if (!ObjectId.TryParse(addressId, out var oid)) throw new KeyNotFoundException("Address record not found.");
            var record = await _db.CaregiverAddressHistories.FirstOrDefaultAsync(a => a.Id == oid)
                ?? throw new KeyNotFoundException("Address record not found.");
            if (!string.Equals(record.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You are not authorised to access this address record.");
            return await ReadFileAsync(_store, record);
        }

        internal static async Task<ClearanceFileContent> ReadFileAsync(IPoliceClearanceFileStore store, CaregiverAddressHistory record)
        {
            var file = record.ProofFiles?.FirstOrDefault();
            if (file == null || string.IsNullOrWhiteSpace(file.PublicId)) throw new KeyNotFoundException("File not found.");
            var bytes = await store.DownloadAsync(file.PublicId, file.ResourceType)
                ?? throw new KeyNotFoundException("File not found.");
            return new ClearanceFileContent
            {
                Bytes = bytes, ContentType = file.ContentType ?? "application/octet-stream", FileName = file.OriginalFileName ?? "bill"
            };
        }
    }

    internal static class AddressProofMapper
    {
        public static string StatusName(CaregiverAddressHistory a) => a.ProofStatus switch
        {
            AddressProofStatuses.Pending => "pending",
            AddressProofStatuses.Approved => "approved",
            AddressProofStatuses.Rejected => "rejected",
            _ => "none"
        };

        public static AddressProofInfoDTO Info(CaregiverAddressHistory a, AddressProofSettings settings)
        {
            var status = StatusName(a);
            var required = a.MovedOut == null;
            return new AddressProofInfoDTO
            {
                Required = required,
                Status = status,
                BillDate = a.ProofBillDate,
                NameOnBill = a.ProofNameOnBill,
                SubmittedAt = a.ProofSubmittedAt,
                ReviewedAt = a.ProofReviewedAt,
                RejectionReason = status == "rejected" ? a.ProofRejectionReason : null,
                CanSubmit = required && (status == "none" || status == "rejected"),
                MaxBillAgeMonths = settings.MaxBillAgeMonths,
                MaxFileSizeMB = settings.MaxFileSizeMB,
                Enforced = settings.IsEnforced(DateTime.UtcNow),
                EnforcementDate = settings.EnforcementDateUtc
            };
        }
    }
}
