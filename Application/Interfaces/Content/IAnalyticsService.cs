using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IAnalyticsService
    {
        Task TrackEventAsync(TrackAnalyticsEventRequest request, string? ipAddress);
        Task<AnalyticsEventsResponse> GetEventsAsync(AnalyticsEventsQuery query);
        Task<GigViewsOverviewResponse> GetGigViewsOverviewAsync(DateTime? from, DateTime? to);
        Task<List<GigViewsTopItem>> GetTopGigViewsAsync(int limit, DateTime? from, DateTime? to);
        Task<GigViewsTimeseriesResponse> GetGigViewsTimeseriesAsync(string gigId, string bucket, DateTime? from, DateTime? to);
    }
}
