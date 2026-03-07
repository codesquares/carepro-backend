using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IObservationReportService
    {
        Task<ObservationReportDTO> CreateAsync(CreateObservationReportRequest request, string caregiverId);
        Task<List<ObservationReportDTO>> GetByOrderAsync(string orderId, string? taskSheetId, string caregiverId, bool isAdmin);
        Task<int> GetCountByTaskSheetIdAsync(string taskSheetId);
    }
}
