using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Email
{
    public interface IEmailService
    {
       // Task SendEmailAsync2(string toEmail, string subject, string body);

        Task SendPasswordResetEmailAsync(string toEmail, string resetLink, string firstName);

        Task SendSignUpVerificationEmailAsync(string toEmail, string verificationToken, string firstName);

        Task SendNotificationEmailAsync(string toEmail, string firstName, int messageCount);
    }
}
