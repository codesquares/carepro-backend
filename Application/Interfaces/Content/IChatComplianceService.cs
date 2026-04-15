using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IChatComplianceService
    {
        Task<ComplianceResult> EvaluateMessageAsync(string senderId, string receiverId, string rawMessage);
        Task<List<ChatViolationDTO>> GetViolationsAsync(int skip, int take, string? userId = null, string? violationType = null);
        Task<List<ChatViolationDTO>> GetRepeatOffendersAsync(int minViolations = 3, int days = 30);
        Task<ChatViolationDTO?> GetViolationByIdAsync(string id);
    }
}
