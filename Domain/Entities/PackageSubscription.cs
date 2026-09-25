using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    /// <summary>
    /// Tracks a recurring Package billing cycle (Phase 10) — the Package-repointed analog
    /// of <see cref="Subscription"/>, created alongside a <see cref="PackageRequest"/> whose
    /// <c>BillingType</c> is <see cref="PackageRequestBillingTypes.Recurring"/>.
    ///
    /// Deliberately does NOT derive or store a CaregiverId anywhere — that was the whole
    /// point of this parallel structure (see Phase 10 discovery). The caregiver reference
    /// for a recurring PackageRequest lives solely on the persistent <see cref="Assignment"/>
    /// / <see cref="PackageRequest.ConfirmedCaregiverId"/>, resolved separately whenever
    /// needed; this record only ever tracks money and timing.
    ///
    /// Also deliberately has no ClientOrder equivalent: there is no per-Gig order to create
    /// each cycle, so each billing cycle's outcome is recorded directly in
    /// <see cref="PaymentHistory"/> on this record, not as a separate order entity.
    /// </summary>
    public class PackageSubscription
    {
        public ObjectId Id { get; set; }

        public string PackageRequestId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        public decimal RecurringAmount { get; set; }
        public string Currency { get; set; } = "NGN";
        public string Email { get; set; } = string.Empty;

        /// <summary>Reused unchanged from Subscription — the status vocabulary (Active, PastDue,
        /// Suspended, Charging, etc.) is already fully Gig-agnostic.</summary>
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        public DateTime CurrentPeriodStart { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
        public DateTime? NextChargeDate { get; set; }
        public int BillingCyclesCompleted { get; set; }
        public bool AutoRenew { get; set; } = true;

        // ── Flutterwave tokenization (v3 token+email path only — no card-update flow exists
        // for Package recurring yet, so the CustomerId/PaymentMethodId fallback path that Gig
        // subscriptions support is intentionally not carried here). ──
        public string? FlutterwavePaymentToken { get; set; }
        public string? CardLastFour { get; set; }
        public string? CardBrand { get; set; }
        public string? CardExpiry { get; set; }

        // ── Retry & failure tracking — same shape as Subscription's ──
        public int FailedChargeAttempts { get; set; }
        public int MaxRetryAttempts { get; set; } = 3;
        public string? LastChargeError { get; set; }
        public string? LastChargeFailureClass { get; set; }
        public DateTime? LastFailedChargeAt { get; set; }
        public string? LastRecurringAttemptKey { get; set; }
        public string? LastRecurringAttemptStatus { get; set; }
        public DateTime? LastRecurringAttemptAt { get; set; }

        // ── Cancellation ──
        public bool CancelAtPeriodEnd { get; set; }
        public DateTime? CancellationRequestedAt { get; set; }
        public string? CancellationReason { get; set; }
        public string? CancelledBy { get; set; }
        public DateTime? TerminatedAt { get; set; }

        public List<PackageSubscriptionPaymentRecord> PaymentHistory { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Records each renewal charge attempt. Mirrors SubscriptionPaymentRecord minus
    /// ClientOrderId — there is no per-cycle order for Package billing.</summary>
    public class PackageSubscriptionPaymentRecord
    {
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "NGN";
        /// <summary>"successful", "failed", "pending", "verification_failed", "amount_mismatch"</summary>
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string? RecurringAttemptKey { get; set; }
        /// <summary>retryable | non_retryable | action_required</summary>
        public string? FailureClass { get; set; }
        public string? AuthorizationUrl { get; set; }
        /// <summary>"system" for auto-renew background processing, "client" for manual renew.</summary>
        public string InitiatedBy { get; set; } = "system";
        public int BillingCycleNumber { get; set; }
        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}
