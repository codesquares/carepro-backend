using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class AnalyticsService : IAnalyticsService
    {
        private readonly CareProDbContext _context;
        private readonly ILogger<AnalyticsService> _logger;

        public AnalyticsService(CareProDbContext context, ILogger<AnalyticsService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task TrackEventAsync(TrackAnalyticsEventRequest request, string? ipAddress)
        {
            var analyticsEvent = new AnalyticsEvent
            {
                EventType = request.EventType,
                Page = request.Page,
                Fbclid = request.Fbclid,
                UserAgent = request.UserAgent,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            };

            _context.AnalyticsEvents.Add(analyticsEvent);
            await _context.SaveChangesAsync();
        }

        public async Task<AnalyticsEventsResponse> GetEventsAsync(AnalyticsEventsQuery query)
        {
            var dbQuery = _context.AnalyticsEvents.AsQueryable();

            if (query.StartDate.HasValue)
                dbQuery = dbQuery.Where(e => e.CreatedAt >= query.StartDate.Value);

            if (query.EndDate.HasValue)
                dbQuery = dbQuery.Where(e => e.CreatedAt <= query.EndDate.Value);

            if (!string.IsNullOrWhiteSpace(query.EventType))
                dbQuery = dbQuery.Where(e => e.EventType == query.EventType);

            if (!string.IsNullOrWhiteSpace(query.Page))
                dbQuery = dbQuery.Where(e => e.Page == query.Page);

            // Summary over the full filtered set (before pagination)
            var allMatching = await dbQuery.ToListAsync();

            var summary = new AnalyticsSummary
            {
                TotalEvents = allMatching.Count,
                ByEventType = allMatching
                    .GroupBy(e => e.EventType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                FacebookSourcedCount = allMatching.Count(e => !string.IsNullOrEmpty(e.Fbclid))
            };

            // Paginated page (most recent first)
            var pageSize = Math.Max(1, Math.Min(query.PageSize, 200));
            var pageNumber = Math.Max(1, query.PageNumber);

            var pagedEvents = allMatching
                .OrderByDescending(e => e.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new AnalyticsEventDTO
                {
                    Id = e.Id.ToString(),
                    EventType = e.EventType,
                    Page = e.Page,
                    Fbclid = e.Fbclid,
                    UserAgent = e.UserAgent,
                    IpAddress = e.IpAddress,
                    CreatedAt = e.CreatedAt
                })
                .ToList();

            return new AnalyticsEventsResponse
            {
                Events = pagedEvents,
                Page = pageNumber,
                PageSize = pageSize,
                TotalCount = summary.TotalEvents,
                Summary = summary
            };
        }

        public async Task<GigViewsOverviewResponse> GetGigViewsOverviewAsync(DateTime? from, DateTime? to)
        {
            var (startUtc, endUtc) = NormalizeRange(from, to);
            var views = await _context.GigViews
                .Where(v => v.ViewedAt >= startUtc && v.ViewedAt <= endUtc)
                .ToListAsync();

            var uniqueViewerKeys = views
                .Select(BuildViewerKey)
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct()
                .Count();

            return new GigViewsOverviewResponse
            {
                From = startUtc,
                To = endUtc,
                TotalViews = views.Count,
                UniqueViewers = uniqueViewerKeys,
                UniqueAuthenticatedUsers = views
                    .Where(v => !string.IsNullOrWhiteSpace(v.ViewerUserId))
                    .Select(v => v.ViewerUserId!)
                    .Distinct()
                    .Count(),
                UniqueSessions = views
                    .Where(v => string.IsNullOrWhiteSpace(v.ViewerUserId) && !string.IsNullOrWhiteSpace(v.ViewerSessionId))
                    .Select(v => v.ViewerSessionId!)
                    .Distinct()
                    .Count(),
            };
        }

        public async Task<List<GigViewsTopItem>> GetTopGigViewsAsync(int limit, DateTime? from, DateTime? to)
        {
            var (startUtc, endUtc) = NormalizeRange(from, to);
            var take = Math.Clamp(limit, 1, 100);

            var views = await _context.GigViews
                .Where(v => v.ViewedAt >= startUtc && v.ViewedAt <= endUtc)
                .ToListAsync();

            var grouped = views
                .GroupBy(v => v.GigId)
                .Select(g => new
                {
                    GigId = g.Key,
                    Views = g.Count(),
                    UniqueViewers = g
                        .Select(BuildViewerKey)
                        .Where(k => !string.IsNullOrEmpty(k))
                        .Distinct()
                        .Count(),
                })
                .OrderByDescending(g => g.Views)
                .ThenBy(g => g.GigId)
                .Take(take)
                .ToList();

            var gigIds = grouped
                .Select(g => g.GigId)
                .ToList();

            var objectIds = gigIds
                .Select(id => ObjectId.TryParse(id, out var parsed) ? parsed : ObjectId.Empty)
                .Where(id => id != ObjectId.Empty)
                .ToList();

            var gigTitles = await _context.Gigs
                .Where(g => objectIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Title })
                .ToListAsync();

            var titleMap = gigTitles.ToDictionary(g => g.Id.ToString(), g => g.Title);

            return grouped
                .Select(g => new GigViewsTopItem
                {
                    GigId = g.GigId,
                    GigTitle = titleMap.TryGetValue(g.GigId, out var title) ? title : null,
                    Views = g.Views,
                    UniqueViewers = g.UniqueViewers,
                })
                .ToList();
        }

        public async Task<GigViewsTimeseriesResponse> GetGigViewsTimeseriesAsync(string gigId, string bucket, DateTime? from, DateTime? to)
        {
            var normalizedBucket = string.IsNullOrWhiteSpace(bucket) ? "day" : bucket.Trim().ToLowerInvariant();
            if (normalizedBucket != "day" && normalizedBucket != "week")
            {
                throw new ArgumentException("Bucket must be either 'day' or 'week'.");
            }

            var (startUtc, endUtc) = NormalizeRange(from, to);

            var views = await _context.GigViews
                .Where(v => v.GigId == gigId && v.ViewedAt >= startUtc && v.ViewedAt <= endUtc)
                .ToListAsync();

            var points = views
                .GroupBy(v => GetBucketStart(v.ViewedAt, normalizedBucket))
                .OrderBy(g => g.Key)
                .Select(g => new GigViewsTimeseriesPoint
                {
                    BucketStart = g.Key,
                    Views = g.Count(),
                    UniqueViewers = g
                        .Select(BuildViewerKey)
                        .Where(k => !string.IsNullOrEmpty(k))
                        .Distinct()
                        .Count(),
                })
                .ToList();

            return new GigViewsTimeseriesResponse
            {
                GigId = gigId,
                Bucket = normalizedBucket,
                From = startUtc,
                To = endUtc,
                Points = points,
            };
        }

        private static (DateTime startUtc, DateTime endUtc) NormalizeRange(DateTime? from, DateTime? to)
        {
            var endUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
            var startUtc = (from ?? endUtc.AddDays(-30)).ToUniversalTime();
            if (startUtc > endUtc)
            {
                (startUtc, endUtc) = (endUtc, startUtc);
            }
            return (startUtc, endUtc);
        }

        private static string? BuildViewerKey(GigView view)
        {
            if (!string.IsNullOrWhiteSpace(view.ViewerUserId))
            {
                return $"u:{view.ViewerUserId}";
            }

            if (!string.IsNullOrWhiteSpace(view.ViewerSessionId))
            {
                return $"s:{view.ViewerSessionId}";
            }

            return null;
        }

        private static DateTime GetBucketStart(DateTime inputUtc, string bucket)
        {
            var dt = inputUtc.ToUniversalTime();
            if (bucket == "week")
            {
                var diff = ((int)dt.DayOfWeek + 6) % 7;
                var monday = dt.Date.AddDays(-diff);
                return DateTime.SpecifyKind(monday, DateTimeKind.Utc);
            }

            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
        }
    }
}
