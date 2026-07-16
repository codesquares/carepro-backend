using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IClientOnboardingService
    {
        Task<ClientOnboardingStateResponse> GetStateAsync(string clientId);
        Task<ClientOnboardingPatchResult> PatchWalkthroughAsync(string clientId, PatchClientOnboardingWalkthroughRequest request);
        Task<ClientOnboardingTipSeenResponse> MarkTipSeenAsync(string clientId, MarkClientOnboardingTipSeenRequest request);
        Task<ClientOnboardingStateResponse> ResetStateAsync(string clientId);
    }
}