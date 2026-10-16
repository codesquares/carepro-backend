using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IIncidentReportService
    {
        Task<IncidentReportDTO> CreateAsync(CreateIncidentReportRequest request, string caregiverId);
        Task<IncidentReportDTO> GetByIdAsync(string reportId, string userId, bool isAdmin, bool isClient);
        Task<List<IncidentReportDTO>> GetByOrderAsync(string orderId, string userId, bool isAdmin, bool isClient = false);
        /// <summary>Package-assignment equivalent of GetByOrderAsync: admin, the assignment's caregiver, or the owning client.</summary>
        Task<List<IncidentReportDTO>> GetByAssignmentAsync(string assignmentId, string userId, bool isAdmin, bool isClient = false);
        /// <summary>Client view across every assignment (incl. reassigned caregivers) of one of their package requests.</summary>
        Task<List<IncidentReportDTO>> GetByPackageRequestAsClientAsync(string packageRequestId, string clientId);
        Task<int> GetCountByTaskSheetIdAsync(string taskSheetId);
    }
}
