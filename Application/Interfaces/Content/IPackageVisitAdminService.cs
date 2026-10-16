using Application.DTOs;

namespace Application.Interfaces.Content
{
    /// <summary>Admin review of package-assignment visits and their GPS check-in flags.</summary>
    public interface IPackageVisitAdminService
    {
        /// <summary>Visits newest-first, uncleared HARD flags first. Flags are never deleted; cleared ones stay in the list.</summary>
        Task<List<PackageVisitAdminRowDTO>> ListAsync(PackageVisitAdminQuery query);

        /// <summary>Marks a flag reviewed (who, when, note). Idempotent: a second clear keeps the first record.</summary>
        Task<PackageVisitAdminRowDTO> ClearFlagAsync(string checkinId, string adminId, string? adminEmail, string note);

        /// <summary>Uncleared HARD-flagged visits for one caregiver in one calendar month (payroll soft gate).</summary>
        Task<List<PackageVisitAdminRowDTO>> GetUnclearedHardFlagsAsync(string caregiverId, int year, int month, string? assignmentId = null);
    }
}
