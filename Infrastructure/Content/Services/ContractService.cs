using Application.Interfaces.Content;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class ContractService : IContractService
    {
        private readonly CareProDbContext _context;
        private readonly IContractLLMService _llmService;
        private readonly IContractNotificationService _notificationService;
        private readonly ILocationService _locationService;
        private readonly ILogger<ContractService> _logger;
        private readonly IConfiguration _configuration;

        public ContractService(
            CareProDbContext context,
            IContractLLMService llmService,
            IContractNotificationService notificationService,
            ILocationService locationService,
            ILogger<ContractService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _llmService = llmService;
            _notificationService = notificationService;
            _locationService = locationService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<ContractDTO> GenerateContractAsync(ContractGenerationRequestDTO request)
        {
            try
            {
                // Validate request
                if (request?.GigId == null || request.ClientId == null || request.CaregiverId == null || request.SelectedPackage == null)
                {
                    throw new ArgumentException("Invalid contract generation request");
                }

                _logger.LogInformation("Generating contract for Gig {GigId}, Client {ClientId}, Caregiver {CaregiverId}",
                    request.GigId, request.ClientId, request.CaregiverId);

                // Generate contract terms using LLM
                var contractTerms = await _llmService.GenerateContractAsync(
                    request.GigId,
                    new PackageSelection
                    {
                        PackageType = request.SelectedPackage.PackageType ?? string.Empty,
                        VisitsPerWeek = request.SelectedPackage.VisitsPerWeek,
                        DurationWeeks = request.SelectedPackage.DurationWeeks,
                        PricePerVisit = request.SelectedPackage.PricePerVisit
                    },
                    request.Tasks?.Select(t => new ClientTask
                    {
                        Title = t.Title ?? string.Empty,
                        Description = t.Description ?? string.Empty,
                        Category = Enum.TryParse<TaskCategory>(t.Category, true, out var category) ? category : TaskCategory.Other,
                        Priority = Enum.TryParse<TaskPriority>(t.Priority, true, out var priority) ? priority : TaskPriority.Medium
                    }).ToList() ?? new List<ClientTask>(),
                    request.SelectedPackage.TotalWeeklyPrice * request.SelectedPackage.DurationWeeks
                );

                // Calculate contract dates
                var startDate = DateTime.UtcNow.AddDays(1);
                var endDate = startDate.AddDays(request.SelectedPackage.DurationWeeks * 7);

                // Create contract entity
                var contract = new Contract
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    GigId = request.GigId,
                    ClientId = request.ClientId,
                    CaregiverId = request.CaregiverId,
                    PaymentTransactionId = request.PaymentTransactionId,
                    SelectedPackage = new PackageSelection
                    {
                        PackageType = request.SelectedPackage.PackageType ?? string.Empty,
                        VisitsPerWeek = request.SelectedPackage.VisitsPerWeek,
                        PricePerVisit = request.SelectedPackage.PricePerVisit,
                        TotalWeeklyPrice = request.SelectedPackage.TotalWeeklyPrice,
                        DurationWeeks = request.SelectedPackage.DurationWeeks
                    },
                    Tasks = request.Tasks?.Select(t => new ClientTask
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Title = t.Title ?? string.Empty,
                        Description = t.Description ?? string.Empty,
                        Category = Enum.TryParse<TaskCategory>(t.Category, true, out var cat) ? cat : TaskCategory.Other,
                        Priority = Enum.TryParse<TaskPriority>(t.Priority, true, out var pri) ? pri : TaskPriority.Medium,
                        SpecialRequirements = t.SpecialRequirements ?? new List<string>()
                    }).ToList() ?? new List<ClientTask>(),
                    GeneratedTerms = contractTerms,
                    TotalAmount = request.SelectedPackage.TotalWeeklyPrice * request.SelectedPackage.DurationWeeks,
                    Status = ContractStatus.Generated,
                    ContractStartDate = startDate,
                    ContractEndDate = endDate,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.Contracts.AddAsync(contract);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Contract {ContractId} generated successfully", contract.Id);
                return MapToContractDTO(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating contract for Gig {GigId}", request.GigId);
                throw;
            }
        }

        public async Task<ContractDTO> GenerateContractFromOrderAsync(string orderId)
        {
            try
            {
                _logger.LogInformation("Generating contract from order {OrderId}", orderId);

                // Get the order
                var order = await _context.ClientOrders
                    .FirstOrDefaultAsync(o => o.Id.ToString() == orderId);

                if (order == null)
                    throw new InvalidOperationException($"Order {orderId} not found");

                // Get OrderTasks to extract contract details
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.ClientOrderId == orderId);

                if (orderTasks == null)
                    throw new InvalidOperationException($"OrderTasks not found for order {orderId}");

                // Check if contract already exists for this order
                var existingContract = await _context.Contracts
                    .FirstOrDefaultAsync(c => c.PaymentTransactionId == order.TransactionId);

                if (existingContract != null)
                    throw new InvalidOperationException($"Contract already exists for order {orderId}");

                // Prepare contract generation request using OrderTasks data
                var contractRequest = new ContractGenerationRequestDTO
                {
                    GigId = orderTasks.GigId,
                    ClientId = orderTasks.ClientId,
                    CaregiverId = orderTasks.CaregiverId,
                    PaymentTransactionId = order.TransactionId,
                    SelectedPackage = new PackageSelectionDTO
                    {
                        PackageType = orderTasks.PackageSelection.PackageType,
                        VisitsPerWeek = orderTasks.PackageSelection.VisitsPerWeek,
                        PricePerVisit = orderTasks.PackageSelection.PricePerVisit,
                        TotalWeeklyPrice = orderTasks.PackageSelection.TotalWeeklyPrice,
                        DurationWeeks = orderTasks.PackageSelection.DurationWeeks
                    },
                    Tasks = orderTasks.CareTasks.Select(t => new ClientTaskDTO
                    {
                        Title = t.Title,
                        Description = t.Description,
                        Category = t.Category.ToString(),
                        Priority = t.Priority.ToString(),
                        SpecialRequirements = t.SpecialRequirements
                    }).ToList()
                };

                // Generate the contract using existing method
                var contract = await GenerateContractAsync(contractRequest);

                // Send contract to caregiver
                await SendContractToCaregiverAsync(contract.Id);

                _logger.LogInformation("Contract {ContractId} generated and sent from order {OrderId}",
                    contract.Id, orderId);

                return contract;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating contract from order {OrderId}", orderId);
                throw;
            }
        }

        public async Task<bool> SendContractToCaregiverAsync(string contractId)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) return false;

                contract.Status = ContractStatus.Sent;
                contract.SentAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _notificationService.SendContractNotificationToCaregiverAsync(contractId);
                await _notificationService.SendContractEmailToCaregiverAsync(contractId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<ContractDTO> ProcessCaregiverResponseAsync(CaregiverContractResponseDTO response)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == response.ContractId);
                if (contract == null) throw new InvalidOperationException("Contract not found");

                contract.RespondedAt = DateTime.UtcNow;
                contract.CaregiverResponse = response.Response;
                contract.Status = ContractStatus.Pending;

                if (response.Response.ToLower() == "review")
                {
                    contract.ReviewComments = string.Join("; ", response.Comments ?? new List<string>());
                }

                await _context.SaveChangesAsync();
                await _notificationService.NotifyClientOfResponseAsync(response.ContractId, response.Response);

                return MapToContractDTO(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing caregiver response for contract {ContractId}", response.ContractId);
                throw;
            }
        }

        public async Task<ContractDTO> AcceptContractAsync(string contractId, string caregiverId)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null)
                    throw new InvalidOperationException("Contract not found");

                if (contract.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("Caregiver not authorized for this contract");

                if (contract.Status != ContractStatus.Sent)
                    throw new InvalidOperationException($"Contract cannot be accepted. Current status: {contract.Status}");

                // Update contract status
                contract.Status = ContractStatus.Accepted;
                contract.RespondedAt = DateTime.UtcNow;
                contract.AcceptedAt = DateTime.UtcNow;
                contract.AcceptedBy = caregiverId;
                contract.CaregiverResponse = "accept";
                contract.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Notify client of acceptance
                await _notificationService.NotifyClientOfResponseAsync(contractId, "accept");

                _logger.LogInformation("Contract {ContractId} accepted by caregiver {CaregiverId}", contractId, caregiverId);
                return MapToContractDTO(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting contract {ContractId} by caregiver {CaregiverId}", contractId, caregiverId);
                throw;
            }
        }

        public async Task<ContractDTO> RejectContractAsync(string contractId, string caregiverId, string? reason)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null)
                    throw new InvalidOperationException("Contract not found");

                if (contract.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("Caregiver not authorized for this contract");

                if (contract.Status != ContractStatus.Sent)
                    throw new InvalidOperationException($"Contract cannot be rejected. Current status: {contract.Status}");

                // Update contract status
                contract.Status = ContractStatus.Rejected;
                contract.RespondedAt = DateTime.UtcNow;
                contract.RejectedAt = DateTime.UtcNow;
                contract.RejectedBy = caregiverId;
                contract.RejectionReason = reason ?? "No reason provided";
                contract.CaregiverResponse = "reject";
                contract.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Notify client of rejection
                await _notificationService.NotifyClientOfResponseAsync(contractId, "reject");

                _logger.LogInformation("Contract {ContractId} rejected by caregiver {CaregiverId} with reason: {Reason}", 
                    contractId, caregiverId, reason ?? "No reason provided");
                return MapToContractDTO(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting contract {ContractId} by caregiver {CaregiverId}", contractId, caregiverId);
                throw;
            }
        }

        public async Task<ContractDTO> RequestContractReviewAsync(string contractId, string caregiverId, string? comments)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null)
                    throw new InvalidOperationException("Contract not found");

                if (contract.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("Caregiver not authorized for this contract");

                if (contract.Status != ContractStatus.Sent)
                    throw new InvalidOperationException($"Contract review cannot be requested. Current status: {contract.Status}");

                // Update contract status
                contract.Status = ContractStatus.ReviewRequested;
                contract.RespondedAt = DateTime.UtcNow;
                contract.ReviewRequestedAt = DateTime.UtcNow;
                contract.ReviewRequestedBy = caregiverId;
                contract.ReviewComments = comments ?? "No comments provided";
                contract.CaregiverResponse = "review";
                contract.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Notify client of review request
                await _notificationService.NotifyClientOfResponseAsync(contractId, "review");

                _logger.LogInformation("Contract review requested for {ContractId} by caregiver {CaregiverId}", contractId, caregiverId);
                return MapToContractDTO(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting contract review for {ContractId} by caregiver {CaregiverId}", contractId, caregiverId);
                throw;
            }
        }

        public async Task<List<AlternativeCaregiverDTO>> GetAlternativeCaregiversAsync(string contractId)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) throw new InvalidOperationException("Contract not found");

                List<AlternativeCaregiverDTO> alternatives = new List<AlternativeCaregiverDTO>();

                // Get client location first, then find nearby caregivers
                var clientLocation = await _locationService.GetUserLocationAsync(contract.ClientId, "client");
                if (clientLocation != null)
                {
                    var proximityRequest = new ProximitySearchRequest
                    {
                        ClientId = contract.ClientId,
                        ServiceLatitude = clientLocation.Latitude,
                        ServiceLongitude = clientLocation.Longitude,
                        MaxDistanceKm = 50
                    };

                    var nearbyResult = await _locationService.FindNearbyCaregivers(proximityRequest);

                    alternatives = nearbyResult
                        .Where(c => c.CaregiverId != contract.CaregiverId)
                        .Take(5)
                        .Select(c => new AlternativeCaregiverDTO
                        {
                            CaregiverId = c.CaregiverId,
                            Name = c.CaregiverName ?? "Caregiver",
                            ProximityScore = c.ProximityScore,
                            Distance = (decimal)c.DistanceKm,
                            Location = c.Location?.Address ?? "Location not specified",
                            Rating = 4.5m,
                            TotalReviews = 10,
                            Specializations = new List<string> { "General Care" },
                            ProfilePicture = c.ProfileImage ?? "",
                            Pricing = new PackagePricingDTO
                            {
                                OneVisitPerWeek = contract.SelectedPackage.PricePerVisit,
                                ThreeVisitsPerWeek = contract.SelectedPackage.PricePerVisit,
                                FiveVisitsPerWeek = contract.SelectedPackage.PricePerVisit
                            }
                        }).ToList();
                }

                return alternatives;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alternative caregivers for contract {ContractId}", contractId);
                throw;
            }
        }

        public async Task<ContractDTO> GetContractByIdAsync(string contractId)
        {
            var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
            return contract != null ? MapToContractDTO(contract) : throw new InvalidOperationException("Contract not found");
        }

        public async Task<ContractDTO?> GetContractByOrderIdAsync(string orderId)
        {
            try
            {
                // First get the order to find the transaction ID
                var order = await _context.ClientOrders.FirstOrDefaultAsync(o => o.Id.ToString() == orderId);
                if (order == null)
                    return null;

                // Find contract by transaction ID
                var contract = await _context.Contracts
                    .FirstOrDefaultAsync(c => c.PaymentTransactionId == order.TransactionId);

                return contract != null ? MapToContractDTO(contract) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contract for order {OrderId}", orderId);
                throw;
            }
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

        public async Task<List<ContractDTO>> GetPendingContractsForCaregiverAsync(string caregiverId)
        {
            var contracts = await _context.Contracts
                .Where(c => c.CaregiverId == caregiverId && (c.Status == ContractStatus.Sent || c.Status == ContractStatus.Pending))
                .OrderBy(c => c.SentAt)
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

        public async Task<ContractDTO> ReviseContractAsync(string contractId, ContractRevisionRequestDTO revision)
        {
            try
            {
                var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
                if (contract == null) throw new ArgumentException("Contract not found");

                if (revision.UpdatedTasks?.Any() == true)
                {
                    contract.Tasks = revision.UpdatedTasks.Select(t => new ClientTask
                    {
                        Title = t.Title ?? string.Empty,
                        Description = t.Description ?? string.Empty,
                        Category = Enum.TryParse<TaskCategory>(t.Category, out var taskCat) ? taskCat : TaskCategory.Other,
                        Priority = Enum.TryParse<TaskPriority>(t.Priority, out var taskPri) ? taskPri : TaskPriority.Medium
                    }).ToList();
                }

                if (!string.IsNullOrEmpty(revision.RevisionNotes))
                {
                    var revisedTerms = await _llmService.ReviseContractAsync(contract.GeneratedTerms, revision.RevisionNotes);
                    contract.GeneratedTerms = revisedTerms;
                }

                contract.Status = ContractStatus.Revised;
                await _context.SaveChangesAsync();

                return MapToContractDTO(contract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising contract {ContractId}", contractId);
                throw;
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
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error terminating contract {ContractId}", contractId);
                return false;
            }
        }

        public async Task<ContractDTO> SendContractToAlternativeCaregiverAsync(string originalContractId, string newCaregiverId)
        {
            try
            {
                var originalContract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == originalContractId);
                if (originalContract == null) throw new ArgumentException("Original contract not found");

                var newContract = new Contract
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    GigId = originalContract.GigId,
                    ClientId = originalContract.ClientId,
                    CaregiverId = newCaregiverId,
                    PaymentTransactionId = originalContract.PaymentTransactionId,
                    SelectedPackage = originalContract.SelectedPackage,
                    Tasks = originalContract.Tasks,
                    GeneratedTerms = originalContract.GeneratedTerms,
                    TotalAmount = originalContract.TotalAmount,
                    Status = ContractStatus.Generated,
                    ContractStartDate = originalContract.ContractStartDate,
                    ContractEndDate = originalContract.ContractEndDate,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Contracts.Add(newContract);
                await _context.SaveChangesAsync();
                await SendContractToCaregiverAsync(newContract.Id);

                return MapToContractDTO(newContract);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contract to alternative caregiver");
                throw;
            }
        }

        private ContractDTO MapToContractDTO(Contract contract)
        {
            return new ContractDTO
            {
                Id = contract.Id,
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
                GeneratedTerms = contract.GeneratedTerms,
                TotalAmount = contract.TotalAmount,
                Status = contract.Status.ToString(),
                PaymentTransactionId = contract.PaymentTransactionId,
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
    }
}