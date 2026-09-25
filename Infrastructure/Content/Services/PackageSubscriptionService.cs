using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Phase 10 renewal-charge engine for recurring Package billing. Mechanically repoints
    /// <c>SubscriptionService.ProcessRecurringChargeInternalAsync</c>'s safety machinery
    /// (idempotency key, Charging-state double-charge guard, server-to-server verification,
    /// amount verification, exponential-backoff retry, failure classification) onto
    /// <see cref="PackageSubscription"/>.
    ///
    /// Deliberately different from the Gig version in two structural ways, both intentional:
    /// (1) no ClientOrder is ever created — there's no per-Gig order concept for Package
    /// billing, so each cycle's outcome is recorded directly on PaymentHistory; (2) only the
    /// v3 token+email charge path is supported (ChargeWithToken) — the CustomerId/PaymentMethodId
    /// fallback path exists for Gig only because of its card-update flow, which Package
    /// recurring billing doesn't have yet.
    /// </summary>
    public class PackageSubscriptionService : IPackageSubscriptionService
    {
        private readonly CareProDbContext _dbContext;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly IMediator _mediator;
        private readonly ILogger<PackageSubscriptionService> _logger;

        private const double RETRY_JITTER_PERCENT = 0.20d;

        public PackageSubscriptionService(
            CareProDbContext dbContext,
            FlutterwaveService flutterwaveService,
            IMediator mediator,
            ILogger<PackageSubscriptionService> logger)
        {
            _dbContext = dbContext;
            _flutterwaveService = flutterwaveService;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<List<PackageSubscription>> GetPackageSubscriptionsDueForBillingAsync()
        {
            var now = DateTime.UtcNow;
            return await _dbContext.PackageSubscriptions
                .Where(s =>
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue) &&
                    s.AutoRenew &&
                    s.NextChargeDate.HasValue &&
                    s.NextChargeDate.Value <= now &&
                    !string.IsNullOrEmpty(s.FlutterwavePaymentToken))
                .ToListAsync();
        }

        public async Task<PackageSubscription?> GetByPackageRequestIdAsync(string packageRequestId)
        {
            return await _dbContext.PackageSubscriptions
                .FirstOrDefaultAsync(s => s.PackageRequestId == packageRequestId);
        }

        public async Task<PackageSubscription> CreatePackageSubscriptionAsync(CreatePackageSubscriptionRequest request)
        {
            var now = DateTime.UtcNow;
            var subscription = new PackageSubscription
            {
                Id = ObjectId.GenerateNewId(),
                PackageRequestId = request.PackageRequestId,
                ClientId = request.ClientId,
                RecurringAmount = request.RecurringAmount,
                Currency = request.Currency,
                Email = request.Email,
                Status = SubscriptionStatus.Active,
                // The charge that triggered this creation is the purchase itself, not a
                // "renewal" — BillingCyclesCompleted stays 0 here, same convention the Gig flow
                // uses (CreateSubscriptionForRecurringPaymentAsync never increments it either;
                // only an actual ProcessRecurringChargeInternalAsync renewal does).
                BillingCyclesCompleted = 0,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = now.AddDays(30),
                NextChargeDate = now.AddDays(30),
                AutoRenew = true,
                FlutterwavePaymentToken = request.FlutterwavePaymentToken,
                CardLastFour = request.CardLastFour,
                CardBrand = request.CardBrand,
                CardExpiry = request.CardExpiry,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.PackageSubscriptions.Add(subscription);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "PackageSubscription {Id} created for PackageRequest {PackageRequestId}. HasToken={HasToken}",
                subscription.Id, request.PackageRequestId, !string.IsNullOrWhiteSpace(request.FlutterwavePaymentToken));

            return subscription;
        }

        public async Task<Result<PackageSubscriptionPaymentRecordDTO>> ProcessRecurringPackageChargeAsync(string packageSubscriptionId, string initiatedBy = "system")
        {
            if (!ObjectId.TryParse(packageSubscriptionId, out var subOid))
                return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Invalid package subscription id." });

            var subscription = await _dbContext.PackageSubscriptions.FirstOrDefaultAsync(s => s.Id == subOid);
            if (subscription == null)
                return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Package subscription not found." });

            if (string.IsNullOrWhiteSpace(subscription.FlutterwavePaymentToken) || string.IsNullOrWhiteSpace(subscription.Email))
            {
                _logger.LogWarning(
                    "Recurring package charge cannot start for PackageSubscription {Id}: missing token or email.",
                    packageSubscriptionId);
                return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "No recurring payment credentials available." });
            }

            var cycleNumber = subscription.BillingCyclesCompleted + 1;
            var attemptNumber = Math.Max(1, subscription.FailedChargeAttempts + 1);
            var recurringAttemptKey = $"pkg-renewal:{subscription.Id}:{cycleNumber}:{attemptNumber}";

            // ── IDEMPOTENCY GUARD ── same cycle+attempt key must never charge twice.
            var existingAttempt = subscription.PaymentHistory
                .Where(p => p.RecurringAttemptKey == recurringAttemptKey)
                .OrderByDescending(p => p.AttemptedAt)
                .FirstOrDefault();

            if (existingAttempt != null)
            {
                _logger.LogWarning(
                    "Recurring package idempotency hit for PackageSubscription {Id}. AttemptKey={AttemptKey}, ExistingStatus={Status}",
                    packageSubscriptionId, recurringAttemptKey, existingAttempt.Status);

                if (string.Equals(existingAttempt.Status, "successful", StringComparison.OrdinalIgnoreCase))
                    return Result<PackageSubscriptionPaymentRecordDTO>.Success(MapPaymentRecordToDTO(existingAttempt));

                return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Renewal attempt already processed. Await next scheduled retry." });
            }

            // ── DOUBLE-CHARGE GUARD ── skip if a charge is already in progress for this subscription.
            if (subscription.Status == SubscriptionStatus.Charging)
            {
                _logger.LogWarning(
                    "SECURITY: Skipping PackageSubscription {Id} — charge already in progress (status=Charging).",
                    packageSubscriptionId);
                return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Charge already in progress." });
            }

            var previousStatus = subscription.Status;
            subscription.Status = SubscriptionStatus.Charging;
            subscription.LastRecurringAttemptKey = recurringAttemptKey;
            subscription.LastRecurringAttemptStatus = "pending";
            subscription.LastRecurringAttemptAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var txRef = $"CAREPRO-PKGSUB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

            var paymentRecord = new PackageSubscriptionPaymentRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TransactionReference = txRef,
                RecurringAttemptKey = recurringAttemptKey,
                Amount = subscription.RecurringAmount,
                Currency = subscription.Currency,
                Status = "pending",
                InitiatedBy = string.IsNullOrWhiteSpace(initiatedBy) ? "system" : initiatedBy,
                BillingCycleNumber = cycleNumber,
                AttemptedAt = DateTime.UtcNow
            };

            try
            {
                var chargeResult = await _flutterwaveService.ChargeWithToken(
                    subscription.FlutterwavePaymentToken!,
                    subscription.RecurringAmount,
                    subscription.Currency,
                    subscription.Email,
                    txRef);

                if (chargeResult == null || !chargeResult.Success)
                {
                    if (chargeResult?.IsPending == true)
                    {
                        // 3DS/OTP required — not a permanent failure. No dedicated webhook route
                        // exists yet for a Package renewal's 3DS follow-up, so this parks as
                        // "pending" and the next scheduled sweep will pick it up again once the
                        // idempotency key check above lets a fresh attempt through.
                        paymentRecord.Status = "pending";
                        paymentRecord.ErrorMessage = "Awaiting cardholder authentication";
                        paymentRecord.AuthorizationUrl = chargeResult.AuthUrl;
                        subscription.PaymentHistory.Add(paymentRecord);
                        subscription.Status = previousStatus;
                        subscription.LastRecurringAttemptStatus = "pending";
                        subscription.LastRecurringAttemptAt = DateTime.UtcNow;
                        subscription.UpdatedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();

                        _logger.LogInformation(
                            "PackageSubscription {Id} charge requires 3DS. AuthUrl: {AuthUrl}",
                            packageSubscriptionId, chargeResult.AuthUrl);

                        return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Awaiting cardholder authentication." });
                    }

                    var error = chargeResult?.ErrorMessage ?? "Charge failed";
                    paymentRecord.Status = "failed";
                    paymentRecord.ErrorMessage = error;
                    paymentRecord.FailureClass = ClassifyFailure(error);
                    subscription.PaymentHistory.Add(paymentRecord);
                    subscription.Status = previousStatus;
                    subscription.LastRecurringAttemptStatus = "failed";
                    subscription.LastRecurringAttemptAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    await HandleFailedChargeAsync(packageSubscriptionId, error);
                    return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { error });
                }

                // ── SERVER-TO-SERVER VERIFICATION ── don't trust the charge response alone.
                var verification = await _flutterwaveService.VerifyTransactionAsync(chargeResult.TransactionId);
                if (verification == null || !verification.Success ||
                    (verification.Status.ToLower() != "successful" && verification.Status.ToLower() != "succeeded"))
                {
                    _logger.LogCritical(
                        "SECURITY: Tokenized package charge for {Id} returned success but server verification FAILED. ChargeTransactionId: {TxId}, VerifyStatus: {Status}",
                        packageSubscriptionId, chargeResult.TransactionId, verification?.Status ?? "null");

                    paymentRecord.Status = "verification_failed";
                    paymentRecord.ErrorMessage = "Charge reported success but verification failed";
                    paymentRecord.FailureClass = "retryable";
                    subscription.PaymentHistory.Add(paymentRecord);
                    subscription.Status = previousStatus;
                    subscription.LastRecurringAttemptStatus = "failed";
                    subscription.LastRecurringAttemptAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    await HandleFailedChargeAsync(packageSubscriptionId, "Charge verification failed");
                    return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment verification failed. Will retry." });
                }

                // ── AMOUNT VERIFICATION ──
                if (Math.Abs(verification.Amount - subscription.RecurringAmount) > 0.01m)
                {
                    _logger.LogCritical(
                        "SECURITY: AMOUNT MISMATCH on recurring package charge! PackageSubscription {Id}, Expected: {Expected}, Verified: {Verified}",
                        packageSubscriptionId, subscription.RecurringAmount, verification.Amount);

                    paymentRecord.Status = "amount_mismatch";
                    paymentRecord.ErrorMessage = $"Expected {subscription.RecurringAmount}, verified {verification.Amount}";
                    paymentRecord.FailureClass = "non_retryable";
                    subscription.PaymentHistory.Add(paymentRecord);
                    subscription.Status = previousStatus;
                    subscription.LastRecurringAttemptStatus = "failed";
                    subscription.LastRecurringAttemptAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    await HandleFailedChargeAsync(packageSubscriptionId, "Payment amount mismatch detected");
                    return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment amount mismatch detected." });
                }

                // Charge verified. No ClientOrder to create — the outcome lives on PaymentHistory.
                paymentRecord.Status = "successful";
                paymentRecord.FlutterwaveTransactionId = chargeResult.TransactionId;
                paymentRecord.CompletedAt = DateTime.UtcNow;

                var now = DateTime.UtcNow;
                subscription.CurrentPeriodStart = now;
                subscription.CurrentPeriodEnd = now.AddDays(30); // Monthly billing only, same as Gig
                subscription.NextChargeDate = subscription.CurrentPeriodEnd;
                subscription.BillingCyclesCompleted = cycleNumber;
                subscription.FailedChargeAttempts = 0;
                subscription.LastChargeError = null;
                subscription.LastChargeFailureClass = null;
                subscription.Status = SubscriptionStatus.Active; // Restore from Charging → Active
                subscription.LastRecurringAttemptStatus = "successful";
                subscription.LastRecurringAttemptAt = now;
                subscription.PaymentHistory.Add(paymentRecord);
                subscription.UpdatedAt = now;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Recurring package charge successful for PackageSubscription {Id}. Cycle #{Cycle}, Amount: {Amount}",
                    packageSubscriptionId, cycleNumber, subscription.RecurringAmount);

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.RecurringPaymentSuccessful,
                    $"Your recurring package payment of {subscription.Currency} {subscription.RecurringAmount:N2} was successful. " +
                    $"Next charge: {subscription.CurrentPeriodEnd:MMM dd, yyyy}.",
                    "Payment Successful",
                    subscription.PackageRequestId));

                return Result<PackageSubscriptionPaymentRecordDTO>.Success(MapPaymentRecordToDTO(paymentRecord));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing recurring package charge for PackageSubscription {Id}", packageSubscriptionId);

                subscription.Status = previousStatus;
                paymentRecord.Status = "failed";
                paymentRecord.ErrorMessage = ex.Message;
                paymentRecord.FailureClass = "retryable";
                subscription.PaymentHistory.Add(paymentRecord);
                subscription.LastRecurringAttemptStatus = "failed";
                subscription.LastRecurringAttemptAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                await HandleFailedChargeAsync(packageSubscriptionId, ex.Message);
                return Result<PackageSubscriptionPaymentRecordDTO>.Failure(new List<string> { "Payment processing error. Will retry." });
            }
        }

        private async Task HandleFailedChargeAsync(string packageSubscriptionId, string errorMessage)
        {
            if (!ObjectId.TryParse(packageSubscriptionId, out var subOid)) return;
            var subscription = await _dbContext.PackageSubscriptions.FirstOrDefaultAsync(s => s.Id == subOid);
            if (subscription == null) return;

            var failureClass = ClassifyFailure(errorMessage);
            subscription.FailedChargeAttempts++;
            subscription.LastChargeError = errorMessage;
            subscription.LastChargeFailureClass = failureClass;
            subscription.LastFailedChargeAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;

            if (failureClass == "action_required")
            {
                subscription.Status = SubscriptionStatus.PastDue;
                subscription.NextChargeDate = null;

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.PaymentActionRequired,
                    "Your recurring package payment needs your card authorization. Automatic retries are paused.",
                    "Payment Authorisation Required",
                    subscription.PackageRequestId));

                await _dbContext.SaveChangesAsync();
                return;
            }

            if (failureClass == "non_retryable")
            {
                subscription.Status = SubscriptionStatus.Suspended;
                subscription.NextChargeDate = null;

                _logger.LogWarning(
                    "PackageSubscription {Id} SUSPENDED due to non-retryable payment failure. Error: {Error}",
                    packageSubscriptionId, errorMessage);

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.SubscriptionSuspended,
                    "Your recurring package subscription has been suspended because the payment method is no longer valid. Please contact support.",
                    "Subscription Suspended",
                    subscription.PackageRequestId));

                await _dbContext.SaveChangesAsync();
                return;
            }

            if (subscription.FailedChargeAttempts >= subscription.MaxRetryAttempts)
            {
                subscription.Status = SubscriptionStatus.Suspended;
                subscription.NextChargeDate = null;

                _logger.LogWarning(
                    "PackageSubscription {Id} SUSPENDED after {Attempts} failed charge attempts. Last error: {Error}",
                    packageSubscriptionId, subscription.FailedChargeAttempts, errorMessage);

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.SubscriptionSuspended,
                    "Your recurring package subscription has been suspended due to repeated payment failures. Please contact support.",
                    "Subscription Suspended",
                    subscription.PackageRequestId));
            }
            else
            {
                // Exponential backoff + jitter, same formula as the Gig renewal engine.
                var retryHours = Math.Pow(4, subscription.FailedChargeAttempts - 1);
                var jitterMultiplier = 1 + ((Random.Shared.NextDouble() * 2 * RETRY_JITTER_PERCENT) - RETRY_JITTER_PERCENT);
                var retryHoursWithJitter = Math.Max(0.25, retryHours * jitterMultiplier);
                subscription.NextChargeDate = DateTime.UtcNow.AddHours(retryHoursWithJitter);
                subscription.Status = SubscriptionStatus.PastDue;

                _logger.LogWarning(
                    "PackageSubscription {Id} charge failed (attempt {Attempt}/{Max}). Retry at {RetryTime}. Error: {Error}",
                    packageSubscriptionId, subscription.FailedChargeAttempts, subscription.MaxRetryAttempts,
                    subscription.NextChargeDate, errorMessage);

                await _mediator.Send(new SendNotificationCommand(
                    subscription.ClientId,
                    "system",
                    NotificationTypes.PaymentFailed,
                    $"Your ₦{subscription.RecurringAmount:N2} recurring package payment failed: {errorMessage}. We'll retry automatically.",
                    "Payment Failed",
                    subscription.PackageRequestId));
            }

            await _dbContext.SaveChangesAsync();
        }

        private static string ClassifyFailure(string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage)) return "retryable";

            var normalized = errorMessage.ToLowerInvariant();

            var nonRetryableMarkers = new[]
            {
                "wrong token or email",
                "invalid token",
                "token or email passed",
                "no recurring payment credentials",
                "payment method is no longer valid",
                "payment amount mismatch",
                "amount mismatch",
                "invalid payment method"
            };

            var actionRequiredMarkers = new[]
            {
                "awaiting cardholder authentication",
                "cardholder browser session timed out",
                "authentication required",
                "3ds",
                "vbv"
            };

            if (actionRequiredMarkers.Any(marker => normalized.Contains(marker)))
                return "action_required";

            return nonRetryableMarkers.Any(marker => normalized.Contains(marker))
                ? "non_retryable"
                : "retryable";
        }

        private static PackageSubscriptionPaymentRecordDTO MapPaymentRecordToDTO(PackageSubscriptionPaymentRecord p) => new()
        {
            Id = p.Id,
            TransactionReference = p.TransactionReference,
            FlutterwaveTransactionId = p.FlutterwaveTransactionId,
            Amount = p.Amount,
            Currency = p.Currency,
            Status = p.Status,
            ErrorMessage = p.ErrorMessage,
            AuthorizationUrl = p.AuthorizationUrl,
            InitiatedBy = p.InitiatedBy,
            BillingCycleNumber = p.BillingCycleNumber,
            AttemptedAt = p.AttemptedAt,
            CompletedAt = p.CompletedAt
        };
    }
}
