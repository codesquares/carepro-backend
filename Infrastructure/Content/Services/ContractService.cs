using Application.Commands;
using Application.Interfaces.Content;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Content.Services
{
    public class ContractService : IContractService
    {
        private readonly CareProDbContext _context;
        private readonly ILogger<ContractService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMediator _mediator;

        public ContractService(
            CareProDbContext context,
            ILogger<ContractService> logger,
            IConfiguration configuration,
            IMediator mediator)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _mediator = mediator;
        }

        public async Task<ContractDTO> GetContractByIdAsync(string contractId)
        {
            var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
            return contract != null ? MapToContractDTO(contract) : throw new InvalidOperationException("Contract not found");
        }

        public async Task<ContractGenerationDataDTO?> GetContractPdfDataAsync(string contractId)
        {
            var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
            if (contract == null) return null;

            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id.ToString() == contract.ClientId);
            var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == contract.CaregiverId);
            var gig = !string.IsNullOrEmpty(contract.GigId)
                ? await _context.Gigs.FirstOrDefaultAsync(g => g.Id.ToString() == contract.GigId)
                : null;

            return new ContractGenerationDataDTO
            {
                ContractId = contract.Id,
                OrderId = contract.OrderId,
                GeneratedAt = contract.CreatedAt,
                ClientId = contract.ClientId,
                ClientFullName = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Client",
                ClientEmail = client?.Email,
                ClientPhone = client?.PhoneNo,
                CaregiverId = contract.CaregiverId,
                CaregiverFullName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "Caregiver",
                CaregiverEmail = caregiver?.Email,
                CaregiverPhone = caregiver?.PhoneNo,
                CaregiverQualifications = caregiver?.AboutMe,
                GigTitle = gig?.Title ?? "Care Service",
                GigDescription = gig != null ? string.Join(", ", gig.PackageDetails ?? new List<string>()) : null,
                GigCategory = gig?.Category,
                Package = contract.SelectedPackage,
                TotalAmountPaid = contract.TotalAmount,
                TransactionReference = contract.PaymentTransactionId,
                Schedule = contract.Schedule,
                ServiceAddress = contract.ServiceAddress ?? "On file",
                City = client?.PreferredCity,
                State = client?.PreferredState,
                SpecialClientRequirements = contract.SpecialClientRequirements,
                AccessInstructions = contract.AccessInstructions,
                CaregiverNotes = contract.CaregiverAdditionalNotes,
                Tasks = contract.Tasks,
                ContractStartDate = contract.ContractStartDate,
                ContractEndDate = contract.ContractEndDate
            };
        }

        public async Task<List<ContractDTO>> GetContractsByClientIdAsync(string clientId)
        {
            var contracts = await _context.Contracts
                .Where(c => c.ClientId == clientId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return contracts.Select(MapToContractDTO).ToList();
        }

        public async Task<List<ContractDTO>> GetContractsByCaregiverIdAsync(string caregiverId)
        {
            var contracts = await _context.Contracts
                .Where(c => c.CaregiverId == caregiverId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return contracts.Select(MapToContractDTO).ToList();
        }

        public async Task<bool> UpdateContractStatusAsync(string contractId, string status)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) return false;

                contract.Status = ContractStatus.Pending;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating contract status for {ContractId}", contractId);
                return false;
            }
        }

        public async Task<ContractAnalyticsDTO> GetContractAnalyticsAsync(string userId, string userType)
        {
            try
            {
                var query = userType.ToLower() == "client"
                    ? _context.Contracts.Where(c => c.ClientId == userId)
                    : _context.Contracts.Where(c => c.CaregiverId == userId);

                var contracts = await query.ToListAsync();
                var currentMonth = DateTime.UtcNow.Month;
                var currentYear = DateTime.UtcNow.Year;

                var monthlyContracts = contracts.Where(c => c.CreatedAt.Month == currentMonth && c.CreatedAt.Year == currentYear).ToList();
                var monthlyEarnings = monthlyContracts.Sum(c => c.TotalAmount);

                var stats = new ContractStatsDTO
                {
                    TotalContracts = contracts.Count,
                    AcceptedContracts = contracts.Count(c => c.Status == ContractStatus.Accepted),
                    RejectedContracts = contracts.Count(c => c.Status == ContractStatus.Rejected),
                    AcceptanceRate = contracts.Any() ? (double)contracts.Count(c => c.Status == ContractStatus.Accepted) / contracts.Count * 100 : 0,
                    TotalEarnings = contracts.Sum(c => c.TotalAmount),
                    AverageRating = 4.5
                };

                return new ContractAnalyticsDTO
                {
                    Stats = stats,
                    RecentContracts = contracts.Take(10).Select(MapToContractDTO).ToList(),
                    MonthlyEarnings = monthlyEarnings,
                    ContractsThisMonth = monthlyContracts.Count,
                    MonthlyTrends = new List<ContractTrendDTO>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract analytics for user {UserId}", userId);
                throw;
            }
        }

        public async Task<List<ContractDTO>> GetActiveContractsAsync()
        {
            try
            {
                var activeContracts = await _context.Contracts
                    .Where(c => c.Status == ContractStatus.Accepted)
                    .ToListAsync();

                return activeContracts.Select(MapToContractDTO).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active contracts");
                return new List<ContractDTO>();
            }
        }

        public async Task<List<ContractDTO>> GetExpiredContractsAsync()
        {
            try
            {
                var expiredContracts = await _context.Contracts
                    .Where(c => c.ContractEndDate < DateTime.UtcNow && c.Status != ContractStatus.Completed)
                    .ToListAsync();

                return expiredContracts.Select(MapToContractDTO).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving expired contracts");
                return new List<ContractDTO>();
            }
        }

        public async Task<List<ContractHistoryDTO>> GetContractHistoryAsync(string contractId)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) return new List<ContractHistoryDTO>();

                var historyItem = new ContractHistoryDTO
                {
                    ActiveContracts = new List<ContractDTO>(),
                    CompletedContracts = new List<ContractDTO>(),
                    PendingContracts = new List<ContractDTO>(),
                    Stats = new ContractStatsDTO()
                };

                var contractDto = MapToContractDTO(contract);

                switch (contract.Status)
                {
                    case ContractStatus.Accepted:
                        historyItem.ActiveContracts.Add(contractDto);
                        break;
                    case ContractStatus.Completed:
                        historyItem.CompletedContracts.Add(contractDto);
                        break;
                    case ContractStatus.Pending:
                    case ContractStatus.Sent:
                        historyItem.PendingContracts.Add(contractDto);
                        break;
                }

                return new List<ContractHistoryDTO> { historyItem };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving contract history for contract {ContractId}", contractId);
                return new List<ContractHistoryDTO>();
            }
        }

        public async Task<bool> ExpireContractAsync(string contractId)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) return false;

                contract.Status = ContractStatus.Expired;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error expiring contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> CompleteContractAsync(string contractId, decimal? rating = null)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) return false;

                contract.Status = ContractStatus.Completed;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<bool> TerminateContractAsync(string contractId, string reason)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) return false;

                contract.Status = ContractStatus.Terminated;
                contract.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Contract {ContractId} terminated. Reason: {Reason}. " +
                    "Checking for linked subscription...",
                    contractId, reason);

                // Find and handle any linked subscription
                var subscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.ContractId == contractId &&
                        s.Status != SubscriptionStatus.Cancelled &&
                        s.Status != SubscriptionStatus.Terminated &&
                        s.Status != SubscriptionStatus.Expired);

                if (subscription != null)
                {
                    await TerminateLinkedSubscriptionAsync(subscription, reason);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error terminating contract {ContractId}", contractId);
                return false;
            }
        }

        private async Task TerminateLinkedSubscriptionAsync(Subscription subscription, string reason)
        {
            var now = DateTime.UtcNow;
            subscription.Status = SubscriptionStatus.Terminated;
            subscription.TerminatedAt = now;
            subscription.CancellationReason = reason;
            subscription.CancelledBy = "system";
            subscription.AutoRenew = false;
            subscription.NextChargeDate = null;
            subscription.RefundAmount = null;
            subscription.UpdatedAt = now;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Linked subscription {SubscriptionId} terminated via contract termination.",
                subscription.Id);

            var clientGuidance = string.IsNullOrEmpty(subscription.OriginalOrderId)
                ? string.Empty
                : " If you want an immediate refund for undelivered service, please cancel the current order as well.";

            await _mediator.Send(new SendNotificationCommand(
                subscription.ClientId,
                "system",
                NotificationTypes.SubscriptionTerminated,
                $"Your subscription has been terminated because the associated contract was ended.{clientGuidance}",
                "Subscription Terminated",
                subscription.Id));

            await _mediator.Send(new SendNotificationCommand(
                subscription.CaregiverId,
                "system",
                NotificationTypes.SubscriptionTerminated,
                "A subscription for your service has been terminated because the associated contract was ended.",
                "Subscription Terminated",
                subscription.Id));
        }

        private ContractDTO MapToContractDTO(Contract contract)
        {
            return new ContractDTO
            {
                Id = contract.Id,
                OrderId = contract.OrderId,
                GigId = contract.GigId,
                ClientId = contract.ClientId,
                CaregiverId = contract.CaregiverId,
                SelectedPackage = new PackageSelectionDTO
                {
                    PackageType = contract.SelectedPackage.PackageType,
                    VisitsPerWeek = contract.SelectedPackage.VisitsPerWeek,
                    PricePerVisit = contract.SelectedPackage.PricePerVisit,
                    TotalWeeklyPrice = contract.SelectedPackage.TotalWeeklyPrice,
                    DurationWeeks = contract.SelectedPackage.DurationWeeks
                },
                Tasks = contract.Tasks.Select(t => new ClientTaskDTO
                {
                    Title = t.Title,
                    Description = t.Description,
                    Category = t.Category.ToString(),
                    Priority = t.Priority.ToString(),
                    SpecialRequirements = t.SpecialRequirements,
                    EstimatedDurationMinutes = t.EstimatedDuration?.TotalMinutes > 0
                        ? (int)t.EstimatedDuration.Value.TotalMinutes
                        : null
                }).ToList(),
                ProposedTasks = contract.ProposedTasks?.Select(pt => new ContractProposedTaskDTO
                {
                    Id = pt.Id,
                    Title = pt.Title,
                    Description = pt.Description,
                    Category = pt.Category.ToString(),
                    Priority = pt.Priority.ToString(),
                    ProposedBy = pt.ProposedBy,
                    ProposedByRole = pt.ProposedByRole,
                    Status = pt.Status,
                    ResponseNote = pt.ResponseNote,
                    ProposedAt = pt.ProposedAt,
                    RespondedAt = pt.RespondedAt
                }).ToList() ?? new List<ContractProposedTaskDTO>(),
                GeneratedTerms = contract.GeneratedTerms,
                TotalAmount = contract.TotalAmount,
                Status = contract.Status.ToString(),
                PaymentTransactionId = contract.PaymentTransactionId,

                SubmittedByCaregiverId = contract.SubmittedByCaregiverId,
                SubmittedByClientId = contract.SubmittedByClientId,
                InitiatedByRole = contract.InitiatedByRole,
                SubmittedAt = contract.SubmittedAt,
                Schedule = contract.Schedule?.Select(s => new ScheduledVisitDTO
                {
                    DayOfWeek = s.DayOfWeek.ToString(),
                    StartTime = s.StartTime,
                    EndTime = s.EndTime
                }).ToList() ?? new List<ScheduledVisitDTO>(),
                ServiceAddress = contract.ServiceAddress,
                ServiceLocationSetByClient = contract.ServiceLocationSetByClient,
                ServiceLocationSetAt = contract.ServiceLocationSetAt,
                SpecialClientRequirements = contract.SpecialClientRequirements,
                AccessInstructions = contract.AccessInstructions,
                CaregiverAdditionalNotes = contract.CaregiverAdditionalNotes,
                ClientApprovedAt = contract.ClientApprovedAt,
                ClientApprovedBy = contract.ClientApprovedBy,
                NegotiationRound = contract.NegotiationRound,
                ClientReviewRequestedAt = contract.ClientReviewRequestedAt,
                ClientReviewComments = contract.ClientReviewComments,

                SentAt = contract.SentAt,
                RespondedAt = contract.RespondedAt,
                AcceptedAt = contract.AcceptedAt,
                AcceptedBy = contract.AcceptedBy,
                RejectedAt = contract.RejectedAt,
                RejectedBy = contract.RejectedBy,
                RejectionReason = contract.RejectionReason,
                ReviewRequestedAt = contract.ReviewRequestedAt,
                ReviewRequestedBy = contract.ReviewRequestedBy,
                ReviewComments = contract.ReviewComments,
                CaregiverResponse = contract.CaregiverResponse,
                Comments = contract.Comments,
                ContractStartDate = contract.ContractStartDate,
                ContractEndDate = contract.ContractEndDate,
                CreatedAt = contract.CreatedAt
            };
        }

        public async Task<SetServiceLocationResponse> SetServiceLocationAsync(
            string contractId, string clientId, SetServiceLocationRequest request)
        {
            var maxAccuracyMeters = _configuration.GetValue<double>("VisitCheckin:MaxAccuracyMeters", 300);

            if (request.Accuracy > maxAccuracyMeters)
                throw new ArgumentException(
                    $"GPS accuracy is too weak ({request.Accuracy:F0}m). " +
                    $"Move outdoors and try again once accuracy improves to {maxAccuracyMeters:F0}m or better.");

            var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
            if (contract == null)
                throw new KeyNotFoundException($"Contract '{contractId}' not found.");

            if (contract.ClientId != clientId)
                throw new UnauthorizedAccessException("You are not the client on this contract.");

            if (contract.Status == ContractStatus.Cancelled || contract.Status == ContractStatus.Expired)
                throw new InvalidOperationException("Cannot set service location on a cancelled or expired contract.");

            var previousLat = contract.ServiceLatitude;
            var previousLng = contract.ServiceLongitude;
            var wasClientSet = contract.ServiceLocationSetByClient;

            contract.ServiceLatitude = request.Latitude;
            contract.ServiceLongitude = request.Longitude;
            contract.ServiceLocationSetByClient = true;
            contract.ServiceLocationSetAt = DateTime.UtcNow;

            _context.Contracts.Update(contract);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Client {ClientId} set GPS on contract {ContractId}: {Lat}, {Lng} (accuracy {Acc:F0}m). " +
                "Previous coords: {PrevLat}, {PrevLng}, wasClientSet={WasClientSet}",
                clientId, contractId, request.Latitude, request.Longitude, request.Accuracy,
                previousLat, previousLng, wasClientSet);

            // Notify the caregiver so they know location verification is now active
            try
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: contract.CaregiverId,
                    SenderId: clientId,
                    Type: NotificationTypes.ServiceLocationSet,
                    Content: "Your client has confirmed their service location. Please ensure you are at the correct address before checking in.",
                    Title: "Service Location Confirmed",
                    RelatedEntityId: contractId,
                    OrderId: contract.OrderId
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send service_location_set notification for contract {ContractId}", contractId);
            }

            return new SetServiceLocationResponse
            {
                Success = true,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Accuracy = request.Accuracy,
                SetAt = contract.ServiceLocationSetAt!.Value
            };
        }
    }
}
