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

        /// <summary>
        /// Client-facing catalog: every <see cref="Domain.Entities.Package"/> with
        /// <c>IsActive == true</c>, projected to <see cref="ClientPackageDTO"/> (no
        /// operational/payroll internals). Ordered by category, then price ascending.
        /// </summary>
        Task<List<ClientPackageDTO>> GetActivePackagesForClientAsync();
        Task<bool> UpdatePackageAsync(UpdatePackageRequest request);
        Task<bool> DeletePackageAsync(string id);
        Task<bool> ToggleActiveStatusAsync(string id, bool isActive);

        /// <summary>
        /// One-time real-pricing seed (see <see cref="Infrastructure.Content.Data.PackageSeedData"/>).
        /// Idempotent per row, matched on (Category, TierLabel) rather than an
        /// all-or-nothing "collection is empty" check — so re-running it never
        /// duplicates a row, and never overwrites a row an admin has since edited.
        /// </summary>
        Task<int> SeedPackagesAsync(List<Domain.Entities.Package> packages);
    }
}
