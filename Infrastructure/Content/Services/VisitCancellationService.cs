using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class VisitCancellationService : IVisitCancellationService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IClientWalletService _clientWalletService;
        private readonly ICaregiverWalletService _caregiverWalletService;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<VisitCancellationService> _logger;

        private static readonly TimeZoneInfo NigerianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

        public VisitCancellationService(
            CareProDbContext dbContext,
            IClientWalletService clientWalletService,
            ICaregiverWalletService caregiverWalletService,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<VisitCancellationService> logger)
        {
            _dbContext = dbContext;
            _clientWalletService = clientWalletService;
            _caregiverWalletService = caregiverWalletService;
            _notificationService = notificationService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<CancelVisitResponse> CancelVisitAsync(string orderId, CancelVisitRequest request, string clientId)
        {
            // Validate order
            if (!ObjectId.TryParse(orderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{orderId}' not found.");

            if (order.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not authorized to cancel visits for this order.");

            // Validate task sheet
            if (!ObjectId.TryParse(request.TaskSheetId, out var taskSheetObjectId))
                throw new ArgumentException("Invalid task sheet ID format.");

            var taskSheet = await _dbContext.TaskSheets.FirstOrDefaultAsync(ts => ts.Id == taskSheetObjectId);
            if (taskSheet == null)
                throw new KeyNotFoundException($"Task sheet '{request.TaskSheetId}' not found.");

            if (taskSheet.OrderId != orderId)
                throw new InvalidOperationException("Task sheet does not belong to the specified order.");

            if (taskSheet.Status == "cancelled")
                throw new InvalidOperationException("This visit has already been cancelled.");

            if (taskSheet.Status == "submitted")
                throw new InvalidOperationException("Cannot cancel a visit that has already been submitted.");

            // Determine hours until the scheduled visit for credit split logic
            var nowNigeria = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NigerianTimeZone);
            double? hoursUntilVisit = null;

            if (taskSheet.ScheduledDate.HasValue)
            {
                // Try to get exact start time from the contract schedule
                var contract = await _dbContext.Contracts
                    .FirstOrDefaultAsync(c => c.OrderId == orderId &&
                        (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted));

                var visitDate = taskSheet.ScheduledDate.Value.Date;
                var visitDow = visitDate.DayOfWeek;
                DateTime visitStartNigeria = visitDate.AddHours(9); // Default 9am if no schedule

                if (contract != null)
                {
                    var slot = contract.Schedule.FirstOrDefault(s => s.DayOfWeek == visitDow);
                    if (slot != null && TimeSpan.TryParse(slot.StartTime, out var startTime))
                    {
                        visitStartNigeria = visitDate.Add(startTime);
                    }
                }

                hoursUntilVisit = (visitStartNigeria - nowNigeria).TotalHours;
            }

            // Get the approved contract for pricing
            var approvedContract = await _dbContext.Contracts
                .FirstOrDefaultAsync(c => c.OrderId == orderId &&
                    (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted));

            decimal fullAmount = 0;
            if (approvedContract?.SelectedPackage != null)
            {
                // Derive per-visit price from TotalAmount ÷ totalVisits — this is authoritative
                // and corrects any contracts where PricePerVisit was stored incorrectly.
                var totalVisits = approvedContract.SelectedPackage.VisitsPerWeek
                    * approvedContract.SelectedPackage.DurationWeeks;
                if (totalVisits > 0 && approvedContract.TotalAmount > 0)
                {
                    fullAmount = Math.Round(approvedContract.TotalAmount / totalVisits, 2);
                }
                else if (approvedContract.SelectedPackage.PricePerVisit > 0)
                {
                    // Fallback for legacy contracts where TotalAmount or DurationWeeks is 0
                    fullAmount = approvedContract.SelectedPackage.PricePerVisit;
                }
            }

            // 3-tier cancellation policy based on hours until visit
            // >= 24h: 100% to client, 0% to caregiver
            // 12-24h: 50% to client, 50% to caregiver
            // < 12h:  0% to client, 100% to caregiver (client forfeits)
            string cancellationTier;
            decimal clientCreditAmount = 0;
            decimal caregiverCreditAmount = 0;

            if (fullAmount > 0)
            {
                if (!hoursUntilVisit.HasValue || hoursUntilVisit.Value >= 24)
                {
                    cancellationTier = "early";
                    clientCreditAmount = fullAmount;
                }
                else if (hoursUntilVisit.Value >= 12)
                {
                    cancellationTier = "mid";
                    clientCreditAmount = Math.Round(fullAmount / 2, 2);
                    caregiverCreditAmount = fullAmount - clientCreditAmount;
                }
                else
                {
                    cancellationTier = "late";
                    caregiverCreditAmount = fullAmount;
                }
            }
            else
            {
                cancellationTier = "early";
            }

            // Mark the task sheet as cancelled
            taskSheet.Status = "cancelled";
            taskSheet.UpdatedAt = DateTime.UtcNow;
            _dbContext.TaskSheets.Update(taskSheet);
            await _dbContext.SaveChangesAsync();

            // Credit the client's wallet
            decimal newBalance = 0;
            if (clientCreditAmount > 0)
            {
                var description = cancellationTier == "mid"
                    ? $"Cancellation (12–24h notice) — 50% credit for visit #{taskSheet.SheetNumber}"
                    : $"Visit cancelled (24h+ notice) — full credit for visit #{taskSheet.SheetNumber}";

                await _clientWalletService.CreditAsync(
                    clientId,
                    clientCreditAmount,
                    description,
                    orderId,
                    request.TaskSheetId);

                var wallet = await _clientWalletService.GetOrCreateWalletAsync(clientId);
                newBalance = wallet.CreditBalance;
            }

            // Credit the caregiver's wallet for mid/late cancellations
            if (caregiverCreditAmount > 0)
            {
                await _caregiverWalletService.CreditVisitApprovedAsync(order.CaregiverId, caregiverCreditAmount);

                _logger.LogInformation(
                    "Cancellation split ({Tier}): ₦{ClientCredit} to client, ₦{CaregiverCredit} to caregiver {CaregiverId} for visit #{SheetNumber}",
                    cancellationTier, clientCreditAmount, caregiverCreditAmount, order.CaregiverId, taskSheet.SheetNumber);
            }

            // Notify the caregiver
            var caregiverId = order.CaregiverId;
            if (!string.IsNullOrEmpty(caregiverId))
            {
                var notificationContent = cancellationTier switch
                {
                    "late" => $"The client has cancelled visit #{taskSheet.SheetNumber} less than 12 hours before delivery. You will receive the full visit fee of ₦{caregiverCreditAmount:N2}.",
                    "mid" => $"The client has cancelled visit #{taskSheet.SheetNumber} between 12–24 hours before delivery. You will receive ₦{caregiverCreditAmount:N2} (50% of visit fee).",
                    _ => $"The client has cancelled visit #{taskSheet.SheetNumber} with 24+ hours notice."
                };

                if (!string.IsNullOrEmpty(request.Reason))
                    notificationContent += $" Reason: {request.Reason}";

                await _notificationService.CreateNotificationAsync(
                    caregiverId,
                    clientId,
                    NotificationTypes.VisitCancelledByClient,
                    notificationContent,
                    "Visit Cancelled",
                    orderId);

                // Send email to caregiver
                try
                {
                    var caregiver = await _dbContext.CareGivers
                        .FirstOrDefaultAsync(c => c.Id.ToString() == caregiverId);

                    if (caregiver != null)
                    {
                        var gig = await _dbContext.Gigs
                            .FirstOrDefaultAsync(g => g.Id.ToString() == order.GigId);

                        await _emailService.SendOrderCancelledEmailAsync(
                            caregiver.Email,
                            caregiver.FirstName,
                            gig?.Title ?? "Service",
                            request.Reason ?? "No reason provided",
                            orderId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send cancellation email to caregiver {CaregiverId}", caregiverId);
                }
            }

            _logger.LogInformation(
                "Visit cancelled: TaskSheet {TaskSheetId}, Order {OrderId}, Client {ClientId}. Tier: {Tier}, ClientCredit: {ClientCredit}, CaregiverCredit: {CaregiverCredit}",
                request.TaskSheetId, orderId, clientId, cancellationTier, clientCreditAmount, caregiverCreditAmount);

            var message = cancellationTier switch
            {
                "late" => $"Visit #{taskSheet.SheetNumber} has been cancelled (less than 12 hours notice). The full visit fee of ₦{fullAmount:N2} goes to the caregiver.",
                "mid" => $"Visit #{taskSheet.SheetNumber} has been cancelled (12–24 hours notice). ₦{clientCreditAmount:N2} has been credited to your wallet. ₦{caregiverCreditAmount:N2} goes to the caregiver.",
                _ when clientCreditAmount > 0 => $"Visit #{taskSheet.SheetNumber} has been cancelled (24+ hours notice). ₦{clientCreditAmount:N2} has been credited to your wallet.",
                _ => $"Visit #{taskSheet.SheetNumber} has been cancelled."
            };

            return new CancelVisitResponse
            {
                Success = true,
                Message = message,
                CreditAmount = clientCreditAmount > 0 ? clientCreditAmount : null,
                NewCreditBalance = clientCreditAmount > 0 ? newBalance : null,
                TaskSheetId = request.TaskSheetId
            };
        }

        public async Task<CancelVisitResponse> CaregiverRequestCancellationAsync(string orderId, CaregiverCancelVisitRequest request, string caregiverId)
        {
            // Validate order
            if (!ObjectId.TryParse(orderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{orderId}' not found.");

            if (order.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not the assigned caregiver for this order.");

            // Validate task sheet
            if (!ObjectId.TryParse(request.TaskSheetId, out var taskSheetObjectId))
                throw new ArgumentException("Invalid task sheet ID format.");

            var taskSheet = await _dbContext.TaskSheets.FirstOrDefaultAsync(ts => ts.Id == taskSheetObjectId);
            if (taskSheet == null)
                throw new KeyNotFoundException($"Task sheet '{request.TaskSheetId}' not found.");

            if (taskSheet.OrderId != orderId)
                throw new InvalidOperationException("Task sheet does not belong to the specified order.");

            if (taskSheet.Status == "cancelled")
                throw new InvalidOperationException("This visit has already been cancelled.");

            if (taskSheet.Status == "submitted")
                throw new InvalidOperationException("Cannot cancel a visit that has already been submitted.");

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidOperationException("A reason is required when a caregiver requests cancellation.");

            // Calculate hours until visit
            var nowNigeria = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NigerianTimeZone);
            double? hoursUntilVisit = null;

            if (taskSheet.ScheduledDate.HasValue)
            {
                var contract = await _dbContext.Contracts
                    .FirstOrDefaultAsync(c => c.OrderId == orderId &&
                        (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted));

                var visitDate = taskSheet.ScheduledDate.Value.Date;
                var visitDow = visitDate.DayOfWeek;
                DateTime visitStartNigeria = visitDate.AddHours(9);

                if (contract != null)
                {
                    var slot = contract.Schedule.FirstOrDefault(s => s.DayOfWeek == visitDow);
                    if (slot != null && TimeSpan.TryParse(slot.StartTime, out var startTime))
                        visitStartNigeria = visitDate.Add(startTime);
                }

                hoursUntilVisit = (visitStartNigeria - nowNigeria).TotalHours;
            }

            bool hasAdequateNotice = !hoursUntilVisit.HasValue || hoursUntilVisit.Value >= 24;

            // Notify the client so they can cancel the visit
            var clientId = order.ClientId;
            if (!string.IsNullOrEmpty(clientId))
            {
                var caregiver = await _dbContext.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == caregiverId);
                var caregiverName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}" : "Your caregiver";

                var notificationContent = hasAdequateNotice
                    ? $"{caregiverName} has requested to cancel visit #{taskSheet.SheetNumber}. Reason: {request.Reason}. Please cancel the visit from your dashboard to receive a full refund."
                    : $"{caregiverName} has requested to cancel visit #{taskSheet.SheetNumber} with less than 24 hours notice. Reason: {request.Reason}. This matter will be reviewed by CarePro.";

                await _notificationService.CreateNotificationAsync(
                    clientId,
                    caregiverId,
                    NotificationTypes.VisitCancellationRequested,
                    notificationContent,
                    "Caregiver Cancellation Request",
                    orderId);

                // Email the client
                try
                {
                    var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == clientId);
                    if (client != null && !string.IsNullOrEmpty(client.Email))
                    {
                        var gig = await _dbContext.Gigs.FirstOrDefaultAsync(g => g.Id.ToString() == order.GigId);
                        await _emailService.SendGenericNotificationEmailAsync(
                            client.Email,
                            client.FirstName,
                            "Caregiver Visit Cancellation Request - CarePro",
                            $"<p>{caregiverName} has requested to cancel visit #{taskSheet.SheetNumber} for <strong>{gig?.Title ?? "your service"}</strong>.</p>" +
                            $"<p><strong>Reason:</strong> {request.Reason}</p>" +
                            (hasAdequateNotice
                                ? "<p>Please log in to your CarePro dashboard and cancel the visit to receive a full refund to your wallet.</p>"
                                : "<p>This cancellation was requested with less than 24 hours notice. CarePro will review this matter.</p>"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send caregiver cancellation email to client {ClientId}", clientId);
                }
            }

            _logger.LogInformation(
                "Caregiver {CaregiverId} requested cancellation of visit #{SheetNumber}, Order {OrderId}. AdequateNotice: {Notice}, HoursUntil: {Hours}",
                caregiverId, taskSheet.SheetNumber, orderId, hasAdequateNotice, hoursUntilVisit);

            var responseMessage = hasAdequateNotice
                ? $"Cancellation request sent. The client has been notified to cancel visit #{taskSheet.SheetNumber}."
                : $"Cancellation request sent with less than 24 hours notice. The client has been notified and CarePro will review this matter.";

            return new CancelVisitResponse
            {
                Success = true,
                Message = responseMessage,
                TaskSheetId = request.TaskSheetId
            };
        }
    }
}
