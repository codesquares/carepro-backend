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
        private readonly IOpsAlertService _opsAlerts;
        private readonly ILogger<PackageContractService> _logger;
        private readonly int _cancellationWindowHours;

        /// <summary>Nominal contract period when the package itself defines no fixed duration.</summary>
        private const int DefaultContractWeeks = 4;

        public PackageContractService(
            CareProDbContext db,
            IContractTemplateService templateService,
            IContractPdfService pdfService,
            IMediator mediator,
            IEmailService emailService,
            IOpsAlertService opsAlerts,
            ILogger<PackageContractService> logger,
            Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
        {
            // Client cancellation window — configurable (PackageContract:CancellationWindowHours), default 48.
            _cancellationWindowHours =
                int.TryParse(configuration?["PackageContract:CancellationWindowHours"], out var configured) && configured >= 0 ? configured : 48; // 0 = no window
            _db = db;
            _templateService = templateService;
            _pdfService = pdfService;
            _mediator = mediator;
            _emailService = emailService;
            _opsAlerts = opsAlerts;
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

            // The cancellation window starts when the contract is sent (now). Care is blocked until it ends, so the
            // contract start date is the LATER of (day after confirmation) and (the window-end date).
            var sentAt = DateTime.UtcNow;
            var windowEnd = sentAt.AddHours(_cancellationWindowHours);
            var start = (request.ConfirmedAt ?? sentAt).Date.AddDays(1);
            if (_cancellationWindowHours > 0 && windowEnd.Date > start) start = windowEnd.Date;
            var end = start.AddDays(DefaultContractWeeks * 7);
            var contractId = ObjectId.GenerateNewId().ToString();

            var data = BuildGenerationData(contractId, request, package, client, caregiver, start, end);

            // Cancellation window starts when the contract is sent (now) and runs for the configured hours.
            data.CancellationWindowHours = _cancellationWindowHours;
            data.ContractSentAt = sentAt;

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
                ContractSentAt = sentAt,
                CancellationWindowEndsAt = windowEnd,
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
            // RelatedEntityId is the package request id, not the contract id: the client's
            // request-detail page (/app/client/requests/:id) is keyed by request id, and
            // nothing client-reachable can resolve a request from a bare contract id — there's
            // no "get request by contract" lookup, so a contract id here would be a dead link.
            foreach (var recipient in new[] { request.ClientId, request.ConfirmedCaregiverId })
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: recipient,
                    SenderId: "system",
                    Type: NotificationTypes.PackageContractGenerated,
                    Content: $"Your care agreement for the {package.Category} ({package.TierLabel}) package is ready to view.",
                    Title: "Care agreement ready",
                    RelatedEntityId: request.Id.ToString()));
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

            // Lazily record agreement-by-non-cancellation if the window has passed (the background sweeper does the same).
            if (FinalizeIfExpired(contract, DateTime.UtcNow))
            {
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException)
                {
                    _db.ChangeTracker.Clear(); // lost a race with a cancel / the sweeper — show what is stored
                    contract = await _db.Contracts.FirstAsync(c => c.PackageRequestId == packageRequestId);
                }
            }

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
            if (contract.ContractSentAt.HasValue && contract.CancellationWindowEndsAt.HasValue)
            {
                data.ContractSentAt = contract.ContractSentAt;
                data.CancellationWindowHours = (int)Math.Round((contract.CancellationWindowEndsAt.Value - contract.ContractSentAt.Value).TotalHours);
            }

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
                await AlertContractDeliveryFailedAsync(contractId, agreementTitle, "the PDF could not be rendered, so neither party was emailed the agreement");
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
                    // The cancellation acknowledgement is addressed to the client only; the caregiver's copy omits the callout.
                    await _emailService.SendContractPdfEmailAsync(
                        email, firstName ?? "there", contractId, agreementTitle, pdfBytes,
                        string.Equals(email, client?.Email, StringComparison.OrdinalIgnoreCase) && data.ContractSentAt.HasValue && data.CancellationWindowHours > 0
                            ? Application.Content.ContractWording.CancellationAcknowledgement(data.CancellationWindowHours, data.ContractSentAt.Value)
                            : null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to email package contract PDF {ContractId} to {Email}", contractId, email);
                    await AlertContractDeliveryFailedAsync(contractId, agreementTitle, $"the agreement email to {email} failed");
                }
            }
        }

        /// <summary>
        /// Records agreement by non-cancellation once the window has ended with no cancellation. The stored
        /// timestamp is the window END (deterministic), not "whenever we noticed". Returns true if it changed anything.
        /// </summary>
        private static bool FinalizeIfExpired(Contract c, DateTime now)
        {
            if (c.PackageRequestId == null || c.ContractSentAt == null || c.CancellationWindowEndsAt == null) return false;
            if (c.CancelledAt != null || c.AcceptedByNonCancellationAt != null) return false;
            if (now < c.CancellationWindowEndsAt.Value) return false;
            c.AcceptedByNonCancellationAt = c.CancellationWindowEndsAt;
            c.UpdatedAt = now;
            return true;
        }

        public async Task<int> FinalizeExpiredWindowsAsync()
        {
            var candidates = (await _db.Contracts
                    .Where(c => c.PackageRequestId != null && c.CancelledAt == null && c.AcceptedByNonCancellationAt == null)
                    .ToListAsync())
                .Where(c => c.CancellationWindowEndsAt != null)
                .ToList();
            var now = DateTime.UtcNow;
            var changed = 0;
            foreach (var c in candidates)
            {
                if (!FinalizeIfExpired(c, now)) continue;
                try { await _db.SaveChangesAsync(); changed++; }
                catch (DbUpdateConcurrencyException)
                {
                    // A concurrent cancel/agree/sweep touched this contract — leave it to that writer.
                    _db.Entry(c).State = EntityState.Detached; // keep the other candidates tracked
                }
            }
            return changed;
        }

        public async Task<PackageContractDTO> CancelByClientAsync(string packageRequestId, string clientId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var prOid)) throw new ArgumentException("Invalid package request id.");
            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == prOid && p.DeletedAt == null)
                ?? throw new KeyNotFoundException("Package request not found.");
            if (!string.Equals(request.ClientId, clientId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This request doesn't belong to you.");

            var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.PackageRequestId == packageRequestId)
                ?? throw new InvalidOperationException("There is no agreement to cancel yet.");

            if (contract.ContractSentAt == null || contract.CancellationWindowEndsAt == null)
                throw new InvalidOperationException("This agreement has no cancellation window.");

            var now = DateTime.UtcNow;
            var wonTheGate = false;

            if (contract.CancelledAt == null)
            {
                if (contract.AcceptedByNonCancellationAt != null || now >= contract.CancellationWindowEndsAt.Value)
                {
                    if (FinalizeIfExpired(contract, now))
                    {
                        try { await _db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { /* someone else recorded it */ }
                    }
                    throw new InvalidOperationException(
                        $"The cancellation window ended on {Application.Content.ContractWording.FormatUtc(contract.CancellationWindowEndsAt.Value)}. This order can no longer be cancelled here.");
                }

                // STEP 1 — the contract is the single arbiter. CancelledAt / AcceptedByNonCancellationAt are
                // optimistic-concurrency tokens, so if the sweeper (or a second cancel) wrote first, this save fails
                // and we never half-cancel.
                contract.Status = ContractStatus.Cancelled;
                contract.CancelledAt = now;
                contract.CancelledBy = $"Client:{clientId}";
                contract.UpdatedAt = now;
                try
                {
                    await _db.SaveChangesAsync();
                    wonTheGate = true;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _db.ChangeTracker.Clear();
                    contract = await _db.Contracts.FirstAsync(c => c.PackageRequestId == packageRequestId);
                    request = await _db.PackageRequests.FirstAsync(p => p.Id == prOid);
                    if (contract.CancelledAt == null)
                        throw new InvalidOperationException(
                            $"The cancellation window ended on {Application.Content.ContractWording.FormatUtc(contract.CancellationWindowEndsAt!.Value)}. This order can no longer be cancelled here.");
                    // else: a concurrent cancel by the same client won — fall through to the idempotent path.
                }
            }
            else if (!string.Equals(contract.CancelledBy, $"Client:{clientId}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("This order has already been cancelled.");
            }

            // STEP 2 — dependants. Idempotent and re-runnable: a repeat/double call (or a retry after a crash between
            // steps) only transitions what is not yet cancelled, and only newly-transitioned rows trigger notifications.
            if (request.Status != PackageRequestStatuses.Cancelled)
            {
                request.Status = PackageRequestStatuses.Cancelled;
                request.UpdatedAt = now;
            }

            var assignments = await _db.Assignments
                .Where(a => a.PackageRequestId == packageRequestId
                            && (a.Status == AssignmentStatuses.Accepted || a.Status == AssignmentStatuses.PendingAcceptance))
                .ToListAsync();
            foreach (var a in assignments)
            {
                a.Status = AssignmentStatuses.Cancelled;
                a.CancelReason = "Cancelled by the client during the cancellation window.";
                a.UpdatedAt = now;
            }

            // Stop future recurring charges (does NOT move money; no refund is automated).
            var subscription = await _db.PackageSubscriptions.FirstOrDefaultAsync(s => s.PackageRequestId == packageRequestId);
            var stoppedSubscription = false;
            if (subscription != null && subscription.Status != SubscriptionStatus.Cancelled && subscription.Status != SubscriptionStatus.Terminated)
            {
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.AutoRenew = false;
                subscription.NextChargeDate = null;
                subscription.CancelAtPeriodEnd = false;
                subscription.CancellationRequestedAt = now;
                subscription.CancellationReason = "Order cancelled by the client within the cancellation window.";
                subscription.CancelledBy = $"Client:{clientId}";
                subscription.TerminatedAt = now;
                subscription.UpdatedAt = now;
                stoppedSubscription = true;
            }

            await _db.SaveChangesAsync();

            // Notifications — caregiver in-app only (no new email type). Only the call that won the contract gate
            // notifies; concurrent/duplicate calls reconcile state silently, so there is exactly one notification.
            foreach (var a in wonTheGate ? assignments : new List<Assignment>())
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: a.CaregiverId,
                    SenderId: "system",
                    Type: NotificationTypes.PackageAssignmentCancelled,
                    Content: "The client cancelled this order during the cancellation window, so the assignment has ended.",
                    Title: "Assignment cancelled by client",
                    RelatedEntityId: a.Id.ToString()));
            }

            // Admin alert — exactly once, by the call that actually won the contract gate.
            if (wonTheGate)
            {
                Client? client = ObjectId.TryParse(clientId, out var cOid) ? await _db.Clients.FirstOrDefaultAsync(c => c.Id == cOid) : null;
                var clientLabel = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "(unknown)";
                var subNote = subscription == null
                    ? "No recurring subscription."
                    : $"Recurring subscription {subscription.Id} existed — future charges were stopped automatically (no money moved).";
                await _opsAlerts.NotifyAdminsAsync(
                    NotificationTypes.PackageRequestCancelledByClient,
                    "Client cancelled within the window — refund decision needed",
                    $"Client {clientLabel} (id {clientId}) cancelled {request.PackageCategory} ({request.PackageTierLabel}) request {request.Id} " +
                    $"within the {_cancellationWindowHours}-hour window. Amount paid: ₦{contract.TotalAmount:N0}. {subNote} " +
                    "No refund has been issued automatically — please review and refund manually if due.",
                    request.Id.ToString());
            }

            _logger.LogInformation("Client {ClientId} cancelled package request {RequestId} (first={First}, subscriptionStopped={Stopped})",
                clientId, packageRequestId, wonTheGate, stoppedSubscription);
            return Map(contract, request, newlyGenerated: false);
        }

        public async Task<PackageContractDTO> AgreeByClientAsync(string packageRequestId, string clientId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var prOid)) throw new ArgumentException("Invalid package request id.");
            var request = await _db.PackageRequests.FirstOrDefaultAsync(p => p.Id == prOid && p.DeletedAt == null)
                ?? throw new KeyNotFoundException("Package request not found.");
            if (!string.Equals(request.ClientId, clientId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This request doesn't belong to you.");
            var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.PackageRequestId == packageRequestId)
                ?? throw new InvalidOperationException("There is no agreement to confirm yet.");
            if (contract.CancelledAt != null)
                throw new InvalidOperationException("This order was cancelled.");

            // Idempotent: a second tick keeps the first timestamp. The tick never touches the window or unblocks care.
            if (contract.ClientAgreedAt == null)
            {
                contract.ClientAgreedAt = DateTime.UtcNow;
                contract.ClientAgreedBy = clientId; // from the JWT, never the body
                contract.UpdatedAt = contract.ClientAgreedAt.Value;
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException)
                {
                    _db.ChangeTracker.Clear(); // a concurrent tick (or cancel) won — report whatever is stored now
                    contract = await _db.Contracts.FirstAsync(c => c.PackageRequestId == packageRequestId);
                    if (contract.CancelledAt != null) throw new InvalidOperationException("This order was cancelled.");
                }
            }
            return Map(contract, request, newlyGenerated: false);
        }

        private Task AlertContractDeliveryFailedAsync(string contractId, string agreementTitle, string reason)
            => _opsAlerts.NotifyAdminsAsync(
                NotificationTypes.PackageContractDeliveryFailed,
                "Package contract email not delivered",
                $"Contract {contractId} ({agreementTitle}) was generated but {reason}. Re-send it manually.",
                contractId,
                alsoEmail: true);

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
            ServiceAddress = c.ServiceAddress,
            ServiceLocationSetByClient = c.ServiceLocationSetByClient,
            ServiceLocationSetAt = c.ServiceLocationSetAt,
            ContractSentAt = c.ContractSentAt,
            CancellationWindowEndsAt = c.CancellationWindowEndsAt,
            CancellationWindowHours = c.ContractSentAt.HasValue && c.CancellationWindowEndsAt.HasValue
                ? (int)Math.Round((c.CancellationWindowEndsAt.Value - c.ContractSentAt.Value).TotalHours) : 0,
            CancellationWindowOpen = c.CancelledAt == null && c.CancellationWindowEndsAt.HasValue && DateTime.UtcNow < c.CancellationWindowEndsAt.Value,
            CancelledAt = c.CancelledAt,
            CancelledBy = c.CancelledBy,
            ClientAgreedAt = c.ClientAgreedAt,
            ClientAgreedBy = c.ClientAgreedBy,
            AcceptedByNonCancellationAt = c.AcceptedByNonCancellationAt,
            CancellationAcknowledgement = c.ContractSentAt.HasValue && c.CancellationWindowEndsAt.HasValue
                && (c.CancellationWindowEndsAt.Value - c.ContractSentAt.Value).TotalMinutes >= 30
                ? Application.Content.ContractWording.CancellationAcknowledgement(
                    (int)Math.Round((c.CancellationWindowEndsAt.Value - c.ContractSentAt.Value).TotalHours), c.ContractSentAt.Value)
                : null,
        };
    }
}
