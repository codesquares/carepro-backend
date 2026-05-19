using Application.Interfaces.Email;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// GDPR-compliant background service that permanently anonymizes or hard-deletes
    /// user account data after the 30-day grace period following a soft-deletion.
    ///
    /// Runs once every 24 hours. For each Caregiver or Client where IsDeleted == true
    /// and DeletedOn is 30+ days ago (and AccountDeletionRequestedAt is set):
    ///
    /// 1. Captures email/name before wiping (needed to send the final confirmation email)
    /// 2. Sends "account permanently deleted" email
    /// 3. Hard-deletes: Notifications, RefreshTokens, Location, Certifications,
    ///    Verifications, AssessmentSessions, NotificationPreferences
    /// 4. Anonymizes: Caregiver/Client profile (name, email, phone, address, photo),
    ///    AppUser (email, name, password, google fields)
    /// 5. Retained as-is: BillingRecords, ClientOrders, Earnings, EarningsLedger,
    ///    Disputes, IncidentReports, Reviews (gig deletion handles review PII)
    ///
    /// Each user is processed independently; a failure on one does not block others.
    /// </summary>
    public class UserHardDeleteProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UserHardDeleteProcessor> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
        private const int GracePeriodDays = 30;
        private const string RedactedMarker = "[REDACTED]";
        private const string RedactedEmail = "deleted@carepro.invalid";
        private const string RedactedName = "[DELETED]";

        public UserHardDeleteProcessor(IServiceScopeFactory scopeFactory, ILogger<UserHardDeleteProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Wait for app to fully start before first run
                await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("UserHardDeleteProcessor: Starting GDPR hard-delete cycle");

                    try
                    {
                        await ProcessExpiredUsersAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "UserHardDeleteProcessor: Unhandled error during hard-delete cycle");
                    }

                    await Task.Delay(RunInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
            }

            _logger.LogInformation("UserHardDeleteProcessor: Stopped");
        }

        private async Task ProcessExpiredUsersAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var cutoffDate = DateTime.UtcNow.AddDays(-GracePeriodDays);

            // ── Expired caregivers ──
            var expiredCaregivers = await dbContext.CareGivers
                .IgnoreQueryFilters()
                .Where(c => c.IsDeleted == true
                    && c.AccountDeletionRequestedAt != null
                    && c.DeletedOn != null
                    && c.DeletedOn <= cutoffDate)
                .ToListAsync(stoppingToken);

            // ── Expired clients ──
            var expiredClients = await dbContext.Clients
                .IgnoreQueryFilters()
                .Where(c => c.IsDeleted == true
                    && c.AccountDeletionRequestedAt != null
                    && c.DeletedOn != null
                    && c.DeletedOn <= cutoffDate)
                .ToListAsync(stoppingToken);

            if (!expiredCaregivers.Any() && !expiredClients.Any())
            {
                _logger.LogDebug("UserHardDeleteProcessor: No users past grace period");
                return;
            }

            _logger.LogInformation(
                "UserHardDeleteProcessor: Found {CaregiverCount} caregiver(s) and {ClientCount} client(s) past grace period",
                expiredCaregivers.Count, expiredClients.Count);

            int processed = 0;
            int failed = 0;

            foreach (var caregiver in expiredCaregivers)
            {
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    await ProcessSingleCaregiverAsync(dbContext, emailService, caregiver);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "UserHardDeleteProcessor: Failed to process caregiver {CaregiverId}", caregiver.Id);
                }
            }

            foreach (var client in expiredClients)
            {
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    await ProcessSingleClientAsync(dbContext, emailService, client);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "UserHardDeleteProcessor: Failed to process client {ClientId}", client.Id);
                }
            }

            _logger.LogInformation(
                "UserHardDeleteProcessor: Cycle complete. Processed: {Processed}, Failed: {Failed}",
                processed, failed);
        }

        private async Task ProcessSingleCaregiverAsync(
            CareProDbContext dbContext, IEmailService emailService, Domain.Entities.Caregiver caregiver)
        {
            var caregiverId = caregiver.Id.ToString();
            var capturedEmail = caregiver.Email;
            var capturedName = caregiver.FirstName;

            _logger.LogInformation(
                "UserHardDeleteProcessor: Anonymising caregiver {CaregiverId} (deleted on {DeletedOn})",
                caregiverId, caregiver.DeletedOn);

            // ── Send final notification email before wiping data ──
            try
            {
                await emailService.SendGenericNotificationEmailAsync(
                    capturedEmail, capturedName,
                    "Your CarePro Account Has Been Permanently Deleted",
                    "As requested, your CarePro account and personal data have now been permanently deleted. " +
                    "Financial and legally required records are retained as mandated by law. " +
                    "If you believe this was an error, please contact support@oncarepro.com.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserHardDeleteProcessor: Failed to send final deletion email to caregiver {CaregiverId}", caregiverId);
            }

            // ── 1. Hard-delete: Notifications ──
            var notifications = await dbContext.Notifications
                .Where(n => n.RecipientId == caregiverId || n.SenderId == caregiverId)
                .ToListAsync();
            if (notifications.Any())
            {
                dbContext.Notifications.RemoveRange(notifications);
                _logger.LogInformation("UserHardDeleteProcessor: Caregiver {CaregiverId} - Deleted {Count} notifications", caregiverId, notifications.Count);
            }

            // ── 2. Hard-delete: RefreshTokens ──
            var refreshTokens = await dbContext.RefreshTokens
                .Where(t => t.UserId == caregiverId)
                .ToListAsync();
            if (refreshTokens.Any())
            {
                dbContext.RefreshTokens.RemoveRange(refreshTokens);
                _logger.LogInformation("UserHardDeleteProcessor: Caregiver {CaregiverId} - Deleted {Count} refresh tokens", caregiverId, refreshTokens.Count);
            }

            // ── 3. Hard-delete: Location ──
            var locations = await dbContext.Locations
                .Where(l => l.UserId == caregiverId)
                .ToListAsync();
            if (locations.Any())
            {
                dbContext.Locations.RemoveRange(locations);
                _logger.LogInformation("UserHardDeleteProcessor: Caregiver {CaregiverId} - Deleted {Count} location record(s)", caregiverId, locations.Count);
            }

            // ── 4. Hard-delete: Certifications (profile PII) ──
            var certifications = await dbContext.Certifications
                .Where(c => c.CaregiverId == caregiverId)
                .ToListAsync();
            if (certifications.Any())
            {
                dbContext.Certifications.RemoveRange(certifications);
                _logger.LogInformation("UserHardDeleteProcessor: Caregiver {CaregiverId} - Deleted {Count} certifications", caregiverId, certifications.Count);
            }

            // ── 5. Hard-delete: Verifications (KYC data) ──
            var verifications = await dbContext.Verifications
                .Where(v => v.UserId == caregiverId)
                .ToListAsync();
            if (verifications.Any())
            {
                dbContext.Verifications.RemoveRange(verifications);
                _logger.LogInformation("UserHardDeleteProcessor: Caregiver {CaregiverId} - Deleted {Count} verification records", caregiverId, verifications.Count);
            }

            // ── 6. Hard-delete: AssessmentSessions ──
            var assessmentSessions = await dbContext.AssessmentSessions
                .Where(a => a.CaregiverId == caregiverId)
                .ToListAsync();
            if (assessmentSessions.Any())
            {
                dbContext.AssessmentSessions.RemoveRange(assessmentSessions);
                _logger.LogInformation("UserHardDeleteProcessor: Caregiver {CaregiverId} - Deleted {Count} assessment sessions", caregiverId, assessmentSessions.Count);
            }

            // ── 7. Hard-delete: Profile sub-documents ──
            var educations = await dbContext.CaregiverEducations.Where(e => e.CaregiverId == caregiverId).ToListAsync();
            var qualifications = await dbContext.CaregiverQualifications.Where(q => q.CaregiverId == caregiverId).ToListAsync();
            var workExperiences = await dbContext.CaregiverWorkExperiences.Where(w => w.CaregiverId == caregiverId).ToListAsync();
            if (educations.Any()) dbContext.CaregiverEducations.RemoveRange(educations);
            if (qualifications.Any()) dbContext.CaregiverQualifications.RemoveRange(qualifications);
            if (workExperiences.Any()) dbContext.CaregiverWorkExperiences.RemoveRange(workExperiences);

            // ── 8. Anonymize: Caregiver profile PII ──
            caregiver.FirstName = RedactedName;
            caregiver.MiddleName = null;
            caregiver.LastName = RedactedName;
            caregiver.Email = RedactedEmail;
            caregiver.PhoneNo = null;
            caregiver.Password = RedactedMarker;
            caregiver.ProfileImage = null;
            caregiver.HomeAddress = null;
            caregiver.Location = null;
            caregiver.AboutMe = null;
            caregiver.IntroVideo = null;
            caregiver.ServiceAddress = null;
            caregiver.ServiceCity = null;
            caregiver.ServiceState = null;
            caregiver.Latitude = null;
            caregiver.Longitude = null;
            caregiver.GoogleId = null;
            caregiver.AuthProvider = null;
            caregiver.ReasonForDeactivation = null;
            dbContext.CareGivers.Update(caregiver);

            // ── 9. Anonymize: AppUser ──
            var appUser = await dbContext.AppUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.AppUserId == caregiver.Id);
            if (appUser != null)
            {
                appUser.Email = RedactedEmail;
                appUser.FirstName = RedactedName;
                appUser.LastName = RedactedName;
                appUser.Password = RedactedMarker;
                appUser.GoogleId = null;
                appUser.AuthProvider = null;
                appUser.ProfilePicture = null;
                appUser.ConnectionId = null;
                appUser.DeviceIp = null;
                dbContext.AppUsers.Update(appUser);
            }

            // ── RETAINED: BillingRecords, Earnings, EarningsLedger, ClientOrders, IncidentReports, Disputes ──

            await dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "UserHardDeleteProcessor: Caregiver {CaregiverId} GDPR anonymisation complete.",
                caregiverId);
        }

        private async Task ProcessSingleClientAsync(
            CareProDbContext dbContext, IEmailService emailService, Domain.Entities.Client client)
        {
            var clientId = client.Id.ToString();
            var capturedEmail = client.Email;
            var capturedName = client.FirstName;

            _logger.LogInformation(
                "UserHardDeleteProcessor: Anonymising client {ClientId} (deleted on {DeletedOn})",
                clientId, client.DeletedOn);

            // ── Send final notification email before wiping data ──
            try
            {
                await emailService.SendGenericNotificationEmailAsync(
                    capturedEmail, capturedName,
                    "Your CarePro Account Has Been Permanently Deleted",
                    "As requested, your CarePro account and personal data have now been permanently deleted. " +
                    "Financial and legally required records are retained as mandated by law. " +
                    "If you believe this was an error, please contact support@oncarepro.com.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UserHardDeleteProcessor: Failed to send final deletion email to client {ClientId}", clientId);
            }

            // ── 1. Hard-delete: Notifications ──
            var notifications = await dbContext.Notifications
                .Where(n => n.RecipientId == clientId || n.SenderId == clientId)
                .ToListAsync();
            if (notifications.Any())
            {
                dbContext.Notifications.RemoveRange(notifications);
                _logger.LogInformation("UserHardDeleteProcessor: Client {ClientId} - Deleted {Count} notifications", clientId, notifications.Count);
            }

            // ── 2. Hard-delete: RefreshTokens ──
            var refreshTokens = await dbContext.RefreshTokens
                .Where(t => t.UserId == clientId)
                .ToListAsync();
            if (refreshTokens.Any())
            {
                dbContext.RefreshTokens.RemoveRange(refreshTokens);
                _logger.LogInformation("UserHardDeleteProcessor: Client {ClientId} - Deleted {Count} refresh tokens", clientId, refreshTokens.Count);
            }

            // ── 3. Hard-delete: Location ──
            var locations = await dbContext.Locations
                .Where(l => l.UserId == clientId)
                .ToListAsync();
            if (locations.Any())
            {
                dbContext.Locations.RemoveRange(locations);
                _logger.LogInformation("UserHardDeleteProcessor: Client {ClientId} - Deleted {Count} location record(s)", clientId, locations.Count);
            }

            // ── 4. Hard-delete: Verifications (KYC data) ──
            var verifications = await dbContext.Verifications
                .Where(v => v.UserId == clientId)
                .ToListAsync();
            if (verifications.Any())
            {
                dbContext.Verifications.RemoveRange(verifications);
                _logger.LogInformation("UserHardDeleteProcessor: Client {ClientId} - Deleted {Count} verification records", clientId, verifications.Count);
            }

            // ── 5. Hard-delete: Client preferences ──
            var preferences = await dbContext.ClientPreferences
                .Where(p => p.ClientId == clientId)
                .ToListAsync();
            if (preferences.Any())
            {
                dbContext.ClientPreferences.RemoveRange(preferences);
                _logger.LogInformation("UserHardDeleteProcessor: Client {ClientId} - Deleted {Count} preferences", clientId, preferences.Count);
            }

            // ── 6. Anonymize: Client profile PII ──
            client.FirstName = RedactedName;
            client.MiddleName = null;
            client.LastName = RedactedName;
            client.Email = RedactedEmail;
            client.PhoneNo = null;
            client.Password = RedactedMarker;
            client.ProfileImage = null;
            client.HomeAddress = null;
            client.Address = null;
            client.PreferredCity = null;
            client.PreferredState = null;
            client.Latitude = null;
            client.Longitude = null;
            client.GoogleId = null;
            client.AuthProvider = null;
            dbContext.Clients.Update(client);

            // ── 7. Anonymize: AppUser ──
            var appUser = await dbContext.AppUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.AppUserId == client.Id);
            if (appUser != null)
            {
                appUser.Email = RedactedEmail;
                appUser.FirstName = RedactedName;
                appUser.LastName = RedactedName;
                appUser.Password = RedactedMarker;
                appUser.GoogleId = null;
                appUser.AuthProvider = null;
                appUser.ProfilePicture = null;
                appUser.ConnectionId = null;
                appUser.DeviceIp = null;
                dbContext.AppUsers.Update(appUser);
            }

            // ── RETAINED: BillingRecords, ClientOrders, Earnings, Disputes, IncidentReports ──

            await dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "UserHardDeleteProcessor: Client {ClientId} GDPR anonymisation complete.",
                clientId);
        }
    }
}
