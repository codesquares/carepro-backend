using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Admin CRUD for the <see cref="Domain.Entities.CaregiverPayRate"/> table (Phase 9.3).
    /// Mirrors <see cref="IPackageService"/>'s admin-CRUD shape.
    /// </summary>
    public interface ICaregiverPayRateService
    {
        Task<CaregiverPayRateDTO> CreatePayRateAsync(AddCaregiverPayRateRequest request);
        Task<CaregiverPayRateDTO?> GetPayRateByIdAsync(string id);
        Task<List<CaregiverPayRateDTO>> GetAllPayRatesAsync();
        Task<bool> UpdatePayRateAsync(UpdateCaregiverPayRateRequest request);
        Task<bool> DeletePayRateAsync(string id);
        Task<bool> ToggleActiveStatusAsync(string id, bool isActive);
    }
}
