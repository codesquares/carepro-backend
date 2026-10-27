using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Settings
{
    public class MailSettings
    {
        public string FromEmail { get; set; }
        public string FromName { get; set; }
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string AppPassword { get; set; }

        // SMTP login/username, when the provider issues one distinct from FromEmail (e.g. Brevo). Falls back to FromEmail if unset.
        public string? SmtpUsername { get; set; }

        // LOCAL/TEST ONLY: when set (and the environment is not Production/Staging), outgoing mail is written as .eml files to this
        // directory instead of being sent over SMTP — a mail sink so local runs can never deliver a real email.
        public string? SinkDirectory { get; set; }
    }
}
