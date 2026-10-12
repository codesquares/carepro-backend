using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IVisitAbsenceService
    {
        /// <summary>Caregiver tells the client + CarePro they cannot attend a day. No money moves.</summary>
        Task<VisitAbsenceNoticeDTO> CreateAsync(string assignmentId, CreateVisitAbsenceRequest request, string caregiverId);
        Task<List<VisitAbsenceNoticeDTO>> GetForAssignmentAsync(string assignmentId, string caregiverId);
        /// <summary>The client's view across every assignment of their package request.</summary>
        Task<List<VisitAbsenceNoticeDTO>> GetForPackageRequestAsClientAsync(string packageRequestId, string clientId);
    }
}
