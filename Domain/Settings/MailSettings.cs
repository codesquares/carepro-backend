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
    }
}
