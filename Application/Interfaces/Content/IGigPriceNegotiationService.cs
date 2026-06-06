using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Manages pre-payment price negotiation between a client and a caregiver.
    ///
    /// Two entry paths:
    ///   1. RegularGig — client paid the ₦5,000 commitment fee for a public gig and wants to
    ///      negotiate the per-visit price before proceeding to full payment.
    ///   2. CareRequestHire — client hired a caregiver via the CareRequest system. The caregiver
    ///      proposed a rate when responding; the client now negotiates or accepts that rate.
    ///
    /// All prices are per-visit rates. Service type (one-time / monthly) and visit frequency
    /// are selected freely by the client at checkout, AFTER price agreement.
    /// </summary>
    public interface IGigPriceNegotiationService
    {
        // ── Initiation ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a price negotiation for the RegularGig path.
        /// Called when a client with a valid completed commitment decides to negotiate the gig price.
        /// If a non-terminal negotiation already exists for this clientId+gigId pair, the existing
        /// record is returned (idempotent).
        /// If proposedPrice is provided, Status is set to ClientProposed and the caregiver is notified.
        /// If proposedPrice is null, Status is set to Initiated (client is viewing the offer component).
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> InitiateAsync(string clientId, string gigId, decimal? proposedPrice, string? note);

        /// <summary>
        /// Creates a price negotiation for the CareRequestHire path.
        /// Called by CareRequestResponseService.HireResponderAsync() when a client hires a responder.
        /// Sets EntrySource = "CareRequestHire", Status = Initiated, LatestProposedPrice = caregiverProposedRate.
        /// No special gig is created yet — that happens when both parties agree on a price.
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> InitiateFromCareRequestHireAsync(
            string clientId,
            string caregiverId,
            string careRequestId,
            string responseId,
            decimal caregiverProposedRate,
            string gigTitleSnapshot,
            string gigCategorySnapshot,
            List<string> gigPackageDetailsSnapshot);

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the full negotiation state used to render the Offer Component on the frontend.
        /// IDOR check: requestingUserId must be the ClientId or CaregiverId on the record.
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> GetNegotiationByIdAsync(string negotiationId, string requestingUserId);

        /// <summary>
        /// Finds the active (non-terminal) negotiation for a clientId+gigId pair.
        /// Used by the frontend to resume an existing negotiation from the gig detail page.
        /// Returns null if no active negotiation exists.
        /// </summary>
        Task<GigPriceNegotiationResponseDTO?> GetNegotiationByGigAsync(string clientId, string gigId);

        /// <summary>
        /// Returns all negotiations in a pending state (Initiated, ClientProposed, CaregiverCountered)
        /// for a given caregiver, paged. Used for the caregiver's dashboard/pending negotiation list.
        /// </summary>
        Task<PaginatedNegotiationListDTO> GetPendingNegotiationsForCaregiverAsync(string caregiverId, int page, int pageSize);

        // ── Client Actions ────────────────────────────────────────────────────

        /// <summary>
        /// Client accepts the current price (either the original gig price or the caregiver's latest counter).
        /// For RegularGig path: marks Status = Agreed, AgreedPrice = LatestProposedPrice.
        ///   No special gig created — GigIdForPayment = OriginalGigId.
        /// For CareRequestHire path: creates the special gig, sets SpecialGigId, marks Status = Agreed.
        ///   GigIdForPayment = SpecialGigId.
        /// Notifies caregiver.
        /// Version must match the stored Version (optimistic concurrency).
        /// </summary>
        Task<NegotiationAgreementResultDTO> ClientAcceptAsync(string clientId, string negotiationId, long version);

        /// <summary>
        /// Client proposes a new per-visit price.
        /// Rules enforced:
        ///   - proposedPrice >= ₦10,000 (minimum floor)
        ///   - proposedPrice &lt;= OriginalGigPrice (no upward negotiation)
        ///   - proposedPrice &lt;= LastClientPrice if client has proposed before (cannot retreat upward)
        ///   - ClientProposalCount must be &lt; 3 (max 3 proposals)
        /// Status set to ClientProposed. Caregiver notified via email + in-app.
        /// Version must match the stored Version (optimistic concurrency).
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> ClientProposeAsync(string clientId, string negotiationId, decimal proposedPrice, string? note, long version);

        // ── Caregiver Actions ─────────────────────────────────────────────────

        /// <summary>
        /// Caregiver either accepts the client's proposed price or counter-proposes.
        /// If accept = true: same outcome as ClientAcceptAsync but triggered by caregiver.
        ///   For both paths: creates special gig, sets SpecialGigId, marks Status = Agreed.
        ///   Client notified with payment redirect information.
        /// If accept = false (counter-propose):
        ///   Rules enforced:
        ///     - counterPrice >= ₦10,000
        ///     - counterPrice &lt;= OriginalGigPrice
        ///     - counterPrice > LatestProposedPrice (must be higher than client's proposal)
        ///     - counterPrice &lt;= LastCaregiverPrice if caregiver has countered before (cannot retreat upward)
        ///     - CaregiverCounterCount must be &lt; 3 (max 3 counters)
        ///   Status set to CaregiverCountered. Client notified via email + in-app.
        /// Version must match the stored Version (optimistic concurrency).
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> CaregiverRespondAsync(string caregiverId, string negotiationId, bool accept, decimal? counterPrice, string? note, long version);

        // ── Rejection ─────────────────────────────────────────────────────────

        /// <summary>
        /// Either party can reject. Sets Status = Rejected, notifies the other party.
        /// For CareRequestHire path, the CareRequestResponse status remains "hired"
        /// so the client can re-initiate negotiation if desired.
        /// Version must match the stored Version (optimistic concurrency).
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> RejectAsync(string userId, string userRole, string negotiationId, string? reason, long version);

        // ── Re-initiation (CareRequestHire path only) ─────────────────────────

        /// <summary>
        /// Allows the client to start a fresh price negotiation for a CareRequest hire after
        /// the previous negotiation was Rejected or Expired.
        ///
        /// The CareRequestResponse must already have Status = "hired" (the hire is not undone).
        /// The new negotiation uses the same caregiver ProposedRate from the original response
        /// as the opening price.
        ///
        /// Idempotent: if a non-terminal negotiation already exists for this careRequestId,
        /// the existing one is returned rather than creating a duplicate.
        ///
        /// Throws InvalidOperationException if the response is not "hired" or if the
        /// calling clientId does not own the CareRequest.
        /// </summary>
        Task<GigPriceNegotiationResponseDTO> ReinitiateFromCareRequestAsync(
            string clientId, string careRequestId, string responseId);
    }
}
