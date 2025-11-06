using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ImmediateNotificationProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ImmediateNotificationProcessor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5); // Run every 5 minutes

        public ImmediateNotificationProcessor(
            IServiceScopeFactory scopeFactory,
            ILogger<ImmediateNotificationProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ImmediateNotificationProcessor started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessImmediateNotificationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing immediate notifications.");
                }

                // Wait for the next run (5 minutes)
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ProcessImmediateNotificationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var trackingService = scope.ServiceProvider.GetRequiredService<IEmailNotificationTrackingService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

            try
            {
                var notifications = await trackingService.GetNotificationsForImmediateEmailAsync();

                if (!notifications.Any())
                {
                    _logger.LogDebug("No immediate notifications found for email sending.");
                    return;
                }

                _logger.LogInformation("Processing {Count} immediate notifications", notifications.Count);

                foreach (var notification in notifications)
                {
                    try
                    {
                        // Check user preferences
                        var shouldSendEmail = await trackingService.ShouldSendEmailToUserAsync(
                            notification.RecipientId, notification.Type);

                        if (!shouldSendEmail)
                        {
                            _logger.LogInformation("Skipping email for user {UserId} due to preferences", 
                                notification.RecipientId);
                            continue;
                        }

                        // Get user details
                        var recipient = await dbContext.AppUsers
                            .FirstOrDefaultAsync(u => u.AppUserId.ToString() == notification.RecipientId, stoppingToken);

                        if (recipient == null || string.IsNullOrEmpty(recipient.Email))
                        {
                            _logger.LogWarning("Recipient {RecipientId} not found or has no email", 
                                notification.RecipientId);
                            continue;
                        }

                        // Send the email based on notification type
                        var emailSubject = GetEmailSubject(notification.Type);
                        await SendImmediateEmailAsync(emailService, recipient, notification, emailSubject);

                        // Log the email sent
                        var emailLog = new EmailNotificationLog
                        {
                            UserId = notification.RecipientId,
                            NotificationId = notification.Id.ToString(),
                            NotificationType = notification.Type,
                            EmailType = EmailType.Immediate,
                            EmailSubject = emailSubject,
                            RelatedEntityId = notification.RelatedEntityId,
                            Status = EmailStatus.Sent
                        };

                        await trackingService.LogEmailSentAsync(emailLog);

                        _logger.LogInformation("Immediate email sent to {Email} for notification type {Type}", 
                            recipient.Email, notification.Type);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending immediate email for notification {NotificationId}", 
                            notification.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessImmediateNotificationsAsync");
                throw;
            }
        }

        private async Task SendImmediateEmailAsync(IEmailService emailService, AppUser recipient, 
            Notification notification, string subject)
        {
            switch (notification.Type)
            {
                case "NewGig":
                    await emailService.SendNewGigNotificationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User", notification.Content);
                    break;

                case "SystemNotice":
                case "SystemAlert":
                    await emailService.SendSystemNotificationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User", notification.Title ?? "System Update", 
                        notification.Content);
                    break;

                case "WithdrawalCompleted":
                case "WithdrawalVerified":
                case "WithdrawalRejected":
                    await emailService.SendWithdrawalStatusEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User", notification.Type, notification.Content);
                    break;

                case "WithdrawalRequest":
                    // Extract amount from notification content (this might need adjustment based on your notification format)
                    var amount = ExtractAmountFromContent(notification.Content);
                    await emailService.SendWithdrawalRequestEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User", amount, "submitted");
                    break;

                case "PaymentReceived":
                case "PaymentConfirmed":
                    var paymentDetails = ExtractPaymentDetailsFromContent(notification.Content);
                    await emailService.SendPaymentConfirmationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User", 
                        paymentDetails.Amount, paymentDetails.Service, paymentDetails.TransactionId);
                    break;

                case "EarningsAdded":
                case "OrderPayment":
                    var earningsDetails = ExtractEarningsDetailsFromContent(notification.Content);
                    await emailService.SendEarningsNotificationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        earningsDetails.Amount, earningsDetails.ClientName, earningsDetails.ServiceType);
                    break;

                case "OrderReceived":
                    var orderDetails = ExtractOrderDetailsFromContent(notification.Content);
                    await emailService.SendOrderReceivedEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        orderDetails.Amount, orderDetails.GigTitle, orderDetails.ClientName, orderDetails.OrderId);
                    break;

                case "OrderConfirmation":
                    var confirmDetails = ExtractOrderConfirmationDetailsFromContent(notification.Content);
                    await emailService.SendOrderConfirmationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        confirmDetails.Amount, confirmDetails.GigTitle, confirmDetails.OrderId);
                    break;

                case "OrderCompleted":
                    var completedDetails = ExtractOrderCompletionDetailsFromContent(notification.Content);
                    await emailService.SendOrderCompletedEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        completedDetails.Amount, completedDetails.GigTitle, completedDetails.OrderId);
                    break;

                case "OrderCancelled":
                    var cancelDetails = ExtractOrderCancellationDetailsFromContent(notification.Content);
                    await emailService.SendOrderCancelledEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        cancelDetails.GigTitle, cancelDetails.Reason, cancelDetails.OrderId);
                    break;

                case "PaymentFailed":
                    var failedPaymentDetails = ExtractPaymentFailureDetailsFromContent(notification.Content);
                    await emailService.SendPaymentFailedEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        failedPaymentDetails.Amount, failedPaymentDetails.Reason);
                    break;

                case "RefundProcessed":
                    var refundDetails = ExtractRefundDetailsFromContent(notification.Content);
                    await emailService.SendRefundNotificationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User",
                        refundDetails.Amount, refundDetails.Reason);
                    break;

                default:
                    // Fallback to generic notification email
                    await emailService.SendGenericNotificationEmailAsync(
                        recipient.Email, recipient.FirstName ?? "User", subject, notification.Content);
                    break;
            }
        }

        private string GetEmailSubject(string notificationType)
        {
            return notificationType switch
            {
                "NewGig" => "New Care Opportunity Available - CarePro",
                "SystemNotice" => "System Update - CarePro",
                "SystemAlert" => "Important System Alert - CarePro",
                "OrderReceived" => "New Order Received - CarePro",
                "OrderConfirmation" => "Order Confirmation - CarePro", 
                "OrderCompleted" => "Order Completed - CarePro",
                "OrderCancelled" => "Order Cancelled - CarePro",
                "WithdrawalCompleted" => "Withdrawal Completed - CarePro",
                "WithdrawalVerified" => "Withdrawal Verified - CarePro",
                "WithdrawalRejected" => "Withdrawal Update - CarePro",
                "WithdrawalRequest" => "Withdrawal Request Submitted - CarePro",
                "PaymentReceived" => "Payment Confirmation - CarePro",
                "PaymentConfirmed" => "Payment Confirmation - CarePro",
                "EarningsAdded" => "Earnings Added - CarePro",
                "OrderPayment" => "Payment Received - CarePro",
                "PaymentFailed" => "Payment Failed - CarePro",
                "RefundProcessed" => "Refund Processed - CarePro",
                _ => "Notification - CarePro"
            };
        }

        // Helper methods to extract details from notification content
        // Note: These are simplified implementations. In a real system, you might want to:
        // 1. Use structured notification content (JSON)
        // 2. Pass additional parameters to the notification creation
        // 3. Store amounts and details in separate fields
        
        private decimal ExtractAmountFromContent(string content)
        {
            // Simple regex to extract amount from content
            // This assumes content contains patterns like "₦1,000.00" or "1000.00"
            var match = System.Text.RegularExpressions.Regex.Match(content, @"₦?([\d,]+\.?\d*)");
            if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), out var amount))
            {
                return amount;
            }
            return 0m; // Default fallback
        }

        private PaymentDetails ExtractPaymentDetailsFromContent(string content)
        {
            return new PaymentDetails
            {
                Amount = ExtractAmountFromContent(content),
                Service = "Care Services", // Default, could be extracted from content
                TransactionId = ExtractTransactionIdFromContent(content)
            };
        }

        private EarningsDetails ExtractEarningsDetailsFromContent(string content)
        {
            return new EarningsDetails
            {
                Amount = ExtractAmountFromContent(content),
                ClientName = ExtractClientNameFromContent(content),
                ServiceType = "Care Services" // Default, could be extracted from content
            };
        }

        private PaymentFailureDetails ExtractPaymentFailureDetailsFromContent(string content)
        {
            return new PaymentFailureDetails
            {
                Amount = ExtractAmountFromContent(content),
                Reason = content // Use full content as reason for now
            };
        }

        private RefundDetails ExtractRefundDetailsFromContent(string content)
        {
            return new RefundDetails
            {
                Amount = ExtractAmountFromContent(content),
                Reason = content // Use full content as reason for now
            };
        }

        private string ExtractTransactionIdFromContent(string content)
        {
            // Try to extract transaction ID from content
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(?:ID|Transaction|Ref)[:\s]+([A-Za-z0-9]+)");
            return match.Success ? match.Groups[1].Value : DateTime.UtcNow.Ticks.ToString();
        }

        private string ExtractClientNameFromContent(string content)
        {
            // Try to extract client name from content
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(?:from|by|client)\s+([A-Za-z\s]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "Client";
        }

        // Order-related helper methods
        private OrderDetails ExtractOrderDetailsFromContent(string content)
        {
            return new OrderDetails
            {
                Amount = ExtractAmountFromContent(content),
                GigTitle = ExtractGigTitleFromContent(content),
                ClientName = ExtractClientNameFromContent(content),
                OrderId = ExtractOrderIdFromContent(content)
            };
        }

        private OrderConfirmationDetails ExtractOrderConfirmationDetailsFromContent(string content)
        {
            return new OrderConfirmationDetails
            {
                Amount = ExtractAmountFromContent(content),
                GigTitle = ExtractGigTitleFromContent(content),
                OrderId = ExtractOrderIdFromContent(content)
            };
        }

        private OrderCompletionDetails ExtractOrderCompletionDetailsFromContent(string content)
        {
            return new OrderCompletionDetails
            {
                Amount = ExtractAmountFromContent(content),
                GigTitle = ExtractGigTitleFromContent(content),
                OrderId = ExtractOrderIdFromContent(content)
            };
        }

        private OrderCancellationDetails ExtractOrderCancellationDetailsFromContent(string content)
        {
            return new OrderCancellationDetails
            {
                GigTitle = ExtractGigTitleFromContent(content),
                Reason = content, // Use full content as reason for now
                OrderId = ExtractOrderIdFromContent(content)
            };
        }

        private string ExtractGigTitleFromContent(string content)
        {
            // Try to extract gig title from content - look for patterns like "service: Title" or "for your service: Title"
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(?:service|for your service):\s*([^-\n]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "Care Service";
        }

        private string ExtractOrderIdFromContent(string content)
        {
            // Try to extract order ID from content
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(?:Order|order)\s*(?:ID|#)?\s*([A-Za-z0-9]+)");
            return match.Success ? match.Groups[1].Value : DateTime.UtcNow.Ticks.ToString();
        }
    }

    // Order-related helper classes
    public class OrderDetails
    {
        public decimal Amount { get; set; }
        public string GigTitle { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string OrderId { get; set; } = "";
    }

    public class OrderConfirmationDetails
    {
        public decimal Amount { get; set; }
        public string GigTitle { get; set; } = "";
        public string OrderId { get; set; } = "";
    }

    public class OrderCompletionDetails
    {
        public decimal Amount { get; set; }
        public string GigTitle { get; set; } = "";
        public string OrderId { get; set; } = "";
    }

    public class OrderCancellationDetails
    {
        public string GigTitle { get; set; } = "";
        public string Reason { get; set; } = "";
        public string OrderId { get; set; } = "";
    }
    }

    // Helper classes for payment details
    public class PaymentDetails
    {
        public decimal Amount { get; set; }
        public string Service { get; set; } = "";
        public string TransactionId { get; set; } = "";
    }

    public class EarningsDetails
    {
        public decimal Amount { get; set; }
        public string ClientName { get; set; } = "";
        public string ServiceType { get; set; } = "";
    }

    public class PaymentFailureDetails
    {
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "";
    }

    public class RefundDetails
    {
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "";
    }
