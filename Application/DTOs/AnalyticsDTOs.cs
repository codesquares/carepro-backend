namespace Application.DTOs
{
    public class TrackAnalyticsEventRequest
    {
        public string EventType { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public string? Fbclid { get; set; }
        public string? UserAgent { get; set; }
    }

    public class AnalyticsEventDTO
    {
        public string Id { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public string? Fbclid { get; set; }
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AnalyticsEventsResponse
    {
        public List<AnalyticsEventDTO> Events { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public AnalyticsSummary Summary { get; set; } = new();
    }

    public class AnalyticsSummary
    {
        public int TotalEvents { get; set; }
        public Dictionary<string, int> ByEventType { get; set; } = new();
        public int FacebookSourcedCount { get; set; }
    }

    public class AnalyticsEventsQuery
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? EventType { get; set; }
        public string? Page { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class GigViewsOverviewResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int TotalViews { get; set; }
        public int UniqueViewers { get; set; }
        public int UniqueAuthenticatedUsers { get; set; }
        public int UniqueSessions { get; set; }
    }

    public class GigViewsTopItem
    {
        public string GigId { get; set; } = string.Empty;
        public string? GigTitle { get; set; }
        public int Views { get; set; }
        public int UniqueViewers { get; set; }
    }

    public class GigViewsTimeseriesPoint
    {
        public DateTime BucketStart { get; set; }
        public int Views { get; set; }
        public int UniqueViewers { get; set; }
    }

    public class GigViewsTimeseriesResponse
    {
        public string GigId { get; set; } = string.Empty;
        public string Bucket { get; set; } = "day";
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<GigViewsTimeseriesPoint> Points { get; set; } = new();
    }
}
