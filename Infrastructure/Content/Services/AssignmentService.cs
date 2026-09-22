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
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Phase 4 — one-sided internal assignment for <see cref="PackageRequest"/>s.
    /// See <see cref="IAssignmentService"/>.
    /// </summary>
    public class AssignmentService : IAssignmentService
    {
        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly ICaregiverReadinessService _readinessService;
        private readonly IPackageContractService _packageContractService;
        private readonly ILogger<AssignmentService> _logger;

        private static readonly string[] ActiveAssignmentStatuses =
            { AssignmentStatuses.PendingAcceptance, AssignmentStatuses.Accepted };

        public AssignmentService(
            CareProDbContext db,
            IMediator mediator,
            IEmailService emailService,
            ICaregiverReadinessService readinessService,
            IPackageContractService packageContractService,
            ILogger<AssignmentService> logger)
        {
            _db = db;
            _mediator = mediator;
            _emailService = emailService;
            _readinessService = readinessService;
            _packageContractService = packageContractService;
            _logger = logger;
        }

        // ─────────────────────────── Staff / system ───────────────────────────

        public async Task<AssignmentDTO> AssignAsync(
            string packageRequestId, string caregiverId,
            string adminId, string? adminEmail, string assignedBy, double? matchScore)
        {
            if (string.IsNullOrWhiteSpace(adminId)) throw new ArgumentException("Admin identity is required.");
            if (!ObjectId.TryParse(packageRequestId, out var prOid)) throw new ArgumentException("Invalid package request id.");
            if (!ObjectId.TryParse(caregiverId, out var cgOid)) throw new ArgumentException("Invalid caregiver id.");

            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == prOid && p.DeletedAt == null)
                ?? throw new KeyNotFoundException($"Package request '{packageRequestId}' not found.");

            if (request.Status is PackageRequestStatuses.Confirmed or PackageRequestStatuses.Cancelled)
                throw new InvalidOperationException($"This request is '{request.Status}' and cannot be assigned.");

            var existingActive = await _db.Assignments.AnyAsync(a =>
                a.PackageRequestId == packageRequestId && ActiveAssignmentStatuses.Contains(a.Status));
            if (existingActive)
                throw new InvalidOperationException(
                    "This request already has an assignment awaiting acceptance (or accepted). " +
                    "Cancel it first to reassign.");

            var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid)
                ?? throw new KeyNotFoundException($"Caregiver '{caregiverId}' not found.");

            // Hard filter: must match the package's required caregiver type / specialty.
            if (caregiver.CaregiverType != request.RequiredCaregiverType)
                throw new InvalidOperationException(
                    $"Caregiver type '{caregiver.CaregiverType?.ToString() ?? "unset"}' does not match the " +
                    $"package requirement '{request.RequiredCaregiverType}'.");
            if (!string.IsNullOrWhiteSpace(request.RequiredSpecialty)
                && !string.Equals(caregiver.Specialty?.Trim(), request.RequiredSpecialty.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"This package requires the '{request.RequiredSpecialty}' specialty.");

            // Readiness gate — same check the competitive hire flow uses.
            var readiness = await _readinessService.GetReadinessAsync(caregiverId, request.ServiceCategory);
            if (!readiness.IsReady)
                throw new CaregiverNotReadyException(
                    "This caregiver isn't ready to be assigned right now.", readiness.IneligibilityReasons);

            var now = DateTime.UtcNow;
            var assignment = new Assignment
            {
                Id = ObjectId.GenerateNewId(),
                PackageRequestId = packageRequestId,
                CaregiverId = caregiverId,
                ClientId = request.ClientId,
                Status = AssignmentStatuses.PendingAcceptance,
                AssignedByAdminId = adminId,
                AssignedByAdminEmail = adminEmail,
                AssignedBy = string.Equals(assignedBy, "system", StringComparison.OrdinalIgnoreCase) ? "system" : "staff",
                AssignedAt = now,
                MatchScore = matchScore,
                CreatedAt = now,
            };
            _db.Assignments.Add(assignment);

            request.Status = PackageRequestStatuses.Assigned;
            request.UpdatedAt = now;

            await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = "Assignment",
                TargetEntityId = assignment.Id.ToString(),
                TargetUserId = caregiverId,
                Action = "PackageCaregiverAssigned",
                AfterJson = JsonSerializer.Serialize(new { assignment.PackageRequestId, assignment.CaregiverId, assignment.AssignedBy }),
                Reason = $"Assigned to package request {packageRequestId}",
                Timestamp = now,
            });

            await _db.SaveChangesAsync();

            // Real notification to the caregiver + email.
            await _mediator.Send(new SendNotificationCommand(
                RecipientId: caregiverId,
                SenderId: assignment.AssignedBy == "system" ? "system" : adminId,
                Type: NotificationTypes.PackageAssignmentOffered,
                Content: $"You've been assigned to a {request.PackageCategory} ({request.PackageTierLabel}) package request. Review and accept to confirm.",
                Title: "New assignment — action needed",
                RelatedEntityId: assignment.Id.ToString()));

            try
            {
                var subject = $"You've been assigned: {request.PackageCategory} ({request.PackageTierLabel})";
                var html = $@"
                    <h3>Hi {caregiver.FirstName},</h3>
                    <p>Our team has assigned you to a <strong>{request.PackageCategory}</strong> package request
                       ({request.PackageTierLabel}).</p>
                    <p>Please log in to review the details and <strong>accept</strong> the assignment.
                       The client will only see you as their caregiver once you accept.</p>
                    <p>— The CarePro Team</p>";
                // Always-Send: an assignment offer is an operational event the caregiver
                // must act on. No unsubscribe header (policy: Preference-Gated only), no
                // preference gate.
                await _emailService.SendGenericNotificationEmailAsync(
                    caregiver.Email, caregiver.FirstName, subject, html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send assignment email for assignment {AssignmentId}", assignment.Id);
            }

            _logger.LogInformation(
                "{AssignedBy} {AdminId} assigned caregiver {CaregiverId} to package request {RequestId} (assignment {AssignmentId})",
                assignment.AssignedBy, adminId, caregiverId, packageRequestId, assignment.Id);

            return Map(assignment);
        }

        public async Task<AssignmentActionResult> CancelAsync(string assignmentId, string adminId, string? adminEmail, string reason)
        {
            if (string.IsNullOrWhiteSpace(adminId)) throw new ArgumentException("Admin identity is required.");
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                throw new ArgumentException("A reason (min 5 chars) is required to cancel an assignment.");

            var assignment = await LoadAssignmentAsync(assignmentId);

            if (!string.Equals(assignment.Status, AssignmentStatuses.PendingAcceptance, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Only a pending assignment can be cancelled here (this one is '{assignment.Status}').");

            var now = DateTime.UtcNow;
            assignment.Status = AssignmentStatuses.Cancelled;
            assignment.CancelledByAdminId = adminId;
            assignment.CancelReason = reason.Trim();
            assignment.UpdatedAt = now;

            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(assignment.PackageRequestId));
            if (request != null && request.Status == PackageRequestStatuses.Assigned)
            {
                request.Status = PackageRequestStatuses.Pending;
                request.UpdatedAt = now;
            }

            await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
            {
                Id = ObjectId.GenerateNewId(),
                AdminId = adminId,
                AdminEmail = adminEmail,
                TargetEntityType = "Assignment",
                TargetEntityId = assignment.Id.ToString(),
                TargetUserId = assignment.CaregiverId,
                Action = "PackageAssignmentCancelled",
                Reason = reason.Trim(),
                Timestamp = now,
            });

            await _db.SaveChangesAsync();

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: assignment.CaregiverId,
                SenderId: adminId,
                Type: NotificationTypes.PackageAssignmentCancelled,
                Content: "An assignment you hadn't yet accepted was withdrawn by our team.",
                Title: "Assignment withdrawn",
                RelatedEntityId: assignment.Id.ToString()));

            _logger.LogInformation("Admin {AdminId} cancelled assignment {AssignmentId}", adminId, assignment.Id);
            return new AssignmentActionResult
            {
                Success = true, AssignmentId = assignment.Id.ToString(),
                Status = assignment.Status, Message = "Assignment cancelled."
            };
        }

        public async Task<List<PendingAssignmentDTO>> GetPendingAcceptanceAsync()
        {
            var pending = await _db.Assignments
                .Where(a => a.Status == AssignmentStatuses.PendingAcceptance)
                .ToListAsync();

            if (pending.Count == 0) return new List<PendingAssignmentDTO>();

            var caregiverOids = pending
                .Select(a => ObjectId.TryParse(a.CaregiverId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var caregivers = await _db.CareGivers.Where(c => caregiverOids.Contains(c.Id)).ToListAsync();
            var caregiverName = caregivers.ToDictionary(
                c => c.Id.ToString(), c => $"{c.FirstName} {c.LastName}".Trim());

            var requestOids = pending
                .Select(a => ObjectId.TryParse(a.PackageRequestId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var requests = await _db.PackageRequests.Where(p => requestOids.Contains(p.Id)).ToListAsync();
            var requestById = requests.ToDictionary(p => p.Id.ToString());

            var now = DateTime.UtcNow;

            // Longest-pending first — this is the whole point of the view.
            return pending
                .OrderBy(a => a.AssignedAt)
                .Select(a =>
                {
                    var pendingFor = now - a.AssignedAt;
                    requestById.TryGetValue(a.PackageRequestId, out var req);
                    return new PendingAssignmentDTO
                    {
                        AssignmentId = a.Id.ToString(),
                        PackageRequestId = a.PackageRequestId,
                        CaregiverId = a.CaregiverId,
                        CaregiverName = caregiverName.GetValueOrDefault(a.CaregiverId, "(unknown)"),
                        ClientId = a.ClientId,
                        PackageCategory = req?.PackageCategory ?? string.Empty,
                        PackageTierLabel = req?.PackageTierLabel ?? string.Empty,
                        AssignedByAdminId = a.AssignedByAdminId,
                        AssignedAt = a.AssignedAt,
                        PendingForHours = Math.Round(pendingFor.TotalHours, 2),
                        PendingForHuman = HumanizeDuration(pendingFor),
                    };
                })
                .ToList();
        }

        public async Task<List<AcceptedAssignmentDTO>> GetAcceptedAsync()
        {
            var accepted = await _db.Assignments
                .Where(a => a.Status == AssignmentStatuses.Accepted)
                .ToListAsync();

            if (accepted.Count == 0) return new List<AcceptedAssignmentDTO>();

            var caregiverOids = accepted
                .Select(a => ObjectId.TryParse(a.CaregiverId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var caregivers = await _db.CareGivers.Where(c => caregiverOids.Contains(c.Id)).ToListAsync();
            var caregiverName = caregivers.ToDictionary(
                c => c.Id.ToString(), c => $"{c.FirstName} {c.LastName}".Trim());

            var requestOids = accepted
                .Select(a => ObjectId.TryParse(a.PackageRequestId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var requests = await _db.PackageRequests.Where(p => requestOids.Contains(p.Id)).ToListAsync();
            var requestById = requests.ToDictionary(p => p.Id.ToString());

            var packageOids = requests
                .Select(r => ObjectId.TryParse(r.PackageId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var packages = await _db.Packages.Where(p => packageOids.Contains(p.Id)).ToListAsync();
            var packageById = packages.ToDictionary(p => p.Id.ToString());

            // Most-recently-accepted first.
            return accepted
                .OrderByDescending(a => a.RespondedAt ?? a.AssignedAt)
                .Select(a =>
                {
                    requestById.TryGetValue(a.PackageRequestId, out var req);
                    Domain.Entities.Package? pkg = null;
                    if (req != null) packageById.TryGetValue(req.PackageId, out pkg);
                    return new AcceptedAssignmentDTO
                    {
                        AssignmentId = a.Id.ToString(),
                        PackageRequestId = a.PackageRequestId,
                        CaregiverId = a.CaregiverId,
                        CaregiverName = caregiverName.GetValueOrDefault(a.CaregiverId, "(unknown)"),
                        ClientId = a.ClientId,
                        PackageCategory = req?.PackageCategory ?? string.Empty,
                        PackageTierLabel = req?.PackageTierLabel ?? string.Empty,
                        PayCalculationType = pkg?.PayCalculationType?.ToString() ?? string.Empty,
                        AcceptedAt = a.RespondedAt,
                    };
                })
                .ToList();
        }

        // ─────────────────────────────── Caregiver ───────────────────────────────

        public async Task<List<AssignmentDTO>> GetMyAssignmentsAsync(string caregiverId)
        {
            var items = await _db.Assignments
                .Where(a => a.CaregiverId == caregiverId)
                .ToListAsync();
            return items.OrderByDescending(a => a.AssignedAt).Select(Map).ToList();
        }

        public async Task<AssignmentActionResult> AcceptAsync(string assignmentId, string caregiverId)
        {
            var assignment = await LoadOwnedAssignmentAsync(assignmentId, caregiverId);

            if (string.Equals(assignment.Status, AssignmentStatuses.Accepted, StringComparison.Ordinal))
                return new AssignmentActionResult
                {
                    Success = true, AssignmentId = assignment.Id.ToString(),
                    Status = assignment.Status, Message = "Already accepted."
                };

            if (!string.Equals(assignment.Status, AssignmentStatuses.PendingAcceptance, StringComparison.Ordinal))
                throw new InvalidOperationException($"This assignment is '{assignment.Status}' and can no longer be accepted.");

            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(assignment.PackageRequestId))
                ?? throw new KeyNotFoundException("The linked package request no longer exists.");

            var now = DateTime.UtcNow;
            assignment.Status = AssignmentStatuses.Accepted;
            assignment.RespondedAt = now;
            assignment.UpdatedAt = now;

            // Finalize to the client — this is the first moment the client sees a caregiver.
            request.Status = PackageRequestStatuses.Confirmed;
            request.ConfirmedCaregiverId = caregiverId;
            request.ConfirmedAt = now;
            request.UpdatedAt = now;

            await _db.SaveChangesAsync();

            var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(caregiverId));
            var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "Your caregiver";

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: request.ClientId,
                SenderId: caregiverId,
                Type: NotificationTypes.PackageAssignmentConfirmed,
                Content: $"{caregiverName} has accepted your {request.PackageCategory} ({request.PackageTierLabel}) request and is now confirmed.",
                Title: "Your caregiver is confirmed",
                RelatedEntityId: request.Id.ToString()));

            try
            {
                if (ObjectId.TryParse(request.ClientId, out var clientOid))
                {
                    var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientOid);
                    if (client != null && !string.IsNullOrEmpty(client.Email))
                    {
                        var subject = $"Your caregiver is confirmed — {request.PackageCategory}";
                        var html = $@"
                            <h3>Hi {client.FirstName},</h3>
                            <p><strong>{caregiverName}</strong> has accepted your
                               {request.PackageCategory} ({request.PackageTierLabel}) package request and is now your confirmed caregiver.</p>
                            <p>Log in to view their profile and next steps.</p>
                            <p>— The CarePro Team</p>";
                        // Always-Send: "your caregiver is confirmed" is a transactional
                        // milestone. No unsubscribe header (policy: Preference-Gated only),
                        // no preference gate.
                        await _emailService.SendGenericNotificationEmailAsync(
                            client.Email, client.FirstName ?? "Client", subject, html);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send acceptance email to client for assignment {AssignmentId}", assignment.Id);
            }

            _logger.LogInformation("Caregiver {CaregiverId} accepted assignment {AssignmentId}; request {RequestId} confirmed",
                caregiverId, assignment.Id, request.Id);

            // Phase 6 — the moment the request reaches "confirmed", auto-generate the
            // package contract (Package terms + CarePro standard terms, no negotiation).
            // Resilient: a hiccup here must not undo the acceptance. Idempotent, so it
            // can also be re-run later (e.g. by the payment flow) if this call fails.
            try
            {
                await _packageContractService.GenerateForConfirmedRequestAsync(request.Id.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Auto contract generation failed for confirmed request {RequestId} — acceptance stands; retry available",
                    request.Id);
            }

            // Phase 7.1's automatic pending-balance credit at confirmation was removed in
            // Phase 9.8 — package-assignment caregivers are now paid exclusively through
            // admin-approved Payroll records (Phase 9.7), not an automatic credit fired
            // by acceptance. Confirmation no longer touches the wallet at all.

            return new AssignmentActionResult
            {
                Success = true, AssignmentId = assignment.Id.ToString(),
                Status = assignment.Status, Message = "Assignment accepted. The client has been notified."
            };
        }

        public async Task<AssignmentActionResult> DeclineAsync(string assignmentId, string caregiverId, string? reason)
        {
            var assignment = await LoadOwnedAssignmentAsync(assignmentId, caregiverId);

            if (!string.Equals(assignment.Status, AssignmentStatuses.PendingAcceptance, StringComparison.Ordinal))
                throw new InvalidOperationException($"This assignment is '{assignment.Status}' and can no longer be declined.");

            var now = DateTime.UtcNow;
            assignment.Status = AssignmentStatuses.Declined;
            assignment.RespondedAt = now;
            assignment.DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            assignment.UpdatedAt = now;

            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(assignment.PackageRequestId));
            if (request != null && request.Status == PackageRequestStatuses.Assigned)
            {
                request.Status = PackageRequestStatuses.Pending; // back to the queue; no auto-reassignment
                request.UpdatedAt = now;
            }

            await _db.SaveChangesAsync();

            // Tell ops — there is deliberately no automatic reassignment.
            var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(caregiverId));
            var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "A caregiver";
            await NotifyOpsAdminsAsync(
                NotificationTypes.PackageAssignmentDeclined,
                $"{caregiverName} declined the assignment for package request {assignment.PackageRequestId}"
                    + (assignment.DeclineReason != null ? $" — \"{assignment.DeclineReason}\"" : "")
                    + ". No automatic reassignment; please reassign or follow up.",
                "Assignment declined — action needed",
                assignment.Id.ToString());

            _logger.LogInformation("Caregiver {CaregiverId} declined assignment {AssignmentId}", caregiverId, assignment.Id);
            return new AssignmentActionResult
            {
                Success = true, AssignmentId = assignment.Id.ToString(),
                Status = assignment.Status, Message = "Assignment declined. Our team has been notified."
            };
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private async Task<Assignment> LoadAssignmentAsync(string assignmentId)
        {
            if (!ObjectId.TryParse(assignmentId, out var oid))
                throw new ArgumentException("Invalid assignment id.");
            return await _db.Assignments.FirstOrDefaultAsync(a => a.Id == oid)
                ?? throw new KeyNotFoundException($"Assignment '{assignmentId}' not found.");
        }

        private async Task<Assignment> LoadOwnedAssignmentAsync(string assignmentId, string caregiverId)
        {
            var assignment = await LoadAssignmentAsync(assignmentId);
            if (!string.Equals(assignment.CaregiverId, caregiverId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This assignment doesn't belong to you.");
            return assignment;
        }

        private async Task NotifyOpsAdminsAsync(string type, string content, string title, string relatedEntityId)
        {
            var admins = await _db.AdminUsers.Where(a => !a.IsDeleted).ToListAsync();
            foreach (var admin in admins)
            {
                await _mediator.Send(new SendNotificationCommand(
                    admin.Id.ToString(), "system", type, content, title, relatedEntityId));
            }
        }

        private static string HumanizeDuration(TimeSpan d)
        {
            if (d.TotalMinutes < 60) return $"{Math.Max(1, (int)d.TotalMinutes)}m";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h {d.Minutes}m";
            return $"{(int)d.TotalDays}d {d.Hours}h";
        }

        private static AssignmentDTO Map(Assignment a) => new()
        {
            Id = a.Id.ToString(),
            PackageRequestId = a.PackageRequestId,
            CaregiverId = a.CaregiverId,
            ClientId = a.ClientId,
            Status = a.Status,
            AssignedByAdminId = a.AssignedByAdminId,
            AssignedBy = a.AssignedBy,
            AssignedAt = a.AssignedAt,
            MatchScore = a.MatchScore,
            RespondedAt = a.RespondedAt,
            DeclineReason = a.DeclineReason,
        };
    }
}
