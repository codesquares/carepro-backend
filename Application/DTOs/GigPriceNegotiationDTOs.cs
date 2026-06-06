using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  REQUESTS (Frontend → Backend)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Client initiates negotiation for a regular gig (after paying commitment fee).
    /// ProposedPrice is optional — if omitted the offer component is shown with no proposal yet.
    /// </summary>
    public class InitiateNegotiationRequest
    {
        [Required]
        public string GigId { get; set; } = string.Empty;

        /// <summary>
        /// Optional per-visit price proposal. Must be between ₦10,000 and the gig's listed price.
        /// If omitted, no proposal is made yet (Status = Initiated).
        /// </summary>
        public decimal? ProposedPrice { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    /// <summary>
    /// Client proposes a new per-visit price.
    /// </summary>
    public class ClientProposeRequest
    {
        [Required]
        [Range(10000, double.MaxValue, ErrorMessage = "Proposed price must be at least ₦10,000.")]
        public decimal ProposedPrice { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        /// <summary>
        /// Must match the current Version on the negotiation record.
        /// Prevents concurrent conflicting writes (optimistic concurrency).
        /// </summary>
        [Required]
        public long Version { get; set; }
    }

    /// <summary>
    /// Client accepts the current price as-is (no counter).
    /// </summary>
    public class ClientAcceptRequest
    {
        /// <summary>
        /// Must match the current Version on the negotiation record.
        /// </summary>
        [Required]
        public long Version { get; set; }
    }

    /// <summary>
    /// Caregiver either accepts the client's proposed price or submits a counter-proposal.
    /// </summary>
    public class CaregiverRespondRequest
    {
        /// <summary>
        /// True = accept the client's price. False = counter-propose.
        /// </summary>
        [Required]
        public bool Accept { get; set; }

        /// <summary>
        /// Required when Accept = false. Per-visit counter price.
        /// Must be >= ₦10,000 and &lt;= original gig price.
        /// Must be greater than the client's latest proposed price.
        /// </summary>
        public decimal? CounterPrice { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        /// <summary>
        /// Must match the current Version on the negotiation record.
        /// </summary>
        [Required]
        public long Version { get; set; }
    }

    /// <summary>
    /// Either party rejects the negotiation.
    /// </summary>
    public class RejectNegotiationRequest
    {
        [MaxLength(500)]
        public string? Reason { get; set; }

        /// <summary>
        /// Must match the current Version on the negotiation record.
        /// </summary>
        [Required]
        public long Version { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  RESPONSES (Backend → Frontend)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full negotiation state. This is the payload used to render the Offer Component.
    /// Returned by all read and write endpoints.
    /// </summary>
    public class GigPriceNegotiationResponseDTO
    {
        public string NegotiationId { get; set; } = string.Empty;

        /// <summary>
        /// "Initiated" | "ClientProposed" | "CaregiverCountered" | "Agreed" | "Rejected" | "Expired"
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// "RegularGig" | "CareRequestHire"
        /// </summary>
        public string EntrySource { get; set; } = string.Empty;

        // ── Gig details (from snapshots — always stable regardless of gig changes) ──
        public NegotiationGigDetailsDTO GigDetails { get; set; } = new();

        // ── Caregiver info (live at read time) ───────────────────────────────
        public NegotiationPartyInfoDTO CaregiverInfo { get; set; } = new();

        // ── Client info (live at read time) ──────────────────────────────────
        public NegotiationPartyInfoDTO ClientInfo { get; set; } = new();

        // ── Pricing ──────────────────────────────────────────────────────────
        /// <summary>
        /// The gig's original listed price (or caregiver's initial ProposedRate for CareRequestHire).
        /// This is a per-visit price. Immutable once the negotiation is created.
        /// </summary>
        public decimal OriginalPrice { get; set; }

        /// <summary>
        /// The price currently on the table (per visit). Updated after each proposal/counter.
        /// </summary>
        public decimal CurrentProposedPrice { get; set; }

        /// <summary>
        /// "Client", "Caregiver", or "None"
        /// </summary>
        public string ProposedBy { get; set; } = string.Empty;

        /// <summary>
        /// Set only when Status = "Agreed". The final agreed per-visit price.
        /// </summary>
        public decimal? AgreedPrice { get; set; }

        // ── Payment Redirect (set when Status = "Agreed") ────────────────────
        /// <summary>
        /// The gig ID to use when calling POST /api/payments/initiate.
        /// For RegularGig + client accepted original price: this is the OriginalGigId.
        /// For all other agreed paths: this is the SpecialGigId.
        /// Null until Status = "Agreed".
        /// </summary>
        public string? GigIdForPayment { get; set; }

        // ── Round Tracking ───────────────────────────────────────────────────
        public int ClientProposalCount { get; set; }

        /// <summary>Always 3.</summary>
        public int ClientMaxProposals { get; set; } = 3;

        /// <summary>
        /// True if the client can still submit a counter-proposal (count &lt; 3 and it is their turn).
        /// </summary>
        public bool CanClientPropose { get; set; }

        public int CaregiverCounterCount { get; set; }

        /// <summary>Always 3.</summary>
        public int CaregiverMaxCounters { get; set; } = 3;

        /// <summary>
        /// True if the caregiver can still submit a counter-proposal (count &lt; 3 and it is their turn).
        /// </summary>
        public bool CanCaregiverCounter { get; set; }

        /// <summary>
        /// True when it is the client's turn to act (Status = Initiated or CaregiverCountered).
        /// False when it is the caregiver's turn (Status = ClientProposed).
        /// </summary>
        public bool IsClientsTurn { get; set; }

        // ── Commitment Fee Info (RegularGig path only) ────────────────────────
        /// <summary>
        /// Amount that will be deducted from the order fee at checkout (₦5,000 for RegularGig path).
        /// Always 0 for CareRequestHire path.
        /// Shown on the offer component so the client knows the net amount at payment time.
        /// </summary>
        public decimal CommitmentFeeDeductedAtCheckout { get; set; }

        // ── History ───────────────────────────────────────────────────────────
        /// <summary>
        /// Full audit trail of all proposals, counters, and actions in chronological order.
        /// </summary>
        public List<NegotiationRoundEntryDTO> History { get; set; } = new();

        // ── Concurrency ───────────────────────────────────────────────────────
        /// <summary>
        /// The current version of this record. Must be sent back unchanged on all write operations
        /// (propose, accept, respond, reject). A mismatch returns HTTP 409 Conflict.
        /// </summary>
        public long Version { get; set; }

        // ── Expiry ────────────────────────────────────────────────────────────
        /// <summary>
        /// UTC datetime when this negotiation will auto-expire if no action is taken.
        /// Extended by 48h on every write operation.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        // ── UI Hint Strings ───────────────────────────────────────────────────
        /// <summary>
        /// Always present. Frontend MUST display this message prominently on the offer component
        /// so both parties understand that all prices are per-visit rates.
        /// Value: "All prices shown are per-visit rates. Your total payment at checkout will depend
        /// on your chosen service type (one-time or monthly) and visit frequency."
        /// </summary>
        public string PerVisitPriceMessage { get; set; } =
            "All prices shown are per-visit rates. Your total payment at checkout will depend on your chosen service type (one-time or monthly) and visit frequency.";

        /// <summary>
        /// Present only when Status = "Agreed" and EntrySource = "RegularGig".
        /// Reminds the client that the ₦5,000 commitment fee will be deducted from their order total.
        /// Value example: "Your ₦5,000 commitment fee will be deducted from your payment total at checkout."
        /// </summary>
        public string? CommitmentFeeReminderMessage { get; set; }

        // ── Rejection info ────────────────────────────────────────────────────
        public string? RejectedBy { get; set; }
        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? AgreedAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
    }

    /// <summary>
    /// Snapshotted gig details embedded in the offer component payload.
    /// </summary>
    public class NegotiationGigDetailsDTO
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public List<string> PackageDetails { get; set; } = new();
    }

    /// <summary>
    /// Basic party info shown in the offer component.
    /// </summary>
    public class NegotiationPartyInfoDTO
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
    }

    /// <summary>
    /// A single entry in the negotiation history shown on the offer component.
    /// </summary>
    public class NegotiationRoundEntryDTO
    {
        public int RoundNumber { get; set; }

        /// <summary>Per-visit price for this round.</summary>
        public decimal Price { get; set; }

        /// <summary>"Client" or "Caregiver"</summary>
        public string ProposedBy { get; set; } = string.Empty;

        /// <summary>"Propose", "Counter", "Accept", or "Reject"</summary>
        public string ActionType { get; set; } = string.Empty;

        public string? Note { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Returned when an agreement is reached (client accept or caregiver accept).
    /// Contains all the information needed to redirect to payment.
    /// </summary>
    public class NegotiationAgreementResultDTO
    {
        public string NegotiationId { get; set; } = string.Empty;

        /// <summary>The agreed per-visit price.</summary>
        public decimal AgreedPrice { get; set; }

        /// <summary>
        /// The gig ID to use when calling POST /api/payments/initiate.
        /// For RegularGig + accepted original price: equals the original gig ID.
        /// For all other cases (negotiated price): equals the newly created SpecialGigId.
        /// </summary>
        public string GigIdForPayment { get; set; } = string.Empty;

        /// <summary>
        /// "RegularGig" or "CareRequestHire"
        /// </summary>
        public string EntrySource { get; set; } = string.Empty;

        /// <summary>
        /// ₦5,000 for RegularGig path. 0 for CareRequestHire path.
        /// </summary>
        public decimal CommitmentFeeDeductedAtCheckout { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Paged list of negotiations awaiting a caregiver's response.
    /// </summary>
    public class PaginatedNegotiationListDTO
    {
        public List<NegotiationSummaryDTO> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// Lightweight summary used in list views (caregiver dashboard).
    /// </summary>
    public class NegotiationSummaryDTO
    {
        public string NegotiationId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string? ClientProfileImage { get; set; }
        public string GigTitle { get; set; } = string.Empty;
        public decimal OriginalPrice { get; set; }
        public decimal CurrentProposedPrice { get; set; }
        public string ProposedBy { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string EntrySource { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Client re-initiates a price negotiation for a CareRequest hire after the previous
    /// negotiation was rejected or expired. The hire itself remains active.
    /// </summary>
    public class ReinitiateFromCareRequestRequest
    {
        [Required]
        public string CareRequestId { get; set; } = string.Empty;

        [Required]
        public string ResponseId { get; set; } = string.Empty;
    }
}
