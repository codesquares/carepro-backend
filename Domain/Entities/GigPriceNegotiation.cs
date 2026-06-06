using MongoDB.Bson;

namespace Domain.Entities
{
    /// <summary>
    /// Represents a pre-payment price negotiation between a client and a caregiver.
    /// Covers two entry paths:
    ///   1. RegularGig   — client paid ₦5,000 commitment fee for an existing gig and chooses to negotiate price.
    ///   2. CareRequestHire — client hired a caregiver via a CareRequest and the caregiver proposed a rate.
    /// Negotiation concerns only the per-visit price. Service type and visit frequency are chosen at checkout.
    /// </summary>
    public class GigPriceNegotiation
    {
        public ObjectId Id { get; set; }

        // ── Parties ──────────────────────────────────────────────────────────
        public string ClientId { get; set; } = string.Empty;
        public string CaregiverId { get; set; } = string.Empty;

        // ── Gig References ───────────────────────────────────────────────────
        /// <summary>
        /// Always the original caregiver gig. For CareRequestHire this is null
        /// because there is no pre-existing gig — only a ProposedRate from the response.
        /// </summary>
        public string? OriginalGigId { get; set; }

        /// <summary>
        /// Set only when EntrySource = "CareRequestHire"
        /// </summary>
        public string? CareRequestId { get; set; }

        /// <summary>
        /// Set only when EntrySource = "CareRequestHire"
        /// </summary>
        public string? CareRequestResponseId { get; set; }

        /// <summary>
        /// Set when agreement is reached (on Agreed status) and the CreateSpecialGig step succeeds.
        /// For the RegularGig+client-accepts-original-price path, this stays null and the
        /// original gig ID is used for payment.
        /// </summary>
        public string? SpecialGigId { get; set; }

        // ── Gig Detail Snapshots (taken at creation — immutable) ─────────────
        /// <summary>Gig title at the time negotiation was initiated.</summary>
        public string GigTitleSnapshot { get; set; } = string.Empty;

        /// <summary>Service category at the time negotiation was initiated.</summary>
        public string GigCategorySnapshot { get; set; } = string.Empty;

        /// <summary>Package details (task list) at the time negotiation was initiated.</summary>
        public List<string> GigPackageDetailsSnapshot { get; set; } = new();

        // ── Price Tracking ───────────────────────────────────────────────────
        /// <summary>
        /// The gig's listed price (or caregiver's ProposedRate for CareRequestHire) at the
        /// moment the negotiation was created. Immutable after creation.
        /// This is a per-visit price.
        /// </summary>
        public decimal OriginalGigPrice { get; set; }

        /// <summary>
        /// The current price on the table. Updated on every proposal or counter.
        /// This is a per-visit price.
        /// </summary>
        public decimal LatestProposedPrice { get; set; }

        /// <summary>
        /// Who made the latest proposal: "Client", "Caregiver", or "None" (just initiated).
        /// </summary>
        public string ProposedBy { get; set; } = "None";

        /// <summary>
        /// Set when Status = Agreed. The final agreed per-visit price.
        /// </summary>
        public decimal? AgreedPrice { get; set; }

        // ── Round Tracking ───────────────────────────────────────────────────
        /// <summary>
        /// Number of counter-proposals the client has made. Max = 3.
        /// When >= 3, client can only Accept or Reject — no more counter-proposals.
        /// </summary>
        public int ClientProposalCount { get; set; } = 0;

        /// <summary>
        /// Number of counter-proposals the caregiver has made. Max = 3.
        /// When >= 3, caregiver can only Accept or Reject — no more counter-proposals.
        /// </summary>
        public int CaregiverCounterCount { get; set; } = 0;

        /// <summary>
        /// The last per-visit price the client proposed.
        /// Used to enforce the no-upward-negotiation rule for the client.
        /// </summary>
        public decimal? LastClientPrice { get; set; }

        /// <summary>
        /// The last per-visit price the caregiver counter-proposed.
        /// Used to enforce the no-upward-negotiation rule for the caregiver.
        /// </summary>
        public decimal? LastCaregiverPrice { get; set; }

        // ── Source and Status ─────────────────────────────────────────────────
        /// <summary>"RegularGig" or "CareRequestHire"</summary>
        public string EntrySource { get; set; } = string.Empty;

        public GigPriceNegotiationStatus Status { get; set; } = GigPriceNegotiationStatus.Initiated;

        // ── Audit Trail ───────────────────────────────────────────────────────
        public List<NegotiationRoundEntry> History { get; set; } = new();

        // ── Optimistic Concurrency ────────────────────────────────────────────
        /// <summary>
        /// Incremented on every write. Client must send the current Version back on all
        /// write operations (propose, accept, respond, reject). A mismatch returns 409.
        /// </summary>
        public long Version { get; set; } = 0;

        // ── Expiry ────────────────────────────────────────────────────────────
        /// <summary>
        /// Negotiation expires 48 hours after the last update (UpdatedAt + 48h).
        /// Reset on every write operation. A background processor marks expired negotiations.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        // ── Timestamps ───────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AgreedAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
        public DateTime? RejectedAt { get; set; }

        /// <summary>
        /// "Client" or "Caregiver" — set when Status = Rejected.
        /// </summary>
        public string? RejectedBy { get; set; }

        /// <summary>
        /// Optional reason supplied by the rejecting party.
        /// </summary>
        public string? RejectionReason { get; set; }
    }

    /// <summary>
    /// A single entry in the negotiation history audit trail.
    /// </summary>
    public class NegotiationRoundEntry
    {
        public int RoundNumber { get; set; }

        /// <summary>
        /// Per-visit price for this round.
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>"Client" or "Caregiver"</summary>
        public string ProposedBy { get; set; } = string.Empty;

        /// <summary>"Propose", "Counter", "Accept", or "Reject"</summary>
        public string ActionType { get; set; } = string.Empty;

        public string? Note { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum GigPriceNegotiationStatus
    {
        /// <summary>Record created, no client proposal yet. For CareRequestHire the caregiver's
        /// ProposedRate is displayed but the client hasn't responded.</summary>
        Initiated,

        /// <summary>Client has proposed a price. Awaiting caregiver response.</summary>
        ClientProposed,

        /// <summary>Caregiver has counter-proposed. Awaiting client response.</summary>
        CaregiverCountered,

        /// <summary>Both parties agreed. Special gig created (if required). Ready for payment.</summary>
        Agreed,

        /// <summary>One party explicitly rejected the negotiation.</summary>
        Rejected,

        /// <summary>No activity for 48 hours. Background processor set this status.</summary>
        Expired
    }
}
