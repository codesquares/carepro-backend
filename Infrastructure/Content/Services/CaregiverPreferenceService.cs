using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class CaregiverPreferenceService : ICaregiverPreferenceService
    {
        private readonly CareProDbContext _dbContext;

        public CaregiverPreferenceService(CareProDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<CaregiverNotificationPreferencesDTO> GetNotificationPreferencesAsync(string caregiverId)
        {
            var caregiverPreference = await _dbContext.CaregiverPreferences
                .FirstOrDefaultAsync(x => x.CaregiverId == caregiverId);

            if (caregiverPreference?.NotificationPreferences == null)
            {
                return new CaregiverNotificationPreferencesDTO
                {
                    EmailNotifications = true,
                    SmsNotifications = true,
                    MarketingEmails = false,
                    Promotions = false,
                    NewGig = false,
                    CareRequestUpdates = false
                };
            }

            return new CaregiverNotificationPreferencesDTO
            {
                EmailNotifications = caregiverPreference.NotificationPreferences.EmailNotifications,
                SmsNotifications = caregiverPreference.NotificationPreferences.SmsNotifications,
                MarketingEmails = caregiverPreference.NotificationPreferences.MarketingEmails,
                Promotions = caregiverPreference.NotificationPreferences.Promotions,
                NewGig = caregiverPreference.NotificationPreferences.NewGig,
                CareRequestUpdates = caregiverPreference.NotificationPreferences.CareRequestUpdates
            };
        }

        public async Task<CaregiverNotificationPreferencesDTO> UpdateNotificationPreferencesAsync(
            string caregiverId,
            UpdateCaregiverNotificationPreferencesRequest updateRequest)
        {
            if (!ObjectId.TryParse(caregiverId, out var caregiverObjectId))
            {
                throw new KeyNotFoundException("The Caregiver ID entered is not a Valid ID");
            }

            var caregiverExists = await _dbContext.CareGivers
                .AnyAsync(c => c.Id == caregiverObjectId);

            if (!caregiverExists)
            {
                throw new KeyNotFoundException("The Caregiver ID entered is not a Valid ID");
            }

            var caregiverPreference = await _dbContext.CaregiverPreferences
                .FirstOrDefaultAsync(x => x.CaregiverId == caregiverId);

            if (caregiverPreference == null)
            {
                caregiverPreference = new CaregiverPreference
                {
                    Id = ObjectId.GenerateNewId(),
                    CaregiverId = caregiverId,
                    Data = new List<string>(),
                    NotificationPreferences = new CaregiverNotificationPreferences
                    {
                        EmailNotifications = updateRequest.EmailNotifications,
                        SmsNotifications = updateRequest.SmsNotifications,
                        MarketingEmails = updateRequest.MarketingEmails,
                        Promotions = updateRequest.Promotions,
                        NewGig = updateRequest.NewGig,
                        CareRequestUpdates = updateRequest.CareRequestUpdates
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedOn = DateTime.UtcNow
                };

                await _dbContext.CaregiverPreferences.AddAsync(caregiverPreference);
            }
            else
            {
                caregiverPreference.NotificationPreferences ??= new CaregiverNotificationPreferences();
                caregiverPreference.NotificationPreferences.EmailNotifications = updateRequest.EmailNotifications;
                caregiverPreference.NotificationPreferences.SmsNotifications = updateRequest.SmsNotifications;
                caregiverPreference.NotificationPreferences.MarketingEmails = updateRequest.MarketingEmails;
                caregiverPreference.NotificationPreferences.Promotions = updateRequest.Promotions;
                caregiverPreference.NotificationPreferences.NewGig = updateRequest.NewGig;
                caregiverPreference.NotificationPreferences.CareRequestUpdates = updateRequest.CareRequestUpdates;
                caregiverPreference.UpdatedOn = DateTime.UtcNow;

                _dbContext.CaregiverPreferences.Update(caregiverPreference);
            }

            await _dbContext.SaveChangesAsync();

            return new CaregiverNotificationPreferencesDTO
            {
                EmailNotifications = caregiverPreference.NotificationPreferences.EmailNotifications,
                SmsNotifications = caregiverPreference.NotificationPreferences.SmsNotifications,
                MarketingEmails = caregiverPreference.NotificationPreferences.MarketingEmails,
                Promotions = caregiverPreference.NotificationPreferences.Promotions,
                NewGig = caregiverPreference.NotificationPreferences.NewGig,
                CareRequestUpdates = caregiverPreference.NotificationPreferences.CareRequestUpdates
            };
        }

        public async Task UpsertFromSignupConsentAsync(string caregiverId, bool marketingConsent)
        {
            var caregiverPreference = await _dbContext.CaregiverPreferences
                .FirstOrDefaultAsync(x => x.CaregiverId == caregiverId);

            if (caregiverPreference == null)
            {
                caregiverPreference = new CaregiverPreference
                {
                    Id = ObjectId.GenerateNewId(),
                    CaregiverId = caregiverId,
                    Data = new List<string>(),
                    NotificationPreferences = new CaregiverNotificationPreferences
                    {
                        MarketingEmails = marketingConsent,
                        Promotions = marketingConsent,
                        NewGig = marketingConsent,
                        CareRequestUpdates = marketingConsent
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedOn = DateTime.UtcNow
                };

                await _dbContext.CaregiverPreferences.AddAsync(caregiverPreference);
            }
            else
            {
                caregiverPreference.NotificationPreferences ??= new CaregiverNotificationPreferences();
                caregiverPreference.NotificationPreferences.MarketingEmails = marketingConsent;
                caregiverPreference.NotificationPreferences.Promotions = marketingConsent;
                caregiverPreference.NotificationPreferences.NewGig = marketingConsent;
                caregiverPreference.NotificationPreferences.CareRequestUpdates = marketingConsent;
                caregiverPreference.UpdatedOn = DateTime.UtcNow;

                _dbContext.CaregiverPreferences.Update(caregiverPreference);
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
