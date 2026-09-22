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
        public decimal BasePrice { get; set; }
        public decimal AdditionalDayAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "NGN";
    }
}
