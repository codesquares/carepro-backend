using Application.DTOs;
using Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Phase 10 — the renewal-charge engine for recurring Package billing. Structurally
    /// repoints <c>SubscriptionService</c>'s renewal mechanics (idempotency key, Charging-state
    /// double-charge guard, server-to-server verification, amount verification, retry/backoff,
    /// failure classification) onto <see cref="PackageSubscription"/> instead of the Gig-anchored
    /// <see cref="Subscription"/>. Deliberately never touches <c>IClientOrderService</c> or
    /// derives a CaregiverId — see <see cref="PackageSubscription"/>'s own remarks.
    /// </summary>
    public interface IPackageSubscriptionService
    {
        /// <summary>Every PackageSubscription whose NextChargeDate has arrived and which has a
        /// stored payment token — the set a recurring-billing background sweep should process.</summary>
        Task<List<PackageSubscription>> GetPackageSubscriptionsDueForBillingAsync();

        /// <summary>Charges the next cycle for one PackageSubscription. Safe to call repeatedly —
        /// idempotency-keyed per (subscription, cycle, attempt), and guarded against concurrent
        /// runs via the Charging status.</summary>
        Task<Result<PackageSubscriptionPaymentRecordDTO>> ProcessRecurringPackageChargeAsync(string packageSubscriptionId, string initiatedBy = "system");

        Task<PackageSubscription?> GetByPackageRequestIdAsync(string packageRequestId);

        /// <summary>Creates the PackageSubscription record for a just-completed first charge on
        /// a Recurring PackageRequest. Called by PackagePaymentService.CompleteRecurringPackagePaymentAsync
        /// after token capture — never called directly from a controller.</summary>
        Task<PackageSubscription> CreatePackageSubscriptionAsync(CreatePackageSubscriptionRequest request);

        /// <summary>The Flutterwave processing fee (1.4%, capped at ₦2,000) that is added on top of a package
        /// price for every charge — the first payment and each renewal.</summary>
        decimal CalculateGatewayFee(decimal baseAmount);

        /// <summary>All of one client's package subscriptions, newest first (list projection, no payment history).</summary>
        Task<List<PackageSubscriptionDTO>> GetClientSubscriptionsAsync(string clientId);

        /// <summary>One subscription with its payment history, or null when it does not exist or belongs to someone else.</summary>
        Task<PackageSubscriptionDTO?> GetClientSubscriptionAsync(string clientId, string packageSubscriptionId);

        /// <summary>Client stops auto-renewal. Service they already paid for continues to the end of the current
        /// period; no further charge is ever made. Idempotent. Fails when the subscription is not the client's.</summary>
        Task<Result<PackageSubscriptionDTO>> CancelClientSubscriptionAsync(string clientId, string packageSubscriptionId, string? reason);
    }
}
