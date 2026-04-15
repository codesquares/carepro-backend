using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using System;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class VisitCheckinService : IVisitCheckinService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IGeocodingService _geocodingService;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<VisitCheckinService> _logger;

        public VisitCheckinService(
            CareProDbContext dbContext,
            IGeocodingService geocodingService,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILogger<VisitCheckinService> logger)
        {
            _dbContext = dbContext;
            _geocodingService = geocodingService;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task<VisitCheckinResponse> CheckinAsync(VisitCheckinRequest request, string caregiverId)
        {
            // Check if already checked in for this task sheet (idempotent)
            var existing = await _dbContext.VisitCheckins
                .FirstOrDefaultAsync(vc => vc.TaskSheetId == request.TaskSheetId);

            if (existing != null)
            {
                _logger.LogInformation("Caregiver {CaregiverId} already checked in for TaskSheet {TaskSheetId}", caregiverId, request.TaskSheetId);
                return new VisitCheckinResponse
                {
                    Success = true,
                    CheckinId = existing.Id.ToString(),
                    CheckinTimestamp = existing.CheckinTimestamp,
                    DistanceFromServiceAddress = existing.DistanceFromServiceAddress,
                    AlreadyCheckedIn = true
                };
            }

            // Validate order exists and caregiver is assigned
            if (!ObjectId.TryParse(request.OrderId, out var orderObjectId))
                throw new ArgumentException("Invalid order ID format.");

            var order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
            if (order == null)
                throw new KeyNotFoundException($"Order '{request.OrderId}' not found.");

            if (order.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("You are not assigned to this order.");

            // Validate the task sheet exists and belongs to this order
            if (!ObjectId.TryParse(request.TaskSheetId, out var taskSheetObjectId))
                throw new ArgumentException("Invalid task sheet ID format.");

            var taskSheet = await _dbContext.TaskSheets.FirstOrDefaultAsync(ts => ts.Id == taskSheetObjectId);
            if (taskSheet == null)
                throw new KeyNotFoundException($"Task sheet '{request.TaskSheetId}' not found.");

            if (taskSheet.OrderId != request.OrderId)
                throw new InvalidOperationException("Task sheet does not belong to the specified order.");

            if (taskSheet.Status == "submitted")
                throw new InvalidOperationException("Cannot check in to an already submitted task sheet.");

            if (taskSheet.Status == "cancelled")
                throw CheckinValidationException.TaskSheetCancelled("This task sheet has been cancelled.");

            // Auto-activate scheduled task sheets on check-in — no separate activation step needed.
            // Applies the same sequential gate as the explicit activate endpoint.
            if (taskSheet.Status == "scheduled")
            {
                var previousSheets = await _dbContext.TaskSheets
                    .Where(ts => ts.OrderId == taskSheet.OrderId
                        && ts.BillingCycleNumber == taskSheet.BillingCycleNumber
                        && ts.SheetNumber < taskSheet.SheetNumber
                        && ts.Status != "cancelled"
                        && ts.Status != "scheduled")
                    .OrderByDescending(ts => ts.SheetNumber)
                    .ToListAsync();

                if (previousSheets.Count > 0)
                {
                    var lastSheet = previousSheets.First();
                    if (lastSheet.Status != "submitted")
                        throw new InvalidOperationException(
                            $"Visit #{lastSheet.SheetNumber} has not been submitted yet. Please submit it before checking in to this visit.");
                    if (lastSheet.ClientReviewStatus != "Approved" && lastSheet.ClientReviewStatus != "Disputed")
                        throw new InvalidOperationException(
                            $"Visit #{lastSheet.SheetNumber} has not been reviewed by the client yet. The client must approve or review it before you can check in to this visit.");
                }

                taskSheet.Status = "in-progress";
                taskSheet.UpdatedAt = DateTime.UtcNow;
                _dbContext.TaskSheets.Update(taskSheet);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("TaskSheet {TaskSheetId} auto-activated during check-in for order {OrderId}",
                    taskSheet.Id, taskSheet.OrderId);
            }

            // Verify the client has approved the contract before allowing check-in
            var approvedContract = await _dbContext.Contracts
                .FirstOrDefaultAsync(c => c.OrderId == request.OrderId &&
                    (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted));

            if (approvedContract == null)
                throw CheckinValidationException.NoApprovedContract("Cannot check in until the client has approved the contract for this order.");

            // Schedule guard — allowed on the task sheet's scheduled date, within the visit window (Nigerian time)
            ValidateSchedule(approvedContract, taskSheet);

            // GPS proximity validation
            double? distanceMeters = await ValidateProximity(request.Latitude, request.Longitude, order, caregiverId);

            var checkin = new VisitCheckin
            {
                Id = ObjectId.GenerateNewId(),
                TaskSheetId = request.TaskSheetId,
                OrderId = request.OrderId,
                CaregiverId = caregiverId,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Accuracy = request.Accuracy,
                DistanceFromServiceAddress = distanceMeters,
                CheckinTimestamp = request.CheckinTimestamp,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.VisitCheckins.AddAsync(checkin);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Caregiver {CaregiverId} checked in for TaskSheet {TaskSheetId}. Distance: {Distance}m",
                caregiverId, request.TaskSheetId, distanceMeters?.ToString("F0") ?? "unknown");

            return new VisitCheckinResponse
            {
                Success = true,
                CheckinId = checkin.Id.ToString(),
                CheckinTimestamp = checkin.CheckinTimestamp,
                DistanceFromServiceAddress = distanceMeters,
                AlreadyCheckedIn = false
            };
        }

        public async Task<VisitCheckinDTO?> GetCheckinByTaskSheetIdAsync(string taskSheetId)
        {
            var checkin = await _dbContext.VisitCheckins
                .FirstOrDefaultAsync(vc => vc.TaskSheetId == taskSheetId);

            if (checkin == null) return null;

            return new VisitCheckinDTO
            {
                CheckinId = checkin.Id.ToString(),
                Latitude = checkin.Latitude,
                Longitude = checkin.Longitude,
                Accuracy = checkin.Accuracy,
                DistanceFromServiceAddress = checkin.DistanceFromServiceAddress,
                CheckinTimestamp = checkin.CheckinTimestamp
            };
        }

        /// <summary>
        /// Validates that today is the task sheet's scheduled date and the current Nigerian time
        /// falls within the visit window (30 minutes before start through end time).
        /// </summary>
        private void ValidateSchedule(Contract approvedContract, TaskSheet taskSheet)
        {
            var nigerianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
            var nowNigeria = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, nigerianTimeZone);
            var todayNigeria = nowNigeria.Date;
            var currentTime = nowNigeria.TimeOfDay;

            // Verify today is the task sheet's specific scheduled date
            if (taskSheet.ScheduledDate.HasValue && taskSheet.ScheduledDate.Value.Date != todayNigeria)
            {
                throw CheckinValidationException.NotScheduledToday(
                    $"This visit is scheduled for {taskSheet.ScheduledDate.Value:dddd, MMMM d}. " +
                    $"Check-in is only allowed on the scheduled date.",
                    nowNigeria.DayOfWeek.ToString(),
                    nowNigeria.ToString("HH:mm"));
            }

            // Use the task sheet's date to look up the correct day's contract schedule slot
            var visitDow = taskSheet.ScheduledDate.HasValue
                ? taskSheet.ScheduledDate.Value.DayOfWeek
                : nowNigeria.DayOfWeek;

            var todaysSlot = approvedContract.Schedule.FirstOrDefault(s => s.DayOfWeek == visitDow);

            if (todaysSlot == null)
            {
                throw CheckinValidationException.NotScheduledToday(
                    $"No visit is scheduled for {visitDow}. Check your contract schedule.",
                    visitDow.ToString(),
                    nowNigeria.ToString("HH:mm"));
            }

            // Allow check-in from 30 minutes before the visit starts through the end of the visit.
            // This covers early arrival and any point during the full visit duration.
            var gracePeriod = TimeSpan.FromMinutes(30);
            if (TimeSpan.TryParse(todaysSlot.StartTime, out var start) &&
                TimeSpan.TryParse(todaysSlot.EndTime, out var end))
            {
                var windowStart = start - gracePeriod;
                var windowEnd = end;

                if (currentTime < windowStart || currentTime > windowEnd)
                {
                    var earlyTime = (start - gracePeriod).ToString(@"hh\:mm");
                    throw CheckinValidationException.OutsideSchedule(
                        $"Check-in is available from {earlyTime} until {todaysSlot.EndTime} (Nigerian time). " +
                        $"Current time: {nowNigeria:HH:mm}.",
                        visitDow.ToString(),
                        todaysSlot.StartTime,
                        todaysSlot.EndTime,
                        nowNigeria.ToString("HH:mm"));
                }
            }
        }

        private async Task<double?> ValidateProximity(double caregiverLat, double caregiverLng, ClientOrder order, string caregiverId)
        {
            int maxDistanceMeters = _configuration.GetValue<int>("VisitCheckin:MaxDistanceMeters", 150);

            // Try to get service location coordinates
            double? serviceLat = null;
            double? serviceLng = null;

            // Priority 1: Contract's geocoded service coordinates
            var contract = await _dbContext.Contracts
                .FirstOrDefaultAsync(c => c.OrderId == order.Id.ToString() &&
                    (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted));

            if (contract != null)
            {
                if (contract.ServiceLatitude.HasValue && contract.ServiceLongitude.HasValue)
                {
                    serviceLat = contract.ServiceLatitude;
                    serviceLng = contract.ServiceLongitude;
                }
                else if (!string.IsNullOrEmpty(contract.ServiceAddress))
                {
                    // Geocode the contract service address and cache it
                    try
                    {
                        var geocoded = await _geocodingService.GeocodeAsync(contract.ServiceAddress);
                        serviceLat = geocoded.Latitude;
                        serviceLng = geocoded.Longitude;

                        // Cache for future check-ins
                        contract.ServiceLatitude = serviceLat;
                        contract.ServiceLongitude = serviceLng;
                        _dbContext.Contracts.Update(contract);
                        await _dbContext.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to geocode contract service address for order {OrderId}", order.Id);
                    }
                }
            }

            // No fallback to client's personal address — it may differ from the service address.
            // If the contract has no geocoded service coordinates, reject the check-in.
            if (!serviceLat.HasValue || !serviceLng.HasValue)
            {
                _logger.LogWarning("No geocoded service address on contract for order {OrderId}. Cannot validate proximity.", order.Id);
                throw CheckinValidationException.NoGeocodedAddress(
                    "The service address for this order has not been geocoded yet. Please contact support or ask the client to confirm the service address.");
            }

            // Calculate distance using Haversine
            double distanceKm = CalculateHaversineDistance(caregiverLat, caregiverLng, serviceLat.Value, serviceLng.Value);
            double distanceMeters = distanceKm * 1000;

            if (distanceMeters > maxDistanceMeters)
            {
                if (_environment.IsDevelopment())
                {
                    _logger.LogWarning(
                        "[DEV] Proximity check SKIPPED for caregiver {CaregiverId} on order {OrderId}. " +
                        "Distance: {Distance:F0}m (limit: {Limit}m). Allowing check-in in Development.",
                        caregiverId, order.Id, distanceMeters, maxDistanceMeters);
                }
                else
                {
                    throw CheckinValidationException.Proximity(
                        $"You are approximately {distanceMeters:F0}m away from the service address. " +
                        $"You must be within {maxDistanceMeters}m to check in.",
                        distanceMeters, maxDistanceMeters);
                }
            }

            return Math.Round(distanceMeters, 1);
        }

        private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Earth's radius in km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
