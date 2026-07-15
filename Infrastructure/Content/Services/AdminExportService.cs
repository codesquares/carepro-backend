using Application.DTOs;
using Application.Interfaces.Content;
using ClosedXML.Excel;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    public class AdminExportService : IAdminExportService
    {
        private readonly CareProDbContext _context;
        private readonly ILogger<AdminExportService> _logger;

        public AdminExportService(CareProDbContext context, ILogger<AdminExportService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ── Caregivers ─────────────────────────────────────────────────────────

        public async Task<byte[]> ExportCaregiversAsync(ExportQuery query)
        {
            var dbQuery = _context.CareGivers
                .Where(c => !c.IsDeleted);

            if (query.StartDate.HasValue)
                dbQuery = dbQuery.Where(c => c.CreatedAt >= query.StartDate.Value);

            if (query.EndDate.HasValue)
                dbQuery = dbQuery.Where(c => c.CreatedAt <= query.EndDate.Value);

            var caregivers = await dbQuery.OrderBy(c => c.CreatedAt).ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Caregivers");

            // Header row
            var headers = new[]
            {
                "ID", "First Name", "Middle Name", "Last Name", "Email", "Phone",
                "Service City", "Service State", "Home Address", "Auth Provider",
                "Is Available", "Identity Verified", "KYC Status", "Profile Image",
                "About Me", "Status", "Registered At"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            // Data rows
            for (int row = 0; row < caregivers.Count; row++)
            {
                var c = caregivers[row];
                int r = row + 2;
                ws.Cell(r, 1).Value = c.Id.ToString();
                ws.Cell(r, 2).Value = c.FirstName ?? "";
                ws.Cell(r, 3).Value = c.MiddleName ?? "";
                ws.Cell(r, 4).Value = c.LastName ?? "";
                ws.Cell(r, 5).Value = c.Email ?? "";
                ws.Cell(r, 6).Value = c.PhoneNo ?? "";
                ws.Cell(r, 7).Value = c.ServiceCity ?? "";
                ws.Cell(r, 8).Value = c.ServiceState ?? "";
                ws.Cell(r, 9).Value = c.HomeAddress ?? "";
                ws.Cell(r, 10).Value = c.AuthProvider ?? "";
                ws.Cell(r, 11).Value = c.IsAvailable ? "Yes" : "No";
                ws.Cell(r, 12).Value = (c.IsIdentityVerified == true) ? "Yes" : "No";
                ws.Cell(r, 13).Value = c.IdentityVerificationStatus ?? "";
                ws.Cell(r, 14).Value = string.IsNullOrEmpty(c.ProfileImage) ? "No" : "Yes";
                ws.Cell(r, 15).Value = string.IsNullOrEmpty(c.AboutMe) ? "No" : "Yes";
                ws.Cell(r, 16).Value = c.Status ? "Active" : "Inactive";
                ws.Cell(r, 17).Value = c.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ── Clients ────────────────────────────────────────────────────────────

        public async Task<byte[]> ExportClientsAsync(ExportQuery query)
        {
            var dbQuery = _context.Clients
                .Where(c => !c.IsDeleted);

            if (query.StartDate.HasValue)
                dbQuery = dbQuery.Where(c => c.CreatedAt >= query.StartDate.Value);

            if (query.EndDate.HasValue)
                dbQuery = dbQuery.Where(c => c.CreatedAt <= query.EndDate.Value);

            var clients = await dbQuery.OrderBy(c => c.CreatedAt).ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Clients");

            var headers = new[]
            {
                "ID", "First Name", "Middle Name", "Last Name", "Email", "Phone",
                "Home Address", "Auth Provider", "Profile Picture", "Status", "Registered At"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            for (int row = 0; row < clients.Count; row++)
            {
                var c = clients[row];
                int r = row + 2;
                ws.Cell(r, 1).Value = c.Id.ToString();
                ws.Cell(r, 2).Value = c.FirstName ?? "";
                ws.Cell(r, 3).Value = c.MiddleName ?? "";
                ws.Cell(r, 4).Value = c.LastName ?? "";
                ws.Cell(r, 5).Value = c.Email ?? "";
                ws.Cell(r, 6).Value = c.PhoneNo ?? "";
                ws.Cell(r, 7).Value = c.HomeAddress ?? c.Address ?? "";
                ws.Cell(r, 8).Value = c.AuthProvider ?? "";
                ws.Cell(r, 9).Value = string.IsNullOrEmpty(c.ProfileImage) ? "No" : "Yes";
                ws.Cell(r, 10).Value = c.Status ? "Active" : "Inactive";
                ws.Cell(r, 11).Value = c.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ── Caregiver Journey Snapshots ────────────────────────────────────────

        public async Task<byte[]> ExportCaregiverSnapshotsAsync(CaregiverSnapshotQuery query)
        {
            var dbQuery = _context.CaregiverJourneySnapshots.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.JourneyStage))
                dbQuery = dbQuery.Where(s => s.JourneyStage == query.JourneyStage);

            if (query.IsIdentityVerified.HasValue)
                dbQuery = dbQuery.Where(s => s.IsIdentityVerified == query.IsIdentityVerified.Value);

            if (query.HasProfilePicture.HasValue)
                dbQuery = dbQuery.Where(s => s.HasProfilePicture == query.HasProfilePicture.Value);

            if (query.HasPassedAssessment.HasValue)
                dbQuery = dbQuery.Where(s => s.HasPassedAnyAssessment == query.HasPassedAssessment.Value);

            if (query.HasPublishedGig.HasValue)
            {
                if (query.HasPublishedGig.Value)
                    dbQuery = dbQuery.Where(s => s.GigsPublishedCount > 0);
                else
                    dbQuery = dbQuery.Where(s => s.GigsPublishedCount == 0);
            }

            if (query.HasCertificate.HasValue)
            {
                if (query.HasCertificate.Value)
                    dbQuery = dbQuery.Where(s => s.CertificatesUploadedCount > 0);
                else
                    dbQuery = dbQuery.Where(s => s.CertificatesUploadedCount == 0);
            }

            if (query.RegisteredFrom.HasValue)
                dbQuery = dbQuery.Where(s => s.CaregiverCreatedAt >= query.RegisteredFrom.Value);

            if (query.RegisteredTo.HasValue)
                dbQuery = dbQuery.Where(s => s.CaregiverCreatedAt <= query.RegisteredTo.Value);

            var snapshots = await dbQuery.OrderBy(s => s.CaregiverCreatedAt).ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Caregiver Journey");

            var headers = new[]
            {
                "Caregiver ID", "First Name", "Last Name", "Email", "Phone",
                "Service City", "Service State", "Auth Provider", "Registered At",
                "Journey Stage",
                "Identity Verified", "KYC Status", "Verified At",
                "Assessment Passed", "Assessment Categories", "Latest Score",
                "Certs Uploaded", "Certs Verified",
                "Has Profile Picture", "Has About Me",
                "Has Work Experience", "Has Qualifications", "Has Education",
                "Gigs Draft", "Gigs Published", "Gigs Deleted",
                "Snapshot Last Updated"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            for (int row = 0; row < snapshots.Count; row++)
            {
                var s = snapshots[row];
                int r = row + 2;
                ws.Cell(r, 1).Value = s.CaregiverId;
                ws.Cell(r, 2).Value = s.FirstName;
                ws.Cell(r, 3).Value = s.LastName;
                ws.Cell(r, 4).Value = s.Email;
                ws.Cell(r, 5).Value = s.PhoneNo ?? "";
                ws.Cell(r, 6).Value = s.ServiceCity ?? "";
                ws.Cell(r, 7).Value = s.ServiceState ?? "";
                ws.Cell(r, 8).Value = s.AuthProvider ?? "";
                ws.Cell(r, 9).Value = s.CaregiverCreatedAt.ToString("yyyy-MM-dd");
                ws.Cell(r, 10).Value = s.JourneyStage;
                ws.Cell(r, 11).Value = s.IsIdentityVerified ? "Yes" : "No";
                ws.Cell(r, 12).Value = s.IdentityVerificationStatus ?? "";
                ws.Cell(r, 13).Value = s.IdentityVerifiedAt?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(r, 14).Value = s.HasPassedAnyAssessment ? "Yes" : "No";
                ws.Cell(r, 15).Value = string.Join(", ", s.PassedAssessmentCategories);
                ws.Cell(r, 16).Value = s.LatestAssessmentScore?.ToString() ?? "";
                ws.Cell(r, 17).Value = s.CertificatesUploadedCount;
                ws.Cell(r, 18).Value = s.CertificatesVerifiedCount;
                ws.Cell(r, 19).Value = s.HasProfilePicture ? "Yes" : "No";
                ws.Cell(r, 20).Value = s.HasAboutMe ? "Yes" : "No";
                ws.Cell(r, 21).Value = s.HasWorkExperience ? "Yes" : "No";
                ws.Cell(r, 22).Value = s.HasQualifications ? "Yes" : "No";
                ws.Cell(r, 23).Value = s.HasEducation ? "Yes" : "No";
                ws.Cell(r, 24).Value = s.GigsDraftCount;
                ws.Cell(r, 25).Value = s.GigsPublishedCount;
                ws.Cell(r, 26).Value = s.GigsDeletedCount;
                ws.Cell(r, 27).Value = s.LastRebuildAt.ToString("yyyy-MM-dd HH:mm");
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public async Task<byte[]> ExportReferralRedemptionsAsync(ExportQuery query)
        {
            var dbQuery = _context.ReferralRedemptions.AsQueryable();

            if (query.StartDate.HasValue)
                dbQuery = dbQuery.Where(r => r.RedeemedAt >= query.StartDate.Value);

            if (query.EndDate.HasValue)
                dbQuery = dbQuery.Where(r => r.RedeemedAt <= query.EndDate.Value);

            var redemptions = await dbQuery
                .OrderByDescending(r => r.RedeemedAt)
                .ToListAsync();

            var referralCodeIds = redemptions.Select(r => r.ReferralCodeId).Distinct().ToList();
            var codes = await _context.ReferralCodes
                .Where(c => referralCodeIds.Contains(c.Id.ToString()))
                .ToListAsync();

            var referrerIds = codes.Select(c => c.ReferrerId).Distinct().ToList();
            var referrers = await _context.Referrers
                .Where(r => referrerIds.Contains(r.Id.ToString()))
                .ToListAsync();

            var codeMap = codes.ToDictionary(c => c.Id.ToString(), c => c);
            var referrerMap = referrers.ToDictionary(r => r.Id.ToString(), r => r);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Referral Redemptions");

            var headers = new[]
            {
                "Redemption ID", "Redeemed At", "Referral Code", "Referrer Name", "Referrer Email",
                "Referrer Phone", "Client ID", "Order ID", "Discount Amount", "Payout Amount",
                "Payout Status", "Paid At"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            for (int row = 0; row < redemptions.Count; row++)
            {
                var redemption = redemptions[row];
                codeMap.TryGetValue(redemption.ReferralCodeId, out var code);

                Referrer? referrer = null;
                if (code != null)
                    referrerMap.TryGetValue(code.ReferrerId, out referrer);

                int r = row + 2;
                ws.Cell(r, 1).Value = redemption.Id.ToString();
                ws.Cell(r, 2).Value = redemption.RedeemedAt.ToString("yyyy-MM-dd HH:mm");
                ws.Cell(r, 3).Value = code?.Code ?? string.Empty;
                ws.Cell(r, 4).Value = referrer?.FullName ?? string.Empty;
                ws.Cell(r, 5).Value = referrer?.Email ?? string.Empty;
                ws.Cell(r, 6).Value = referrer?.PhoneNo ?? string.Empty;
                ws.Cell(r, 7).Value = redemption.ClientId;
                ws.Cell(r, 8).Value = redemption.OrderId;
                ws.Cell(r, 9).Value = redemption.DiscountAmount;
                ws.Cell(r, 10).Value = redemption.PayoutAmount;
                ws.Cell(r, 11).Value = redemption.PayoutStatus;
                ws.Cell(r, 12).Value = redemption.PaidAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
