using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class WebhookLogService : IWebhookLogService
    {
        private readonly CareProDbContext _context;
        private readonly ICareGiverService _careGiverService;
        private readonly IVerificationService _verificationService;
        private readonly ILogger<WebhookLogService> _logger;

        public WebhookLogService(
            CareProDbContext context,
            ICareGiverService careGiverService,
            IVerificationService verificationService,
            ILogger<WebhookLogService> logger)
        {
            _context = context;
            _careGiverService = careGiverService;
            _verificationService = verificationService;
            _logger = logger;
        }

        public async Task<string> StoreRawWebhookAsync(
            string rawPayload,
            Dictionary<string, string> headers,
            string clientIp,
            string userId,
            string webhookType = "verification")
        {
            try
            {
                var webhookLog = new WebhookLog
                {
                    Id = ObjectId.GenerateNewId(),
                    UserId = userId,
                    RawPayload = rawPayload,
                    WebhookType = webhookType,
                    ReceivedAt = DateTime.UtcNow,
                    Status = "received",
                    ClientIp = clientIp,
                    Headers = headers,
                    Signature = headers.ContainsKey("X-Dojah-Signature") ? headers["X-Dojah-Signature"] : 
                               headers.ContainsKey("x-dojah-signature") ? headers["x-dojah-signature"] : string.Empty
                };

                await _context.WebhookLogs.AddAsync(webhookLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Stored webhook log with ID: {WebhookLogId} for user: {UserId}", 
                    webhookLog.Id.ToString(), userId);

                return webhookLog.Id.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing webhook log for user: {UserId}", userId);
                throw;
            }
        }

        public async Task<WebhookLogResponse?> GetWebhookLogAsync(string webhookLogId)
        {
            try
            {
                if (!ObjectId.TryParse(webhookLogId, out var objectId))
                {
                    return null;
                }

                var webhookLog = await _context.WebhookLogs.FindAsync(objectId);
                if (webhookLog == null)
                {
                    return null;
                }

                return new WebhookLogResponse
                {
                    Id = webhookLog.Id.ToString(),
                    UserId = webhookLog.UserId,
                    WebhookType = webhookLog.WebhookType,
                    ReceivedAt = webhookLog.ReceivedAt,
                    ProcessedAt = webhookLog.ProcessedAt,
                    Status = webhookLog.Status,
                    VerificationId = webhookLog.VerificationId,
                    ClientIp = webhookLog.ClientIp,
                    HasRawData = !string.IsNullOrEmpty(webhookLog.RawPayload)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webhook log: {WebhookLogId}", webhookLogId);
                return null;
            }
        }

        public async Task<List<WebhookLogResponse>> GetWebhookLogsByUserIdAsync(string userId)
        {
            try
            {
                var webhookLogs = await _context.WebhookLogs
                    .Where(w => w.UserId == userId)
                    .OrderByDescending(w => w.ReceivedAt)
                    .ToListAsync();

                return webhookLogs.Select(w => new WebhookLogResponse
                {
                    Id = w.Id.ToString(),
                    UserId = w.UserId,
                    WebhookType = w.WebhookType,
                    ReceivedAt = w.ReceivedAt,
                    ProcessedAt = w.ProcessedAt,
                    Status = w.Status,
                    VerificationId = w.VerificationId,
                    ClientIp = w.ClientIp,
                    HasRawData = !string.IsNullOrEmpty(w.RawPayload)
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webhook logs for user: {UserId}", userId);
                return new List<WebhookLogResponse>();
            }
        }

        public async Task<ParsedWebhookDataResponse?> GetParsedWebhookDataAsync(string webhookLogId)
        {
            try
            {
                if (!ObjectId.TryParse(webhookLogId, out var objectId))
                {
                    return null;
                }

                var webhookLog = await _context.WebhookLogs.FindAsync(objectId);
                if (webhookLog == null)
                {
                    _logger.LogWarning("Webhook log not found: {WebhookLogId}", webhookLogId);
                    return null;
                }

                // Parse the raw JSON
                var parsedData = ParseWebhookData(webhookLog.RawPayload);

                // Fetch caregiver profile for comparison
                var caregiverProfile = await GetCaregiverProfileData(webhookLog.UserId);

                return new ParsedWebhookDataResponse
                {
                    WebhookLogId = webhookLog.Id.ToString(),
                    UserId = webhookLog.UserId,
                    ReceivedAt = webhookLog.ReceivedAt,
                    ParsedData = parsedData,
                    RawPayload = webhookLog.RawPayload,
                    RegisteredProfile = caregiverProfile
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing webhook data for log: {WebhookLogId}", webhookLogId);
                return null;
            }
        }

        public async Task UpdateWebhookLogStatusAsync(
            string webhookLogId,
            string status,
            string? verificationId = null,
            string? processingNotes = null)
        {
            try
            {
                if (!ObjectId.TryParse(webhookLogId, out var objectId))
                {
                    return;
                }

                var webhookLog = await _context.WebhookLogs.FindAsync(objectId);
                if (webhookLog == null)
                {
                    return;
                }

                webhookLog.Status = status;
                webhookLog.ProcessedAt = DateTime.UtcNow;
                
                if (!string.IsNullOrEmpty(verificationId))
                {
                    webhookLog.VerificationId = verificationId;
                }

                if (!string.IsNullOrEmpty(processingNotes))
                {
                    webhookLog.ProcessingNotes = processingNotes;
                }

                _context.WebhookLogs.Update(webhookLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Updated webhook log {WebhookLogId} status to: {Status}", 
                    webhookLogId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating webhook log status: {WebhookLogId}", webhookLogId);
            }
        }

        public async Task<List<PendingVerificationReviewResponse>> GetPendingVerificationsForReviewAsync()
        {
            try
            {
                // Get all non-verified verifications
                var pendingVerifications = await _context.Verifications
                    .Where(v => !v.IsVerified)
                    .OrderByDescending(v => v.VerifiedOn)
                    .ToListAsync();

                var result = new List<PendingVerificationReviewResponse>();

                foreach (var verification in pendingVerifications)
                {
                    try
                    {
                        // Fetch caregiver details
                        var caregiver = await _careGiverService.GetCaregiverUserAsync(verification.UserId);
                        
                        // Find latest webhook log for this user
                        var latestWebhookLog = await _context.WebhookLogs
                            .Where(w => w.UserId == verification.UserId && w.WebhookType == "verification")
                            .OrderByDescending(w => w.ReceivedAt)
                            .FirstOrDefaultAsync();

                        result.Add(new PendingVerificationReviewResponse
                        {
                            UserId = verification.UserId,
                            CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}",
                            CaregiverEmail = caregiver.Email,
                            VerificationId = verification.VerificationId.ToString(),
                            VerificationStatus = verification.VerificationStatus,
                            VerificationMethod = verification.VerificationMethod,
                            IsVerified = verification.IsVerified,
                            VerifiedOn = verification.VerifiedOn,
                            WebhookLogId = latestWebhookLog?.Id.ToString(),
                            HasRawData = latestWebhookLog != null && !string.IsNullOrEmpty(latestWebhookLog.RawPayload)
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error fetching details for verification: {VerificationId}", 
                            verification.VerificationId);
                        // Continue with other verifications
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending verifications for review");
                return new List<PendingVerificationReviewResponse>();
            }
        }

        private WebhookParsedData ParseWebhookData(string rawPayload)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var webhook = JsonSerializer.Deserialize<DojahWebhookRequest>(rawPayload, options);
                if (webhook == null)
                {
                    return new WebhookParsedData();
                }

                var parsedData = new WebhookParsedData
                {
                    VerificationStatus = webhook.VerificationStatus,
                    VerificationMethod = webhook.VerificationType,
                    IdType = webhook.IdType,
                    Message = webhook.Message,
                    VerificationNo = webhook.Value
                };

                // Extract nested data from BVN or NIN
                if (webhook.Data?.GovernmentData?.Data?.Bvn?.Entity != null)
                {
                    var entity = webhook.Data.GovernmentData.Data.Bvn.Entity;
                    if (entity != null)
                    {
                        parsedData.VerifiedName = new VerifiedNameData
                        {
                            FirstName = entity.FirstName ?? string.Empty,
                            LastName = entity.LastName ?? string.Empty,
                            MiddleName = entity.MiddleName ?? string.Empty
                        };
                        parsedData.VerifiedDetails = new VerifiedDetailsData
                        {
                            DateOfBirth = entity.DateOfBirth ?? string.Empty,
                            PhoneNumber = entity.PhoneNumber ?? string.Empty,
                            Gender = entity.Gender ?? string.Empty
                        };
                    }
                }
                else if (webhook.Data?.GovernmentData?.Data?.Nin?.Entity != null)
                {
                    var entity = webhook.Data.GovernmentData.Data.Nin.Entity;
                    if (entity != null)
                    {
                        parsedData.VerifiedName = new VerifiedNameData
                        {
                            FirstName = entity.FirstName ?? string.Empty,
                            LastName = entity.LastName ?? string.Empty,
                            MiddleName = entity.MiddleName ?? string.Empty
                        };
                        parsedData.VerifiedDetails = new VerifiedDetailsData
                        {
                            DateOfBirth = entity.DateOfBirth ?? string.Empty,
                            PhoneNumber = entity.PhoneNumber ?? string.Empty,
                            Gender = entity.Gender ?? string.Empty
                        };
                    }
                }
                else if (webhook.Data?.UserData?.Data != null)
                {
                    var userData = webhook.Data.UserData.Data;
                    parsedData.VerifiedName = new VerifiedNameData
                    {
                        FirstName = userData.FirstName ?? string.Empty,
                        LastName = userData.LastName ?? string.Empty,
                        MiddleName = string.Empty
                    };
                    parsedData.VerifiedDetails = new VerifiedDetailsData
                    {
                        DateOfBirth = userData.Dob ?? string.Empty,
                        PhoneNumber = string.Empty,
                        Gender = string.Empty
                    };
                }

                return parsedData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing webhook raw payload");
                return new WebhookParsedData();
            }
        }

        private async Task<CaregiverProfileData> GetCaregiverProfileData(string userId)
        {
            try
            {
                var caregiver = await _careGiverService.GetCaregiverUserAsync(userId);
                
                return new CaregiverProfileData
                {
                    FirstName = caregiver.FirstName ?? string.Empty,
                    LastName = caregiver.LastName ?? string.Empty,
                    MiddleName = caregiver.MiddleName ?? string.Empty,
                    PhoneNumber = caregiver.PhoneNo ?? string.Empty,
                    Email = caregiver.Email ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching caregiver profile for user: {UserId}", userId);
                return new CaregiverProfileData();
            }
        }
    }
}
