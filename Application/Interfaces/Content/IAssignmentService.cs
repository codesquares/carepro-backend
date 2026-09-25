using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Phase 4 — one-sided internal assignment. Staff/system assign a specific caregiver
    /// to a package request; the caregiver must accept before it's finalized to the client.
    /// No automatic reassignment or timeout — stalled assignments are surfaced by
    /// <see cref="GetPendingAcceptanceAsync"/>.
    /// </summary>
    public interface IAssignmentService
    {
        // Staff / system
        Task<AssignmentDTO> AssignAsync(
            string packageRequestId, string caregiverId,
            string adminId, string? adminEmail, string assignedBy, double? matchScore);

        Task<AssignmentActionResult> CancelAsync(string assignmentId, string adminId, string? adminEmail, string reason);

        /// <summary>Every assignment awaiting a caregiver response, longest-pending first.</summary>
        Task<List<PendingAssignmentDTO>> GetPendingAcceptanceAsync();

        /// <summary>
        /// Every Accepted assignment, most-recently-accepted first — the staff-facing picker
        /// for Payroll (an admin needs a real AssignmentId + its PayCalculationType before
        /// creating a payroll record; there was previously no admin-facing way to list these).
        /// </summary>
        Task<List<AcceptedAssignmentDTO>> GetAcceptedAsync();

        // Caregiver
        Task<List<CaregiverAssignmentDTO>> GetMyAssignmentsAsync(string caregiverId);
        Task<CaregiverAssignmentDTO> GetMyAssignmentDetailAsync(string assignmentId, string caregiverId);
        Task<AssignmentActionResult> AcceptAsync(string assignmentId, string caregiverId);
        Task<AssignmentActionResult> DeclineAsync(string assignmentId, string caregiverId, string? reason);
    }
}
