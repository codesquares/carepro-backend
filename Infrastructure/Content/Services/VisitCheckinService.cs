using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
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
        private readonly IMediator _mediator;
        private readonly ILogger<VisitCheckinService> _logger;

        public VisitCheckinService(
            CareProDbContext dbContext,
            IGeocodingService geocodingService,
            IConfiguration configuration,
            IHostEnvironment environment,
            IMediator mediator,
            ILogger<VisitCheckinService> logger)
        {
            _dbContext = dbContext;
            _geocodingService = geocodingService;
            _configuration = configuration;
            _environment = environment;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<VisitCheckinResponse> CheckinAsync(VisitCheckinRequest request, string caregiverId)
        {
            // Captured first, before any validation, so it reflects the moment the
            // request actually reached the server — this is the authoritative timestamp
            // for hour/payroll calculations (Phase 9.4), not the device-supplied one below.
            var serverReceivedAt = DateTime.UtcNow;

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
                    AlreadyCheckedIn = true,
                    IsLateCheckin = existing.IsLateCheckin,
                    MinutesLate = existing.MinutesLate,
                    HasTimestampDiscrepancy = existing.HasTimestampDiscrepancy
                };
            }

            // Validate the task sheet exists — fetched first since its AssignmentId tells
            // us which authorization path applies (Phase 9.5).
            if (!ObjectId.TryParse(request.TaskSheetId, out var taskSheetObjectId))
                throw new ArgumentException("Invalid task sheet ID format.");

            var taskSheet = await _dbContext.TaskSheets.FirstOrDefaultAsync(ts => ts.Id == taskSheetObjectId);
            if (taskSheet == null)
                throw new KeyNotFoundException($"Task sheet '{request.TaskSheetId}' not found.");

            // Resolve authorization: legacy ClientOrder+Contract path, or (Phase 9.5) an
            // Accepted Assignment with its Generated Contract — alongside, not replacing,
            // the legacy path below, which is otherwise unchanged.
            ClientOrder? order = null;
            Assignment? assignment = null;
            string? notifyClientId;

            if (!string.IsNullOrEmpty(taskSheet.AssignmentId))
            {
                assignment = await _dbContext.Assignments
                    .FirstOrDefaultAsync(a => a.Id.ToString() == taskSheet.AssignmentId)
                    ?? throw new KeyNotFoundException($"Assignment '{taskSheet.AssignmentId}' not found.");

                if (assignment.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("You are not assigned to this package.");

                if (assignment.Status != AssignmentStatuses.Accepted)
                    throw new InvalidOperationException("This assignment has not been accepted yet.");

                notifyClientId = assignment.ClientId;
            }
            else
            {
                if (string.IsNullOrEmpty(request.OrderId) || !ObjectId.TryParse(request.OrderId, out var orderObjectId))
                    throw new ArgumentException("Invalid order ID format.");

                order = await _dbContext.ClientOrders.FirstOrDefaultAsync(o => o.Id == orderObjectId);
                if (order == null)
                    throw new KeyNotFoundException($"Order '{request.OrderId}' not found.");

                if (order.CaregiverId != caregiverId)
                    throw new UnauthorizedAccessException("You are not assigned to this order.");

                if (taskSheet.OrderId != request.OrderId)
                    throw new InvalidOperationException("Task sheet does not belong to the specified order.");

                notifyClientId = order.ClientId;
            }

            if (taskSheet.Status == "submitted")
                throw new InvalidOperationException("Cannot check in to an already submitted task sheet.");

            if (taskSheet.Status == "cancelled")
                throw CheckinValidationException.TaskSheetCancelled("This task sheet has been cancelled.");

            // Auto-activate scheduled task sheets on check-in — no separate activation step needed.
            // Applies the same sequential gate as the explicit activate endpoint.
            if (taskSheet.Status == "scheduled")
            {
                IQueryable<TaskSheet> priorSheetsQuery = _dbContext.TaskSheets
                    .Where(ts => ts.BillingCycleNumber == taskSheet.BillingCycleNumber
                        && ts.SheetNumber < taskSheet.SheetNumber
                        && ts.Status != "cancelled"
                        && ts.Status != "scheduled");

                priorSheetsQuery = !string.IsNullOrEmpty(taskSheet.AssignmentId)
                    ? priorSheetsQuery.Where(ts => ts.AssignmentId == taskSheet.AssignmentId)
                    : priorSheetsQuery.Where(ts => ts.OrderId == taskSheet.OrderId);

                var previousSheets = await priorSheetsQuery.OrderByDescending(ts => ts.SheetNumber).ToListAsync();

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
                _logger.LogInformation("TaskSheet {TaskSheetId} auto-activated during check-in", taskSheet.Id);
            }

            // Verify the client has approved the contract before allowing check-in
            Contract approvedContract;
            if (assignment != null)
            {
                approvedContract = await _dbContext.Contracts.FirstOrDefaultAsync(c =>
                        c.PackageRequestId == assignment.PackageRequestId && c.Status == ContractStatus.Generated)
                    ?? throw CheckinValidationException.NoApprovedContract("No active contract found for this package assignment.");
            }
            else
            {
                approvedContract = await _dbContext.Contracts
                        .FirstOrDefaultAsync(c => c.OrderId == request.OrderId &&
                            (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted))
                    ?? throw CheckinValidationException.NoApprovedContract("Cannot check in until the client has approved the contract for this order.");
            }

            // Schedule guard — allowed on the task sheet's scheduled date, within the visit window (Nigerian time).
            // Package contracts have no Schedule yet (Phase 5 is deferred), so the time-window
            // check is skipped for that path — allowMissingSchedule only relaxes that, not the date check.
            var (isLateCheckin, minutesLate) = ValidateSchedule(approvedContract, taskSheet, allowMissingSchedule: assignment != null);

            // GPS proximity validation
            double? distanceMeters = order != null
                ? await ValidateProximity(request.Latitude, request.Longitude, order, caregiverId)
                : await ValidateProximityForPackageAsync(request.Latitude, request.Longitude, approvedContract, caregiverId);

            var thresholdSeconds = _configuration.GetValue<int>("VisitCheckin:TimestampDiscrepancyThresholdSeconds", 300);
            var discrepancySeconds = Math.Round(Math.Abs((request.CheckinTimestamp - serverReceivedAt).TotalSeconds), 1);
            var hasTimestampDiscrepancy = discrepancySeconds > thresholdSeconds;

            if (hasTimestampDiscrepancy)
            {
                _logger.LogWarning(
                    "Check-in timestamp discrepancy for caregiver {CaregiverId} TaskSheet {TaskSheetId}: " +
                    "device={DeviceTimestamp:o}, server={ServerTimestamp:o}, diff={DiffSeconds}s (threshold {ThresholdSeconds}s). Flagged for admin review.",
                    caregiverId, request.TaskSheetId, request.CheckinTimestamp, serverReceivedAt, discrepancySeconds, thresholdSeconds);
            }

            var checkin = new VisitCheckin
            {
                Id = ObjectId.GenerateNewId(),
                TaskSheetId = request.TaskSheetId,
                OrderId = order?.Id.ToString() ?? string.Empty,
                AssignmentId = taskSheet.AssignmentId,
                PackageRequestId = taskSheet.PackageRequestId,
                CaregiverId = caregiverId,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Accuracy = request.Accuracy,
                DistanceFromServiceAddress = distanceMeters,
                CheckinTimestamp = request.CheckinTimestamp,
                ServerReceivedAt = serverReceivedAt,
                HasTimestampDiscrepancy = hasTimestampDiscrepancy,
                TimestampDiscrepancySeconds = discrepancySeconds,
                IsLateCheckin = isLateCheckin,
                MinutesLate = minutesLate,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.VisitCheckins.AddAsync(checkin);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Caregiver {CaregiverId} checked in for TaskSheet {TaskSheetId}. Distance: {Distance}m",
                caregiverId, request.TaskSheetId, distanceMeters?.ToString("F0") ?? "unknown");

            // ── Notify client that caregiver has arrived and checked in ──
            try
            {
                if (!string.IsNullOrEmpty(notifyClientId))
                {
                    await _mediator.Send(new SendNotificationCommand(
                        RecipientId: notifyClientId,
                        SenderId: caregiverId,
                        Type: NotificationTypes.CaregiverCheckedIn,
                        Content: $"Your caregiver has arrived and checked in for Visit #{taskSheet.SheetNumber}.",
                        Title: "Caregiver Checked In",
                        RelatedEntityId: request.TaskSheetId,
                        OrderId: order?.Id.ToString()));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send check-in notification for TaskSheet {TaskSheetId}", request.TaskSheetId);
            }

            return new VisitCheckinResponse
            {
                Success = true,
                CheckinId = checkin.Id.ToString(),
                CheckinTimestamp = checkin.CheckinTimestamp,
                DistanceFromServiceAddress = distanceMeters,
                AlreadyCheckedIn = false,
                IsLateCheckin = isLateCheckin,
                MinutesLate = minutesLate,
                HasTimestampDiscrepancy = hasTimestampDiscrepancy
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
                AssignmentId = checkin.AssignmentId,
                PackageRequestId = checkin.PackageRequestId,
                Latitude = checkin.Latitude,
                Longitude = checkin.Longitude,
                Accuracy = checkin.Accuracy,
                DistanceFromServiceAddress = checkin.DistanceFromServiceAddress,
                CheckinTimestamp = checkin.CheckinTimestamp,
                ServerReceivedAt = checkin.ServerReceivedAt,
                HasTimestampDiscrepancy = checkin.HasTimestampDiscrepancy,
                TimestampDiscrepancySeconds = checkin.TimestampDiscrepancySeconds,
                IsLateCheckin = checkin.IsLateCheckin,
                MinutesLate = checkin.MinutesLate
            };
        }

        /// <summary>
        /// Validates that today is the task sheet's scheduled date and the current Nigerian time
        /// falls within the visit window. Returns whether the check-in is late and by how many minutes.
        /// Early window: EarlyCheckinMinutes (default 60) before start.
        /// Late window: LateCheckinHours (default 2) after scheduled end — recorded as late.
        /// </summary>
        private (bool IsLate, double MinutesLate) ValidateSchedule(Contract approvedContract, TaskSheet taskSheet, bool allowMissingSchedule = false)
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

            var earlyMinutes = _configuration.GetValue<int>("VisitCheckin:EarlyCheckinMinutes", 60);
            var lateHours = _configuration.GetValue<int>("VisitCheckin:LateCheckinHours", 2);

            string? startTimeStr;
            string? endTimeStr;

            // Primary path: task sheet carries its own time window (set at creation or backfilled at reschedule)
            if (!string.IsNullOrEmpty(taskSheet.ScheduledStartTime) && !string.IsNullOrEmpty(taskSheet.ScheduledEndTime))
            {
                startTimeStr = taskSheet.ScheduledStartTime;
                endTimeStr = taskSheet.ScheduledEndTime;
            }
            else
            {
                // Fallback path: legacy sheets without stored time — look up by day-of-week from contract
                var visitDow = taskSheet.ScheduledDate.HasValue
                    ? taskSheet.ScheduledDate.Value.DayOfWeek
                    : nowNigeria.DayOfWeek;

                var todaysSlot = approvedContract.Schedule.FirstOrDefault(s => s.DayOfWeek == visitDow);

                if (todaysSlot == null)
                {
                    // Phase 9.5: package contracts are generated with an empty Schedule
                    // (Phase 5's scheduling flow is still deferred) — there's genuinely no
                    // time-window data to validate against yet, so skip the window rather
                    // than block every package check-in. The scheduled-date check above
                    // still applies. Legacy behavior (throw) is unchanged by default.
                    if (allowMissingSchedule)
                        return (false, 0);

                    throw CheckinValidationException.NotScheduledToday(
                        $"No visit is scheduled for {visitDow}. Check your contract schedule.",
                        visitDow.ToString(),
                        nowNigeria.ToString("HH:mm"));
                }

                startTimeStr = todaysSlot.StartTime;
                endTimeStr = todaysSlot.EndTime;
            }

            if (TimeSpan.TryParse(startTimeStr, out var start) &&
                TimeSpan.TryParse(endTimeStr, out var end))
            {
                var windowStart = start - TimeSpan.FromMinutes(earlyMinutes);
                var windowEnd = end + TimeSpan.FromHours(lateHours);

                if (currentTime < windowStart)
                {
                    var opensAt = windowStart.ToString(@"hh\:mm");
                    throw CheckinValidationException.OutsideSchedule(
                        $"Check-in opens at {opensAt} ({earlyMinutes} minutes before the visit starts, Nigerian time). " +
                        $"Current time: {nowNigeria:HH:mm}.",
                        nowNigeria.DayOfWeek.ToString(),
                        startTimeStr,
                        endTimeStr,
                        nowNigeria.ToString("HH:mm"));
                }

                if (currentTime > windowEnd)
                {
                    var closedAt = windowEnd.ToString(@"hh\:mm");
                    throw CheckinValidationException.OutsideSchedule(
                        $"The check-in window for this visit closed at {closedAt} (Nigerian time). " +
                        $"Please contact support if you need to record this visit. Current time: {nowNigeria:HH:mm}.",
                        nowNigeria.DayOfWeek.ToString(),
                        startTimeStr,
                        endTimeStr,
                        nowNigeria.ToString("HH:mm"));
                }

                var isLate = currentTime > start;
                var minutesLate = isLate ? Math.Round((currentTime - start).TotalMinutes, 1) : 0;
                return (isLate, minutesLate);
            }

            return (false, 0);
        }

        private async Task<double?> ValidateProximity(double caregiverLat, double caregiverLng, ClientOrder order, string caregiverId)
        {
            int maxDistanceMeters = _configuration.GetValue<int>("VisitCheckin:MaxDistanceMeters", 1500);

            // Try to get service location coordinates
            double? serviceLat = null;
            double? serviceLng = null;
            bool isClientVerifiedGps = false;

            var contract = await _dbContext.Contracts
                .FirstOrDefaultAsync(c => c.OrderId == order.Id.ToString() &&
                    (c.Status == ContractStatus.Approved || c.Status == ContractStatus.Accepted));

            // Capture the pre-existing state before any geocoding modifies the field.
            // Used below to send the client nudge notification exactly once (when it's still null).
            bool? locationStateBeforeCheck = contract?.ServiceLocationSetByClient;

            if (contract != null)
            {
                if (contract.ServiceLatitude.HasValue && contract.ServiceLongitude.HasValue)
                {
                    serviceLat = contract.ServiceLatitude;
                    serviceLng = contract.ServiceLongitude;
                    isClientVerifiedGps = contract.ServiceLocationSetByClient == true;
                }
                else if (!string.IsNullOrEmpty(contract.ServiceAddress))
                {
                    // Geocode the address as a last resort — coordinates will NOT be treated
                    // as client-verified, so the distance check is informational only.
                    try
                    {
                        var geocoded = await _geocodingService.GeocodeAsync(contract.ServiceAddress);
                        serviceLat = geocoded.Latitude;
                        serviceLng = geocoded.Longitude;
                        isClientVerifiedGps = false;

                        // Cache geocoded result so we don't call the API on every check-in,
                        // but keep ServiceLocationSetByClient = false to signal it's not GPS-accurate.
                        contract.ServiceLatitude = serviceLat;
                        contract.ServiceLongitude = serviceLng;
                        contract.ServiceLocationSetByClient = false;
                        _dbContext.Contracts.Update(contract);
                        await _dbContext.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to geocode contract service address for order {OrderId}", order.Id);
                    }
                }
            }

            if (!serviceLat.HasValue || !serviceLng.HasValue)
            {
                // No reference point at all — allow check-in but log so ops can follow up.
                _logger.LogWarning(
                    "No service coordinates on contract for order {OrderId}. " +
                    "Caregiver {CaregiverId} check-in allowed without proximity validation. " +
                    "Client should set GPS via POST /api/contracts/{{id}}/service-location.",
                    order.Id, caregiverId);
                return null;
            }

            // Calculate distance using Haversine
            double distanceKm = CalculateHaversineDistance(caregiverLat, caregiverLng, serviceLat.Value, serviceLng.Value);
            double distanceMeters = distanceKm * 1000;

            if (!isClientVerifiedGps)
            {
                // Coordinates came from geocoding — accuracy is unreliable for Nigerian addresses.
                // Log the distance for audit but do NOT block the check-in.
                _logger.LogWarning(
                    "Proximity check INFORMATIONAL (geocoded coords, not client GPS) for caregiver {CaregiverId} " +
                    "on order {OrderId}. Distance: {Distance:F0}m. Client has not yet set GPS via /service-location endpoint.",
                    caregiverId, order.Id, distanceMeters);

                // Nudge the client once — only when locationStateBeforeCheck is null,
                // meaning this is the very first check-in and the client has never been prompted.
                // After this point ServiceLocationSetByClient = false (geocoded), so it won't fire again.
                if (locationStateBeforeCheck == null && contract != null && !string.IsNullOrEmpty(order.ClientId))
                {
                    try
                    {
                        await _mediator.Send(new SendNotificationCommand(
                            RecipientId: order.ClientId,
                            SenderId: caregiverId,
                            Type: NotificationTypes.ServiceLocationNotSet,
                            Content: "Your caregiver just checked in for their first visit. To enable accurate location verification on future visits, please confirm your service address GPS in the app.",
                            Title: "Action Needed: Confirm Your Service Location",
                            RelatedEntityId: contract.Id,
                            OrderId: order.Id.ToString()
                        ));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send service_location_not_set notification for order {OrderId}", order.Id);
                    }
                }

                return Math.Round(distanceMeters, 1);
            }

            // Client GPS-verified: enforce the hard limit
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

        /// <summary>
        /// Package-assignment equivalent of <see cref="ValidateProximity"/> (Phase 9.5) —
        /// same GPS-verification logic, sourced from the Contract already fetched by the
        /// caller instead of looking one up via a ClientOrder. In practice this almost
        /// always hits the "no coordinates" branch today, since package contracts don't
        /// yet have ServiceAddress/ServiceLatitude/Longitude populated (Phase 5 — payment
        /// and location capture — is still deferred), but it's built out fully for when
        /// that lands rather than silently skipping proximity forever.
        /// </summary>
        private async Task<double?> ValidateProximityForPackageAsync(double caregiverLat, double caregiverLng, Contract contract, string caregiverId)
        {
            int maxDistanceMeters = _configuration.GetValue<int>("VisitCheckin:MaxDistanceMeters", 1500);

            double? serviceLat = null;
            double? serviceLng = null;
            bool isClientVerifiedGps = false;

            bool? locationStateBeforeCheck = contract.ServiceLocationSetByClient;

            if (contract.ServiceLatitude.HasValue && contract.ServiceLongitude.HasValue)
            {
                serviceLat = contract.ServiceLatitude;
                serviceLng = contract.ServiceLongitude;
                isClientVerifiedGps = contract.ServiceLocationSetByClient == true;
            }
            else if (!string.IsNullOrEmpty(contract.ServiceAddress))
            {
                try
                {
                    var geocoded = await _geocodingService.GeocodeAsync(contract.ServiceAddress);
                    serviceLat = geocoded.Latitude;
                    serviceLng = geocoded.Longitude;
                    isClientVerifiedGps = false;

                    contract.ServiceLatitude = serviceLat;
                    contract.ServiceLongitude = serviceLng;
                    contract.ServiceLocationSetByClient = false;
                    _dbContext.Contracts.Update(contract);
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to geocode contract service address for package assignment (PackageRequestId {PackageRequestId})",
                        contract.PackageRequestId);
                }
            }

            if (!serviceLat.HasValue || !serviceLng.HasValue)
            {
                _logger.LogWarning(
                    "No service coordinates on contract for package assignment (PackageRequestId {PackageRequestId}). " +
                    "Caregiver {CaregiverId} check-in allowed without proximity validation.",
                    contract.PackageRequestId, caregiverId);
                return null;
            }

            double distanceKm = CalculateHaversineDistance(caregiverLat, caregiverLng, serviceLat.Value, serviceLng.Value);
            double distanceMeters = distanceKm * 1000;

            if (!isClientVerifiedGps)
            {
                _logger.LogWarning(
                    "Proximity check INFORMATIONAL (geocoded coords, not client GPS) for caregiver {CaregiverId} " +
                    "on package assignment (PackageRequestId {PackageRequestId}). Distance: {Distance:F0}m.",
                    caregiverId, contract.PackageRequestId, distanceMeters);

                if (locationStateBeforeCheck == null && !string.IsNullOrEmpty(contract.ClientId))
                {
                    try
                    {
                        await _mediator.Send(new SendNotificationCommand(
                            RecipientId: contract.ClientId,
                            SenderId: caregiverId,
                            Type: NotificationTypes.ServiceLocationNotSet,
                            Content: "Your caregiver just checked in for their first visit. To enable accurate location verification on future visits, please confirm your service address GPS in the app.",
                            Title: "Action Needed: Confirm Your Service Location",
                            RelatedEntityId: contract.Id
                        ));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send service_location_not_set notification for package assignment (PackageRequestId {PackageRequestId})",
                            contract.PackageRequestId);
                    }
                }

                return Math.Round(distanceMeters, 1);
            }

            if (distanceMeters > maxDistanceMeters)
            {
                if (_environment.IsDevelopment())
                {
                    _logger.LogWarning(
                        "[DEV] Proximity check SKIPPED for caregiver {CaregiverId} on package assignment. " +
                        "Distance: {Distance:F0}m (limit: {Limit}m). Allowing check-in in Development.",
                        caregiverId, distanceMeters, maxDistanceMeters);
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
