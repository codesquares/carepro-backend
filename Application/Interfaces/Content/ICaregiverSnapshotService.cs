using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface ICaregiverSnapshotService
    {
        Task RebuildAllSnapshotsAsync();
        Task<CaregiverSnapshotResponse> GetSnapshotsAsync(CaregiverSnapshotQuery query);
    }
}
