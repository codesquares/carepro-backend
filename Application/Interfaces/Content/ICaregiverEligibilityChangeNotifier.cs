using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Notifies a client when a caregiver they've already hired for a care request
    /// (negotiation still in flight, not yet Agreed) loses hire-readiness — e.g. their
    /// identity verification fails. Shared so every write path that can flip a caregiver's
    /// readiness (verification, gig, assessment) triggers the same check instead of
    /// duplicating the "does this caregiver have an active hire in flight" query.
    /// </summary>
    public interface ICaregiverEligibilityChangeNotifier
    {
        Task NotifyIfActiveHireAffectedAsync(string caregiverId);
    }
}
