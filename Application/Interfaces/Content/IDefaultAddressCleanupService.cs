using Application.DTOs;

namespace Application.Interfaces.Content;

public interface IDefaultAddressCleanupService
{
    Task<DefaultAddressCleanupResult> CleanupDefaultAddressAsync(
        DefaultAddressCleanupRequest request,
        string adminId,
        string adminEmail);
}
