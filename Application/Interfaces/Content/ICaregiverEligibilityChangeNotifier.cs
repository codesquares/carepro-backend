using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Shared hook for every write path that can flip a caregiver's readiness
    /// (verification, gig, assessment) — e.g. to trigger marketing re-sync.
    /// </summary>
    public interface ICaregiverEligibilityChangeNotifier
    {
        Task NotifyIfActiveHireAffectedAsync(string caregiverId);
    }
}
