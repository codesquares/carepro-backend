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

namespace Infrastructure.Content.Services
{
    public class GigPriceNegotiationService : IGigPriceNegotiationService
    {
        private const decimal MinimumPrice = 10_000m;
        private const int MaxClientProposals = 3;
        private const int MaxCaregiverCounters = 3;
        private const int ExpiryHours = 48;

        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly ILogger<GigPriceNegotiationService> _logger;

        public GigPriceNegotiationService(
            CareProDbContext db,
            IMediator mediator,
            IEmailService emailService,
            ILogger<GigPriceNegotiationService> logger)
        {
            _db = db;
            _mediator = mediator;
            _emailService = emailService;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  INITIATION
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<GigPriceNegotiationResponseDTO> InitiateAsync(
            string clientId, string gigId, decimal? proposedPrice, string? note)
        {
            // Validate gig exists and is active
            var gig = await _db.Gigs
                .FirstOrDefaultAsync(g => g.Id == ObjectId.Parse(gigId));

            if (gig == null || gig.IsDeleted == true)
                throw new KeyNotFoundException($"Gig '{gigId}' not found.");

            if (gig.Status?.ToLower() != "active" && gig.Status?.ToLower() != "published")
                throw new InvalidOperationException("This gig is not currently active and cannot be negotiated.");

            // Idempotency: return existing non-terminal negotiation if present
            var existing = await FindActiveNegotiationAsync(clientId, gigId: gigId, careRequestId: null);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Returning existing active negotiation {NegotiationId} for ClientId: {ClientId}, GigId: {GigId}",
                    existing.Id, clientId, gigId);
                return await MapToDTOAsync(existing);
            }

            // Validate proposed price if provided
            if (proposedPrice.HasValue)
                ValidateProposedPrice(proposedPrice.Value, gig.Price, previousClientPrice: null, partyName: "Client");

            var now = DateTime.UtcNow;
            var negotiation = new GigPriceNegotiation
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = clientId,
                CaregiverId = gig.CaregiverId,
                OriginalGigId = gigId,
                EntrySource = "RegularGig",
                GigTitleSnapshot = gig.Title,
                GigCategorySnapshot = gig.Category,
                GigPackageDetailsSnapshot = gig.PackageDetails ?? new List<string>(),
                OriginalGigPrice = gig.Price,
                LatestProposedPrice = proposedPrice ?? gig.Price,
                ProposedBy = proposedPrice.HasValue ? "Client" : "None",
                Status = proposedPrice.HasValue ? GigPriceNegotiationStatus.ClientProposed : GigPriceNegotiationStatus.Initiated,
                Version = 0,
                ExpiresAt = now.AddHours(ExpiryHours),
                CreatedAt = now,
                UpdatedAt = now
            };

            if (proposedPrice.HasValue)
            {
                negotiation.LastClientPrice = proposedPrice.Value;
                negotiation.ClientProposalCount = 1;
                AddHistoryEntry(negotiation, proposedPrice.Value, "Client", "Propose", note);
            }

            await _db.GigPriceNegotiations.AddAsync(negotiation);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Negotiation {NegotiationId} created. ClientId: {ClientId}, GigId: {GigId}, Status: {Status}",
                negotiation.Id, clientId, gigId, negotiation.Status);

            // Notify caregiver if a proposal was included
            if (proposedPrice.HasValue)
                await NotifyCaregiverOfProposalAsync(negotiation, gig.CaregiverId, clientId, proposedPrice.Value);

            return await MapToDTOAsync(negotiation);
        }

        public async Task<GigPriceNegotiationResponseDTO> InitiateFromCareRequestHireAsync(
            string clientId,
            string caregiverId,
            string careRequestId,
            string responseId,
            decimal caregiverProposedRate,
            string gigTitleSnapshot,
            string gigCategorySnapshot,
            List<string> gigPackageDetailsSnapshot)
        {
            // Idempotency: return existing non-terminal negotiation for this hire pair
            var existing = await FindActiveNegotiationAsync(
                clientId, gigId: null, careRequestId: careRequestId);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Returning existing hire negotiation {NegotiationId} for ClientId: {ClientId}, CareRequestId: {CareRequestId}",
                    existing.Id, clientId, careRequestId);
                return await MapToDTOAsync(existing);
            }

            var now = DateTime.UtcNow;
            var negotiation = new GigPriceNegotiation
            {
                Id = ObjectId.GenerateNewId(),
                ClientId = clientId,
                CaregiverId = caregiverId,
                OriginalGigId = null,
                CareRequestId = careRequestId,
                CareRequestResponseId = responseId,
                EntrySource = "CareRequestHire",
                GigTitleSnapshot = gigTitleSnapshot,
                GigCategorySnapshot = gigCategorySnapshot,
                GigPackageDetailsSnapshot = gigPackageDetailsSnapshot,
                OriginalGigPrice = caregiverProposedRate,
                LatestProposedPrice = caregiverProposedRate,
                ProposedBy = "Caregiver",
                LastCaregiverPrice = caregiverProposedRate,
                CaregiverCounterCount = 0, // Initial proposal from hire doesn't count as a counter
                Status = GigPriceNegotiationStatus.Initiated,
                Version = 0,
                ExpiresAt = now.AddHours(ExpiryHours),
                CreatedAt = now,
                UpdatedAt = now
            };

            AddHistoryEntry(negotiation, caregiverProposedRate, "Caregiver", "Propose", note: null);

            await _db.GigPriceNegotiations.AddAsync(negotiation);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "CareRequestHire negotiation {NegotiationId} created. ClientId: {ClientId}, CareRequestId: {CareRequestId}",
                negotiation.Id, clientId, careRequestId);

            return await MapToDTOAsync(negotiation);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  READ
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<GigPriceNegotiationResponseDTO> GetNegotiationByIdAsync(
            string negotiationId, string requestingUserId)
        {
            var negotiation = await FindByIdAsync(negotiationId);
            EnsureAccessAuthorized(negotiation, requestingUserId);
            return await MapToDTOAsync(negotiation);
        }

        public async Task<GigPriceNegotiationResponseDTO?> GetNegotiationByGigAsync(
            string clientId, string gigId)
        {
            var negotiation = await FindActiveNegotiationAsync(clientId, gigId: gigId, careRequestId: null);
            if (negotiation == null) return null;
            return await MapToDTOAsync(negotiation);
        }

        public async Task<PaginatedNegotiationListDTO> GetPendingNegotiationsForCaregiverAsync(
            string caregiverId, int page, int pageSize)
        {
            var pendingStatuses = new[]
            {
                GigPriceNegotiationStatus.Initiated,
                GigPriceNegotiationStatus.ClientProposed,
                GigPriceNegotiationStatus.CaregiverCountered
            };

            var query = _db.GigPriceNegotiations
                .Where(n => n.CaregiverId == caregiverId && pendingStatuses.Contains(n.Status));

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(n => n.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var summaries = new List<NegotiationSummaryDTO>();
            foreach (var item in items)
            {
                var clientName = await GetUserNameAsync(item.ClientId, isCaregiver: false);
                var clientImage = await GetUserProfileImageAsync(item.ClientId, isCaregiver: false);
                summaries.Add(new NegotiationSummaryDTO
                {
                    NegotiationId = item.Id.ToString(),
                    ClientId = item.ClientId,
                    ClientName = clientName,
                    ClientProfileImage = clientImage,
                    GigTitle = item.GigTitleSnapshot,
                    OriginalPrice = item.OriginalGigPrice,
                    CurrentProposedPrice = item.LatestProposedPrice,
                    ProposedBy = item.ProposedBy,
                    Status = item.Status.ToString(),
                    EntrySource = item.EntrySource,
                    UpdatedAt = item.UpdatedAt,
                    ExpiresAt = item.ExpiresAt
                });
            }

            return new PaginatedNegotiationListDTO
            {
                Items = summaries,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)total / pageSize)
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CLIENT ACCEPT
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<NegotiationAgreementResultDTO> ClientAcceptAsync(
            string clientId, string negotiationId, long version)
        {
            var negotiation = await FindByIdAsync(negotiationId);
            EnsureAccessAuthorized(negotiation, clientId, mustBeClient: true);
            EnsureNotTerminal(negotiation);
            EnsureVersionMatch(negotiation, version);

            // For RegularGig: client accepting means pay at original price → no special gig needed
            // For CareRequestHire: client accepts caregiver's rate → create special gig
            string gigIdForPayment;

            if (negotiation.EntrySource == "RegularGig")
            {
                // No special gig needed — original gig used for payment
                gigIdForPayment = negotiation.OriginalGigId!;
            }
            else
            {
                // CareRequestHire — must create special gig now
                gigIdForPayment = await CreateSpecialGigOnAgreementAsync(negotiation);
                negotiation.SpecialGigId = gigIdForPayment;
            }

            var agreedPrice = negotiation.LatestProposedPrice;
            negotiation.AgreedPrice = agreedPrice;
            negotiation.Status = GigPriceNegotiationStatus.Agreed;
            negotiation.AgreedAt = DateTime.UtcNow;
            AddHistoryEntry(negotiation, agreedPrice, "Client", "Accept", note: null);
            BumpVersion(negotiation);

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Negotiation {NegotiationId} agreed by client. AgreedPrice: {Price}, GigIdForPayment: {GigId}",
                negotiation.Id, agreedPrice, gigIdForPayment);

            // Notify caregiver of agreement — RelatedEntityId = negotiationId so the
            // caregiver can deep-link to the negotiation summary screen (they have no payment action).
            await _mediator.Send(new SendNotificationCommand(
                RecipientId: negotiation.CaregiverId,
                SenderId: clientId,
                Type: NotificationTypes.PriceNegotiationAgreed,
                Content: $"The client accepted the price of ₦{agreedPrice:N0} per visit for \"{negotiation.GigTitleSnapshot}\".",
                Title: "Price Agreement Reached",
                RelatedEntityId: negotiation.Id.ToString()));

            var commitmentFee = negotiation.EntrySource == "RegularGig" ? 5000m : 0m;

            return new NegotiationAgreementResultDTO
            {
                NegotiationId = negotiationId,
                AgreedPrice = agreedPrice,
                GigIdForPayment = gigIdForPayment,
                EntrySource = negotiation.EntrySource,
                CommitmentFeeDeductedAtCheckout = commitmentFee,
                Message = $"Price agreed at ₦{agreedPrice:N0} per visit. Proceed to payment."
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CLIENT PROPOSE
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<GigPriceNegotiationResponseDTO> ClientProposeAsync(
            string clientId, string negotiationId, decimal proposedPrice, string? note, long version)
        {
            var negotiation = await FindByIdAsync(negotiationId);
            EnsureAccessAuthorized(negotiation, clientId, mustBeClient: true);
            EnsureNotTerminal(negotiation);
            EnsureVersionMatch(negotiation, version);

            // Must be client's turn
            if (negotiation.Status == GigPriceNegotiationStatus.ClientProposed)
                throw new InvalidOperationException("Awaiting caregiver's response. You cannot propose again until the caregiver responds.");

            // Round limit
            if (negotiation.ClientProposalCount >= MaxClientProposals)
                throw new InvalidOperationException(
                    $"You have reached the maximum of {MaxClientProposals} proposals. You may only accept or reject the current price.");

            // Price rules
            ValidateProposedPrice(proposedPrice, negotiation.OriginalGigPrice, negotiation.LastClientPrice, "Client");

            negotiation.LatestProposedPrice = proposedPrice;
            negotiation.ProposedBy = "Client";
            negotiation.LastClientPrice = proposedPrice;
            negotiation.ClientProposalCount++;
            negotiation.Status = GigPriceNegotiationStatus.ClientProposed;
            AddHistoryEntry(negotiation, proposedPrice, "Client", "Counter", note);
            ResetExpiry(negotiation);
            BumpVersion(negotiation);

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Client {ClientId} proposed ₦{Price} on negotiation {NegotiationId}",
                clientId, proposedPrice, negotiationId);

            await NotifyCaregiverOfProposalAsync(negotiation, negotiation.CaregiverId, clientId, proposedPrice);

            return await MapToDTOAsync(negotiation);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CAREGIVER RESPOND
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<GigPriceNegotiationResponseDTO> CaregiverRespondAsync(
            string caregiverId, string negotiationId, bool accept, decimal? counterPrice, string? note, long version)
        {
            var negotiation = await FindByIdAsync(negotiationId);
            EnsureAccessAuthorized(negotiation, caregiverId, mustBeCaregiver: true);
            EnsureNotTerminal(negotiation);
            EnsureVersionMatch(negotiation, version);

            // Must be caregiver's turn
            if (negotiation.Status == GigPriceNegotiationStatus.CaregiverCountered)
                throw new InvalidOperationException("Awaiting the client's response. You cannot counter again until the client responds.");

            if (negotiation.Status != GigPriceNegotiationStatus.ClientProposed
                && !(negotiation.EntrySource == "CareRequestHire" && negotiation.Status == GigPriceNegotiationStatus.Initiated))
            {
                throw new InvalidOperationException("There is no pending client proposal for you to respond to.");
            }

            if (accept)
            {
                // Caregiver accepts the client's proposed price
                var agreedPrice = negotiation.LatestProposedPrice;
                var gigIdForPayment = await CreateSpecialGigOnAgreementAsync(negotiation);
                negotiation.SpecialGigId = gigIdForPayment;
                negotiation.AgreedPrice = agreedPrice;
                negotiation.Status = GigPriceNegotiationStatus.Agreed;
                negotiation.AgreedAt = DateTime.UtcNow;
                AddHistoryEntry(negotiation, agreedPrice, "Caregiver", "Accept", note);
                BumpVersion(negotiation);

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Caregiver {CaregiverId} accepted ₦{Price} on negotiation {NegotiationId}. SpecialGigId: {SpecialGigId}",
                    caregiverId, agreedPrice, negotiationId, gigIdForPayment);

                var commitmentFee = negotiation.EntrySource == "RegularGig" ? 5000m : 0m;

                // Notify client — include GigIdForPayment so frontend can redirect to payment
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: negotiation.ClientId,
                    SenderId: caregiverId,
                    Type: NotificationTypes.PriceNegotiationAgreed,
                    Content: $"The caregiver agreed to ₦{agreedPrice:N0} per visit for \"{negotiation.GigTitleSnapshot}\". Tap to proceed to payment.",
                    Title: "Price Agreement — Proceed to Payment",
                    RelatedEntityId: gigIdForPayment));
            }
            else
            {
                // Caregiver counter-proposes
                if (!counterPrice.HasValue)
                    throw new ArgumentException("CounterPrice is required when Accept is false.");

                // Round limit
                if (negotiation.CaregiverCounterCount >= MaxCaregiverCounters)
                    throw new InvalidOperationException(
                        $"You have reached the maximum of {MaxCaregiverCounters} counter-proposals. You may only accept or reject the current price.");

                // Price rules
                //  - must be >= minimum
                //  - must be <= original gig price (no upward beyond original)
                //  - must be > client's latest proposal (otherwise just accept)
                //  - must be <= caregiver's own previous counter (no retreating upward)
                if (counterPrice.Value < MinimumPrice)
                    throw new ArgumentException($"Counter price must be at least ₦{MinimumPrice:N0}.");

                if (counterPrice.Value > negotiation.OriginalGigPrice)
                    throw new ArgumentException(
                        $"Counter price cannot exceed the original gig price of ₦{negotiation.OriginalGigPrice:N0}.");

                if (counterPrice.Value <= negotiation.LatestProposedPrice)
                    throw new ArgumentException(
                        $"Your counter price (₦{counterPrice.Value:N0}) must be higher than the client's proposed price (₦{negotiation.LatestProposedPrice:N0}). If you agree with the client's price, use Accept instead.");

                if (negotiation.LastCaregiverPrice.HasValue && counterPrice.Value > negotiation.LastCaregiverPrice.Value)
                    throw new ArgumentException(
                        $"Your counter price (₦{counterPrice.Value:N0}) cannot be higher than your previous counter of ₦{negotiation.LastCaregiverPrice.Value:N0}.");

                negotiation.LatestProposedPrice = counterPrice.Value;
                negotiation.ProposedBy = "Caregiver";
                negotiation.LastCaregiverPrice = counterPrice.Value;
                negotiation.CaregiverCounterCount++;
                negotiation.Status = GigPriceNegotiationStatus.CaregiverCountered;
                AddHistoryEntry(negotiation, counterPrice.Value, "Caregiver", "Counter", note);
                ResetExpiry(negotiation);
                BumpVersion(negotiation);

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Caregiver {CaregiverId} countered with ₦{Price} on negotiation {NegotiationId}",
                    caregiverId, counterPrice.Value, negotiationId);

                // Notify client
                await NotifyClientOfCounterAsync(negotiation, negotiation.ClientId, caregiverId, counterPrice.Value);
            }

            return await MapToDTOAsync(negotiation);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  REJECTION
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<GigPriceNegotiationResponseDTO> RejectAsync(
            string userId, string userRole, string negotiationId, string? reason, long version)
        {
            var negotiation = await FindByIdAsync(negotiationId);
            EnsureAccessAuthorized(negotiation, userId);
            EnsureNotTerminal(negotiation);
            EnsureVersionMatch(negotiation, version);

            negotiation.Status = GigPriceNegotiationStatus.Rejected;
            negotiation.RejectedBy = userRole;
            negotiation.RejectionReason = reason;
            negotiation.RejectedAt = DateTime.UtcNow;
            AddHistoryEntry(negotiation, negotiation.LatestProposedPrice, userRole, "Reject", reason);
            BumpVersion(negotiation);

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Negotiation {NegotiationId} rejected by {UserRole} ({UserId})",
                negotiationId, userRole, userId);

            // Notify the other party
            var otherPartyId = userRole == "Client" ? negotiation.CaregiverId : negotiation.ClientId;
            var actorLabel = userRole == "Client" ? "The client" : "The caregiver";

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: otherPartyId,
                SenderId: userId,
                Type: NotificationTypes.PriceNegotiationRejected,
                Content: $"{actorLabel} rejected the price negotiation for \"{negotiation.GigTitleSnapshot}\"." +
                         (string.IsNullOrEmpty(reason) ? string.Empty : $" Reason: {reason}"),
                Title: "Price Negotiation Rejected",
                RelatedEntityId: negotiationId));

            return await MapToDTOAsync(negotiation);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  RE-INITIATION (CareRequestHire path after rejection / expiry)
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<GigPriceNegotiationResponseDTO> ReinitiateFromCareRequestAsync(
            string clientId, string careRequestId, string responseId)
        {
            // Idempotency first: return any existing non-terminal negotiation for this pair
            var existing = await FindActiveNegotiationAsync(clientId, gigId: null, careRequestId: careRequestId);
            if (existing != null)
            {
                _logger.LogInformation(
                    "ReinitiateFromCareRequestAsync: returning existing active negotiation {NegotiationId}",
                    existing.Id);
                return await MapToDTOAsync(existing);
            }

            // Load the care request and validate ownership
            if (!ObjectId.TryParse(careRequestId, out var careRequestOid))
                throw new ArgumentException("Invalid care request ID format.");

            var careRequest = await _db.CareRequests.FindAsync(careRequestOid);
            if (careRequest == null)
                throw new KeyNotFoundException("Care request not found.");

            if (careRequest.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorised to re-initiate a negotiation for this care request.");

            // Load the response and validate it belongs to this care request and is hired
            if (!ObjectId.TryParse(responseId, out var responseOid))
                throw new ArgumentException("Invalid response ID format.");

            var response = await _db.CareRequestResponses.FindAsync(responseOid);
            if (response == null || response.CareRequestId != careRequestId)
                throw new KeyNotFoundException("Care request response not found.");

            if (response.Status != "hired")
                throw new InvalidOperationException(
                    "This caregiver has not been hired for the care request. Cannot re-initiate negotiation.");

            // Determine opening rate (same logic as the original hire)
            var openingRate = response.ProposedRate.HasValue ? response.ProposedRate.Value
                            : careRequest.BudgetMax.HasValue ? careRequest.BudgetMax.Value
                            : careRequest.BudgetMin.HasValue ? careRequest.BudgetMin.Value
                            : 10_000m;

            _logger.LogInformation(
                "Reinitiating CareRequest negotiation. ClientId: {ClientId}, CareRequestId: {CareRequestId}, " +
                "ResponseId: {ResponseId}, OpeningRate: {Rate}",
                clientId, careRequestId, responseId, openingRate);

            // Delegate to the standard initiation method (which creates a fresh record)
            return await InitiateFromCareRequestHireAsync(
                clientId: clientId,
                caregiverId: response.CaregiverId,
                careRequestId: careRequestId,
                responseId: responseId,
                caregiverProposedRate: openingRate,
                gigTitleSnapshot: careRequest.Title,
                gigCategorySnapshot: careRequest.ServiceCategory,
                gigPackageDetailsSnapshot: careRequest.Tasks ?? new List<string>());
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        private async Task<GigPriceNegotiation> FindByIdAsync(string negotiationId)
        {
            if (!ObjectId.TryParse(negotiationId, out var oid))
                throw new ArgumentException("Invalid negotiation ID format.");

            var record = await _db.GigPriceNegotiations.FindAsync(oid);
            if (record == null)
                throw new KeyNotFoundException($"Negotiation '{negotiationId}' not found.");

            return record;
        }

        private async Task<GigPriceNegotiation?> FindActiveNegotiationAsync(
            string clientId, string? gigId, string? careRequestId)
        {
            var terminalStatuses = new[]
            {
                GigPriceNegotiationStatus.Agreed,
                GigPriceNegotiationStatus.Rejected,
                GigPriceNegotiationStatus.Expired
            };

            if (gigId != null)
            {
                return await _db.GigPriceNegotiations
                    .FirstOrDefaultAsync(n =>
                        n.ClientId == clientId &&
                        n.OriginalGigId == gigId &&
                        !terminalStatuses.Contains(n.Status));
            }

            if (careRequestId != null)
            {
                return await _db.GigPriceNegotiations
                    .FirstOrDefaultAsync(n =>
                        n.ClientId == clientId &&
                        n.CareRequestId == careRequestId &&
                        !terminalStatuses.Contains(n.Status));
            }

            return null;
        }

        private static void EnsureAccessAuthorized(
            GigPriceNegotiation n, string userId,
            bool mustBeClient = false, bool mustBeCaregiver = false)
        {
            var isClient = n.ClientId == userId;
            var isCaregiver = n.CaregiverId == userId;

            if (mustBeClient && !isClient)
                throw new UnauthorizedAccessException("Only the client can perform this action.");

            if (mustBeCaregiver && !isCaregiver)
                throw new UnauthorizedAccessException("Only the caregiver can perform this action.");

            if (!isClient && !isCaregiver)
                throw new UnauthorizedAccessException("You are not authorised to access this negotiation.");
        }

        private static void EnsureNotTerminal(GigPriceNegotiation n)
        {
            var terminal = new[]
            {
                GigPriceNegotiationStatus.Agreed,
                GigPriceNegotiationStatus.Rejected,
                GigPriceNegotiationStatus.Expired
            };

            if (terminal.Contains(n.Status))
                throw new InvalidOperationException(
                    $"This negotiation is already in a terminal state: {n.Status}. No further actions are allowed.");
        }

        private static void EnsureVersionMatch(GigPriceNegotiation n, long version)
        {
            if (n.Version != version)
                throw new InvalidOperationException(
                    $"CONCURRENCY_CONFLICT: The negotiation has been updated by another request. " +
                    $"Expected version {version}, found {n.Version}. Please refresh and try again.");
        }

        private static void ValidateProposedPrice(
            decimal price, decimal originalPrice, decimal? previousClientPrice, string partyName)
        {
            if (price < MinimumPrice)
                throw new ArgumentException(
                    $"Proposed price must be at least ₦{MinimumPrice:N0}. All prices are per-visit rates.");

            if (price > originalPrice)
                throw new ArgumentException(
                    $"Proposed price (₦{price:N0}) cannot exceed the original gig price of ₦{originalPrice:N0}. " +
                    "Upward negotiation is not allowed.");

            if (previousClientPrice.HasValue && price > previousClientPrice.Value)
                throw new ArgumentException(
                    $"Your new proposal (₦{price:N0}) cannot be higher than your previous proposal of ₦{previousClientPrice.Value:N0}. " +
                    "You cannot negotiate upward.");
        }

        private static void AddHistoryEntry(
            GigPriceNegotiation n, decimal price, string proposedBy, string actionType, string? note)
        {
            var round = n.History.Count + 1;
            n.History.Add(new NegotiationRoundEntry
            {
                RoundNumber = round,
                Price = price,
                ProposedBy = proposedBy,
                ActionType = actionType,
                Note = note,
                Timestamp = DateTime.UtcNow
            });
        }

        private static void ResetExpiry(GigPriceNegotiation n)
        {
            n.ExpiresAt = DateTime.UtcNow.AddHours(ExpiryHours);
            n.UpdatedAt = DateTime.UtcNow;
        }

        private static void BumpVersion(GigPriceNegotiation n)
        {
            n.Version++;
            n.UpdatedAt = DateTime.UtcNow;
        }

        private async Task<string> CreateSpecialGigOnAgreementAsync(GigPriceNegotiation negotiation)
        {
            // Guard: ensure caregiver account is still active
            if (!ObjectId.TryParse(negotiation.CaregiverId, out var caregiverOid))
                throw new InvalidOperationException("Invalid caregiver ID on negotiation record.");

            var caregiver = await _db.CareGivers.FindAsync(caregiverOid);
            if (caregiver == null || caregiver.IsDeleted)
                throw new InvalidOperationException(
                    "The caregiver's account is no longer active. Cannot create the booking.");

            // Profile image: caregiver profile → fallback to one of their gig images
            string? gigImage = caregiver.ProfileImage;
            if (string.IsNullOrEmpty(gigImage))
            {
                gigImage = await _db.Gigs
                    .Where(g => g.CaregiverId == negotiation.CaregiverId
                             && g.IsDeleted != true
                             && g.Image1 != null)
                    .Select(g => g.Image1)
                    .FirstOrDefaultAsync();
            }

            var agreedPrice = negotiation.AgreedPrice ?? negotiation.LatestProposedPrice;

            var specialGig = new Gig
            {
                Id = ObjectId.GenerateNewId(),
                Title = $"[Negotiated] {negotiation.GigTitleSnapshot}",
                Category = negotiation.GigCategorySnapshot,
                SubCategory = "Negotiated",
                Tags = string.Join(", ", negotiation.GigPackageDetailsSnapshot),
                PackageType = "Negotiated",
                PackageName = "Negotiated Care Package",
                PackageDetails = negotiation.GigPackageDetailsSnapshot,
                DeliveryTime = "As agreed",
                Price = (int)agreedPrice,
                Image1 = gigImage,
                Status = "Active",
                CaregiverId = negotiation.CaregiverId,
                CreatedAt = DateTime.UtcNow,
                IsSpecialGig = true,
                ScopedClientId = negotiation.ClientId,
                OriginalGigId = negotiation.OriginalGigId,       // null for CareRequestHire
                CareRequestId = negotiation.CareRequestId,        // null for RegularGig
                CareRequestResponseId = negotiation.CareRequestResponseId // null for RegularGig
            };

            await _db.Gigs.AddAsync(specialGig);
            // Note: SaveChangesAsync is called by the calling method after updating the negotiation

            _logger.LogInformation(
                "Special gig {SpecialGigId} created for negotiation {NegotiationId}. AgreedPrice: ₦{Price}",
                specialGig.Id, negotiation.Id, agreedPrice);

            return specialGig.Id.ToString();
        }

        private async Task NotifyCaregiverOfProposalAsync(
            GigPriceNegotiation negotiation, string caregiverId, string clientId, decimal proposedPrice)
        {
            var clientName = await GetUserNameAsync(clientId, isCaregiver: false);

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: caregiverId,
                SenderId: clientId,
                Type: NotificationTypes.PriceNegotiationOfferReceived,
                Content: $"{clientName} proposed ₦{proposedPrice:N0} per visit for \"{negotiation.GigTitleSnapshot}\". Note: this is a per-visit rate — the total depends on service type and visit frequency.",
                Title: "New Price Proposal",
                RelatedEntityId: negotiation.Id.ToString()));

            try
            {
                var caregiver = await _db.CareGivers.FindAsync(ObjectId.Parse(caregiverId));
                if (caregiver != null)
                {
                    var subject = $"New price proposal for \"{negotiation.GigTitleSnapshot}\"";
                    var html = $@"
                        <h3>New Price Proposal</h3>
                        <p>Hello {caregiver.FirstName},</p>
                        <p><strong>{clientName}</strong> has proposed a price for your gig <strong>{negotiation.GigTitleSnapshot}</strong>.</p>
                        <div style='background:#f8f9fa;padding:15px;border-radius:5px;margin:20px 0;'>
                            <p><strong>Proposed per-visit price:</strong> ₦{proposedPrice:N0}</p>
                            <p><strong>Your original listed price:</strong> ₦{negotiation.OriginalGigPrice:N0}</p>
                        </div>
                        <p style='color:#666;font-size:13px;'>Note: all prices are per-visit rates. The total amount the client pays depends on their chosen service type (one-time or monthly) and visit frequency, which they select at checkout.</p>
                        <p>Log in to review and respond.</p>
                        <p>— The CarePro Team</p>";
                    await _emailService.SendGenericNotificationEmailAsync(caregiver.Email, caregiver.FirstName, subject, html);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send proposal email for negotiation {NegotiationId}", negotiation.Id);
            }
        }

        private async Task NotifyClientOfCounterAsync(
            GigPriceNegotiation negotiation, string clientId, string caregiverId, decimal counterPrice)
        {
            var caregiverName = await GetUserNameAsync(caregiverId, isCaregiver: true);

            await _mediator.Send(new SendNotificationCommand(
                RecipientId: clientId,
                SenderId: caregiverId,
                Type: NotificationTypes.PriceNegotiationCounterReceived,
                Content: $"{caregiverName} countered with ₦{counterPrice:N0} per visit for \"{negotiation.GigTitleSnapshot}\". Note: this is a per-visit rate.",
                Title: "Caregiver Counter-Proposal",
                RelatedEntityId: negotiation.Id.ToString()));

            try
            {
                if (ObjectId.TryParse(clientId, out var clientOid))
                {
                    var client = await _db.Clients.FindAsync(clientOid);
                    if (client != null)
                    {
                        var subject = $"Counter-proposal for \"{negotiation.GigTitleSnapshot}\"";
                        var html = $@"
                            <h3>Caregiver Counter-Proposal</h3>
                            <p>Hello {client.FirstName},</p>
                            <p><strong>{caregiverName}</strong> has responded to your price proposal for <strong>{negotiation.GigTitleSnapshot}</strong>.</p>
                            <div style='background:#f8f9fa;padding:15px;border-radius:5px;margin:20px 0;'>
                                <p><strong>Caregiver counter-price (per visit):</strong> ₦{counterPrice:N0}</p>
                                <p><strong>Your last proposed price:</strong> ₦{negotiation.LastClientPrice:N0}</p>
                                <p><strong>Original gig price:</strong> ₦{negotiation.OriginalGigPrice:N0}</p>
                            </div>
                            <p style='color:#666;font-size:13px;'>Note: all prices are per-visit rates. Your total at checkout depends on your chosen service type (one-time or monthly) and visit frequency.</p>
                            <p>Log in to accept, counter-propose, or reject.</p>
                            <p>— The CarePro Team</p>";
                        await _emailService.SendGenericNotificationEmailAsync(client.Email, client.FirstName, subject, html);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send counter email for negotiation {NegotiationId}", negotiation.Id);
            }
        }

        private async Task<string> GetUserNameAsync(string userId, bool isCaregiver)
        {
            if (!ObjectId.TryParse(userId, out var oid)) return "Unknown";

            if (isCaregiver)
            {
                var cg = await _db.CareGivers.FindAsync(oid);
                return cg != null ? $"{cg.FirstName} {cg.LastName}".Trim() : "Unknown Caregiver";
            }
            else
            {
                var cl = await _db.Clients.FindAsync(oid);
                return cl != null ? $"{cl.FirstName} {cl.LastName}".Trim() : "Unknown Client";
            }
        }

        private async Task<string?> GetUserProfileImageAsync(string userId, bool isCaregiver)
        {
            if (!ObjectId.TryParse(userId, out var oid)) return null;

            if (isCaregiver)
            {
                var cg = await _db.CareGivers.FindAsync(oid);
                return cg?.ProfileImage;
            }
            else
            {
                var cl = await _db.Clients.FindAsync(oid);
                return cl?.ProfileImage;
            }
        }

        private async Task<GigPriceNegotiationResponseDTO> MapToDTOAsync(GigPriceNegotiation n)
        {
            var caregiverName = await GetUserNameAsync(n.CaregiverId, isCaregiver: true);
            var caregiverImage = await GetUserProfileImageAsync(n.CaregiverId, isCaregiver: true);
            var clientName = await GetUserNameAsync(n.ClientId, isCaregiver: false);
            var clientImage = await GetUserProfileImageAsync(n.ClientId, isCaregiver: false);

            var isClientsTurn =
                n.Status == GigPriceNegotiationStatus.Initiated ||
                n.Status == GigPriceNegotiationStatus.CaregiverCountered;

            var canClientPropose =
                isClientsTurn &&
                n.ClientProposalCount < MaxClientProposals &&
                n.Status != GigPriceNegotiationStatus.Agreed &&
                n.Status != GigPriceNegotiationStatus.Rejected &&
                n.Status != GigPriceNegotiationStatus.Expired;

            // For CareRequestHire Initiated state: caregiver action is to wait for client first
            var caregiverTurn = n.Status == GigPriceNegotiationStatus.ClientProposed;
            var canCaregiverCounter =
                caregiverTurn &&
                n.CaregiverCounterCount < MaxCaregiverCounters;

            // Compute GigIdForPayment (non-null only when Agreed)
            string? gigIdForPayment = null;
            if (n.Status == GigPriceNegotiationStatus.Agreed)
            {
                gigIdForPayment = n.SpecialGigId ?? n.OriginalGigId;
            }

            var commitmentFee = n.EntrySource == "RegularGig" ? 5000m : 0m;

            string? commitmentFeeReminderMessage = null;
            if (n.Status == GigPriceNegotiationStatus.Agreed && n.EntrySource == "RegularGig")
            {
                commitmentFeeReminderMessage =
                    $"Your ₦5,000 commitment fee will be deducted from your payment total at checkout.";
            }

            return new GigPriceNegotiationResponseDTO
            {
                NegotiationId = n.Id.ToString(),
                Status = n.Status.ToString(),
                EntrySource = n.EntrySource,
                GigDetails = new NegotiationGigDetailsDTO
                {
                    Title = n.GigTitleSnapshot,
                    Category = n.GigCategorySnapshot,
                    PackageDetails = n.GigPackageDetailsSnapshot
                },
                CaregiverInfo = new NegotiationPartyInfoDTO
                {
                    UserId = n.CaregiverId,
                    Name = caregiverName,
                    ProfileImage = caregiverImage
                },
                ClientInfo = new NegotiationPartyInfoDTO
                {
                    UserId = n.ClientId,
                    Name = clientName,
                    ProfileImage = clientImage
                },
                OriginalPrice = n.OriginalGigPrice,
                CurrentProposedPrice = n.LatestProposedPrice,
                ProposedBy = n.ProposedBy,
                AgreedPrice = n.AgreedPrice,
                GigIdForPayment = gigIdForPayment,
                ClientProposalCount = n.ClientProposalCount,
                ClientMaxProposals = MaxClientProposals,
                CanClientPropose = canClientPropose,
                CaregiverCounterCount = n.CaregiverCounterCount,
                CaregiverMaxCounters = MaxCaregiverCounters,
                CanCaregiverCounter = canCaregiverCounter,
                IsClientsTurn = isClientsTurn,
                CommitmentFeeDeductedAtCheckout = commitmentFee,
                CommitmentFeeReminderMessage = commitmentFeeReminderMessage,
                History = n.History.Select(h => new NegotiationRoundEntryDTO
                {
                    RoundNumber = h.RoundNumber,
                    Price = h.Price,
                    ProposedBy = h.ProposedBy,
                    ActionType = h.ActionType,
                    Note = h.Note,
                    Timestamp = h.Timestamp
                }).ToList(),
                Version = n.Version,
                ExpiresAt = n.ExpiresAt,
                RejectedBy = n.RejectedBy,
                RejectionReason = n.RejectionReason,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
                AgreedAt = n.AgreedAt,
                ExpiredAt = n.ExpiredAt
            };
        }
    }
}
