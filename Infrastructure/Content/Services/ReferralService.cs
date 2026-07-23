using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class ReferralService : IReferralService
    {
        private const decimal ReferralDiscountAmount = 10000m;

        private readonly CareProDbContext _dbContext;
        private readonly IEmailService _emailService;
        private readonly ReferralSettings _referralSettings;
        private readonly ILogger<ReferralService> _logger;

        public ReferralService(
            CareProDbContext dbContext,
            IEmailService emailService,
            IOptions<ReferralSettings> referralSettings,
            ILogger<ReferralService> logger)
        {
            _dbContext = dbContext;
            _emailService = emailService;
            _referralSettings = referralSettings.Value;
            _logger = logger;
        }

        public async Task<Result<Referrer>> CreateReferrerAsync(CreateReferrerRequest request)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(request.FullName))
                errors.Add("FullName is required.");
            if (string.IsNullOrWhiteSpace(request.Email))
                errors.Add("Email is required.");

            var aliasValid = TryNormalizeAlias(request.Alias, out var normalizedAlias, out var aliasError);
            if (!aliasValid)
                errors.Add(aliasError!);

            if (errors.Any())
                return Result<Referrer>.Failure(errors);

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var existing = await _dbContext.Referrers.FirstOrDefaultAsync(r => r.Email.ToLower() == normalizedEmail);
            if (existing != null)
                return Result<Referrer>.Failure(new List<string> { "A referrer with this email already exists." });

            var referrer = new Referrer
            {
                Id = ObjectId.GenerateNewId(),
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNo = string.IsNullOrWhiteSpace(request.PhoneNo) ? null : request.PhoneNo.Trim(),
                Alias = normalizedAlias,
                Status = ReferrerStatus.Approved,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Referrers.Add(referrer);

            if (request.BankAccount != null)
            {
                var bank = new ReferrerBankAccount
                {
                    Id = ObjectId.GenerateNewId(),
                    ReferrerId = referrer.Id.ToString(),
                    FullName = request.BankAccount.FullName,
                    BankName = request.BankAccount.BankName,
                    AccountNumber = request.BankAccount.AccountNumber,
                    AccountName = request.BankAccount.AccountName,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.ReferrerBankAccounts.Add(bank);
            }

            await _dbContext.SaveChangesAsync();
            return Result<Referrer>.Success(referrer);
        }

        public async Task<Result<Referrer>> ApplyForReferrerAsync(ApplyForReferrerRequest request)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(request.FullName))
                errors.Add("FullName is required.");
            if (string.IsNullOrWhiteSpace(request.Email))
                errors.Add("Email is required.");

            var aliasValid = TryNormalizeAlias(request.Alias, out var normalizedAlias, out var aliasError);
            if (!aliasValid)
                errors.Add(aliasError!);

            if (errors.Any())
                return Result<Referrer>.Failure(errors);

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var existing = await _dbContext.Referrers.FirstOrDefaultAsync(r => r.Email.ToLower() == normalizedEmail);
            if (existing != null)
                return Result<Referrer>.Failure(new List<string> { "A referrer with this email already exists." });

            var referrer = new Referrer
            {
                Id = ObjectId.GenerateNewId(),
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNo = string.IsNullOrWhiteSpace(request.PhoneNo) ? null : request.PhoneNo.Trim(),
                Alias = normalizedAlias,
                Status = ReferrerStatus.PendingApproval,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Referrers.Add(referrer);
            await _dbContext.SaveChangesAsync();
            return Result<Referrer>.Success(referrer);
        }

        public async Task<Result<Referrer>> ApproveReferrerAsync(string referrerId)
        {
            var (referrer, error) = await GetPendingReferrerOrNullAsync(referrerId);
            if (referrer == null)
                return Result<Referrer>.Failure(new List<string> { error! });

            referrer.Status = ReferrerStatus.Approved;
            _dbContext.Referrers.Update(referrer);
            await _dbContext.SaveChangesAsync();
            return Result<Referrer>.Success(referrer);
        }

        public async Task<Result<Referrer>> RejectReferrerAsync(string referrerId)
        {
            var (referrer, error) = await GetPendingReferrerOrNullAsync(referrerId);
            if (referrer == null)
                return Result<Referrer>.Failure(new List<string> { error! });

            referrer.Status = ReferrerStatus.Rejected;
            _dbContext.Referrers.Update(referrer);
            await _dbContext.SaveChangesAsync();
            return Result<Referrer>.Success(referrer);
        }

        private async Task<(Referrer? Referrer, string? Error)> GetPendingReferrerOrNullAsync(string referrerId)
        {
            if (!ObjectId.TryParse(referrerId, out var referrerObjectId))
                return (null, "Invalid ReferrerId format.");

            var referrer = await _dbContext.Referrers.FindAsync(referrerObjectId);
            if (referrer == null)
                return (null, "Referrer not found.");

            if (referrer.Status != ReferrerStatus.PendingApproval)
                return (null, "Referrer is not pending approval.");

            return (referrer, null);
        }

        public async Task<Result<ReferralCode>> CreateReferralCodeAsync(string referrerId)
        {
            if (!ObjectId.TryParse(referrerId, out var referrerObjectId))
                return Result<ReferralCode>.Failure(new List<string> { "Invalid ReferrerId format." });

            var referrer = await _dbContext.Referrers.FindAsync(referrerObjectId);
            if (referrer == null)
                return Result<ReferralCode>.Failure(new List<string> { "Referrer not found." });

            if (referrer.Status != ReferrerStatus.Approved)
                return Result<ReferralCode>.Failure(new List<string> { "Referrer must be approved before generating a referral code." });

            var alias = referrer.Alias;
            if (string.IsNullOrWhiteSpace(alias))
                return Result<ReferralCode>.Failure(new List<string> { "Referrer does not have an alias set. An alias is required to generate a code." });

            string code;
            do
            {
                code = GenerateReferralCode(alias);
            }
            while (await _dbContext.ReferralCodes.AnyAsync(rc => rc.Code == code));

            var referralCode = new ReferralCode
            {
                Id = ObjectId.GenerateNewId(),
                Code = code,
                ReferrerId = referrer.Id.ToString(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _dbContext.ReferralCodes.Add(referralCode);
            await _dbContext.SaveChangesAsync();

            return Result<ReferralCode>.Success(referralCode);
        }

        public async Task<Result<bool>> SendReferralCodeEmailAsync(string referralCodeId)
        {
            if (!ObjectId.TryParse(referralCodeId, out var codeObjectId))
                return Result<bool>.Failure(new List<string> { "Invalid referral code ID format." });

            var code = await _dbContext.ReferralCodes.FindAsync(codeObjectId);
            if (code == null)
                return Result<bool>.Failure(new List<string> { "Referral code not found." });

            var referrer = await _dbContext.Referrers.FirstOrDefaultAsync(r => r.Id.ToString() == code.ReferrerId);
            if (referrer == null)
                return Result<bool>.Failure(new List<string> { "Referrer not found for this referral code." });

            if (string.IsNullOrWhiteSpace(referrer.Email))
                return Result<bool>.Failure(new List<string> { "Referrer has no email on file." });

            var firstName = referrer.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";

            try
            {
                await _emailService.SendReferralCodeEmailAsync(referrer.Email, firstName, code.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send referral code email for ReferralCodeId {ReferralCodeId}", code.Id);
                return Result<bool>.Failure(new List<string> { "Failed to send referral code email." });
            }

            return Result<bool>.Success(true);
        }

        public async Task<List<ReferrerListItem>> GetReferrersAsync(string? status = null)
        {
            var query = _dbContext.Referrers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            return await query
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReferrerListItem
                {
                    Id = r.Id.ToString(),
                    FullName = r.FullName,
                    Email = r.Email,
                    PhoneNo = r.PhoneNo,
                    Alias = r.Alias ?? string.Empty,
                    Status = r.Status ?? string.Empty,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<Result<ReferralCheckoutApplication>> ValidateReferralForCheckoutAsync(
            string clientId,
            string referralCode,
            string serviceType,
            decimal currentOrderFee)
        {
            if (string.IsNullOrWhiteSpace(referralCode))
                return Result<ReferralCheckoutApplication>.Failure(new List<string> { "Referral code is required." });

            if (string.Equals(serviceType, "one-time", StringComparison.OrdinalIgnoreCase))
            {
                return Result<ReferralCheckoutApplication>.Failure(new List<string>
                {
                    "Referral codes can only be used on recurring services."
                });
            }

            if (_referralSettings.PayoutAmount <= 0)
            {
                return Result<ReferralCheckoutApplication>.Failure(new List<string>
                {
                    "Referral payout amount is not configured yet. Please contact support."
                });
            }

            var normalizedCode = referralCode.Trim().ToUpperInvariant();
            var code = await _dbContext.ReferralCodes
                .FirstOrDefaultAsync(rc => rc.Code == normalizedCode && rc.IsActive);

            if (code == null)
                return Result<ReferralCheckoutApplication>.Failure(new List<string> { "Invalid or inactive referral code." });

            var referrer = await _dbContext.Referrers.FirstOrDefaultAsync(r => r.Id.ToString() == code.ReferrerId);
            if (referrer == null)
                return Result<ReferralCheckoutApplication>.Failure(new List<string> { "Referrer not found for this referral code." });

            var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == clientId);
            if (client == null)
                return Result<ReferralCheckoutApplication>.Failure(new List<string> { "Client not found." });

            bool emailMatch = !string.IsNullOrWhiteSpace(client.Email)
                              && !string.IsNullOrWhiteSpace(referrer.Email)
                              && string.Equals(client.Email.Trim(), referrer.Email.Trim(), StringComparison.OrdinalIgnoreCase);

            bool phoneMatch = !string.IsNullOrWhiteSpace(client.PhoneNo)
                              && !string.IsNullOrWhiteSpace(referrer.PhoneNo)
                              && string.Equals(NormalizePhone(client.PhoneNo), NormalizePhone(referrer.PhoneNo), StringComparison.OrdinalIgnoreCase);

            if (emailMatch || phoneMatch)
            {
                _logger.LogWarning(
                    "REFERRAL_ABUSE_SELF_REFERRAL blocked. ClientId: {ClientId}, ReferralCode: {Code}, EmailMatch: {EmailMatch}, PhoneMatch: {PhoneMatch}",
                    clientId, normalizedCode, emailMatch, phoneMatch);

                return Result<ReferralCheckoutApplication>.Failure(new List<string>
                {
                    "Self-referral is not allowed."
                });
            }

            // Acquisition rule: referral is only for first-time platform users.
            // Any prior order record (regardless of status) disqualifies eligibility.
            var hasAnyPreviousOrder = await _dbContext.ClientOrders
                .AnyAsync(o => o.ClientId == clientId);

            if (hasAnyPreviousOrder)
            {
                return Result<ReferralCheckoutApplication>.Failure(new List<string>
                {
                    "Referral codes are only available to first-time clients with no prior orders."
                });
            }

            var resultingOrderFee = currentOrderFee - ReferralDiscountAmount;
            if (resultingOrderFee <= 0)
            {
                return Result<ReferralCheckoutApplication>.Failure(new List<string>
                {
                    "This order is not eligible for referral discount because the payable amount would be zero or negative."
                });
            }

            return Result<ReferralCheckoutApplication>.Success(new ReferralCheckoutApplication
            {
                ReferralCodeId = code.Id.ToString(),
                ReferrerId = referrer.Id.ToString(),
                DiscountAmount = ReferralDiscountAmount
            });
        }

        public async Task<Result<ReferralRedemption>> CreateRedemptionAsync(
            string referralCodeId,
            string referrerId,
            string clientId,
            string orderId,
            decimal discountAmount)
        {
            if (_referralSettings.PayoutAmount <= 0)
            {
                return Result<ReferralRedemption>.Failure(new List<string>
                {
                    "Referral payout amount is not configured."
                });
            }

            var existingForClient = await _dbContext.ReferralRedemptions.AnyAsync(r => r.ClientId == clientId);
            if (existingForClient)
            {
                return Result<ReferralRedemption>.Failure(new List<string>
                {
                    "Client already has a referral redemption."
                });
            }

            var redemption = new ReferralRedemption
            {
                Id = ObjectId.GenerateNewId(),
                ReferralCodeId = referralCodeId,
                ClientId = clientId,
                OrderId = orderId,
                DiscountAmount = discountAmount,
                RedeemedAt = DateTime.UtcNow,
                PayoutStatus = ReferralPayoutStatus.Pending,
                PayoutAmount = _referralSettings.PayoutAmount
            };

            _dbContext.ReferralRedemptions.Add(redemption);
            await _dbContext.SaveChangesAsync();

            try
            {
                var referrer = await _dbContext.Referrers.FirstOrDefaultAsync(r => r.Id.ToString() == referrerId);
                if (referrer != null && !string.IsNullOrWhiteSpace(referrer.Email))
                {
                    var firstName = referrer.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Referrer";
                    var content =
                        $"Great news. Your referral code was used successfully for order {orderId}. " +
                        $"Your payout for this referral is pending month-end settlement.";

                    await _emailService.SendSystemNotificationEmailAsync(
                        referrer.Email,
                        firstName,
                        "Your referral code was used",
                        content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send referrer redemption email for RedemptionId {RedemptionId}", redemption.Id);
            }

            return Result<ReferralRedemption>.Success(redemption);
        }

        public async Task<Result<bool>> MarkRedemptionPaidAsync(string redemptionId)
        {
            if (!ObjectId.TryParse(redemptionId, out var redemptionObjectId))
                return Result<bool>.Failure(new List<string> { "Invalid redemption ID format." });

            var redemption = await _dbContext.ReferralRedemptions.FindAsync(redemptionObjectId);
            if (redemption == null)
                return Result<bool>.Failure(new List<string> { "Redemption not found." });

            redemption.PayoutStatus = ReferralPayoutStatus.Paid;
            redemption.PaidAt = DateTime.UtcNow;
            _dbContext.ReferralRedemptions.Update(redemption);
            await _dbContext.SaveChangesAsync();

            return Result<bool>.Success(true);
        }

        public async Task<List<ReferralRedemptionReportItem>> GetRedemptionsAsync(DateTime? startDate, DateTime? endDate)
        {
            var query = _dbContext.ReferralRedemptions.AsQueryable();

            if (startDate.HasValue)
                query = query.Where(r => r.RedeemedAt >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(r => r.RedeemedAt <= endDate.Value);

            var redemptions = await query
                .OrderByDescending(r => r.RedeemedAt)
                .ToListAsync();

            var referralCodeIds = redemptions.Select(r => r.ReferralCodeId).Distinct().ToList();
            var codes = await _dbContext.ReferralCodes
                .Where(rc => referralCodeIds.Contains(rc.Id.ToString()))
                .ToListAsync();

            var referrerIds = codes.Select(c => c.ReferrerId).Distinct().ToList();
            var referrers = await _dbContext.Referrers
                .Where(r => referrerIds.Contains(r.Id.ToString()))
                .ToListAsync();

            var codeMap = codes.ToDictionary(c => c.Id.ToString(), c => c);
            var referrerMap = referrers.ToDictionary(r => r.Id.ToString(), r => r);

            return redemptions.Select(r =>
            {
                codeMap.TryGetValue(r.ReferralCodeId, out var referralCode);
                Referrer? referrer = null;
                if (referralCode != null)
                    referrerMap.TryGetValue(referralCode.ReferrerId, out referrer);

                return new ReferralRedemptionReportItem
                {
                    RedemptionId = r.Id.ToString(),
                    ReferralCode = referralCode?.Code ?? string.Empty,
                    ReferrerName = referrer?.FullName ?? string.Empty,
                    ReferrerEmail = referrer?.Email ?? string.Empty,
                    ReferrerPhoneNo = referrer?.PhoneNo,
                    ClientId = r.ClientId,
                    OrderId = r.OrderId,
                    DiscountAmount = r.DiscountAmount,
                    PayoutStatus = r.PayoutStatus,
                    PayoutAmount = r.PayoutAmount,
                    RedeemedAt = r.RedeemedAt,
                    PaidAt = r.PaidAt
                };
            }).ToList();
        }

        private static string GenerateReferralCode(string alias)
        {
            var suffix = Random.Shared.Next(0, 10000).ToString("D4");
            return $"{alias.ToUpperInvariant()}{suffix}";
        }

        private static bool TryNormalizeAlias(string? alias, out string normalizedAlias, out string? error)
        {
            normalizedAlias = string.Empty;

            if (string.IsNullOrWhiteSpace(alias))
            {
                error = "Alias is required.";
                return false;
            }

            var trimmed = alias.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 20 || !trimmed.All(char.IsLetterOrDigit))
            {
                error = "Alias must be 3-20 alphanumeric characters (letters and numbers only).";
                return false;
            }

            normalizedAlias = trimmed.ToLowerInvariant();
            error = null;
            return true;
        }

        private static string NormalizePhone(string phone)
        {
            return new string(phone.Where(char.IsDigit).ToArray());
        }
    }
}
