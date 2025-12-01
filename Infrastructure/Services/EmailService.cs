using Application.Interfaces.Email;
using Domain.Entities;
using Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using MailKit.Net.Smtp;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MimeKit;
using Infrastructure.Content.Data;
using static Org.BouncyCastle.Math.EC.ECCurve;
using Microsoft.EntityFrameworkCore;
using Application.Interfaces.Authentication;
using Org.BouncyCastle.Cms;


namespace Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly MailSettings emailSettings;
        private readonly CareProDbContext careProDbContext;
        private readonly ITokenHandler tokenHandler;

        public EmailService(IOptions<MailSettings> emailSettingsOptions, CareProDbContext careProDbContext, ITokenHandler tokenHandler)
        {
            this.emailSettings = emailSettingsOptions.Value;
            this.careProDbContext = careProDbContext;
            this.tokenHandler = tokenHandler;
        }

        public async Task SendNotificationEmailAsync(string toEmail, string firstName, int messageCount)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "You Have Unread Message!";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    
                    <h3>Dear {firstName},</h3>
                    <br />

                    <p>You have {messageCount} unread message(s) in your CarePro account.</p>

                    <p>Please log in to your dashboard to check them.</p>

                    <p>Thanks,<br />The CarePro Team</p>"


            };

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(emailSettings.SmtpServer, emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(emailSettings.FromEmail, emailSettings.AppPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink, string firstName)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Password Reset Request";



            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Hello {firstName},</h3>
                    <p>You requested a password reset. Click the link below to reset your password:</p>
                    <p><a href=""{resetLink}"">Reset Password</a></p>
                    <br />                    
                    <p>Or copy and paste this link into your browser:<br /> {resetLink}</p> <br />
                    <p>If you did not request this, please ignore this email.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();


            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(emailSettings.SmtpServer, emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(emailSettings.FromEmail, emailSettings.AppPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }



        public async Task SendSignUpVerificationEmailAsync(string toEmail, string verificationLink, string firstName)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Confirm Your Email - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Hello {firstName},</h3>
                    <h3>Welcome to CarePro!</h3>
                    <p>Thank you for signing up. Please confirm your email address by clicking the link below:</p>
                    <p><a href=""{verificationLink}"">Verify My Email</a></p>
                    <p>Or copy and paste this link into your browser:<br /> {verificationLink}</p>
                    <p>This helps us ensure we have the right contact information and lets you access your account securely.</p>
                    <p>If you did not sign up for CarePro, please ignore this email.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(emailSettings.SmtpServer, emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(emailSettings.FromEmail, emailSettings.AppPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        public async Task SendCaregiverWelcomeEmailAsync(string toEmail, string firstName)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Welcome to CarePro - Next Steps to Get Started";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <h2 style='color: #2c3e50;'>Welcome to CarePro, {firstName}!</h2>
                        
                        <p>Congratulations on successfully verifying your email! We're excited to have you join our community of professional caregivers.</p>
                        
                        <h3 style='color: #3498db; margin-top: 30px;'>📋 Next Steps to Start Offering Services:</h3>
                        
                        <div style='background-color: #f8f9fa; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                            <h4 style='color: #2c3e50; margin-top: 0;'>1. Verify Your Identity</h4>
                            <p>Click the <strong>""Verify""</strong> button in your dashboard to complete identity verification.</p>
                            <p><strong>Required Document (choose one):</strong></p>
                            <ul>
                                <li>Bank Verification Number (BVN)</li>
                                <li>Voter's Card</li>
                                <li>National Identification Number (NIN) Slip</li>
                                <li>National ID Card</li>
                                <li>Driver's License</li>
                            </ul>
                        </div>
                        
                        <div style='background-color: #fff3cd; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                            <h4 style='color: #856404; margin-top: 0;'>2. Take the Assessment</h4>
                            <p>Complete our professional assessment to demonstrate your caregiving knowledge and skills.</p>
                            <p><strong>⚠️ Passing Score Required:</strong> 80% or higher</p>
                            <p style='margin-bottom: 0;'>This ensures our clients receive the highest quality care from qualified professionals.</p>
                        </div>
                        
                        <div style='background-color: #d1ecf1; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                            <h4 style='color: #0c5460; margin-top: 0;'>3. Upload Educational Documents</h4>
                            <p><strong>Required (choose one):</strong></p>
                            <ul style='margin-bottom: 0;'>
                                <li>NECO Certificate</li>
                                <li>WAEC Certificate</li>
                                <li>NABTEB Certificate</li>
                                <li>NYSC Certificate</li>
                            </ul>
                        </div>
                        
                        <div style='background-color: #d4edda; padding: 20px; border-radius: 8px; margin: 30px 0; text-align: center;'>
                            <h4 style='color: #155724; margin-top: 0;'>✅ Once Approved, You Can:</h4>
                            <ul style='text-align: left; display: inline-block;'>
                                <li>Create service listings (gigs)</li>
                                <li>Receive client requests</li>
                                <li>Start earning as a professional caregiver</li>
                            </ul>
                        </div>
                        
                        <p style='margin-top: 30px;'>If you have any questions or need assistance, don't hesitate to reach out to our support team.</p>
                        
                        <p style='margin-top: 30px;'>Best regards,<br />
                        <strong>The CarePro Team</strong></p>
                        
                        <hr style='border: none; border-top: 1px solid #e0e0e0; margin: 30px 0;' />
                        <p style='font-size: 12px; color: #6c757d; text-align: center;'>
                            This email was sent to {toEmail} because you registered as a caregiver on CarePro.
                        </p>
                    </div>"
            };

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(emailSettings.SmtpServer, emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(emailSettings.FromEmail, emailSettings.AppPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        // New immediate notification methods
        public async Task SendNewGigNotificationEmailAsync(string toEmail, string firstName, string gigDetails)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "New Care Opportunity Available - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <p>A new care opportunity is available that matches your profile!</p>
                    <p>{gigDetails}</p>
                    <p>Please log in to your CarePro dashboard to view details and apply.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendSystemNotificationEmailAsync(string toEmail, string firstName, string title, string content)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = $"{title} - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4>{title}</h4>
                    <p>{content}</p>
                    <p>Please log in to your CarePro dashboard for more information.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendWithdrawalStatusEmailAsync(string toEmail, string firstName, string status, string content)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = $"Withdrawal {status} - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <p>Your withdrawal request has been {status.ToLower()}.</p>
                    <p>{content}</p>
                    <p>Please log in to your CarePro dashboard to view your earnings and withdrawal history.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendGenericNotificationEmailAsync(string toEmail, string firstName, string subject, string content)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <p>{content}</p>
                    <p>Please log in to your CarePro dashboard for more information.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        // Payment-related notification methods
        public async Task SendPaymentConfirmationEmailAsync(string toEmail, string firstName, decimal amount, string service, string transactionId)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Payment Confirmation - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #28a745;'>✅ Payment Confirmed</h4>
                    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                        <p><strong>Service:</strong> {service}</p>
                        <p><strong>Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Transaction ID:</strong> {transactionId}</p>
                        <p><strong>Status:</strong> <span style='color: #28a745;'>Confirmed</span></p>
                    </div>
                    <p>Your payment has been successfully processed. Your caregiver will be notified and will contact you soon.</p>
                    <p>You can track your service progress in your CarePro dashboard.</p>
                    <p>Thanks for choosing CarePro!<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendEarningsNotificationEmailAsync(string toEmail, string firstName, decimal amount, string clientName, string serviceType)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Earnings Added - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #28a745;'>💰 You've Earned Money!</h4>
                    <div style='background-color: #e8f5e8; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #28a745;'>
                        <p><strong>Amount Earned:</strong> ₦{amount:N2}</p>
                        <p><strong>Client:</strong> {clientName}</p>
                        <p><strong>Service:</strong> {serviceType}</p>
                        <p><strong>Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>Congratulations! Your earnings have been added to your account.</p>
                    <p>You can view your earnings and request withdrawals from your CarePro dashboard.</p>
                    <p>Keep up the great work!<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendWithdrawalRequestEmailAsync(string toEmail, string firstName, decimal amount, string status)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = $"Withdrawal Request {status} - CarePro";

            var statusColor = status.ToLower() switch
            {
                "submitted" => "#007bff",
                "verified" => "#17a2b8", 
                "completed" => "#28a745",
                "rejected" => "#dc3545",
                _ => "#6c757d"
            };

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: {statusColor};'>Withdrawal Request Update</h4>
                    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                        <p><strong>Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Status:</strong> <span style='color: {statusColor}; font-weight: bold;'>{status.ToUpper()}</span></p>
                        <p><strong>Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>{GetWithdrawalStatusMessage(status, amount)}</p>
                    <p>You can view your withdrawal history and account balance in your CarePro dashboard.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendPaymentFailedEmailAsync(string toEmail, string firstName, decimal amount, string reason)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Payment Failed - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #dc3545;'>❌ Payment Failed</h4>
                    <div style='background-color: #f8d7da; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #dc3545;'>
                        <p><strong>Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Reason:</strong> {reason}</p>
                        <p><strong>Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>Your payment could not be processed. Please try again or contact our support team.</p>
                    <p>You can retry the payment from your CarePro dashboard.</p>
                    <p>If you continue to experience issues, please contact our support team.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendRefundNotificationEmailAsync(string toEmail, string firstName, decimal amount, string reason)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Refund Processed - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #17a2b8;'>💳 Refund Processed</h4>
                    <div style='background-color: #d1ecf1; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #17a2b8;'>
                        <p><strong>Refund Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Reason:</strong> {reason}</p>
                        <p><strong>Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>Your refund has been processed and will be credited to your original payment method within 3-5 business days.</p>
                    <p>If you have any questions about this refund, please contact our support team.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        // Helper method for withdrawal status messages
        private string GetWithdrawalStatusMessage(string status, decimal amount)
        {
            return status.ToLower() switch
            {
                "submitted" => $"Your withdrawal request for ₦{amount:N2} has been submitted and is being reviewed by our team.",
                "verified" => $"Your withdrawal request for ₦{amount:N2} has been verified and is being processed.",
                "completed" => $"Your withdrawal of ₦{amount:N2} has been completed and transferred to your bank account.",
                "rejected" => $"Your withdrawal request for ₦{amount:N2} has been rejected. Please contact support for more information.",
                _ => $"Your withdrawal request for ₦{amount:N2} status has been updated."
            };
        }

        // Batch notification methods
        public async Task SendBatchMessageNotificationEmailAsync(string toEmail, string firstName, int messageCount, 
            Dictionary<string, ConversationSummary> conversationSummaries)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = $"You have {messageCount} unread message{(messageCount > 1 ? "s" : "")} - CarePro";

            var conversationList = new StringBuilder();
            foreach (var conversation in conversationSummaries.Values.OrderByDescending(c => c.LatestMessageTime))
            {
                conversationList.AppendLine($"<li><strong>{conversation.SenderName}</strong>: {conversation.MessageCount} message{(conversation.MessageCount > 1 ? "s" : "")} (latest: {conversation.LatestMessageTime:MMM dd, HH:mm})</li>");
            }

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <p>You have {messageCount} unread message{(messageCount > 1 ? "s" : "")} in your CarePro account from {conversationSummaries.Count} conversation{(conversationSummaries.Count > 1 ? "s" : "")}:</p>
                    <ul>
                        {conversationList}
                    </ul>
                    <p>Please log in to your CarePro dashboard to read and respond to your messages.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        // Contract reminder methods
        public async Task SendContractReminderEmailAsync(string toEmail, string firstName, string subject, string message, 
            ContractDetails? contractDetails, EmailType reminderLevel)
        {
            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            emailMessage.To.Add(MailboxAddress.Parse(toEmail));
            emailMessage.Subject = subject;

            var urgencyText = reminderLevel switch
            {
                EmailType.Reminder1 => "Reminder",
                EmailType.Reminder2 => "Urgent Reminder",
                EmailType.Final => "Final Reminder",
                _ => "Reminder"
            };

            var contractInfo = contractDetails != null 
                ? $@"
                    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                        <h4 style='margin-top: 0;'>Contract Details:</h4>
                        <p><strong>Client:</strong> {contractDetails.ClientName}</p>
                        <p><strong>Service:</strong> {contractDetails.GigTitle}</p>
                        <p><strong>Contract Date:</strong> {contractDetails.CreatedAt:MMM dd, yyyy}</p>
                        {(contractDetails.ExpiryDate.HasValue ? $"<p><strong>Expires:</strong> {contractDetails.ExpiryDate.Value:MMM dd, yyyy}</p>" : "")}
                    </div>"
                : "";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: {(reminderLevel == EmailType.Final ? "#dc3545" : "#007bff")};'>{urgencyText}: Contract Response Needed</h4>
                    <p>{message}</p>
                    {contractInfo}
                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='#' style='background-color: #28a745; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>View Contract</a>
                    </div>
                    <p>Please log in to your CarePro dashboard to respond to this contract.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            emailMessage.Body = builder.ToMessageBody();
            await SendEmailAsync(emailMessage);
        }

        // Order notification methods
        public async Task SendOrderReceivedEmailAsync(string toEmail, string firstName, decimal amount, string gigTitle, string clientName, string orderId)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "New Order Received - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #28a745;'>📋 New Order Received</h4>
                    <div style='background-color: #d4edda; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #28a745;'>
                        <p><strong>Service:</strong> {gigTitle}</p>
                        <p><strong>Client:</strong> {clientName}</p>
                        <p><strong>Order Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Order ID:</strong> {orderId}</p>
                        <p><strong>Order Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>You have received a new order for your service! The client has paid and is ready to begin working with you.</p>
                    <p>Please log in to your CarePro dashboard to review the order details and begin coordination with your client.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendOrderConfirmationEmailAsync(string toEmail, string firstName, decimal amount, string gigTitle, string orderId)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Order Confirmation - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #007bff;'>✅ Order Confirmed</h4>
                    <div style='background-color: #d1ecf1; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #007bff;'>
                        <p><strong>Service:</strong> {gigTitle}</p>
                        <p><strong>Order Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Order ID:</strong> {orderId}</p>
                        <p><strong>Confirmation Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>Your order has been confirmed! Your caregiver will begin providing the requested services according to your agreed schedule.</p>
                    <p>You can track your order progress and communicate with your caregiver through your CarePro dashboard.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendOrderCompletedEmailAsync(string toEmail, string firstName, decimal amount, string gigTitle, string orderId)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Order Completed - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #28a745;'>🎉 Order Completed</h4>
                    <div style='background-color: #d4edda; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #28a745;'>
                        <p><strong>Service:</strong> {gigTitle}</p>
                        <p><strong>Order Amount:</strong> ₦{amount:N2}</p>
                        <p><strong>Order ID:</strong> {orderId}</p>
                        <p><strong>Completion Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                    </div>
                    <p>Your order has been successfully completed! We hope you had a great experience with our care services.</p>
                    <p>Please consider leaving a review for your caregiver to help other clients make informed decisions.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        public async Task SendOrderCancelledEmailAsync(string toEmail, string firstName, string gigTitle, string reason, string orderId)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Order Cancelled - CarePro";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <h3>Dear {firstName},</h3>
                    <br />
                    <h4 style='color: #dc3545;'>❌ Order Cancelled</h4>
                    <div style='background-color: #f8d7da; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #dc3545;'>
                        <p><strong>Service:</strong> {gigTitle}</p>
                        <p><strong>Order ID:</strong> {orderId}</p>
                        <p><strong>Cancellation Date:</strong> {DateTime.UtcNow:MMM dd, yyyy}</p>
                        <p><strong>Reason:</strong> {reason}</p>
                    </div>
                    <p>Your order has been cancelled. If you paid for this service, a refund will be processed within 3-5 business days.</p>
                    <p>If you have any questions about this cancellation, please contact our support team.</p>
                    <p>Thanks,<br />The CarePro Team</p>"
            };

            message.Body = builder.ToMessageBody();
            await SendEmailAsync(message);
        }

        // Helper method to reduce code duplication
        private async Task SendEmailAsync(MimeMessage message)
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(emailSettings.SmtpServer, emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(emailSettings.FromEmail, emailSettings.AppPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }



        //public async Task SendSignUpVerificationEmailAsync(string toEmail, string subject, string body)
        //{
        //    _smtpClient = new System.Net.Mail.SmtpClient(_mailSettings.Host)
        //    {

        //        Port = 587,
        //        EnableSsl = true,
        //        UseDefaultCredentials = false,
        //        Credentials = new NetworkCredential(_mailSettings.Mail, _mailSettings.Password)
        //    };
        //    try
        //    {
        //        var message = new MailMessage(_mailSettings.Mail, toEmail, subject, body)
        //        {
        //            IsBodyHtml = true
        //        };

        //        await _smtpClient.SendMailAsync(message);
        //    }
        //    catch (Exception ex)
        //    {
        //        // Handle exceptions
        //        _logger.LogError($"Failed to send email: {ex.Message}");
        //        throw new Exception(ex.Message);
        //    }
        //}



    }
}

