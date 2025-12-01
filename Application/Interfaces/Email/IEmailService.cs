using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Email
{
    public interface IEmailService
    {
        // Existing methods
        Task SendPasswordResetEmailAsync(string toEmail, string resetLink, string firstName);
        Task SendSignUpVerificationEmailAsync(string toEmail, string verificationToken, string firstName);
        Task SendCaregiverWelcomeEmailAsync(string toEmail, string firstName);
        Task SendNotificationEmailAsync(string toEmail, string firstName, int messageCount);

        // New immediate notification methods
        Task SendNewGigNotificationEmailAsync(string toEmail, string firstName, string gigDetails);
        Task SendSystemNotificationEmailAsync(string toEmail, string firstName, string title, string content);
        Task SendWithdrawalStatusEmailAsync(string toEmail, string firstName, string status, string content);
        Task SendGenericNotificationEmailAsync(string toEmail, string firstName, string subject, string content);
        
        // Payment-related notification methods
        Task SendPaymentConfirmationEmailAsync(string toEmail, string firstName, decimal amount, string service, string transactionId);
        Task SendEarningsNotificationEmailAsync(string toEmail, string firstName, decimal amount, string clientName, string serviceType);
        Task SendWithdrawalRequestEmailAsync(string toEmail, string firstName, decimal amount, string status);
        Task SendPaymentFailedEmailAsync(string toEmail, string firstName, decimal amount, string reason);
        Task SendRefundNotificationEmailAsync(string toEmail, string firstName, decimal amount, string reason);

        // Order-related notification methods
        Task SendOrderReceivedEmailAsync(string toEmail, string firstName, decimal amount, string gigTitle, string clientName, string orderId);
        Task SendOrderConfirmationEmailAsync(string toEmail, string firstName, decimal amount, string gigTitle, string orderId);
        Task SendOrderCompletedEmailAsync(string toEmail, string firstName, decimal amount, string gigTitle, string orderId);
        Task SendOrderCancelledEmailAsync(string toEmail, string firstName, string gigTitle, string reason, string orderId);

        // Batch notification methods
        Task SendBatchMessageNotificationEmailAsync(string toEmail, string firstName, int messageCount, 
            Dictionary<string, ConversationSummary> conversationSummaries);

        // Contract reminder methods
        Task SendContractReminderEmailAsync(string toEmail, string firstName, string subject, string message, 
            ContractDetails? contractDetails, EmailType reminderLevel);
    }

    // Supporting classes for email service
    public class ConversationSummary
    {
        public required string SenderName { get; set; }
        public int MessageCount { get; set; }
        public DateTime LatestMessageTime { get; set; }
    }

    public class ContractDetails
    {
        public required string ContractId { get; set; }
        public required string ClientName { get; set; }
        public required string GigTitle { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }
}
