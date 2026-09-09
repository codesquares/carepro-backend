using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Admin CRUD for pre-priced care <see cref="Domain.Entities.Package"/> variants (Phase 3).
    /// Mirrors <c>ITrainingMaterialService</c>'s admin-CRUD shape.
    /// </summary>
    public interface IPackageService
    {
        Task<PackageDTO> CreatePackageAsync(AddPackageRequest request);
        Task<PackageDTO?> GetPackageByIdAsync(string id);
        Task<List<PackageDTO>> GetAllPackagesAsync();
        Task<bool> UpdatePackageAsync(UpdatePackageRequest request);
        Task<bool> DeletePackageAsync(string id);
        Task<bool> ToggleActiveStatusAsync(string id, bool isActive);
    }
}
