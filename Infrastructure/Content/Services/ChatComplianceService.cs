using Application.DTOs;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class ChatComplianceService : IChatComplianceService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IContactPatternDetector _patternDetector;
        private readonly ILogger<ChatComplianceService> _logger;

        // Threshold: after this many violations in the window, messages are blocked entirely
        private const int BlockThreshold = 3;
        private const int ViolationWindowDays = 30;

        public ChatComplianceService(
            CareProDbContext dbContext,
            IContactPatternDetector patternDetector,
            ILogger<ChatComplianceService> logger)
        {
            _dbContext = dbContext;
            _patternDetector = patternDetector;
            _logger = logger;
        }

        public async Task<ComplianceResult> EvaluateMessageAsync(string senderId, string receiverId, string rawMessage)
        {
            var detection = _patternDetector.Detect(rawMessage);

            if (!detection.HasViolation)
            {
                return new ComplianceResult
                {
                    Allowed = true,
                    Message = rawMessage
                };
            }

            // Count recent violations for this sender
            var windowStart = DateTime.UtcNow.AddDays(-ViolationWindowDays);
            var recentViolationCount = await _dbContext.ChatViolations
                .CountAsync(v => v.UserId == senderId && v.CreatedAt >= windowStart);

            var primaryCategory = detection.Patterns.First().Category;
            var patternDescriptions = detection.Patterns
                .Select(p => $"{p.Category}:{p.MatchedText}")
                .ToList();

            if (recentViolationCount >= BlockThreshold)
            {
                // Block entirely — repeat offender
                var blockedViolation = new ChatViolation
                {
                    Id = ObjectId.GenerateNewId(),
                    UserId = senderId,
                    RecipientId = receiverId,
                    OriginalMessage = rawMessage,
                    DetectedPatterns = patternDescriptions,
                    ViolationType = primaryCategory,
                    Action = "Blocked",
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.ChatViolations.AddAsync(blockedViolation);
                await _dbContext.SaveChangesAsync();

                // Create admin notification for repeat offender
                var adminNotification = new Notification
                {
                    Id = ObjectId.GenerateNewId(),
                    RecipientId = "admin",
                    SenderId = senderId,
                    Type = "ChatViolation",
                    Title = "Repeat chat violation — message blocked",
                    Content = $"User {senderId} has been blocked from sending a message ({recentViolationCount + 1} violations in {ViolationWindowDays} days). Detected: {string.Join(", ", patternDescriptions)}",
                    IsRead = false,
                    RelatedEntityId = blockedViolation.Id.ToString()
                };

                await _dbContext.Notifications.AddAsync(adminNotification);
                await _dbContext.SaveChangesAsync();

                _logger.LogWarning(
                    "Chat BLOCKED: User {SenderId} repeat offender ({ViolationCount} violations). Patterns: {Patterns}",
                    senderId, recentViolationCount + 1, string.Join(", ", patternDescriptions));

                return new ComplianceResult
                {
                    Allowed = false,
                    Message = string.Empty,
                    Warning = "Your message was blocked because it appears to contain contact information. Sharing personal contact details is not permitted on CarePro. All communication must stay on the platform.",
                    ViolationLogged = true
                };
            }

            // Redact and deliver — strip contact info but send the cleaned message
            var redactedViolation = new ChatViolation
            {
                Id = ObjectId.GenerateNewId(),
                UserId = senderId,
                RecipientId = receiverId,
                OriginalMessage = rawMessage,
                DetectedPatterns = patternDescriptions,
                ViolationType = primaryCategory,
                Action = "Redacted",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.ChatViolations.AddAsync(redactedViolation);
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning(
                "Chat REDACTED: User {SenderId} violation #{ViolationCount}. Patterns: {Patterns}",
                senderId, recentViolationCount + 1, string.Join(", ", patternDescriptions));

            // If redacted message is empty after stripping, block it entirely
            if (string.IsNullOrWhiteSpace(detection.RedactedMessage))
            {
                return new ComplianceResult
                {
                    Allowed = false,
                    Message = string.Empty,
                    Warning = "Your message could not be sent because it contained only contact information.",
                    ViolationLogged = true,
                    WasRedacted = false
                };
            }

            return new ComplianceResult
            {
                Allowed = true,
                Message = detection.RedactedMessage,
                Warning = "Some contact information was removed from your message. All communication must stay on CarePro.",
                ViolationLogged = true,
                WasRedacted = true
            };
        }

        public async Task<List<ChatViolationDTO>> GetViolationsAsync(int skip, int take, string? userId = null, string? violationType = null)
        {
            take = Math.Min(take, 100);

            var query = _dbContext.ChatViolations.AsQueryable();

            // Load into memory for filtering (MongoDB EF Core provider limitation)
            var violations = await query.OrderByDescending(v => v.CreatedAt).ToListAsync();

            if (!string.IsNullOrEmpty(userId))
                violations = violations.Where(v => v.UserId == userId).ToList();

            if (!string.IsNullOrEmpty(violationType))
                violations = violations.Where(v => v.ViolationType == violationType).ToList();

            return violations
                .Skip(skip)
                .Take(take)
                .Select(MapToDTO)
                .ToList();
        }

        public async Task<List<ChatViolationDTO>> GetRepeatOffendersAsync(int minViolations = 3, int days = 30)
        {
            var windowStart = DateTime.UtcNow.AddDays(-days);

            var violations = await _dbContext.ChatViolations.ToListAsync();

            var repeatOffenderIds = violations
                .Where(v => v.CreatedAt >= windowStart)
                .GroupBy(v => v.UserId)
                .Where(g => g.Count() >= minViolations)
                .SelectMany(g => g.OrderByDescending(v => v.CreatedAt))
                .Select(MapToDTO)
                .ToList();

            return repeatOffenderIds;
        }

        public async Task<ChatViolationDTO?> GetViolationByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return null;

            var violations = await _dbContext.ChatViolations.ToListAsync();
            var violation = violations.FirstOrDefault(v => v.Id == objectId);

            return violation != null ? MapToDTO(violation) : null;
        }

        private static ChatViolationDTO MapToDTO(ChatViolation v) => new()
        {
            Id = v.Id.ToString(),
            UserId = v.UserId,
            RecipientId = v.RecipientId,
            OriginalMessage = v.OriginalMessage,
            DetectedPatterns = v.DetectedPatterns,
            ViolationType = v.ViolationType,
            Action = v.Action,
            CreatedAt = v.CreatedAt
        };
    }
}
