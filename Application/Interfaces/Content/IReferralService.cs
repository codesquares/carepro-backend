using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces.Content
{
    public interface IReferralService
    {
        Task<Result<Referrer>> CreateReferrerAsync(CreateReferrerRequest request);
        Task<Result<Referrer>> ApplyForReferrerAsync(ApplyForReferrerRequest request);
        Task<Result<Referrer>> ApproveReferrerAsync(string referrerId);
        Task<Result<Referrer>> RejectReferrerAsync(string referrerId);
        Task<Result<ReferralCode>> CreateReferralCodeAsync(string referrerId);
        Task<Result<bool>> SendReferralCodeEmailAsync(string referralCodeId);
        Task<List<ReferrerListItem>> GetReferrersAsync(string? status = null);
        Task<Result<ReferralCheckoutApplication>> ValidateReferralForCheckoutAsync(
            string clientId,
            string referralCode,
            string serviceType,
            decimal currentOrderFee);
        Task<Result<ReferralRedemption>> CreateRedemptionAsync(
            string referralCodeId,
            string referrerId,
            string clientId,
            string orderId,
            decimal discountAmount);
        Task<Result<bool>> MarkRedemptionPaidAsync(string redemptionId);
        Task<List<ReferralRedemptionReportItem>> GetRedemptionsAsync(DateTime? startDate, DateTime? endDate);
    }
}
