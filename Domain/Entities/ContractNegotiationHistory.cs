using MongoDB.Bson;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks contract negotiation history for safety and audit purposes.
    /// Records all contract-related actions between client and caregiver.
    /// </summary>
    public class ContractNegotiationHistory
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        
        // References
        public string ContractId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        
        // Who performed the action
        public string ActorId { get; set; } = string.Empty;
        public ActorType ActorType { get; set; }
        
        // What action was taken
        public NegotiationAction Action { get; set; }
        public int Round { get; set; } = 1;
        
        // Snapshot of contract details at this point
        public List<ScheduledVisit> ScheduleSnapshot { get; set; } = new List<ScheduledVisit>();
        public string? ServiceAddressSnapshot { get; set; }
        public string? SpecialRequirementsSnapshot { get; set; }
        public string? AccessInstructionsSnapshot { get; set; }
        public string? AdditionalNotesSnapshot { get; set; }
        
        // Comments/Notes for this action
        public string? Comments { get; set; }
        public string? RevisionNotes { get; set; }
        
        // Timestamp
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ActorType
    {
        Client,
        Caregiver,
        System
    }

    public enum NegotiationAction
    {
        ContractGenerated,      // Caregiver generated the contract
        SentToClient,           // Contract sent to client for approval
        ClientApproved,         // Client approved the contract
        ClientRequestedReview,  // Client requested changes
        CaregiverRevised,       // Caregiver submitted revised contract
        ClientRejected,         // Client rejected (after round 2)
        ContractExpired,        // Contract expired without action
        ContractCompleted,      // Contract service completed
        ContractTerminated      // Contract terminated early
    }
}
