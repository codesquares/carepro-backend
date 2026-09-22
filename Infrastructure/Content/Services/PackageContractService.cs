using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Phase 6 — auto-generated package contract. Reuses <see cref="IContractTemplateService"/>
    /// and <see cref="IContractPdfService"/> exactly as-is (both operate on the generic
    /// <see cref="ContractGenerationDataDTO"/>); the only new code is the adapter that
    /// builds that DTO from a Package + PackageRequest. No LLM, no negotiation subsystem.
    /// </summary>
    public class PackageContractService : IPackageContractService
    {
        private readonly CareProDbContext _db;
        private readonly IContractTemplateService _templateService;
        private readonly IContractPdfService _pdfService;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly ILogger<PackageContractService> _logger;

        /// <summary>Nominal contract period when the package itself defines no fixed duration.</summary>
        private const int DefaultContractWeeks = 4;

        public PackageContractService(
            CareProDbContext db,
            IContractTemplateService templateService,
            IContractPdfService pdfService,
            IMediator mediator,
            IEmailService emailService,
            ILogger<PackageContractService> logger)
        {
            _db = db;
            _templateService = templateService;
            _pdfService = pdfService;
            _mediator = mediator;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<PackageContractDTO> GenerateForConfirmedRequestAsync(string packageRequestId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var prOid))
                throw new ArgumentException("Invalid package request id.");

            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == prOid)
                ?? throw new KeyNotFoundException($"Package request '{packageRequestId}' not found.");

            if (!string.Equals(request.Status, PackageRequestStatuses.Confirmed, StringComparison.Ordinal)
                || string.IsNullOrEmpty(request.ConfirmedCaregiverId))
            {
                throw new InvalidOperationException(
                    "A contract can only be generated once the package request is confirmed with a caregiver.");
            }

            // Idempotent — one contract per package request.
            var existing = await _db.Contracts.FirstOrDefaultAsync(c => c.PackageRequestId == packageRequestId);
            if (existing != null)
            {
                _logger.LogInformation("PackageContract already exists for request {RequestId} (contract {ContractId})",
                    packageRequestId, existing.Id);
                return Map(existing, request, newlyGenerated: false);
            }

            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(request.PackageId))
                ?? throw new KeyNotFoundException($"Package '{request.PackageId}' not found.");

            Client? client = ObjectId.TryParse(request.ClientId, out var clientOid)
                ? await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientOid) : null;
            Caregiver? caregiver = ObjectId.TryParse(request.ConfirmedCaregiverId, out var cgOid)
                ? await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid) : null;

            var start = (request.ConfirmedAt ?? DateTime.UtcNow).Date.AddDays(1);
            var end = start.AddDays(DefaultContractWeeks * 7);
            var contractId = ObjectId.GenerateNewId().ToString();

            var data = BuildGenerationData(contractId, request, package, client, caregiver, start, end);

            // Reused, unmodified — renders CarePro's standard terms + the package details.
            var termsHtml = _templateService.RenderContract(data);

            var contract = new Contract
            {
                Id = contractId,
                OrderId = string.Empty,   // payment is decoupled (Phase 5); no order needed
                GigId = string.Empty,     // package-based, not gig-based
                ClientId = request.ClientId,
                CaregiverId = request.ConfirmedCaregiverId,
                PackageRequestId = packageRequestId,
                PackageId = request.PackageId,
                SelectedPackage = data.Package,
                Tasks = new List<ClientTask>(),
                Schedule = new List<ScheduledVisit>(),
                ServiceAddress = request.Location,
                GeneratedTerms = termsHtml,
                TotalAmount = package.BasePrice,
                Status = ContractStatus.Generated,   // exists & active; no approval/negotiation step
                InitiatedByRole = "System",
                SubmittedAt = request.ConfirmedAt ?? DateTime.UtcNow,
                ContractStartDate = start,
                ContractEndDate = end,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await _db.Contracts.AddAsync(contract);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Auto-generated package contract {ContractId} for confirmed request {RequestId} " +
                "(package {Category}/{Tier}) — no negotiation",
                contract.Id, packageRequestId, package.Category, package.TierLabel);

            // Inform both parties (informational — no action required, so no reminder loop).
            foreach (var recipient in new[] { request.ClientId, request.ConfirmedCaregiverId })
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: recipient,
                    SenderId: "system",
                    Type: NotificationTypes.PackageContractGenerated,
                    Content: $"Your care agreement for the {package.Category} ({package.TierLabel}) package is ready to view.",
                    Title: "Care agreement ready",
                    RelatedEntityId: contract.Id));
            }

            // Deliver the agreement PDF by email to both parties — same as the negotiated-
            // contract flow (SendContractPdfEmailAsync, policy #22, Always-Send). Best-effort:
            // a mail failure must not undo a generated contract.
            await EmailContractPdfAsync(contract.Id, data, client, caregiver,
                $"{package.Category} ({package.TierLabel}) Package");

            return Map(contract, request, newlyGenerated: true);
        }

        public async Task<PackageContractDTO?> GetByPackageRequestAsync(string packageRequestId)
        {
            var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.PackageRequestId == packageRequestId);
            if (contract == null) return null;

            PackageRequest? request = ObjectId.TryParse(packageRequestId, out var oid)
                ? await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == oid) : null;
            return Map(contract, request, newlyGenerated: false);
        }

        public async Task<byte[]> GeneratePdfAsync(string contractId)
        {
            var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId)
                ?? throw new KeyNotFoundException($"Contract '{contractId}' not found.");

            if (string.IsNullOrEmpty(contract.PackageRequestId) || string.IsNullOrEmpty(contract.PackageId))
                throw new InvalidOperationException("This contract is not a package contract.");

            var request = await _db.PackageRequests.FirstOrDefaultAsync(
                p => p.Id == ObjectId.Parse(contract.PackageRequestId));
            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(contract.PackageId));

            Client? client = ObjectId.TryParse(contract.ClientId, out var clientOid)
                ? await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientOid) : null;
            Caregiver? caregiver = ObjectId.TryParse(contract.CaregiverId, out var cgOid)
                ? await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid) : null;

            var data = BuildGenerationData(
                contract.Id, request, package, client, caregiver,
                contract.ContractStartDate, contract.ContractEndDate);
            data.GeneratedAt = contract.CreatedAt;

            // Reused, unmodified.
            return _pdfService.GeneratePdf(data);
        }

        /// <summary>
        /// Renders the contract PDF and emails it to the client and the caregiver.
        /// Best-effort — every failure is logged and swallowed so a mail problem never
        /// undoes a generated contract.
        /// </summary>
        private async Task EmailContractPdfAsync(
            string contractId, ContractGenerationDataDTO data,
            Client? client, Caregiver? caregiver, string agreementTitle)
        {
            byte[] pdfBytes;
            try
            {
                pdfBytes = _pdfService.GeneratePdf(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render package contract PDF for contract {ContractId} — email skipped", contractId);
                return;
            }

            var recipients = new[]
            {
                (Email: client?.Email, FirstName: client?.FirstName),
                (Email: caregiver?.Email, FirstName: caregiver?.FirstName),
            };

            foreach (var (email, firstName) in recipients)
            {
                if (string.IsNullOrWhiteSpace(email)) continue;
                try
                {
                    await _emailService.SendContractPdfEmailAsync(
                        email, firstName ?? "there", contractId, agreementTitle, pdfBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to email package contract PDF {ContractId} to {Email}", contractId, email);
                }
            }
        }

        // ─────────────────────── Adapter: Package → ContractGenerationDataDTO ───────────────────────

        private static ContractGenerationDataDTO BuildGenerationData(
            string contractId, PackageRequest? request, Package? package,
            Client? client, Caregiver? caregiver, DateTime start, DateTime end)
        {
            var category = package?.Category ?? request?.PackageCategory ?? "Care Package";
            var tier = package?.TierLabel ?? request?.PackageTierLabel ?? "Standard";

            return new ContractGenerationDataDTO
            {
                ContractId = contractId,
                OrderId = string.Empty,
                GeneratedAt = DateTime.UtcNow,

                ClientId = request?.ClientId ?? client?.Id.ToString() ?? string.Empty,
                ClientFullName = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Client",
                ClientEmail = client?.Email,
                ClientPhone = client?.PhoneNo,

                CaregiverId = request?.ConfirmedCaregiverId ?? caregiver?.Id.ToString() ?? string.Empty,
                CaregiverFullName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "Caregiver",
                CaregiverEmail = caregiver?.Email,
                CaregiverPhone = caregiver?.PhoneNo,
                CaregiverQualifications = caregiver?.CaregiverType?.ToString()
                    + (string.IsNullOrWhiteSpace(caregiver?.Specialty) ? "" : $" ({caregiver!.Specialty})"),

                // Package terms — straight from the Package record.
                GigTitle = $"{category} — {tier} Package",
                GigDescription = package?.Description,
                GigCategory = category,

                Package = new PackageSelection
                {
                    PackageType = $"{category} / {tier}",
                    VisitsPerWeek = 0,
                    PricePerVisit = 0,
                    TotalWeeklyPrice = 0,
                    DurationWeeks = (int)Math.Round((end - start).TotalDays / 7.0),
                },
                TotalAmountPaid = package?.BasePrice ?? request?.Budget ?? 0,
                TransactionReference = null,

                Schedule = new List<ScheduledVisit>(),

                ServiceAddress = request?.Location ?? client?.Address ?? client?.HomeAddress ?? "To be confirmed by client",
                City = client?.PreferredCity,
                State = client?.PreferredState,
                SpecialClientRequirements = string.IsNullOrWhiteSpace(package?.RequiredSpecialty)
                    ? null
                    : $"Requires a {package!.RequiredCaregiverType} with {package.RequiredSpecialty} specialty.",
                AccessInstructions = null,
                CaregiverNotes = null,

                Tasks = new List<ClientTask>(),

                ContractStartDate = start,
                ContractEndDate = end,
            };
        }

        private static PackageContractDTO Map(Contract c, PackageRequest? request, bool newlyGenerated) => new()
        {
            Id = c.Id,
            PackageRequestId = c.PackageRequestId ?? string.Empty,
            PackageId = c.PackageId ?? string.Empty,
            ClientId = c.ClientId,
            CaregiverId = c.CaregiverId,
            Status = c.Status.ToString(),
            TotalAmount = c.TotalAmount,
            PackageCategory = request?.PackageCategory ?? string.Empty,
            PackageTierLabel = request?.PackageTierLabel ?? string.Empty,
            GeneratedTermsHtml = c.GeneratedTerms,
            ContractStartDate = c.ContractStartDate,
            ContractEndDate = c.ContractEndDate,
            CreatedAt = c.CreatedAt,
            NewlyGenerated = newlyGenerated,
        };
    }
}
