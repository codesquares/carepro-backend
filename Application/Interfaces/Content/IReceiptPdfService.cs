using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IReceiptPdfService
    {
        /// <summary>
        /// Generates a PDF receipt for a booking commitment fee payment.
        /// Returns raw PDF bytes ready for email attachment or HTTP file response.
        /// </summary>
        byte[] GenerateCommitmentReceipt(CommitmentReceiptData data);

        /// <summary>
        /// Generates a PDF receipt for a full gig order payment.
        /// Returns raw PDF bytes ready for email attachment or HTTP file response.
        /// </summary>
        byte[] GenerateOrderReceipt(OrderReceiptData data);
    }
}
