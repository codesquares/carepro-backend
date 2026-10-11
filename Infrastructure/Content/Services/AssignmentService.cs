using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IOpsAlertService _opsAlerts;
        private readonly ILogger<AssignmentService> _logger;
        private readonly IConfiguration? _configuration;
        private readonly TimeProvider _time;

        /// <summary>
        /// A claimed-but-unfinished transition younger than this is presumed to still be running on another request (the double
        /// click / racing tab), so a second caller gets a 409 instead of joining in. Older than this, the owner's retry (or an admin's,
        /// for a staff cancel) is treated as "please finish this" and completes the missing steps.
        /// </summary>
        private static readonly TimeSpan InFlightGrace = TimeSpan.FromSeconds(30);

        private static bool StillInFlight(Assignment a) =>
            a.DownstreamPendingSince != null && DateTime.UtcNow - a.DownstreamPendingSince.Value < InFlightGrace;

        private static InvalidOperationException JustChanged(string verb) =>
            new($"This assignment was just changed by someone else and can no longer be {verb}. Please refresh to see its current status.");

        private static readonly string[] ActiveAssignmentStatuses =
            { AssignmentStatuses.PendingAcceptance, AssignmentStatuses.Accepted };

        public AssignmentService(
            CareProDbContext db,
            IMediator mediator,
            IEmailService emailService,
            ICaregiverReadinessService readinessService,
            IPackageContractService packageContractService,
            IOpsAlertService opsAlerts,
            ILogger<AssignmentService> logger,
            IConfiguration? configuration = null,
            TimeProvider? time = null)
        {
            _configuration = configuration;
            _time = time ?? TimeProvider.System;
            _db = db;
            _mediator = mediator;
            _emailService = emailService;
            _readinessService = readinessService;
            _packageContractService = packageContractService;
            _opsAlerts = opsAlerts;
            _logger = logger;
        }

        // ─────────────────────────── Staff / system ───────────────────────────

        public async Task<AssignmentDTO> AssignAsync(
            string packageRequestId, string caregiverId,
            string adminId, string? adminEmail, string assignedBy, double? matchScore,
            CareBriefDTO? careBrief = null, string? serviceAddress = null)
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

            // Readiness gate: identity, guarantors, address history, caregiver type, and the category's
            // assessment/certificates. Gigs are deliberately NOT part of it for package assignment —
            // matching and ranking already ignore them, and the requirement could never be met anyway
            // (gigs use a different category vocabulary than packages, and "Published" gigs never count as
            // "Active"). `knownHasActiveGig: true` tells the readiness service the gig condition is already
            // settled, so it neither queries gigs nor adds no_active_gig. Other readiness callers are unchanged.
            var readiness = await _readinessService.GetReadinessAsync(
                caregiverId, request.ServiceCategory, knownHasActiveGig: true);
            if (!readiness.IsReady)
                throw new CaregiverNotReadyException(
                    "This caregiver isn't ready to be assigned right now.", readiness.IneligibilityReasons);

            var now = DateTime.UtcNow;

            // Atomic claim of the request (Status is a concurrency token): of two staff assigning the same request at
            // once, only the first to flip it to Assigned proceeds; the other 409s before any Assignment row exists.
            // The AnyAsync check above is only a friendly early error — it is not what enforces one-active-assignment.
            var previousRequestStatus = request.Status;
            request.Status = PackageRequestStatuses.Assigned;
            request.UpdatedAt = now;
            await SaveRequestTransitionAsync();

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
                BriefCareNeeds = Clean(careBrief?.CareNeedsSummary),
                BriefSchedule = Clean(careBrief?.Schedule),
                BriefStartDate = careBrief?.StartDate,
                BriefDuration = Clean(careBrief?.Duration),
                BriefArea = Clean(careBrief?.Area),
                ServiceAddress = Clean(serviceAddress),
            };
            try
            {
                _db.Assignments.Add(assignment);

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
            }
            catch (Exception ex)
            {
                // The request was already flipped to Assigned; if the assignment never landed, put it back so it can't
                // sit "Assigned" with nobody assigned.
                await ReleaseRequestClaimAsync(prOid, assignment.Id, previousRequestStatus, ex);
                throw;
            }

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

            if (string.Equals(assignment.Status, AssignmentStatuses.PendingAcceptance, StringComparison.Ordinal))
            {
                var now = DateTime.UtcNow;
                assignment.Status = AssignmentStatuses.Cancelled;
                assignment.CancelledByAdminId = adminId;
                assignment.CancelReason = reason.Trim();
                assignment.UpdatedAt = now;
                assignment.DownstreamPendingSince = now;

                // Atomic claim (see AcceptAsync); the follow-on work below is idempotent and re-runnable.
                await ClaimAssignmentTransitionAsync(assignment, "cancelled");
            }
            else if (IsHalfDoneStaffCancel(assignment))
            {
                // Same cancel, first attempt stopped part-way: finish it (idempotent) instead of refusing — unless it is still in flight.
                if (StillInFlight(assignment)) throw JustChanged("cancelled");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Only a pending assignment can be cancelled here (this one is '{assignment.Status}').");
            }

            await FinishCancelAsync(assignment, adminEmail);
            return new AssignmentActionResult
            {
                Success = true, AssignmentId = assignment.Id.ToString(),
                Status = assignment.Status, Message = "Assignment cancelled."
            };
        }

        private static bool IsHalfDoneStaffCancel(Assignment a) =>
            a.Status == AssignmentStatuses.Cancelled && a.DownstreamPendingSince != null && a.DownstreamDoneAt == null
            && !string.IsNullOrEmpty(a.CancelledByAdminId);

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

        public async Task<List<CaregiverAssignmentDTO>> GetMyAssignmentsAsync(string caregiverId)
        {
            var items = await _db.Assignments
                .Where(a => a.CaregiverId == caregiverId)
                .OrderByDescending(a => a.AssignedAt)
                .ToListAsync();
            return await BuildCaregiverAssignmentDtosAsync(items);
        }

        public async Task<CaregiverAssignmentDTO> GetMyAssignmentDetailAsync(string assignmentId, string caregiverId)
        {
            var assignment = await LoadOwnedAssignmentAsync(assignmentId, caregiverId);
            var dtos = await BuildCaregiverAssignmentDtosAsync(new List<Assignment> { assignment });
            return dtos[0];
        }

        /// <summary>
        /// Enriches a batch of Assignments with client name and package category/tier/pay-type —
        /// the same join pattern <see cref="GetAcceptedAsync"/> uses for the admin picker, scoped
        /// to whichever assignments the caller passes in (one for detail, many for the list).
        /// </summary>
        private async Task<List<CaregiverAssignmentDTO>> BuildCaregiverAssignmentDtosAsync(List<Assignment> items)
        {
            if (items.Count == 0) return new List<CaregiverAssignmentDTO>();

            var clientOids = items
                .Select(a => ObjectId.TryParse(a.ClientId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var clients = await _db.Clients.Where(c => clientOids.Contains(c.Id)).ToListAsync();
            var clientName = clients.ToDictionary(
                c => c.Id.ToString(), c => $"{c.FirstName} {c.LastName}".Trim());

            var requestOids = items
                .Select(a => ObjectId.TryParse(a.PackageRequestId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var requests = await _db.PackageRequests.Where(p => requestOids.Contains(p.Id)).ToListAsync();
            var requestById = requests.ToDictionary(p => p.Id.ToString());

            var packageOids = requests
                .Select(r => ObjectId.TryParse(r.PackageId, out var o) ? o : (ObjectId?)null)
                .Where(o => o.HasValue).Select(o => o!.Value).ToList();
            var packages = await _db.Packages.Where(p => packageOids.Contains(p.Id)).ToListAsync();
            var packageById = packages.ToDictionary(p => p.Id.ToString());

            return items.Select(a =>
            {
                requestById.TryGetValue(a.PackageRequestId, out var req);
                Domain.Entities.Package? pkg = null;
                if (req != null) packageById.TryGetValue(req.PackageId, out pkg);
                return new CaregiverAssignmentDTO
                {
                    Id = a.Id.ToString(),
                    PackageRequestId = a.PackageRequestId,
                    // Privacy: before acceptance the caregiver gets the client's FIRST NAME only and no client id
                    // (enforced here, not in the UI). PackageRequest.Notes/Location are never returned at all.
                    ClientDetailsReleased = ClientDetailsReleased(a),
                    ClientId = ClientDetailsReleased(a) ? a.ClientId : string.Empty,
                    ClientName = ClientDetailsReleased(a)
                        ? clientName.GetValueOrDefault(a.ClientId, "(unknown)")
                        : FirstNameOf(clientName.GetValueOrDefault(a.ClientId)),
                    CareBrief = ToBrief(a),
                    // Full address: only after acceptance (same rule as the client's surname/id).
                    ServiceAddress = ClientDetailsReleased(a) ? a.ServiceAddress : null,
                    Status = a.Status,
                    PackageCategory = req?.PackageCategory ?? string.Empty,
                    PackageTierLabel = req?.PackageTierLabel ?? string.Empty,
                    PayCalculationType = pkg?.PayCalculationType?.ToString() ?? string.Empty,
                    AssignedAt = a.AssignedAt,
                    RespondedAt = a.RespondedAt,
                    DeclineReason = a.DeclineReason,
                };
            }).ToList();
        }

        private async Task EnsurePoliceClearanceAllowsAcceptAsync(string caregiverId)
        {
            var settings = PoliceClearanceSettings.Load(_configuration);
            if (!settings.IsEnforced(_time.GetUtcNow().UtcDateTime)) return;   // grace period / unset: informational only

            var records = await _db.PoliceClearances.Where(c => c.CaregiverId == caregiverId).ToListAsync();
            var status = PoliceClearanceLogic.StatusName(PoliceClearanceLogic.Latest(records));
            var reason = PoliceClearanceLogic.ReadinessReason(status);
            if (reason == null) return;

            throw new CaregiverNotReadyException(
                status == "pending"
                    ? "Your police clearance is still being reviewed. You can accept this assignment once our team has approved it."
                    : "A police clearance approved by our team is required before you can accept assignments. Please submit yours from the Vetting page.",
                new List<string> { reason });
        }

        public async Task<AssignmentActionResult> AcceptAsync(string assignmentId, string caregiverId)
        {
            // Ownership is enforced here, before anything else: only this assignment's caregiver can accept it or
            // complete its half-done acceptance.
            var assignment = await LoadOwnedAssignmentAsync(assignmentId, caregiverId);

            if (string.Equals(assignment.Status, AssignmentStatuses.Accepted, StringComparison.Ordinal))
            {
                if (await IsAcceptanceCompleteAsync(assignment))
                    return new AssignmentActionResult
                    {
                        Success = true, AssignmentId = assignment.Id.ToString(),
                        Status = assignment.Status, Message = "Already accepted."
                    };

                // Another request is (very probably) still finishing this one — a double click or second tab, not a retry. Before the
                // notifications are claimed that is a lost race (409); once they are, the acceptance is real and the contract is
                // just being generated, so a duplicate gets the same idempotent "Already accepted" it always did.
                if (StillInFlight(assignment))
                {
                    if (assignment.DownstreamDoneAt == null) throw JustChanged("accepted");
                    return new AssignmentActionResult
                    {
                        Success = true, AssignmentId = assignment.Id.ToString(),
                        Status = assignment.Status, Message = "Already accepted."
                    };
                }

                // The first accept stopped part-way (status saved, follow-on work not finished). Same caregiver retrying:
                // finish it. Every step is idempotent and notifications are claimed atomically, so nothing is repeated.
                await FinishAcceptAsync(assignment, alertOpsOnContractFailure: true);
                return new AssignmentActionResult
                {
                    Success = true, AssignmentId = assignment.Id.ToString(),
                    Status = assignment.Status, Message = "Assignment accepted. The client has been notified."
                };
            }

            if (!string.Equals(assignment.Status, AssignmentStatuses.PendingAcceptance, StringComparison.Ordinal))
                throw new InvalidOperationException($"This assignment is '{assignment.Status}' and can no longer be accepted.");

            // Police clearance re-check (only this one criterion — accept behaviour for every other readiness reason is
            // unchanged). Runs before anything is modified, so a refusal leaves the assignment exactly as it was.
            await EnsurePoliceClearanceAllowsAcceptAsync(caregiverId);

            _ = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(assignment.PackageRequestId))
                ?? throw new KeyNotFoundException("The linked package request no longer exists.");

            var now = DateTime.UtcNow;
            assignment.Status = AssignmentStatuses.Accepted;
            assignment.RespondedAt = now;
            assignment.UpdatedAt = now;
            assignment.DownstreamPendingSince = now;

            // Atomic claim: only one concurrent responder can move this row off PendingAcceptance (Status is a
            // concurrency token). The loser gets a 409 and sends nothing. Saved on its own so a lost race can
            // never leave the PackageRequest half-updated.
            await ClaimAssignmentTransitionAsync(assignment, "accepted");

            await FinishAcceptAsync(assignment, alertOpsOnContractFailure: true);

            return new AssignmentActionResult
            {
                Success = true, AssignmentId = assignment.Id.ToString(),
                Status = assignment.Status, Message = "Assignment accepted. The client has been notified."
            };
        }

        /// <summary>Accepted is only "complete" once the request is Confirmed, the notifications were claimed/sent, and a contract exists.
        /// Legacy rows (no pending marker) predate this tracking and count as complete.</summary>
        private async Task<bool> IsAcceptanceCompleteAsync(Assignment assignment)
        {
            if (assignment.DownstreamPendingSince == null) return true;
            if (assignment.DownstreamDoneAt == null) return false;
            return await _db.Contracts.AnyAsync(c => c.PackageRequestId == assignment.PackageRequestId);
        }

        /// <summary>
        /// Everything that follows a successful accept claim. Idempotent and safe to run any number of times, from any of: the
        /// original call, the caregiver retrying, or the recovery sweeper — including concurrently.
        /// </summary>
        private async Task FinishAcceptAsync(Assignment assignment, bool alertOpsOnContractFailure)
        {
            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(assignment.PackageRequestId))
                ?? throw new KeyNotFoundException("The linked package request no longer exists.");
            var caregiverId = assignment.CaregiverId;
            var now = DateTime.UtcNow;

            // 1. Request → Confirmed (first moment the client sees a caregiver). Skipped when already done or the
            //    client has cancelled in the meantime.
            if (request.Status != PackageRequestStatuses.Confirmed && request.Status != PackageRequestStatuses.Cancelled)
            {
                request.Status = PackageRequestStatuses.Confirmed;
                request.ConfirmedCaregiverId = caregiverId;
                request.ConfirmedAt = assignment.RespondedAt ?? now;
                request.UpdatedAt = now;
                await SaveRequestTransitionAsync();
            }

            // 2. Notifications + emails: claimed atomically (DownstreamDoneAt is a concurrency token), so exactly one
            //    runner sends them even if the caregiver's retry and the sweeper arrive together.
            if (assignment.DownstreamDoneAt == null && request.Status == PackageRequestStatuses.Confirmed)
            {
                if (await TryClaimDownstreamAsync(assignment))
                {
                    try { await SendAcceptedNotificationsAsync(assignment, request); }
                    catch { await ReleaseDownstreamClaimAsync(assignment); throw; }
                }
            }

            // 3. Contract (idempotent generator). A hiccup must not undo the acceptance; the sweeper retries it.
            if (request.Status == PackageRequestStatuses.Confirmed)
            {
                try
                {
                    await _packageContractService.GenerateForConfirmedRequestAsync(request.Id.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Auto contract generation failed for confirmed request {RequestId} — acceptance stands; retry available",
                        request.Id);
                    if (alertOpsOnContractFailure && await TryClaimRecoveryAlertAsync(assignment))
                    {
                        await _opsAlerts.NotifyAdminsAsync(
                            NotificationTypes.PackageContractDeliveryFailed,
                            "Package contract was not generated",
                            $"Caregiver accepted request {request.Id} ({request.PackageCategory}, {request.PackageTierLabel}) but the care agreement " +
                            $"could not be generated ({ex.GetType().Name}: {ex.Message}). Neither party has received a contract — follow up manually.",
                            request.Id.ToString(),
                            alsoEmail: true);
                    }
                }
            }

            // Phase 7.1's automatic pending-balance credit at confirmation was removed in
            // Phase 9.8 — package-assignment caregivers are now paid exclusively through
            // admin-approved Payroll records (Phase 9.7), not an automatic credit fired
            // by acceptance. Confirmation no longer touches the wallet at all.
        }

        private async Task SendAcceptedNotificationsAsync(Assignment assignment, PackageRequest request)
        {
            var caregiverId = assignment.CaregiverId;
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

            // Dedicated "you're confirmed" email to the caregiver — sent in addition to (not instead of)
            // the contract PDF email delivered by GenerateForConfirmedRequestAsync.
            try
            {
                if (caregiver != null && !string.IsNullOrEmpty(caregiver.Email))
                {
                    var subject = $"You're confirmed — {request.PackageCategory} ({request.PackageTierLabel})";
                    var html = $@"
                        <h3>Hi {caregiver.FirstName},</h3>
                        <p>Thank you for accepting. You are now the <strong>confirmed caregiver</strong> for the
                           <strong>{request.PackageCategory}</strong> ({request.PackageTierLabel}) package request.</p>
                        <p>The care agreement is being prepared and will be emailed to you separately.
                           Log in to see the details and your next steps.</p>
                        <p>— The CarePro Team</p>";
                    // Always-Send: a transactional confirmation of an action the caregiver just took.
                    await _emailService.SendGenericNotificationEmailAsync(
                        caregiver.Email, caregiver.FirstName, subject, html);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send acceptance confirmation email to caregiver for assignment {AssignmentId}", assignment.Id);
            }

            _logger.LogInformation("Caregiver {CaregiverId} accepted assignment {AssignmentId}; request {RequestId} confirmed",
                caregiverId, assignment.Id, request.Id);
        }

        public async Task<AssignmentActionResult> DeclineAsync(string assignmentId, string caregiverId, string? reason)
        {
            var assignment = await LoadOwnedAssignmentAsync(assignmentId, caregiverId);

            if (string.Equals(assignment.Status, AssignmentStatuses.PendingAcceptance, StringComparison.Ordinal))
            {
                var now = DateTime.UtcNow;
                assignment.Status = AssignmentStatuses.Declined;
                assignment.RespondedAt = now;
                assignment.DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                assignment.UpdatedAt = now;
                assignment.DownstreamPendingSince = now;

                // Atomic claim — see AcceptAsync. A concurrent accept/decline/cancel that got there first wins; this one 409s.
                await ClaimAssignmentTransitionAsync(assignment, "declined");
            }
            else if (assignment.Status == AssignmentStatuses.Declined && assignment.DownstreamPendingSince != null && assignment.DownstreamDoneAt == null)
            {
                // Same caregiver retrying a decline that stopped part-way: finish it (idempotent) — unless it is still in flight.
                if (StillInFlight(assignment)) throw JustChanged("declined");
            }
            else
            {
                throw new InvalidOperationException($"This assignment is '{assignment.Status}' and can no longer be declined.");
            }

            await FinishDeclineAsync(assignment);
            return new AssignmentActionResult
            {
                Success = true, AssignmentId = assignment.Id.ToString(),
                Status = assignment.Status, Message = "Assignment declined. Our team has been notified."
            };
        }

        private async Task FinishDeclineAsync(Assignment assignment)
        {
            await ReturnRequestToQueueAsync(assignment);

            if (assignment.DownstreamDoneAt != null || !await TryClaimDownstreamAsync(assignment)) return;
            try
            {
                var caregiver = await _db.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(assignment.CaregiverId));
                var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "A caregiver";
                // Tell ops — there is deliberately no automatic reassignment.
                await NotifyOpsAdminsAsync(
                    NotificationTypes.PackageAssignmentDeclined,
                    $"{caregiverName} declined the assignment for package request {assignment.PackageRequestId}"
                        + (assignment.DeclineReason != null ? $" — \"{assignment.DeclineReason}\"" : "")
                        + ". No automatic reassignment; please reassign or follow up.",
                    "Assignment declined — action needed",
                    assignment.Id.ToString());
            }
            catch { await ReleaseDownstreamClaimAsync(assignment); throw; }

            _logger.LogInformation("Caregiver {CaregiverId} declined assignment {AssignmentId}", assignment.CaregiverId, assignment.Id);
        }

        private async Task FinishCancelAsync(Assignment assignment, string? adminEmail)
        {
            await ReturnRequestToQueueAsync(assignment);

            if (assignment.DownstreamDoneAt != null || !await TryClaimDownstreamAsync(assignment)) return;
            try
            {
                var adminId = assignment.CancelledByAdminId ?? string.Empty;
                await _db.AdminAuditLogs.AddAsync(new AdminAuditLog
                {
                    Id = ObjectId.GenerateNewId(),
                    AdminId = adminId,
                    AdminEmail = adminEmail,
                    TargetEntityType = "Assignment",
                    TargetEntityId = assignment.Id.ToString(),
                    TargetUserId = assignment.CaregiverId,
                    Action = "PackageAssignmentCancelled",
                    Reason = assignment.CancelReason,
                    Timestamp = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync();

                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: assignment.CaregiverId,
                    SenderId: adminId,
                    Type: NotificationTypes.PackageAssignmentCancelled,
                    Content: "An assignment you hadn't yet accepted was withdrawn by our team.",
                    Title: "Assignment withdrawn",
                    RelatedEntityId: assignment.Id.ToString()));
            }
            catch { await ReleaseDownstreamClaimAsync(assignment); throw; }

            _logger.LogInformation("Admin {AdminId} cancelled assignment {AssignmentId}", assignment.CancelledByAdminId, assignment.Id);
        }

        /// <summary>Decline / staff cancel: the request goes back to the queue (only if it is still Assigned). Idempotent.</summary>
        private async Task ReturnRequestToQueueAsync(Assignment assignment)
        {
            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(assignment.PackageRequestId));
            if (request != null && request.Status == PackageRequestStatuses.Assigned)
            {
                request.Status = PackageRequestStatuses.Pending; // back to the queue; no auto-reassignment
                request.UpdatedAt = DateTime.UtcNow;
                await SaveRequestTransitionAsync();
            }
        }

        // ───────────────────────── Recovery of half-done transitions ─────────────────────────

        /// <summary>
        /// Safety net for transitions nobody retried. Finds assignments whose status was claimed more than
        /// <paramref name="olderThan"/> ago but whose follow-on work never completed (or Accepted ones still without a contract) and
        /// finishes them through the same idempotent code the normal path uses. Admins are alerted (once per assignment, through the
        /// shared approved-admin recipient rule) only if completion fails. Safe to run on several API instances at once: every side
        /// effect is behind an atomic claim (Status / DownstreamDoneAt / RecoveryAlertedAt are concurrency tokens, the contract
        /// generator is idempotent), so a concurrent run just loses the claim and moves on.
        /// </summary>
        public async Task<int> RecoverIncompleteTransitionsAsync(TimeSpan olderThan)
        {
            var cutoff = DateTime.UtcNow - olderThan;
            var stuck = await _db.Assignments
                .Where(a => a.DownstreamPendingSince != null && a.DownstreamPendingSince < cutoff
                            && (a.Status == AssignmentStatuses.Accepted
                                || a.Status == AssignmentStatuses.Declined
                                || a.Status == AssignmentStatuses.Cancelled))
                .ToListAsync();

            var fixedCount = 0;
            foreach (var a in stuck)
            {
                try
                {
                    var wasComplete = a.Status == AssignmentStatuses.Accepted
                        ? await IsAcceptanceCompleteAsync(a)
                        : a.DownstreamDoneAt != null;
                    if (wasComplete) continue;

                    if (a.Status == AssignmentStatuses.Accepted) await FinishAcceptAsync(a, alertOpsOnContractFailure: false);
                    else if (a.Status == AssignmentStatuses.Declined) await FinishDeclineAsync(a);
                    else await FinishCancelAsync(a, adminEmail: null);

                    var nowComplete = a.Status == AssignmentStatuses.Accepted ? await IsAcceptanceCompleteAsync(a) : a.DownstreamDoneAt != null;
                    if (nowComplete) { fixedCount++; _logger.LogWarning("Recovered half-done {Status} for assignment {AssignmentId}", a.Status, a.Id); }
                    else await AlertRecoveryFailedAsync(a, "some follow-on steps are still incomplete after a retry");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recovery of assignment {AssignmentId} failed", a.Id);
                    try { await AlertRecoveryFailedAsync(a, $"{ex.GetType().Name}: {ex.Message}"); } catch (Exception aex) { _logger.LogError(aex, "Recovery alert failed for {AssignmentId}", a.Id); }
                }
            }
            return fixedCount;
        }

        private async Task AlertRecoveryFailedAsync(Assignment a, string detail)
        {
            if (!await TryClaimRecoveryAlertAsync(a)) return;   // another instance/run already alerted
            await _opsAlerts.NotifyAdminsAsync(
                NotificationTypes.PackageContractDeliveryFailed,
                "Assignment left half-finished — follow up",
                $"Assignment {a.Id} for package request {a.PackageRequestId} ({a.Status}) did not finish all of its follow-on steps " +
                $"(request status / notifications / contract) and automatic recovery could not complete it: {detail}. Please check it manually.",
                a.PackageRequestId);
        }

        // ───────────────────────────── Atomic claim helpers ─────────────────────────────

        /// <summary>First caller wins; everyone else gets false (their save hits the DownstreamDoneAt token).</summary>
        private async Task<bool> TryClaimDownstreamAsync(Assignment assignment)
        {
            assignment.DownstreamDoneAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(); return true; }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else holds the claim (or the status moved on). Drop our failed update so a later save in this scope
                // can't re-attempt it. (Deliberately not Reload: the Mongo EF provider can't build the by-key query it needs.)
                _db.Entry(assignment).State = EntityState.Detached;
                return false;
            }
        }

        /// <summary>Sending failed after we claimed: hand the claim back so a retry/the sweeper will send them.</summary>
        private async Task ReleaseDownstreamClaimAsync(Assignment assignment)
        {
            try
            {
                assignment.DownstreamDoneAt = null;
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) { _logger.LogError(ex, "Could not release the downstream claim on assignment {AssignmentId}", assignment.Id); }
        }

        private async Task<bool> TryClaimRecoveryAlertAsync(Assignment assignment)
        {
            if (assignment.RecoveryAlertedAt != null) return false;
            assignment.RecoveryAlertedAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(); return true; }
            catch (DbUpdateConcurrencyException) { _db.Entry(assignment).State = EntityState.Detached; return false; }
        }

        private async Task ReleaseRequestClaimAsync(ObjectId requestId, ObjectId assignmentId, string previousStatus, Exception cause)
        {
            _db.ChangeTracker.Clear();
            try
            {
                // The insert may actually have landed (e.g. a timeout after commit) — then leave the request as Assigned.
                if (await _db.Assignments.AnyAsync(a => a.Id == assignmentId)) return;
                var r = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == requestId);
                if (r != null && r.Status == PackageRequestStatuses.Assigned)
                {
                    r.Status = previousStatus;   // conditional: Status is a concurrency token, so this fails if anyone else changed it
                    r.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    _logger.LogWarning(cause, "Assignment insert failed after claiming request {RequestId}; request put back to '{Previous}'", requestId, previousStatus);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not put request {RequestId} back to '{Previous}' after a failed assignment insert (original error: {Cause})",
                    requestId, previousStatus, cause.Message);
            }
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        /// <summary>Saves a status change on an already-loaded assignment; a lost race becomes a 409 (InvalidOperationException).</summary>
        private async Task ClaimAssignmentTransitionAsync(Assignment assignment, string verb)
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(assignment).State = EntityState.Detached;
                throw new InvalidOperationException(
                    $"This assignment was just changed by someone else and can no longer be {verb}. Please refresh to see its current status.");
            }
        }

        private async Task SaveRequestTransitionAsync()
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "This package request was just changed by someone else. Please refresh and try again.");
            }
        }

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

        private Task NotifyOpsAdminsAsync(string type, string content, string title, string relatedEntityId)
            => _opsAlerts.NotifyAdminsAsync(type, title, content, relatedEntityId);

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
            CareBrief = ToBrief(a),
            ServiceAddress = a.ServiceAddress,
        };

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>Null when no brief fields were ever set (legacy assignments) — never throws on missing data.</summary>
        private static CareBriefDTO? ToBrief(Assignment a)
        {
            if (a.BriefCareNeeds == null && a.BriefSchedule == null && a.BriefStartDate == null
                && a.BriefDuration == null && a.BriefArea == null) return null;
            return new CareBriefDTO
            {
                CareNeedsSummary = a.BriefCareNeeds, Schedule = a.BriefSchedule,
                StartDate = a.BriefStartDate, Duration = a.BriefDuration, Area = a.BriefArea,
            };
        }

        /// <summary>
        /// The client's name/contact is released only once the caregiver has actually accepted
        /// (Accepted, or Cancelled after a prior acceptance — RespondedAt is set on accept and a decline
        /// moves to Declined instead). Pending/Declined/never-accepted offers see the first name only.
        /// </summary>
        private static bool ClientDetailsReleased(Assignment a) =>
            a.Status == AssignmentStatuses.Accepted
            || (a.Status == AssignmentStatuses.Cancelled && a.RespondedAt != null);

        private static string FirstNameOf(string? full) =>
            string.IsNullOrWhiteSpace(full) ? "(client)" : full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}
