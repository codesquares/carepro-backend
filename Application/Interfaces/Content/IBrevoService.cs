using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Thin wrapper over Brevo's Contacts REST API. Distinct from IEmailService, which only
    /// sends transactional mail via SMTP — this is for contact/list segmentation so marketing
    /// can build campaigns in the Brevo dashboard.
    /// </summary>
    public interface IBrevoService
    {
        Task UpsertContactAsync(
            string email,
            Dictionary<string, object> attributes,
            List<int>? addToListIds = null,
            List<int>? removeFromListIds = null);
    }
}
