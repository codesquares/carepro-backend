using Application.DTOs;

namespace Application.Interfaces.Common
{
    public interface IDojahDataFormattingService
    {
        AddVerificationRequest FormatWebhookData(DojahWebhookRequest webhook, string userId);
    }
}