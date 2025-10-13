using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class WithdrawalRequestDTO
    {
        public string Id { get; set; }
        public string CaregiverId { get; set; }
        public decimal AmountRequested { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal FinalAmount { get; set; }
        public string Token { get; set; }
        public string Status { get; set; } // Pending, Verified, Completed, Rejected
        public DateTime CreatedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? AdminNotes { get; set; }
        public string? AdminId { get; set; }
        public string? AccountNumber { get; set; }
        public string? BankName { get; set; }
        public string? AccountName { get; set; }
    }

    public class CreateWithdrawalRequestRequest
    {
        public string CaregiverId { get; set; }
        public decimal AmountRequested { get; set; }
        public string? AccountNumber { get; set; }
        public string? BankName { get; set; }
        public string? AccountName { get; set; }
    }

    public class UpdateWithdrawalRequestStatusRequest
    {
        public string Token { get; set; }
        public string Status { get; set; }
        public string? AdminNotes { get; set; }
        public string AdminId { get; set; }
    }

    public class WithdrawalRequestResponse
    {
        public string Id { get; set; }
        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public decimal AmountRequested { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal FinalAmount { get; set; }
        public string Token { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? AdminNotes { get; set; }
        public string? AdminId { get; set; }
        public string? AdminName { get; set; }
        public string? AccountNumber { get; set; }
        public string? BankName { get; set; }
        public string? AccountName { get; set; }
    }


    public class CaregiverWithdrawalHistoryResponse
    {
        public string Id { get; set; }
        public string CaregiverId { get; set; }
        public string Description { get; set; }
        public string Activity { get; set; }
        public decimal AmountRequested { get; set; }
        //public string Status { get; set; }
        public DateTime WithdrawalRequestDate { get; set; }
        public DateTime? CompletedAt { get; set; }
      
    }



    public class CaregiverWithdrawalSummaryResponse
    {
        public decimal TotalAmountEarned { get; set; }
        public decimal TotalAmountWithdrawn { get; set; }
        public decimal WithdrawableAmount { get; set; }
    }


    public class AdminWithdrawalVerificationRequest
    {
        public string Token { get; set; }
        public string AdminId { get; set; }
        public string? AdminNotes { get; set; }
    }


    public class TransactionHistoryResponse
    {
        public string Id { get; set; }
        public string CaregiverId { get; set; }
        public string CaregiverName { get; set; }
        public string Activity { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        
       
    }
}
