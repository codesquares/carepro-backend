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
            if (approvedContract?.SelectedPackage != null && approvedContract.SelectedPackage.PricePerVisit > 0)
            {
                fullAmount = approvedContract.SelectedPackage.PricePerVisit;
            }

            // Credit split: >= 12h before = 100% to client; < 12h = 50% client + 50% caregiver
            bool isLateCancellation = hoursUntilVisit.HasValue && hoursUntilVisit.Value < 12;
            decimal clientCreditAmount = 0;
            decimal caregiverCreditAmount = 0;

            if (fullAmount > 0)
            {
                if (isLateCancellation)
                {
                    clientCreditAmount = Math.Round(fullAmount / 2, 2);
                    caregiverCreditAmount = fullAmount - clientCreditAmount; // Avoid rounding loss
                }
                else
                {
                    clientCreditAmount = fullAmount;
                }
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
                var description = isLateCancellation
                    ? $"Late cancellation (<12h) — 50% credit for visit #{taskSheet.SheetNumber}"
                    : $"Visit cancelled — full credit for visit #{taskSheet.SheetNumber}";

                await _clientWalletService.CreditAsync(
                    clientId,
                    clientCreditAmount,
                    description,
                    orderId,
                    request.TaskSheetId);

                var wallet = await _clientWalletService.GetOrCreateWalletAsync(clientId);
                newBalance = wallet.CreditBalance;
            }

            // Credit the caregiver's wallet for late cancellations
            if (caregiverCreditAmount > 0)
            {
                await _caregiverWalletService.CreditVisitApprovedAsync(order.CaregiverId, caregiverCreditAmount);

                _logger.LogInformation(
                    "Late cancellation split: ₦{ClientCredit} to client, ₦{CaregiverCredit} to caregiver {CaregiverId} for visit #{SheetNumber}",
                    clientCreditAmount, caregiverCreditAmount, order.CaregiverId, taskSheet.SheetNumber);
            }

            // Notify the caregiver
            var caregiverId = order.CaregiverId;
            if (!string.IsNullOrEmpty(caregiverId))
            {
                var notificationContent = isLateCancellation
                    ? $"The client has cancelled visit #{taskSheet.SheetNumber} less than 12 hours before delivery. You will receive ₦{caregiverCreditAmount:N2} compensation."
                    : $"The client has cancelled visit #{taskSheet.SheetNumber}.";

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
                "Visit cancelled: TaskSheet {TaskSheetId}, Order {OrderId}, Client {ClientId}. ClientCredit: {ClientCredit}, CaregiverCredit: {CaregiverCredit}, LateCancellation: {Late}",
                request.TaskSheetId, orderId, clientId, clientCreditAmount, caregiverCreditAmount, isLateCancellation);

            var message = isLateCancellation
                ? $"Visit #{taskSheet.SheetNumber} has been cancelled (late cancellation). ₦{clientCreditAmount:N2} has been credited to your wallet. ₦{caregiverCreditAmount:N2} goes to the caregiver."
                : clientCreditAmount > 0
                    ? $"Visit #{taskSheet.SheetNumber} has been cancelled. ₦{clientCreditAmount:N2} has been credited to your wallet."
                    : $"Visit #{taskSheet.SheetNumber} has been cancelled.";

            return new CancelVisitResponse
            {
                Success = true,
                Message = message,
                CreditAmount = clientCreditAmount > 0 ? clientCreditAmount : null,
                NewCreditBalance = clientCreditAmount > 0 ? newBalance : null,
                TaskSheetId = request.TaskSheetId
            };
        }
    }
}
