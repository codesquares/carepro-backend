using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IVisitAbsenceService
    {
        /// <summary>Caregiver tells the client + CarePro they cannot attend a day. No money moves.</summary>
        Task<VisitAbsenceNoticeDTO> CreateAsync(string assignmentId, CreateVisitAbsenceRequest request, string caregiverId);
        Task<List<VisitAbsenceNoticeDTO>> GetForAssignmentAsync(string assignmentId, string caregiverId);
        /// <summary>The giving caregiver withdraws a notice before the day and before staff arranged cover/credit. 409 if no longer possible.</summary>
        Task<VisitAbsenceNoticeDTO> WithdrawAsync(string assignmentId, string noticeId, string caregiverId);
        /// <summary>The client's view (date + category only) across every assignment of their package request.</summary>
        Task<List<ClientVisitAbsenceNoticeDTO>> GetForPackageRequestAsClientAsync(string packageRequestId, string clientId);

        // ── staff (OperationsPolicy) ──
        Task<List<AdminVisitAbsenceNoticeDTO>> ListForAdminAsync(string? status, bool? shortNotice);
        Task<AdminVisitAbsenceNoticeDTO> GetForAdminAsync(string noticeId);
        /// <summary>Records a status change (admin id from the JWT, time, note). Moves no money / payroll / task sheet / assignment.</summary>
        Task<AdminVisitAbsenceNoticeDTO> SetStatusAsync(string noticeId, SetAbsenceStatusRequest request, string adminId);
        /// <summary>Re-alerts approved admins ONCE about still-open notices whose day is within 24h. Returns how many this instance claimed.</summary>
        Task<int> EscalateDueAsync();
    }
}
