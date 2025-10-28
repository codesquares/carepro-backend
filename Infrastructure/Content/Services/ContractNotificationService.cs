using Application.Interfaces.Content;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Interfaces.Email;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class ContractNotificationService : IContractNotificationService
    {
        private readonly CareProDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<ContractNotificationService> _logger;

        public ContractNotificationService(
            CareProDbContext context,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<ContractNotificationService> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<bool> SendContractNotificationToCaregiverAsync(string contractId)
        {
            try
            {
                var contract = await GetContractWithDetailsAsync(contractId);
                if (contract == null)
                {
                    _logger.LogWarning("Contract {ContractId} not found for notification", contractId);
                    return false;
                }

                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.ClientId));
                var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.CaregiverId));

                if (client == null || caregiver == null)
                {
                    _logger.LogWarning("Client or Caregiver not found for contract {ContractId}", contractId);
                    return false;
                }

                // Create in-app notification
                var notificationMessage = $"New care contract from {client.FirstName} {client.LastName}. " +
                    $"Package: {contract.SelectedPackage.VisitsPerWeek} visits/week for {contract.SelectedPackage.DurationWeeks} weeks. " +
                    $"Total: ${contract.TotalAmount:F2}";

                await _notificationService.CreateNotificationAsync(
                    recipientId: contract.CaregiverId,
                    senderId: contract.ClientId,
                    type: "contract_received",
                    content: notificationMessage,
                    Title: "New Care Contract",
                    relatedEntityId: contractId
                );

                // Create dashboard notification for contract response
                await CreateDashboardNotificationAsync(
                    contract.CaregiverId,
                    "You have received a new care contract. Please review and respond.",
                    "contract_pending",
                    contractId
                );

                _logger.LogInformation("Contract notification sent to caregiver {CaregiverId} for contract {ContractId}",
                    contract.CaregiverId, contractId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contract notification for contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> SendContractEmailToCaregiverAsync(string contractId)
        {
            try
            {
                var contract = await GetContractWithDetailsAsync(contractId);
                if (contract == null) return false;

                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.ClientId));
                var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.CaregiverId));
                var gig = await _context.Gigs.FirstOrDefaultAsync(g => g.Id == ObjectId.Parse(contract.GigId));

                if (client == null || caregiver == null || string.IsNullOrEmpty(caregiver.Email))
                {
                    _logger.LogWarning("Missing required data for email notification for contract {ContractId}", contractId);
                    return false;
                }

                var emailContent = GenerateContractEmailContent(contract, client, gig);
                var subject = $"New Care Contract - {gig?.Title ?? "Care Services"}";

                // Use existing interface method
                await _emailService.SendNotificationEmailAsync(
                    caregiver.Email,
                    caregiver.FirstName,
                    1
                );

                _logger.LogInformation("Contract email sent to caregiver {CaregiverId} for contract {ContractId}",
                    contract.CaregiverId, contractId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contract email for contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> NotifyClientOfResponseAsync(string contractId, string response)
        {
            try
            {
                var contract = await GetContractWithDetailsAsync(contractId);
                if (contract == null) return false;

                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.ClientId));
                var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.CaregiverId));

                if (client == null || caregiver == null) return false;

                var caregiverName = $"{caregiver.FirstName} {caregiver.LastName}";
                string notificationMessage;
                string notificationType;

                switch (response.ToLower())
                {
                    case "accept":
                        notificationMessage = $"{caregiverName} has accepted your care contract. Your service is confirmed!";
                        notificationType = "contract_accepted";
                        break;
                    case "reject":
                        notificationMessage = $"{caregiverName} has declined your care contract. View alternative caregivers.";
                        notificationType = "contract_rejected";
                        break;
                    case "review":
                        notificationMessage = $"{caregiverName} has requested to review your care contract terms.";
                        notificationType = "contract_review_requested";
                        break;
                    default:
                        notificationMessage = $"{caregiverName} has responded to your care contract.";
                        notificationType = "contract_response";
                        break;
                }

                // Create in-app notification
                await _notificationService.CreateNotificationAsync(
                    recipientId: contract.ClientId,
                    senderId: contract.CaregiverId,
                    type: notificationType,
                    content: notificationMessage,
                    Title: "Care Contract Response",
                    relatedEntityId: contractId
                );

                // Send email notification
                if (!string.IsNullOrEmpty(client.Email))
                {
                    await _emailService.SendNotificationEmailAsync(
                        client.Email,
                        client.FirstName,
                        1
                    );
                }

                _logger.LogInformation("Client notification sent for contract {ContractId} response: {Response}",
                    contractId, response);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying client of response for contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> SendContractReminderToCaregiverAsync(string contractId)
        {
            try
            {
                var contract = await GetContractWithDetailsAsync(contractId);
                if (contract == null) return false;

                var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.CaregiverId));
                if (caregiver == null) return false;

                var reminderMessage = "Reminder: You have a pending care contract awaiting your response. " +
                    "Please review and respond to avoid expiry.";

                await _notificationService.CreateNotificationAsync(
                    recipientId: contract.CaregiverId,
                    senderId: "system",
                    type: "contract_reminder",
                    content: reminderMessage,
                    Title: "Contract Response Reminder",
                    relatedEntityId: contractId
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contract reminder for contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> NotifyClientOfExpiryAsync(string contractId)
        {
            try
            {
                var contract = await GetContractWithDetailsAsync(contractId);
                if (contract == null) return false;

                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.ClientId));
                if (client == null) return false;

                var expiryMessage = "Your care contract has expired without a response. " +
                    "You can view alternative caregivers or create a new contract.";

                await _notificationService.CreateNotificationAsync(
                    recipientId: contract.ClientId,
                    senderId: "system",
                    type: "contract_expired",
                    content: expiryMessage,
                    Title: "Contract Expired",
                    relatedEntityId: contractId
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying client of expiry for contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> SendResponseEmailToClientAsync(string contractId, string response)
        {
            try
            {
                var contract = await GetContractWithDetailsAsync(contractId);
                if (contract == null) return false;

                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.ClientId));
                var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == ObjectId.Parse(contract.CaregiverId));

                if (client == null || caregiver == null || string.IsNullOrEmpty(client.Email))
                    return false;

                var emailContent = GenerateResponseEmailContent(contract, client, caregiver, response);
                var subject = $"Care Contract {response.ToTitleCase()} - CarePro";

                await _emailService.SendNotificationEmailAsync(
                    client.Email,
                    client.FirstName,
                    1
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending response email for contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> CreateDashboardNotificationAsync(string userId, string message, string type, string contractId)
        {
            try
            {
                await _notificationService.CreateNotificationAsync(
                    recipientId: userId,
                    senderId: "system",
                    type: type,
                    content: message,
                    Title: "Contract Update",
                    relatedEntityId: contractId
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating dashboard notification for user {UserId}", userId);
                return false;
            }
        }

        private async Task<Contract> GetContractWithDetailsAsync(string contractId)
        {
            return await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
        }

        private string GenerateContractEmailContent(Contract contract, Client client, Gig gig)
        {
            var clientName = $"{client.FirstName} {client.LastName}";
            var gigTitle = gig?.Title ?? "Care Services";
            var tasksList = string.Join("\n", contract.Tasks.Select(t => $"• {t.Title}: {t.Description}"));

            return $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #2c5aa0;'>New Care Contract Received</h2>
        
        <p>You have received a new care contract from <strong>{clientName}</strong>.</p>
        
        <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
            <h3 style='margin-top: 0;'>Contract Details:</h3>
            <p><strong>Service:</strong> {gigTitle}</p>
            <p><strong>Package:</strong> {contract.SelectedPackage.VisitsPerWeek} visits per week for {contract.SelectedPackage.DurationWeeks} weeks</p>
            <p><strong>Rate:</strong> ${contract.SelectedPackage.PricePerVisit:F2} per visit</p>
            <p><strong>Total Value:</strong> ${contract.TotalAmount:F2}</p>
            <p><strong>Start Date:</strong> {contract.ContractStartDate:MMMM dd, yyyy}</p>
        </div>
        
        <div style='background-color: #e8f4fd; padding: 15px; border-radius: 5px; margin: 20px 0;'>
            <h4 style='margin-top: 0;'>Required Tasks:</h4>
            <div style='margin-left: 10px;'>
                {tasksList.Replace("\n", "<br/>")}
            </div>
        </div>
        
        <div style='background-color: #fff3cd; padding: 15px; border-radius: 5px; margin: 20px 0;'>
            <p style='margin: 0;'><strong>Action Required:</strong> Please log in to your CarePro dashboard to review the full contract terms and respond.</p>
        </div>
        
        <div style='text-align: center; margin: 30px 0;'>
            <a href='#' style='background-color: #28a745; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>Review Contract</a>
        </div>
        
        <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;'>
        
        <p style='font-size: 12px; color: #666; text-align: center;'>
            This is an automated message from CarePro. Please do not reply to this email.
        </p>
    </div>
</body>
</html>";
        }

        private string GenerateResponseEmailContent(Contract contract, Client client, Caregiver caregiver, string response)
        {
            var caregiverName = $"{caregiver.FirstName} {caregiver.LastName}";
            var clientName = $"{client.FirstName} {client.LastName}";

            string responseMessage;
            string actionButton = "";
            string responseColor;

            switch (response.ToLower())
            {
                case "accept":
                    responseMessage = $"{caregiverName} has <strong style='color: #28a745;'>accepted</strong> your care contract! Your service is now confirmed.";
                    responseColor = "#28a745";
                    actionButton = "<a href='#' style='background-color: #17a2b8; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>View Contract Details</a>";
                    break;
                case "reject":
                    responseMessage = $"{caregiverName} has <strong style='color: #dc3545;'>declined</strong> your care contract. Don't worry - we'll help you find alternative caregivers.";
                    responseColor = "#dc3545";
                    actionButton = "<a href='#' style='background-color: #ffc107; color: #212529; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>View Alternatives</a>";
                    break;
                case "review":
                    responseMessage = $"{caregiverName} has requested to <strong style='color: #ffc107;'>review</strong> your care contract terms. You can make adjustments or discuss the details.";
                    responseColor = "#ffc107";
                    actionButton = "<a href='#' style='background-color: #6c757d; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>Review & Revise</a>";
                    break;
                default:
                    responseMessage = $"{caregiverName} has responded to your care contract.";
                    responseColor = "#6c757d";
                    break;
            }

            return $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #2c5aa0;'>Care Contract Response</h2>
        
        <p>Hello {clientName},</p>
        
        <div style='background-color: #f8f9fa; padding: 20px; border-radius: 5px; margin: 20px 0; border-left: 4px solid {responseColor};'>
            <p style='margin: 0; font-size: 16px;'>{responseMessage}</p>
        </div>
        
        <div style='background-color: #e8f4fd; padding: 15px; border-radius: 5px; margin: 20px 0;'>
            <h4 style='margin-top: 0;'>Contract Summary:</h4>
            <p><strong>Package:</strong> {contract.SelectedPackage.VisitsPerWeek} visits per week for {contract.SelectedPackage.DurationWeeks} weeks</p>
            <p><strong>Total Value:</strong> ${contract.TotalAmount:F2}</p>
            <p><strong>Tasks:</strong> {contract.Tasks.Count} care tasks specified</p>
        </div>
        
        <div style='text-align: center; margin: 30px 0;'>
            {actionButton}
        </div>
        
        <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;'>
        
        <p style='font-size: 12px; color: #666; text-align: center;'>
            This is an automated message from CarePro. Please do not reply to this email.<br/>
            For support, contact us at support@carepro.com
        </p>
    </div>
</body>
</html>";
        }
    }
}