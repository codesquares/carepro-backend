using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IIncidentReportService
    {
        Task<IncidentReportDTO> CreateAsync(CreateIncidentReportRequest request, string caregiverId);
        Task<List<IncidentReportDTO>> GetByOrderAsync(string orderId, string userId, bool isAdmin, bool isClient = false);
        Task<int> GetCountByTaskSheetIdAsync(string taskSheetId);
    }
}
