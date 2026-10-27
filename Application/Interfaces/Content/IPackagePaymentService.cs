using Application.DTOs;
using Domain.Entities;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Admin-initiated Package payment link generation + webhook-driven completion
    /// (Option A). Structurally mirrors <see cref="IBookingCommitmentService"/>'s
    /// initiate/complete pair — a standalone payment flow, not routed through
    /// <see cref="IPendingPaymentService"/>, which is Gig-purchase-specific.
    /// </summary>
    public interface IPackagePaymentService
    {
        /// <summary>
        /// Staff-triggered: generates a real Flutterwave payment link for a client + package
        /// (+ optional extra-days add-on) for manual delivery over WhatsApp.
        /// </summary>
        Task<Result<PackagePaymentResponse>> InitiatePackagePaymentAsync(AdminInitiatePackagePaymentRequest request, string? adminId);

        /// <summary>
        /// Called from the Flutterwave webhook (tx_ref prefix CAREPRO-PKG-, OneTime only) on a
        /// verified successful charge. Verifies the paid amount, then creates the real PackageRequest.
        /// </summary>
        Task<Result<PendingPackagePayment>> CompletePackagePaymentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount);

        /// <summary>
        /// Phase 10 — called from the Flutterwave webhook (tx_ref prefix CAREPRO-PKG-RECURRING-)
        /// on a verified successful charge for a Recurring purchase. Verifies the paid amount,
        /// captures and stores a Flutterwave payment token (mirroring
        /// PendingPaymentService.CreateSubscriptionForRecurringPaymentAsync's Gig-flow token
        /// capture), creates the real PackageRequest (same CreateAsync call as the OneTime path),
        /// and creates the corresponding PackageSubscription that the renewal engine will bill
        /// going forward.
        /// </summary>
        Task<Result<PendingPackagePayment>> CompleteRecurringPackagePaymentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount);

        Task<PendingPackagePayment?> GetByTransactionReferenceAsync(string transactionReference);
    }
}
