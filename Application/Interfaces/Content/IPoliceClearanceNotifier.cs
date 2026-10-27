using Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>Notifications for the police clearance flow. Every method is best-effort: a delivery failure is logged, never thrown.</summary>
    public interface IPoliceClearanceNotifier
    {
        /// <summary>To Approved admins, in-app only.</summary>
        Task NotifySubmittedAsync(PoliceClearance clearance, string caregiverName);

        /// <summary>To the caregiver: in-app + email.</summary>
        Task NotifyApprovedAsync(string caregiverId, string clearanceId);

        /// <summary>To the caregiver: in-app + email, both carrying the reason.</summary>
        Task NotifyRejectedAsync(string caregiverId, string clearanceId, string reason);

        /// <summary>To the caregiver: in-app + email, both carrying the reason.</summary>
        Task NotifyRevokedAsync(string caregiverId, string clearanceId, string reason);

        /// <summary>To Approved admins, in-app only: lists the caregiver's active assignments so a human decides. Nothing is cancelled.</summary>
        Task NotifyAssignmentsAtRiskAsync(string caregiverId, string caregiverName, string trigger, string clearanceId, IReadOnlyList<string> assignmentLines);
    }
}
