using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ICareRequestMatchingService
    {
        Task<CareRequestMatchResponse> FindMatchesForCareRequestAsync(string careRequestId);
        Task<CareRequestMatchResponse> GetMatchesForCareRequestAsync(string careRequestId, string requestingUserId);
    }
}
