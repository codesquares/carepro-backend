using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface IGigServices
    {
        Task<GigDTO> CreateGigAsync(AddGigRequest addServiceRequest);

        Task<IEnumerable<GigDTO>> GetAllCaregiverGigsAsync(string caregiverId);

        Task<IEnumerable<GigDTO>> GetAllCaregiverPausedGigsAsync(string caregiverId);

        Task<IEnumerable<GigDTO>> GetAllCaregiverDraftGigsAsync(string caregiverId);

        Task<IEnumerable<GigDTO>> GetAllGigsAsync();

        Task<PaginatedResponse<GigDTO>> GetAllGigsPaginatedAsync(int page = 1, int pageSize = 20, string? status = null, string? search = null, string? category = null);

        // Task<IEnumerable<GigDTO>> GetAllCaregiverServicesAsync(string caregiverId);

        Task<List<string>> GetAllSubCategoriesForCaregiverAsync(string caregiverId);

        Task<GigDTO> GetGigAsync(string serviceId);

        Task<string> UpdateGigStatusToPauseAsync(string gigId, UpdateGigStatusToPauseRequest updateGigStatusToPauseRequest);

        Task<string> UpdateGigAsync(string gigId, UpdateGigRequest updateGigRequest);

        Task<string> SoftDeleteGigAsync(string gigId, string caregiverId);

        Task<AdminBulkDeleteResult> AdminBulkSoftDeleteGigsAsync(List<string>? gigIds, bool deleteAll, string adminUserId);

        Task<string> RestoreGigAsync(string gigId, string caregiverId);

        Task<IEnumerable<DeletedGigDTO>> GetDeletedGigsByCaregiverAsync(string caregiverId);

        Task<PaginatedResponse<DeletedGigDTO>> GetAllDeletedGigsPaginatedAsync(int page = 1, int pageSize = 20, string? caregiverId = null);

    }
}
