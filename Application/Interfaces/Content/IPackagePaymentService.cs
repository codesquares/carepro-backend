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
        /// Called from the Flutterwave webhook (tx_ref prefix CAREPRO-PKG-) on a verified
        /// successful charge. Verifies the paid amount, then creates the real PackageRequest.
        /// </summary>
        Task<Result<PendingPackagePayment>> CompletePackagePaymentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount);

        Task<PendingPackagePayment?> GetByTransactionReferenceAsync(string transactionReference);
    }
}
