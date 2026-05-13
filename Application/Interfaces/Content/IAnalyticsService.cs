using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IAnalyticsService
    {
        Task TrackEventAsync(TrackAnalyticsEventRequest request, string? ipAddress);
        Task<AnalyticsEventsResponse> GetEventsAsync(AnalyticsEventsQuery query);
    }
}
