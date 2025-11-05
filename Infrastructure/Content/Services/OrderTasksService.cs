using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Domain;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class OrderTasksService : IOrderTasksService
    {
        private readonly CareProDbContext _context;
        private readonly IGigServices _gigService;
        private readonly ICareGiverService _careGiverService;
        private readonly IClientService _clientService;
        private readonly ILogger<OrderTasksService> _logger;

        public OrderTasksService(
            CareProDbContext context,
            IGigServices gigService,
            ICareGiverService careGiverService,
            IClientService clientService,
            ILogger<OrderTasksService> logger)
        {
            _context = context;
            _gigService = gigService;
            _careGiverService = careGiverService;
            _clientService = clientService;
            _logger = logger;
        }

        public async Task<OrderTasksResponseDTO> CreateOrderTasksAsync(CreateOrderTasksRequestDTO request)
        {
            try
            {
                // Validate input
                await ValidateOrderTasksRequestAsync(request);

                // Calculate pricing
                var pricing = await EstimatePricingAsync(request);

                // Create OrderTasks entity
                var orderTasks = new OrderTasks
                {
                    ClientId = request.ClientId!,
                    GigId = request.GigId!,
                    CaregiverId = request.CaregiverId!,
                    PackageSelection = MapToPackageSelection(request.PackageSelection!),
                    CareTasks = request.CareTasks.Select(MapToCareTask).ToList(),
                    SpecialInstructions = request.SpecialInstructions,
                    PreferredTimes = request.PreferredTimes,
                    EmergencyContacts = request.EmergencyContacts,
                    TotalAmount = pricing.TotalAmount,
                    EstimatedCostPerVisit = pricing.EstimatedCostPerVisit,
                    EstimatedWeeklyCost = pricing.EstimatedWeeklyCost,
                    Status = OrderTasksStatus.Draft,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.OrderTasks.AddAsync(orderTasks);
                await _context.SaveChangesAsync();

                _logger.LogInformation("OrderTasks {OrderTasksId} created for Client {ClientId}",
                    orderTasks.Id, request.ClientId);

                return await MapToResponseDTOAsync(orderTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating OrderTasks for Client {ClientId}", request.ClientId);
                throw;
            }
        }

        public async Task<OrderTasksResponseDTO> UpdateOrderTasksAsync(UpdateOrderTasksRequestDTO request)
        {
            try
            {
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.Id.ToString() == request.OrderTasksId);

                if (orderTasks == null)
                    throw new InvalidOperationException("OrderTasks not found");

                if (orderTasks.Status != OrderTasksStatus.Draft)
                    throw new InvalidOperationException("OrderTasks cannot be modified after draft status");

                // Update fields
                if (request.PackageSelection != null)
                    orderTasks.PackageSelection = MapToPackageSelection(request.PackageSelection);

                if (request.CareTasks.Any())
                    orderTasks.CareTasks = request.CareTasks.Select(MapToCareTask).ToList();

                orderTasks.SpecialInstructions = request.SpecialInstructions;
                orderTasks.PreferredTimes = request.PreferredTimes;
                orderTasks.EmergencyContacts = request.EmergencyContacts;

                // Recalculate pricing
                var pricing = await CalculatePricingForOrderTasksAsync(orderTasks);
                orderTasks.TotalAmount = pricing.TotalAmount;
                orderTasks.EstimatedCostPerVisit = pricing.EstimatedCostPerVisit;
                orderTasks.EstimatedWeeklyCost = pricing.EstimatedWeeklyCost;

                await _context.SaveChangesAsync();

                _logger.LogInformation("OrderTasks {OrderTasksId} updated", request.OrderTasksId);

                return await MapToResponseDTOAsync(orderTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating OrderTasks {OrderTasksId}", request.OrderTasksId);
                throw;
            }
        }

        public async Task<OrderTasksResponseDTO> GetOrderTasksByIdAsync(string orderTasksId)
        {
            var orderTasks = await _context.OrderTasks
                .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

            if (orderTasks == null)
                throw new InvalidOperationException("OrderTasks not found");

            return await MapToResponseDTOAsync(orderTasks);
        }

        public async Task<List<OrderTasksResponseDTO>> GetOrderTasksByClientIdAsync(string clientId)
        {
            var orderTasksList = await _context.OrderTasks
                .Where(ot => ot.ClientId == clientId)
                .OrderByDescending(ot => ot.CreatedAt)
                .ToListAsync();

            var results = new List<OrderTasksResponseDTO>();
            foreach (var orderTasks in orderTasksList)
            {
                results.Add(await MapToResponseDTOAsync(orderTasks));
            }

            return results;
        }

        public async Task<bool> DeleteOrderTasksAsync(string orderTasksId)
        {
            try
            {
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

                if (orderTasks == null)
                    return false;

                if (orderTasks.Status != OrderTasksStatus.Draft)
                    throw new InvalidOperationException("OrderTasks cannot be deleted after draft status");

                _context.OrderTasks.Remove(orderTasks);
                await _context.SaveChangesAsync();

                _logger.LogInformation("OrderTasks {OrderTasksId} deleted", orderTasksId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting OrderTasks {OrderTasksId}", orderTasksId);
                throw;
            }
        }

        public async Task<OrderTasksPricingDTO> CalculatePricingAsync(string orderTasksId)
        {
            var orderTasks = await _context.OrderTasks
                .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

            if (orderTasks == null)
                throw new InvalidOperationException("OrderTasks not found");

            return await CalculatePricingForOrderTasksAsync(orderTasks);
        }

        public async Task<OrderTasksPricingDTO> EstimatePricingAsync(CreateOrderTasksRequestDTO request)
        {
            var tempOrderTasks = new OrderTasks
            {
                PackageSelection = MapToPackageSelection(request.PackageSelection!),
                CareTasks = request.CareTasks.Select(MapToCareTask).ToList()
            };

            return await CalculatePricingForOrderTasksAsync(tempOrderTasks);
        }

        public async Task<bool> MarkAsPendingPaymentAsync(string orderTasksId)
        {
            return await UpdateStatusAsync(orderTasksId, OrderTasksStatus.PendingPayment);
        }

        public async Task<bool> MarkAsPaidAsync(string orderTasksId, string clientOrderId)
        {
            try
            {
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

                if (orderTasks == null)
                    return false;

                orderTasks.Status = OrderTasksStatus.Paid;
                orderTasks.ClientOrderId = clientOrderId;
                orderTasks.PaidAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking OrderTasks {OrderTasksId} as paid", orderTasksId);
                return false;
            }
        }

        public async Task<bool> MarkAsContractGeneratedAsync(string orderTasksId)
        {
            return await UpdateStatusAsync(orderTasksId, OrderTasksStatus.ContractGenerated);
        }

        public async Task<bool> MarkAsCompletedAsync(string orderTasksId)
        {
            try
            {
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

                if (orderTasks == null)
                    return false;

                orderTasks.Status = OrderTasksStatus.Completed;
                orderTasks.CompletedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking OrderTasks {OrderTasksId} as completed", orderTasksId);
                return false;
            }
        }

        public async Task<OrderTasksResponseDTO?> GetOrderTasksByClientOrderIdAsync(string clientOrderId)
        {
            var orderTasks = await _context.OrderTasks
                .FirstOrDefaultAsync(ot => ot.ClientOrderId == clientOrderId);

            return orderTasks != null ? await MapToResponseDTOAsync(orderTasks) : null;
        }

        public async Task<bool> LinkToClientOrderAsync(string orderTasksId, string clientOrderId)
        {
            try
            {
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

                if (orderTasks == null)
                    return false;

                orderTasks.ClientOrderId = clientOrderId;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error linking OrderTasks {OrderTasksId} to ClientOrder {ClientOrderId}",
                    orderTasksId, clientOrderId);
                return false;
            }
        }

        public async Task<ContractGenerationRequestDTO> PrepareContractDataAsync(string orderTasksId, string paymentTransactionId)
        {
            var orderTasks = await _context.OrderTasks
                .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

            if (orderTasks == null)
                throw new InvalidOperationException("OrderTasks not found");

            return new ContractGenerationRequestDTO
            {
                GigId = orderTasks.GigId,
                ClientId = orderTasks.ClientId,
                CaregiverId = orderTasks.CaregiverId,
                PaymentTransactionId = paymentTransactionId,
                SelectedPackage = MapToPackageSelectionDTO(orderTasks.PackageSelection),
                Tasks = orderTasks.CareTasks.Select(MapToClientTaskDTO).ToList()
            };
        }

        // Private helper methods
        private async Task ValidateOrderTasksRequestAsync(CreateOrderTasksRequestDTO request)
        {
            if (string.IsNullOrEmpty(request.ClientId))
                throw new ArgumentException("ClientId is required");

            if (string.IsNullOrEmpty(request.GigId))
                throw new ArgumentException("GigId is required");

            if (string.IsNullOrEmpty(request.CaregiverId))
                throw new ArgumentException("CaregiverId is required");

            if (request.PackageSelection == null)
                throw new ArgumentException("PackageSelection is required");

            if (!request.CareTasks.Any())
                throw new ArgumentException("At least one care task is required");

            // Validate that entities exist
            var client = await _clientService.GetClientUserAsync(request.ClientId);
            if (client == null)
                throw new InvalidOperationException("Client not found");

            var gig = await _gigService.GetGigAsync(request.GigId);
            if (gig == null)
                throw new InvalidOperationException("Gig not found");

            var caregiver = await _careGiverService.GetCaregiverUserAsync(request.CaregiverId);
            if (caregiver == null)
                throw new InvalidOperationException("Caregiver not found");
        }

        private async Task<OrderTasksPricingDTO> CalculatePricingForOrderTasksAsync(OrderTasks orderTasks)
        {
            // Basic pricing calculation logic
            var basePrice = orderTasks.PackageSelection.PricePerVisit;
            var visitsPerWeek = orderTasks.PackageSelection.VisitsPerWeek;
            var durationWeeks = orderTasks.PackageSelection.DurationWeeks;

            // Calculate complexity multiplier based on tasks
            var complexityMultiplier = CalculateTaskComplexityMultiplier(orderTasks.CareTasks);

            // Calculate frequency discount
            var frequencyDiscount = CalculateFrequencyDiscount(visitsPerWeek);

            var costPerVisit = basePrice * complexityMultiplier * (1 - frequencyDiscount);
            var weeklyCost = costPerVisit * visitsPerWeek;
            var totalAmount = weeklyCost * durationWeeks;

            return new OrderTasksPricingDTO
            {
                BasePrice = basePrice,
                TaskComplexityMultiplier = complexityMultiplier,
                FrequencyDiscount = frequencyDiscount,
                EstimatedCostPerVisit = costPerVisit,
                EstimatedWeeklyCost = weeklyCost,
                TotalAmount = totalAmount,
                PricingBreakdown = $"Base: ${basePrice:F2} x Complexity: {complexityMultiplier:F2} x Visits: {visitsPerWeek} x Weeks: {durationWeeks}"
            };
        }

        private decimal CalculateTaskComplexityMultiplier(List<CareTask> tasks)
        {
            if (!tasks.Any()) return 1.0m;

            var complexityScore = 0;
            foreach (var task in tasks)
            {
                complexityScore += task.Priority switch
                {
                    TaskPriority.Critical => 4,
                    TaskPriority.High => 3,
                    TaskPriority.Medium => 2,
                    TaskPriority.Low => 1,
                    _ => 1
                };

                complexityScore += task.Category switch
                {
                    TaskCategory.MedicalCare => 3,
                    TaskCategory.PersonalCare => 2,
                    TaskCategory.Mobility => 2,
                    TaskCategory.Medication => 3,
                    _ => 1
                };
            }

            var averageComplexity = (decimal)complexityScore / (tasks.Count * 4); // Normalize to 0-2 range
            return Math.Max(1.0m, Math.Min(2.0m, 1.0m + averageComplexity));
        }

        private decimal CalculateFrequencyDiscount(int visitsPerWeek)
        {
            return visitsPerWeek switch
            {
                >= 5 => 0.15m, // 15% discount for 5+ visits
                >= 3 => 0.10m, // 10% discount for 3-4 visits
                _ => 0.0m      // No discount for 1-2 visits
            };
        }

        private async Task<bool> UpdateStatusAsync(string orderTasksId, OrderTasksStatus status)
        {
            try
            {
                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.Id.ToString() == orderTasksId);

                if (orderTasks == null)
                    return false;

                orderTasks.Status = status;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating OrderTasks {OrderTasksId} status to {Status}",
                    orderTasksId, status);
                return false;
            }
        }

        private async Task<OrderTasksResponseDTO> MapToResponseDTOAsync(OrderTasks orderTasks)
        {
            // Get additional context data
            GigSummaryDTO? gigDetails = null;
            CaregiverSummaryDTO? caregiverDetails = null;

            try
            {
                var gig = await _gigService.GetGigAsync(orderTasks.GigId);
                if (gig != null)
                {
                    gigDetails = new GigSummaryDTO
                    {
                        Id = gig.Id,
                        Title = gig.Title,
                        Description = gig.PackageDetails?.FirstOrDefault() ?? "",
                        Location = "Location info" // Add location mapping if available
                    };
                }

                var caregiver = await _careGiverService.GetCaregiverUserAsync(orderTasks.CaregiverId);
                if (caregiver != null)
                {
                    caregiverDetails = new CaregiverSummaryDTO
                    {
                        Id = caregiver.Id,
                        Name = $"{caregiver.FirstName} {caregiver.LastName}",
                        ProfilePicture = caregiver.ProfileImage,
                        Rating = 0, // CaregiverResponse doesn't have rating info, set to 0
                        TotalReviews = 0, // CaregiverResponse doesn't have review count, set to 0
                        Location = caregiver.Location ?? caregiver.HomeAddress
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading context data for OrderTasks {OrderTasksId}", orderTasks.Id);
            }

            return new OrderTasksResponseDTO
            {
                Id = orderTasks.Id.ToString(),
                ClientId = orderTasks.ClientId,
                GigId = orderTasks.GigId,
                CaregiverId = orderTasks.CaregiverId,
                PackageSelection = MapToPackageSelectionDTO(orderTasks.PackageSelection),
                CareTasks = orderTasks.CareTasks.Select(MapToCareTaskDTO).ToList(),
                SpecialInstructions = orderTasks.SpecialInstructions,
                PreferredTimes = orderTasks.PreferredTimes,
                EmergencyContacts = orderTasks.EmergencyContacts,
                TotalAmount = orderTasks.TotalAmount,
                EstimatedCostPerVisit = orderTasks.EstimatedCostPerVisit,
                EstimatedWeeklyCost = orderTasks.EstimatedWeeklyCost,
                Status = orderTasks.Status.ToString(),
                CreatedAt = orderTasks.CreatedAt,
                CompletedAt = orderTasks.CompletedAt,
                ClientOrderId = orderTasks.ClientOrderId,
                GigDetails = gigDetails,
                CaregiverDetails = caregiverDetails
            };
        }

        private PackageSelection MapToPackageSelection(PackageSelectionDTO dto)
        {
            return new PackageSelection
            {
                PackageType = dto.PackageType ?? string.Empty,
                VisitsPerWeek = dto.VisitsPerWeek,
                PricePerVisit = dto.PricePerVisit,
                TotalWeeklyPrice = dto.TotalWeeklyPrice,
                DurationWeeks = dto.DurationWeeks
            };
        }

        private PackageSelectionDTO MapToPackageSelectionDTO(PackageSelection entity)
        {
            return new PackageSelectionDTO
            {
                PackageType = entity.PackageType,
                VisitsPerWeek = entity.VisitsPerWeek,
                PricePerVisit = entity.PricePerVisit,
                TotalWeeklyPrice = entity.TotalWeeklyPrice,
                DurationWeeks = entity.DurationWeeks
            };
        }

        private CareTask MapToCareTask(CareTaskDTO dto)
        {
            return new CareTask
            {
                Id = dto.Id ?? ObjectId.GenerateNewId().ToString(),
                Title = dto.Title,
                Description = dto.Description,
                Category = Enum.TryParse<TaskCategory>(dto.Category, true, out var category) ? category : TaskCategory.Other,
                Priority = Enum.TryParse<TaskPriority>(dto.Priority, true, out var priority) ? priority : TaskPriority.Medium,
                SpecialRequirements = dto.SpecialRequirements,
                EstimatedDuration = dto.EstimatedDurationMinutes.HasValue ? TimeSpan.FromMinutes(dto.EstimatedDurationMinutes.Value) : null,
                IsRecurring = dto.IsRecurring,
                Frequency = dto.Frequency
            };
        }

        private CareTaskDTO MapToCareTaskDTO(CareTask entity)
        {
            return new CareTaskDTO
            {
                Id = entity.Id,
                Title = entity.Title,
                Description = entity.Description,
                Category = entity.Category.ToString(),
                Priority = entity.Priority.ToString(),
                SpecialRequirements = entity.SpecialRequirements,
                EstimatedDurationMinutes = entity.EstimatedDuration?.TotalMinutes > 0 ? (int)entity.EstimatedDuration.Value.TotalMinutes : null,
                IsRecurring = entity.IsRecurring,
                Frequency = entity.Frequency
            };
        }

        private ClientTaskDTO MapToClientTaskDTO(CareTask entity)
        {
            return new ClientTaskDTO
            {
                Title = entity.Title,
                Description = entity.Description,
                Category = entity.Category.ToString(),
                Priority = entity.Priority.ToString(),
                SpecialRequirements = entity.SpecialRequirements,
                EstimatedDurationMinutes = entity.EstimatedDuration?.TotalMinutes > 0 ? (int)entity.EstimatedDuration.Value.TotalMinutes : null
            };
        }
    }
}