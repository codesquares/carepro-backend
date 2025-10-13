using Application.Interfaces.Email;
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
            message.From.Add(new MailboxAddress(emailSettings.FromName, emailSettings.FromEmail ));
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

