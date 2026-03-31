using System;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs
{
    public class CreateRefundRequestDTO
    {
        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, 50000000, ErrorMessage = "Amount must be between ₦0.01 and ₦50,000,000")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Reason is required")]
        [StringLength(1000, ErrorMessage = "Reason cannot exceed 1000 characters")]
        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewRefundRequestDTO
    {
        [Required(ErrorMessage = "Status is required (Approved or Rejected)")]
        public string Status { get; set; } = string.Empty;

        [StringLength(1000, ErrorMessage = "Admin note cannot exceed 1000 characters")]
        public string? AdminNote { get; set; }
    }

    public class RefundRequestResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public string? ClientEmail { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ReviewedByAdminId { get; set; }
        public string? AdminNote { get; set; }
        public decimal WalletBalanceAtRequest { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
