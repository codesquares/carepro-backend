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
    }
}
