using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IAdminExportService
    {
        Task<byte[]> ExportCaregiversAsync(ExportQuery query);
        Task<byte[]> ExportClientsAsync(ExportQuery query);
        Task<byte[]> ExportCaregiverSnapshotsAsync(CaregiverSnapshotQuery query);
    }
}
