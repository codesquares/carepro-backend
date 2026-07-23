using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// Thrown when a client tries to hire a caregiver who isn't ready to deliver
    /// (unverified, hasn't passed the required assessment, or has no active gig).
    /// Carries structured reason codes so the controller/frontend can render a
    /// targeted message instead of a generic failure.
    /// </summary>
    public class CaregiverNotReadyException : Exception
    {
        public string ErrorCode { get; } = "CAREGIVER_NOT_READY";
        public List<string> Reasons { get; }

        public CaregiverNotReadyException(string message, List<string> reasons) : base(message)
        {
            Reasons = reasons;
        }
    }
}
