using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    /// <summary>
    /// Admin-only request to generate a real Flutterwave payment link for a specific
    /// client + package, for staff to copy into WhatsApp manually (Option A —
    /// no WhatsApp automation platform). No RedirectUrl or Email field: RedirectUrl is
    /// built server-side from the configured FrontendUrl, and Email is resolved from the
    /// Client record — staff only know who the client is and what they're buying.
    /// </summary>
    public class AdminInitiatePackagePaymentRequest
    {
        [Required]
        public string ClientId { get; set; } = string.Empty;

        [Required]
        public string PackageId { get; set; } = string.Empty;

        /// <summary>Extra-day add-on count. Only valid when the Package has an AdditionalDayPrice.</summary>
        [Range(0, 365)]
        public int ExtraDays { get; set; } = 0;

        /// <summary>
        /// Phase 10 — "OneTime" or "Recurring" (<see cref="Domain.Entities.PackageRequestBillingTypes"/>),
        /// the client's choice as relayed to staff. Defaults to OneTime.
        /// </summary>
        public string BillingType { get; set; } = Domain.Entities.PackageRequestBillingTypes.OneTime;

        /// <summary>Optional staff note, carried over onto the resulting PackageRequest.</summary>
        [StringLength(2000)]
        public string? Notes { get; set; }
    }

    public class PackagePaymentResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string? PaymentLink { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public int ExtraDays { get; set; }
        public string BillingType { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal AdditionalDayAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "NGN";
    }

    /// <summary>Returned after a recurring renewal charge attempt (success or failure).</summary>
    public class PackageSubscriptionPaymentRecordDTO
    {
        public string Id { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string? FlutterwaveTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "NGN";
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string? AuthorizationUrl { get; set; }
        public string InitiatedBy { get; set; } = "system";
        public int BillingCycleNumber { get; set; }
        public System.DateTime AttemptedAt { get; set; }
        public System.DateTime? CompletedAt { get; set; }
    }

    /// <summary>Creates the PackageSubscription that will drive future renewal charges, after
    /// the first charge succeeds and a payment token has been captured.</summary>
    public class CreatePackageSubscriptionRequest
    {
        public string PackageRequestId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public decimal RecurringAmount { get; set; }
        public string Currency { get; set; } = "NGN";
        public string Email { get; set; } = string.Empty;
        public string? FlutterwavePaymentToken { get; set; }
        public string? CardLastFour { get; set; }
        public string? CardBrand { get; set; }
        public string? CardExpiry { get; set; }
    }
}
