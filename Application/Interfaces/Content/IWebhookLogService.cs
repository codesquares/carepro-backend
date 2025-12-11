using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IWebhookLogService
    {
        Task<string> StoreRawWebhookAsync(string rawPayload, Dictionary<string, string> headers, string clientIp, string userId, string webhookType = "verification");
        
        Task<WebhookLogResponse?> GetWebhookLogAsync(string webhookLogId);
        
        Task<List<WebhookLogResponse>> GetWebhookLogsByUserIdAsync(string userId);
        
        Task<ParsedWebhookDataResponse?> GetParsedWebhookDataAsync(string webhookLogId);
        
        Task UpdateWebhookLogStatusAsync(string webhookLogId, string status, string? verificationId = null, string? processingNotes = null);
        
        Task<List<PendingVerificationReviewResponse>> GetPendingVerificationsForReviewAsync();
    }
}
