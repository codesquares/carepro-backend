using Application.DTOs;
using Application.Interfaces.Content;
using System.Text;

namespace Infrastructure.Content.Services
{
    public class ContractTemplateService : IContractTemplateService
    {
        public string RenderContract(ContractGenerationDataDTO data)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='en'><head><meta charset='UTF-8'/>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; color: #222; line-height: 1.6; margin: 0; padding: 40px; }");
            sb.AppendLine("h1 { text-align: center; font-size: 22px; margin-bottom: 4px; }");
            sb.AppendLine("h2 { font-size: 16px; border-bottom: 2px solid #0066cc; padding-bottom: 4px; margin-top: 28px; color: #0066cc; }");
            sb.AppendLine("h3 { font-size: 14px; margin-top: 16px; margin-bottom: 4px; }");
            sb.AppendLine(".subtitle { text-align: center; color: #555; font-size: 13px; margin-bottom: 24px; }");
            sb.AppendLine(".meta { font-size: 12px; color: #666; margin-bottom: 20px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin: 8px 0; }");
            sb.AppendLine("th, td { text-align: left; padding: 6px 10px; border: 1px solid #ddd; font-size: 13px; }");
            sb.AppendLine("th { background-color: #f0f6ff; font-weight: 600; }");
            sb.AppendLine("ul { margin: 4px 0; padding-left: 24px; }");
            sb.AppendLine("li { margin-bottom: 4px; font-size: 13px; }");
            sb.AppendLine(".signature-block { margin-top: 40px; display: flex; justify-content: space-between; }");
            sb.AppendLine(".signature-line { border-top: 1px solid #333; width: 45%; padding-top: 4px; font-size: 13px; }");
            sb.AppendLine("</style></head><body>");

            // Header
            sb.AppendLine("<h1>CAREPRO CARE SERVICE AGREEMENT</h1>");
            sb.AppendLine("<div class='subtitle'>Facilitated through the CarePro Platform</div>");

            // Meta
            sb.AppendLine("<div class='meta'>");
            sb.AppendLine($"<strong>Contract Reference:</strong> {Sanitize(data.ContractId)}<br/>");
            sb.AppendLine($"<strong>Order Reference:</strong> {Sanitize(data.OrderId)}<br/>");
            sb.AppendLine($"<strong>Date:</strong> {data.GeneratedAt:dddd, MMMM dd, yyyy 'at' HH:mm} UTC");
            sb.AppendLine("</div>");

            // Section 1: Parties
            sb.AppendLine("<h2>Section 1: Parties to This Agreement</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th></th><th>Client (Care Recipient)</th><th>Caregiver (Service Provider)</th></tr>");
            sb.AppendLine($"<tr><td><strong>Full Name</strong></td><td>{Sanitize(data.ClientFullName)}</td><td>{Sanitize(data.CaregiverFullName)}</td></tr>");
            sb.AppendLine($"<tr><td><strong>ID</strong></td><td>{Sanitize(data.ClientId)}</td><td>{Sanitize(data.CaregiverId)}</td></tr>");
            sb.AppendLine($"<tr><td><strong>Phone</strong></td><td>{Sanitize(data.ClientPhone ?? "On file")}</td><td>{Sanitize(data.CaregiverPhone ?? "On file")}</td></tr>");
            if (!string.IsNullOrWhiteSpace(data.CaregiverQualifications))
                sb.AppendLine($"<tr><td><strong>Qualifications</strong></td><td>—</td><td>{Sanitize(data.CaregiverQualifications)}</td></tr>");
            sb.AppendLine("</table>");

            // Section 2: Platform Relationship
            sb.AppendLine("<h2>Section 2: Platform Relationship</h2>");
            sb.AppendLine("<p>CarePro is a technology platform that connects clients seeking care services with independent caregivers. " +
                "Caregivers are independent contractors and market participants offering their services on the CarePro platform. " +
                "CarePro does not employ caregivers and is not a party to the care relationship between client and caregiver. " +
                "CarePro facilitates the matching, payment processing, and contract management only.</p>");

            // Section 3: Service Details
            sb.AppendLine("<h2>Section 3: Service Details</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><td><strong>Service</strong></td><td>{Sanitize(data.GigTitle)}</td></tr>");
            if (!string.IsNullOrWhiteSpace(data.GigDescription))
                sb.AppendLine($"<tr><td><strong>Description</strong></td><td>{Sanitize(data.GigDescription)}</td></tr>");
            if (!string.IsNullOrWhiteSpace(data.GigCategory))
                sb.AppendLine($"<tr><td><strong>Category</strong></td><td>{Sanitize(data.GigCategory)}</td></tr>");
            sb.AppendLine($"<tr><td><strong>Package</strong></td><td>{Sanitize(data.Package.PackageType?.Replace("_", " ") ?? "Standard")}</td></tr>");
            sb.AppendLine($"<tr><td><strong>Visits Per Week</strong></td><td>{data.Package.VisitsPerWeek}</td></tr>");
            sb.AppendLine("<tr><td><strong>Hours Per Visit</strong></td><td>4–6 hours</td></tr>");
            sb.AppendLine($"<tr><td><strong>Duration</strong></td><td>{data.Package.DurationWeeks} weeks</td></tr>");
            sb.AppendLine("</table>");

            // Section 4: Contract Period
            sb.AppendLine("<h2>Section 4: Contract Period</h2>");
            sb.AppendLine($"<p><strong>Start Date:</strong> {data.ContractStartDate:dddd, MMMM dd, yyyy}<br/>");
            sb.AppendLine($"<strong>End Date:</strong> {data.ContractEndDate:dddd, MMMM dd, yyyy}</p>");
            sb.AppendLine("<p>This contract is binding for as long as the associated order is not terminated or cancelled by either party or by CarePro.</p>");

            // Section 5: Agreed Weekly Schedule
            sb.AppendLine("<h2>Section 5: Agreed Weekly Schedule</h2>");
            if (data.Schedule.Any())
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Day</th><th>Start Time</th><th>End Time</th></tr>");
                foreach (var slot in data.Schedule)
                    sb.AppendLine($"<tr><td>{slot.DayOfWeek}</td><td>{Sanitize(slot.StartTime)}</td><td>{Sanitize(slot.EndTime)}</td></tr>");
                sb.AppendLine("</table>");
            }
            else
            {
                sb.AppendLine("<p>Schedule to be confirmed by both parties.</p>");
            }

            // Section 6: Service Location
            sb.AppendLine("<h2>Section 6: Service Location</h2>");
            sb.AppendLine($"<p><strong>Address:</strong> {Sanitize(data.ServiceAddress)}");
            if (!string.IsNullOrWhiteSpace(data.City))
                sb.Append($", {Sanitize(data.City)}");
            if (!string.IsNullOrWhiteSpace(data.State))
                sb.Append($", {Sanitize(data.State)}");
            sb.AppendLine("</p>");
            if (!string.IsNullOrWhiteSpace(data.AccessInstructions))
                sb.AppendLine($"<p><strong>Access Instructions:</strong> {Sanitize(data.AccessInstructions)}</p>");

            // Section 7: Care Responsibilities
            sb.AppendLine("<h2>Section 7: Care Responsibilities</h2>");
            sb.AppendLine("<h3>7.1 Primary Care Tasks</h3>");
            if (data.Tasks.Any())
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Task</th><th>Description</th><th>Priority</th></tr>");
                foreach (var task in data.Tasks)
                    sb.AppendLine($"<tr><td>{Sanitize(task.Title)}</td><td>{Sanitize(task.Description)}</td><td>{task.Priority}</td></tr>");
                sb.AppendLine("</table>");
            }
            else
            {
                sb.AppendLine("<p>General care services as discussed and agreed upon by both parties.</p>");
            }

            if (!string.IsNullOrWhiteSpace(data.SpecialClientRequirements))
            {
                sb.AppendLine("<h3>7.2 Special Client Requirements</h3>");
                sb.AppendLine($"<p>{Sanitize(data.SpecialClientRequirements)}</p>");
            }
            if (!string.IsNullOrWhiteSpace(data.CaregiverNotes))
            {
                sb.AppendLine("<h3>7.3 Caregiver Notes</h3>");
                sb.AppendLine($"<p>{Sanitize(data.CaregiverNotes)}</p>");
            }
            sb.AppendLine("<h3>7.4 Service Standards</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>All services shall be provided with professionalism, compassion, and care.</li>");
            sb.AppendLine("<li>The Caregiver shall follow established care protocols and best practices.</li>");
            sb.AppendLine("<li>Regular communication with the Client and/or family members as appropriate.</li>");
            sb.AppendLine("<li>Maintain accurate records of care provided through the CarePro platform.</li>");
            sb.AppendLine("</ul>");

            // Section 8: Visit Cancellation & Rescheduling Policy
            sb.AppendLine("<h2>Section 8: Visit Cancellation &amp; Rescheduling Policy</h2>");

            sb.AppendLine("<h3>8.1 Cancellation by Client</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Notice Period</th><th>Client Receives</th><th>Caregiver Receives</th></tr>");
            sb.AppendLine("<tr><td>24 hours or more before visit</td><td>100% of visit fee credited to CarePro wallet</td><td>0%</td></tr>");
            sb.AppendLine("<tr><td>12–24 hours before visit</td><td>50% of visit fee credited to CarePro wallet</td><td>50% of visit fee</td></tr>");
            sb.AppendLine("<tr><td>Less than 12 hours before visit</td><td>0% — visit fee forfeited</td><td>100% of visit fee</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<h3>8.2 Cancellation by Caregiver</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>The Caregiver <strong>must</strong> contact the Client at least 24 hours before a scheduled visit if they need to cancel, so that the Client can cancel the visit through the platform.</li>");
            sb.AppendLine("<li>If the Caregiver fails to show without providing 24-hour notice, the matter will be reviewed by CarePro and may result in penalties, suspension, or contract termination.</li>");
            sb.AppendLine("</ul>");

            sb.AppendLine("<h3>8.3 Rescheduling</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>Either party may request schedule changes with 48-hour advance notice.</li>");
            sb.AppendLine("<li>Permanent schedule changes require mutual agreement via the CarePro platform.</li>");
            sb.AppendLine("<li>Emergency changes should be communicated immediately through the platform messaging system.</li>");
            sb.AppendLine("</ul>");

            // Section 9: Reporting & Documentation
            sb.AppendLine("<h2>Section 9: Reporting &amp; Documentation</h2>");

            sb.AppendLine("<h3>9.1 Observation Reports</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>The Caregiver is required to submit an observation report after each visit documenting the care provided, client condition, and any notable observations.</li>");
            sb.AppendLine("<li>Observation reports protect both the Caregiver and Client in case of any disputes or allegations.</li>");
            sb.AppendLine("</ul>");

            sb.AppendLine("<h3>9.2 Incident Reports</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>The Caregiver must submit an incident report immediately through the CarePro platform for any accidents, injuries, unusual events, or safety concerns that occur during a visit.</li>");
            sb.AppendLine("<li>Failure to report incidents may result in liability for the Caregiver.</li>");
            sb.AppendLine("</ul>");

            // Section 10: Disputes
            sb.AppendLine("<h2>Section 10: Disputes</h2>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>The Client may dispute a visit if not satisfied with the care provided. All disputes are subject to investigation by CarePro.</li>");
            sb.AppendLine("<li>CarePro will review observation reports, incident reports, and any other evidence before making a determination.</li>");
            sb.AppendLine("<li>Both parties agree to cooperate fully with CarePro's dispute resolution process.</li>");
            sb.AppendLine("</ul>");

            // Section 11: Safety & Confidentiality
            sb.AppendLine("<h2>Section 11: Safety &amp; Confidentiality</h2>");

            sb.AppendLine("<h3>11.1 Safety</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>The Caregiver has been verified through CarePro's onboarding and background check process.</li>");
            sb.AppendLine("<li>Both parties must maintain a safe environment for care delivery.</li>");
            sb.AppendLine("<li>Emergency situations should be handled by contacting emergency services first, then reporting through CarePro.</li>");
            sb.AppendLine("</ul>");

            sb.AppendLine("<h3>11.2 Confidentiality</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>All personal, medical, and household information shared during this care arrangement is strictly confidential.</li>");
            sb.AppendLine("<li>Neither party may share the other's personal information outside the care relationship.</li>");
            sb.AppendLine("<li>Confidentiality obligations survive termination of this Agreement.</li>");
            sb.AppendLine("</ul>");

            // Section 12: Termination
            sb.AppendLine("<h2>Section 12: Termination</h2>");

            sb.AppendLine("<h3>12.1 By Either Party</h3>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>Either party may terminate this contract through the CarePro platform.</li>");
            sb.AppendLine("<li>The contract remains binding until formally terminated or until the order is cancelled.</li>");
            sb.AppendLine("</ul>");

            sb.AppendLine("<h3>12.2 Immediate Termination (For Cause)</h3>");
            sb.AppendLine("<p>CarePro reserves the right to immediately terminate this contract for:</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>Safety protocol breach</li>");
            sb.AppendLine("<li>Unprofessional conduct or abuse</li>");
            sb.AppendLine("<li>Confidentiality violation</li>");
            sb.AppendLine("<li>Repeated failure to provide agreed services</li>");
            sb.AppendLine("<li>Fraud or misrepresentation</li>");
            sb.AppendLine("</ul>");

            // Section 13: Limitation of Liability
            sb.AppendLine("<h2>Section 13: Limitation of Liability</h2>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li>CarePro operates as a marketplace platform and is not liable for the actions or omissions of either party.</li>");
            sb.AppendLine("<li>The Caregiver operates as an independent contractor and is solely responsible for the quality of care provided.</li>");
            sb.AppendLine("</ul>");

            // Section 14: Acknowledgment
            sb.AppendLine("<h2>Section 14: Acknowledgment</h2>");
            sb.AppendLine("<p>Both parties acknowledge they have read, understood, and agree to all terms of this Care Service Agreement.</p>");

            sb.AppendLine("<div class='signature-block'>");
            sb.AppendLine($"<div class='signature-line'><strong>Client:</strong> {Sanitize(data.ClientFullName)}<br/>Date: {data.GeneratedAt:MMMM dd, yyyy}</div>");
            sb.AppendLine($"<div class='signature-line'><strong>Caregiver:</strong> {Sanitize(data.CaregiverFullName)}<br/>Date: {data.GeneratedAt:MMMM dd, yyyy}</div>");
            sb.AppendLine("</div>");

            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        /// <summary>
        /// HTML-encodes user-supplied text to prevent XSS in rendered contracts.
        /// </summary>
        private static string Sanitize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return System.Net.WebUtility.HtmlEncode(value);
        }
    }
}
