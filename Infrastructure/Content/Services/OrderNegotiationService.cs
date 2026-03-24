using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class OrderNegotiationService : IOrderNegotiationService
    {
        private readonly CareProDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IContractLLMService _llmService;
        private readonly IGeocodingService _geocodingService;
        private readonly ILogger<OrderNegotiationService> _logger;

        public OrderNegotiationService(
            CareProDbContext context,
            INotificationService notificationService,
            IContractLLMService llmService,
            IGeocodingService geocodingService,
            ILogger<OrderNegotiationService> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _llmService = llmService;
            _geocodingService = geocodingService;
            _logger = logger;
        }

        public async Task<OrderNegotiationDTO> CreateNegotiationAsync(string userId, CreateNegotiationDTO request)
        {
            try
            {
                // Block if an active (non-terminal) negotiation already exists for this order
                var existingNegotiation = await _context.OrderNegotiations
                    .FirstOrDefaultAsync(n => n.OrderId == request.OrderId
                        && n.Status != NegotiationStatus.Abandoned
                        && n.Status != NegotiationStatus.ConvertedToContract);

                if (existingNegotiation != null)
                    throw new InvalidOperationException($"An active negotiation already exists for order {request.OrderId}.");

                // Block if a contract already exists via old flow
                var existingContract = await _context.Contracts
                    .FirstOrDefaultAsync(c => c.OrderId == request.OrderId);

                if (existingContract != null)
                    throw new InvalidOperationException($"A contract already exists for order {request.OrderId}.");

                // Verify the order and authorise the caller
                var order = await _context.ClientOrders
                    .FirstOrDefaultAsync(o => o.Id.ToString() == request.OrderId);

                if (order == null)
                    throw new InvalidOperationException($"Order {request.OrderId} not found.");

                string clientId;
                if (request.CreatedByRole == "Client")
                {
                    if (order.ClientId != userId)
                        throw new UnauthorizedAccessException("You are not the client for this order.");
                    clientId = userId;
                }
                else if (request.CreatedByRole == "Caregiver")
                {
                    if (order.CaregiverId != userId)
                        throw new UnauthorizedAccessException("You are not the caregiver for this order.");
                    clientId = order.ClientId;
                }
                else
                {
                    throw new ArgumentException("CreatedByRole must be 'Client' or 'Caregiver'.");
                }

                var negotiation = new OrderNegotiation
                {
                    OrderId = request.OrderId,
                    ClientId = clientId,
                    CaregiverId = request.CaregiverId,
                    GigId = request.GigId,
                    CreatedByRole = request.CreatedByRole,
                    ClientProposedTasks = request.ClientProposedTasks ?? new List<string>(),
                    CaregiverProposedTasks = request.CaregiverProposedTasks ?? new List<string>(),
                    ClientProposedSchedule = MapSlotDTOs(request.ClientProposedSchedule),
                    CaregiverProposedSchedule = MapSlotDTOs(request.CaregiverProposedSchedule),
                    ServiceAddress = request.ServiceAddress,
                    AccessInstructions = request.AccessInstructions,
                    SpecialClientRequirements = request.SpecialClientRequirements,
                    AdditionalNotes = request.AdditionalNotes,
                    LastClientNote = request.CreatedByRole == "Client" ? request.OpeningNote : null,
                    LastCaregiverNote = request.CreatedByRole == "Caregiver" ? request.OpeningNote : null,
                    Status = NegotiationStatus.Drafting,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                if (!string.IsNullOrEmpty(request.ServiceAddress))
                {
                    if (request.ConfirmAtServiceAddress && request.ServiceLatitude.HasValue && request.ServiceLongitude.HasValue)
                    {
                        negotiation.ServiceLatitude = request.ServiceLatitude.Value;
                        negotiation.ServiceLongitude = request.ServiceLongitude.Value;
                    }
                    else
                    {
                        await TryGeocodeAsync(negotiation, request.ServiceAddress);
                    }
                }

                await _context.OrderNegotiations.AddAsync(negotiation);
                await _context.SaveChangesAsync();

                // Notify the other party
                var otherPartyId = request.CreatedByRole == "Client"
                    ? request.CaregiverId
                    : clientId;

                await _notificationService.CreateNotificationAsync(
                    otherPartyId,
                    userId,
                    NotificationTypes.NegotiationStarted,
                    "A new negotiation has been started for your order.",
                    "Negotiation Started",
                    negotiation.Id,
                    request.OrderId);

                _logger.LogInformation("Negotiation {NegotiationId} created for order {OrderId} by {Role} {UserId}",
                    negotiation.Id, request.OrderId, request.CreatedByRole, userId);

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating negotiation for order {OrderId}", request.OrderId);
                throw;
            }
        }

        public async Task<OrderNegotiationDTO?> GetNegotiationByOrderIdAsync(string orderId)
        {
            var negotiation = await _context.OrderNegotiations
                .FirstOrDefaultAsync(n => n.OrderId == orderId
                    && n.Status != NegotiationStatus.Abandoned
                    && n.Status != NegotiationStatus.ConvertedToContract);

            return negotiation == null ? null : MapToDTO(negotiation);
        }

        public async Task<OrderNegotiationDTO?> GetNegotiationByIdAsync(string negotiationId)
        {
            var negotiation = await _context.OrderNegotiations
                .FirstOrDefaultAsync(n => n.Id == negotiationId);

            return negotiation == null ? null : MapToDTO(negotiation);
        }

        public async Task<bool> HasActiveNegotiationForOrderAsync(string orderId)
        {
            return await _context.OrderNegotiations
                .AnyAsync(n => n.OrderId == orderId
                    && n.Status != NegotiationStatus.Abandoned
                    && n.Status != NegotiationStatus.ConvertedToContract);
        }

        public async Task<OrderNegotiationDTO> ClientUpdateAsync(string negotiationId, string clientId, ClientNegotiationUpdateDTO update)
        {
            try
            {
                var negotiation = await GetActiveNegotiationOrThrow(negotiationId);

                if (negotiation.ClientId != clientId)
                    throw new UnauthorizedAccessException("You are not the client for this negotiation.");

                if (update.ClientProposedTasks != null)
                    negotiation.ClientProposedTasks = update.ClientProposedTasks;

                if (update.ClientProposedSchedule != null)
                    negotiation.ClientProposedSchedule = MapSlotDTOs(update.ClientProposedSchedule);

                if (update.SpecialClientRequirements != null)
                    negotiation.SpecialClientRequirements = update.SpecialClientRequirements;

                if (update.ServiceAddress != null)
                {
                    negotiation.ServiceAddress = update.ServiceAddress;

                    if (update.ConfirmAtServiceAddress && update.ServiceLatitude.HasValue && update.ServiceLongitude.HasValue)
                    {
                        negotiation.ServiceLatitude = update.ServiceLatitude.Value;
                        negotiation.ServiceLongitude = update.ServiceLongitude.Value;
                    }
                    else
                    {
                        negotiation.ServiceLatitude = null;
                        negotiation.ServiceLongitude = null;
                        await TryGeocodeAsync(negotiation, update.ServiceAddress);
                    }
                }

                if (update.AccessInstructions != null)
                    negotiation.AccessInstructions = update.AccessInstructions;

                if (update.Note != null)
                    negotiation.LastClientNote = update.Note;

                // Any client change invalidates caregiver's prior agreement
                negotiation.CaregiverAgreed = false;
                negotiation.CaregiverAgreedAt = null;

                if (update.SubmitForCaregiverReview)
                {
                    negotiation.Status = NegotiationStatus.PendingCaregiverReview;
                    negotiation.NegotiationRound++;
                }

                negotiation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                if (update.SubmitForCaregiverReview)
                {
                    await _notificationService.CreateNotificationAsync(
                        negotiation.CaregiverId,
                        clientId,
                        NotificationTypes.NegotiationClientSubmitted,
                        "The client has submitted updates for your review.",
                        "Negotiation Update",
                        negotiation.Id,
                        negotiation.OrderId);
                }

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in client update for negotiation {NegotiationId}", negotiationId);
                throw;
            }
        }

        public async Task<OrderNegotiationDTO> CaregiverUpdateAsync(string negotiationId, string caregiverId, CaregiverNegotiationUpdateDTO update)
        {
            try
            {
                var negotiation = await GetActiveNegotiationOrThrow(negotiationId);

                if (negotiation.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("You are not the caregiver for this negotiation.");

                if (update.CaregiverProposedTasks != null)
                    negotiation.CaregiverProposedTasks = update.CaregiverProposedTasks;

                if (update.CaregiverProposedSchedule != null)
                    negotiation.CaregiverProposedSchedule = MapSlotDTOs(update.CaregiverProposedSchedule);

                if (update.AdditionalNotes != null)
                    negotiation.AdditionalNotes = update.AdditionalNotes;

                if (update.Note != null)
                    negotiation.LastCaregiverNote = update.Note;

                // Any caregiver change invalidates client's prior agreement
                negotiation.ClientAgreed = false;
                negotiation.ClientAgreedAt = null;

                if (update.SubmitForClientReview)
                {
                    negotiation.Status = NegotiationStatus.PendingClientReview;
                    negotiation.NegotiationRound++;
                }

                negotiation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                if (update.SubmitForClientReview)
                {
                    await _notificationService.CreateNotificationAsync(
                        negotiation.ClientId,
                        caregiverId,
                        NotificationTypes.NegotiationCaregiverSubmitted,
                        "The caregiver has submitted updates for your review.",
                        "Negotiation Update",
                        negotiation.Id,
                        negotiation.OrderId);
                }

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in caregiver update for negotiation {NegotiationId}", negotiationId);
                throw;
            }
        }

        public async Task<OrderNegotiationDTO> ClientAgreeAsync(string negotiationId, string clientId)
        {
            try
            {
                var negotiation = await GetActiveNegotiationOrThrow(negotiationId);

                if (negotiation.ClientId != clientId)
                    throw new UnauthorizedAccessException("You are not the client for this negotiation.");

                negotiation.ClientAgreed = true;
                negotiation.ClientAgreedAt = DateTime.UtcNow;
                negotiation.UpdatedAt = DateTime.UtcNow;

                await _notificationService.CreateNotificationAsync(
                    negotiation.CaregiverId,
                    clientId,
                    NotificationTypes.NegotiationClientAgreed,
                    "The client has agreed to the current negotiation terms.",
                    "Client Agreed",
                    negotiation.Id,
                    negotiation.OrderId);

                if (negotiation.CaregiverAgreed)
                    await FinaliseAgreementAsync(negotiation);
                else
                    await _context.SaveChangesAsync();

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing client agree for negotiation {NegotiationId}", negotiationId);
                throw;
            }
        }

        public async Task<OrderNegotiationDTO> CaregiverAgreeAsync(string negotiationId, string caregiverId)
        {
            try
            {
                var negotiation = await GetActiveNegotiationOrThrow(negotiationId);

                if (negotiation.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("You are not the caregiver for this negotiation.");

                negotiation.CaregiverAgreed = true;
                negotiation.CaregiverAgreedAt = DateTime.UtcNow;
                negotiation.UpdatedAt = DateTime.UtcNow;

                await _notificationService.CreateNotificationAsync(
                    negotiation.ClientId,
                    caregiverId,
                    NotificationTypes.NegotiationCaregiverAgreed,
                    "The caregiver has agreed to the current negotiation terms.",
                    "Caregiver Agreed",
                    negotiation.Id,
                    negotiation.OrderId);

                if (negotiation.ClientAgreed)
                    await FinaliseAgreementAsync(negotiation);
                else
                    await _context.SaveChangesAsync();

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing caregiver agree for negotiation {NegotiationId}", negotiationId);
                throw;
            }
        }

        public async Task<OrderNegotiationDTO> AbandonAsync(string negotiationId, string userId, NegotiationAbandonDTO request)
        {
            try
            {
                var negotiation = await _context.OrderNegotiations
                    .FirstOrDefaultAsync(n => n.Id == negotiationId);

                if (negotiation == null)
                    throw new InvalidOperationException($"Negotiation {negotiationId} not found.");

                bool isClient = negotiation.ClientId == userId;
                bool isCaregiver = negotiation.CaregiverId == userId;

                if (!isClient && !isCaregiver)
                    throw new UnauthorizedAccessException("You are not a participant in this negotiation.");

                if (negotiation.Status == NegotiationStatus.Abandoned)
                    throw new InvalidOperationException("This negotiation is already abandoned.");

                if (negotiation.Status == NegotiationStatus.ConvertedToContract)
                    throw new InvalidOperationException("Cannot abandon a negotiation that has been converted to a contract.");

                var role = isClient ? "Client" : "Caregiver";
                negotiation.Status = NegotiationStatus.Abandoned;
                negotiation.AbandonedByRole = role;
                negotiation.AbandonReason = request.Reason;
                negotiation.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var otherPartyId = isClient ? negotiation.CaregiverId : negotiation.ClientId;
                var reasonSuffix = string.IsNullOrEmpty(request.Reason) ? "" : $" Reason: {request.Reason}";
                await _notificationService.CreateNotificationAsync(
                    otherPartyId,
                    userId,
                    NotificationTypes.NegotiationAbandoned,
                    $"The {role.ToLower()} has abandoned the negotiation.{reasonSuffix}",
                    "Negotiation Abandoned",
                    negotiation.Id,
                    negotiation.OrderId);

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error abandoning negotiation {NegotiationId}", negotiationId);
                throw;
            }
        }

        public async Task<OrderNegotiationDTO> ConvertToContractAsync(string negotiationId, string userId)
        {
            try
            {
                var negotiation = await _context.OrderNegotiations
                    .FirstOrDefaultAsync(n => n.Id == negotiationId);

                if (negotiation == null)
                    throw new InvalidOperationException($"Negotiation {negotiationId} not found.");

                if (negotiation.Status != NegotiationStatus.BothAgreed)
                    throw new InvalidOperationException("Both parties must agree before converting to a contract.");

                if (negotiation.ClientId != userId && negotiation.CaregiverId != userId)
                    throw new UnauthorizedAccessException("You are not a participant in this negotiation.");

                // Service address is required — only the client can provide it
                if (string.IsNullOrWhiteSpace(negotiation.ServiceAddress))
                    throw new InvalidOperationException("Cannot convert to contract — service address is required. The client must provide the address where care will be rendered.");

                // Fetch relational data
                var order = await _context.ClientOrders
                    .FirstOrDefaultAsync(o => o.Id.ToString() == negotiation.OrderId);

                if (order == null)
                    throw new InvalidOperationException($"Order {negotiation.OrderId} not found.");

                var gig = await _context.Gigs
                    .FirstOrDefaultAsync(g => g.Id.ToString() == order.GigId);

                if (gig == null)
                    throw new InvalidOperationException($"Gig not found for order {negotiation.OrderId}.");

                var client = await _context.Clients
                    .FirstOrDefaultAsync(c => c.Id.ToString() == negotiation.ClientId);

                var caregiver = await _context.CareGivers
                    .FirstOrDefaultAsync(c => c.Id.ToString() == negotiation.CaregiverId);

                var orderTasks = await _context.OrderTasks
                    .FirstOrDefaultAsync(ot => ot.ClientOrderId == negotiation.OrderId);

                // Build package selection
                var expectedVisitsPerWeek = order.FrequencyPerWeek
                    ?? orderTasks?.PackageSelection?.VisitsPerWeek
                    ?? 1;

                var packageSelection = orderTasks?.PackageSelection ?? new PackageSelection
                {
                    PackageType = order.PaymentOption ?? "standard",
                    VisitsPerWeek = expectedVisitsPerWeek,
                    PricePerVisit = expectedVisitsPerWeek > 0 ? order.Amount / expectedVisitsPerWeek : order.Amount,
                    TotalWeeklyPrice = order.Amount,
                    DurationWeeks = 4
                };

                // Build tasks: prefer agreed negotiation tasks, fall back to order tasks
                var tasks = negotiation.AgreedTasks.Count > 0
                    ? negotiation.AgreedTasks.Select(title => new ClientTask
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Title = title,
                        Description = string.Empty,
                        Category = TaskCategory.Other,
                        Priority = TaskPriority.Medium
                    }).ToList()
                    : (orderTasks?.CareTasks?.Select(t => new ClientTask
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Description = t.Description,
                        Category = t.Category,
                        Priority = t.Priority,
                        SpecialRequirements = t.SpecialRequirements
                    }).ToList() ?? new List<ClientTask>());

                // Validate agreed schedule is not empty
                if (negotiation.AgreedSchedule.Count == 0)
                    throw new InvalidOperationException("Cannot convert to contract — no agreed schedule exists. Both parties must agree on a schedule first.");

                // Build schedule from agreed schedule (days were validated during finalisation)
                var scheduleEntities = negotiation.AgreedSchedule.Select(s => new ScheduledVisit
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    DayOfWeek = Enum.Parse<DayOfWeek>(s.DayOfWeek, true),
                    StartTime = s.StartTime,
                    EndTime = s.EndTime
                }).ToList();

                // Contract dates
                var startDate = DateTime.UtcNow.AddDays(1);
                var endDate = startDate.AddDays(packageSelection.DurationWeeks * 7);
                var contractId = ObjectId.GenerateNewId().ToString();

                // Ensure coordinates are resolved
                double? lat = negotiation.ServiceLatitude;
                double? lng = negotiation.ServiceLongitude;
                if (!string.IsNullOrEmpty(negotiation.ServiceAddress) && (lat == null || lng == null))
                    await TryGeocodeAsync(negotiation, negotiation.ServiceAddress);

                // Build LLM data
                var enrichedData = new ContractGenerationDataDTO
                {
                    ContractId = contractId,
                    OrderId = negotiation.OrderId,
                    GeneratedAt = DateTime.UtcNow,
                    ClientId = negotiation.ClientId,
                    ClientFullName = client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Client",
                    ClientEmail = client?.Email,
                    ClientPhone = client?.PhoneNo,
                    CaregiverId = negotiation.CaregiverId,
                    CaregiverFullName = caregiver != null ? $"{caregiver.FirstName} {caregiver.LastName}".Trim() : "Caregiver",
                    CaregiverEmail = caregiver?.Email,
                    CaregiverPhone = caregiver?.PhoneNo,
                    CaregiverQualifications = caregiver?.AboutMe,
                    GigTitle = gig.Title,
                    GigDescription = string.Join(", ", gig.PackageDetails ?? new List<string>()),
                    GigCategory = gig.Category,
                    Package = packageSelection,
                    TotalAmountPaid = order.Amount,
                    TransactionReference = order.TransactionId,
                    Schedule = scheduleEntities,
                    ServiceAddress = negotiation.ServiceAddress ?? "To be confirmed",
                    City = client?.PreferredCity,
                    State = client?.PreferredState,
                    SpecialClientRequirements = negotiation.SpecialClientRequirements,
                    AccessInstructions = negotiation.AccessInstructions,
                    CaregiverNotes = negotiation.AdditionalNotes,
                    Tasks = tasks,
                    ContractStartDate = startDate,
                    ContractEndDate = endDate
                };

                var contractTerms = await _llmService.GenerateContractWithScheduleAsync(enrichedData);

                var contract = new Contract
                {
                    Id = contractId,
                    OrderId = negotiation.OrderId,
                    GigId = order.GigId,
                    ClientId = negotiation.ClientId,
                    CaregiverId = negotiation.CaregiverId,
                    PaymentTransactionId = order.TransactionId,
                    SelectedPackage = packageSelection,
                    Tasks = tasks,
                    Schedule = scheduleEntities,
                    ServiceAddress = negotiation.ServiceAddress,
                    ServiceLatitude = negotiation.ServiceLatitude,
                    ServiceLongitude = negotiation.ServiceLongitude,
                    SpecialClientRequirements = negotiation.SpecialClientRequirements,
                    AccessInstructions = negotiation.AccessInstructions,
                    CaregiverAdditionalNotes = negotiation.AdditionalNotes,
                    GeneratedTerms = contractTerms,
                    TotalAmount = order.Amount,
                    Status = ContractStatus.Approved,
                    InitiatedByRole = "Negotiation",
                    SubmittedByCaregiverId = negotiation.CaregiverId,
                    SubmittedAt = DateTime.UtcNow,
                    SentAt = DateTime.UtcNow,
                    NegotiationRound = negotiation.NegotiationRound,
                    ContractStartDate = startDate,
                    ContractEndDate = endDate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _context.Contracts.AddAsync(contract);

                negotiation.ContractId = contractId;
                negotiation.Status = NegotiationStatus.ConvertedToContract;
                negotiation.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Notify both parties
                await _notificationService.CreateNotificationAsync(
                    negotiation.ClientId,
                    negotiation.CaregiverId,
                    NotificationTypes.NegotiationConverted,
                    "Your negotiation has been converted to a contract. You can review it now.",
                    "Contract Generated",
                    contractId,
                    negotiation.OrderId);

                await _notificationService.CreateNotificationAsync(
                    negotiation.CaregiverId,
                    negotiation.ClientId,
                    NotificationTypes.NegotiationConverted,
                    "The negotiation has been converted to a contract. You can review it now.",
                    "Contract Generated",
                    contractId,
                    negotiation.OrderId);

                _logger.LogInformation("Negotiation {NegotiationId} converted to contract {ContractId} for order {OrderId}",
                    negotiationId, contractId, negotiation.OrderId);

                return MapToDTO(negotiation);
            }
            catch (InvalidOperationException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting negotiation {NegotiationId} to contract", negotiationId);
                throw;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private async Task<OrderNegotiation> GetActiveNegotiationOrThrow(string negotiationId)
        {
            var negotiation = await _context.OrderNegotiations
                .FirstOrDefaultAsync(n => n.Id == negotiationId);

            if (negotiation == null)
                throw new InvalidOperationException($"Negotiation {negotiationId} not found.");

            if (negotiation.Status == NegotiationStatus.Abandoned || negotiation.Status == NegotiationStatus.ConvertedToContract)
                throw new InvalidOperationException("This negotiation is no longer active.");

            return negotiation;
        }

        private async Task FinaliseAgreementAsync(OrderNegotiation negotiation)
        {
            // Agreed tasks = union of both parties' proposals, deduplicated (case-insensitive)
            negotiation.AgreedTasks = negotiation.ClientProposedTasks
                .Union(negotiation.CaregiverProposedTasks, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (negotiation.AgreedTasks.Count == 0)
                throw new InvalidOperationException("Cannot finalise agreement — at least one party must propose tasks before both can agree.");

            // Agreed schedule = caregiver's proposal (they set availability); fall back to client's
            negotiation.AgreedSchedule = negotiation.CaregiverProposedSchedule.Count > 0
                ? negotiation.CaregiverProposedSchedule
                : negotiation.ClientProposedSchedule;

            if (negotiation.AgreedSchedule.Count == 0)
                throw new InvalidOperationException("Cannot finalise agreement — at least one party must propose a schedule before both can agree.");

            // Validate all schedule day names are valid
            foreach (var slot in negotiation.AgreedSchedule)
            {
                if (!Enum.TryParse<DayOfWeek>(slot.DayOfWeek, true, out _))
                    throw new InvalidOperationException($"Invalid day of week in agreed schedule: '{slot.DayOfWeek}'. Must be Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, or Sunday.");
            }

            negotiation.Status = NegotiationStatus.BothAgreed;

            await _context.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                negotiation.ClientId,
                negotiation.CaregiverId,
                NotificationTypes.NegotiationBothAgreed,
                "Both parties have agreed. You can now generate the contract.",
                "Agreement Reached",
                negotiation.Id,
                negotiation.OrderId);

            await _notificationService.CreateNotificationAsync(
                negotiation.CaregiverId,
                negotiation.ClientId,
                NotificationTypes.NegotiationBothAgreed,
                "Both parties have agreed. The client can now generate the contract.",
                "Agreement Reached",
                negotiation.Id,
                negotiation.OrderId);
        }

        private async Task TryGeocodeAsync(OrderNegotiation negotiation, string address)
        {
            try
            {
                var geo = await _geocodingService.GeocodeAsync(address);
                if (geo != null)
                {
                    negotiation.ServiceLatitude = geo.Latitude;
                    negotiation.ServiceLongitude = geo.Longitude;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Geocoding failed for negotiation {NegotiationId}, address: {Address}",
                    negotiation.Id, address);
            }
        }

        private static List<NegotiationScheduleSlot> MapSlotDTOs(IEnumerable<NegotiationScheduleSlotDTO>? slots)
        {
            if (slots == null) return new List<NegotiationScheduleSlot>();

            var result = new List<NegotiationScheduleSlot>();
            foreach (var s in slots)
            {
                if (!Enum.TryParse<DayOfWeek>(s.DayOfWeek, true, out _))
                    throw new ArgumentException($"Invalid day of week: '{s.DayOfWeek}'. Must be Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, or Sunday.");

                result.Add(new NegotiationScheduleSlot
                {
                    DayOfWeek = s.DayOfWeek,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime
                });
            }
            return result;
        }

        private static OrderNegotiationDTO MapToDTO(OrderNegotiation n) => new OrderNegotiationDTO
        {
            Id = n.Id,
            OrderId = n.OrderId,
            ClientId = n.ClientId,
            CaregiverId = n.CaregiverId,
            GigId = n.GigId,
            Status = n.Status.ToString(),
            ClientProposedTasks = n.ClientProposedTasks,
            CaregiverProposedTasks = n.CaregiverProposedTasks,
            AgreedTasks = n.AgreedTasks,
            ClientProposedSchedule = n.ClientProposedSchedule.Select(s => new NegotiationScheduleSlotDTO
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList(),
            CaregiverProposedSchedule = n.CaregiverProposedSchedule.Select(s => new NegotiationScheduleSlotDTO
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList(),
            AgreedSchedule = n.AgreedSchedule.Select(s => new NegotiationScheduleSlotDTO
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList(),
            ServiceAddress = n.ServiceAddress,
            ServiceLatitude = n.ServiceLatitude,
            ServiceLongitude = n.ServiceLongitude,
            AccessInstructions = n.AccessInstructions,
            SpecialClientRequirements = n.SpecialClientRequirements,
            AdditionalNotes = n.AdditionalNotes,
            ClientAgreed = n.ClientAgreed,
            ClientAgreedAt = n.ClientAgreedAt,
            CaregiverAgreed = n.CaregiverAgreed,
            CaregiverAgreedAt = n.CaregiverAgreedAt,
            LastClientNote = n.LastClientNote,
            LastCaregiverNote = n.LastCaregiverNote,
            CreatedByRole = n.CreatedByRole,
            NegotiationRound = n.NegotiationRound,
            ContractId = n.ContractId,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.UpdatedAt
        };
    }
}
