using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IObservationReportService
    {
        Task<ObservationReportDTO> CreateAsync(CreateObservationReportRequest request, string caregiverId);
        Task<List<ObservationReportDTO>> GetByOrderAsync(string orderId, string? taskSheetId, string userId, bool isAdmin, bool isClient = false);
        /// <summary>Package-assignment equivalent of GetByOrderAsync: admin, the assignment's caregiver, or the owning client.</summary>
        Task<List<ObservationReportDTO>> GetByAssignmentAsync(string assignmentId, string? taskSheetId, string userId, bool isAdmin, bool isClient = false);
        /// <summary>Client view across every assignment (incl. reassigned caregivers) of one of their package requests.</summary>
        Task<List<ObservationReportDTO>> GetByPackageRequestAsClientAsync(string packageRequestId, string? taskSheetId, string clientId);
        Task<int> GetCountByTaskSheetIdAsync(string taskSheetId);
    }
}
