using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ClientPreferenceService : IClientPreferenceService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly IClientService clientService;
        private readonly ILogger<ClientPreferenceService> logger;

        public ClientPreferenceService(CareProDbContext careProDbContext, IClientService clientService, ILogger<ClientPreferenceService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.clientService = clientService;
            this.logger = logger;
        }



        public async Task<string> CreateClientPreferenceAsync(AddClientPreferenceRequest addClientPreferenceRequest)
        {
            logger.LogInformation($"CreateClientPreferenceAsync called for ClientId: {addClientPreferenceRequest.ClientId}");
            logger.LogInformation($"Received data count: {addClientPreferenceRequest.Data?.Count ?? 0}");
            logger.LogInformation($"Received data: {System.Text.Json.JsonSerializer.Serialize(addClientPreferenceRequest.Data)}");

            var clientUser = await clientService.GetClientUserAsync(addClientPreferenceRequest.ClientId ?? string.Empty);
            if (clientUser == null)
            {
                logger.LogError($"Client with ID '{addClientPreferenceRequest.ClientId}' not found");
                throw new KeyNotFoundException("The Client ID entered is not a Valid ID");
            }

            logger.LogInformation($"Client validation successful for ClientId: {addClientPreferenceRequest.ClientId}");

            // Check if preferences already exist for this client (UPSERT logic)
            var existingPreference = await careProDbContext.ClientPreferences
                .FirstOrDefaultAsync(x => x.ClientId == addClientPreferenceRequest.ClientId);

            if (existingPreference != null)
            {
                // UPDATE existing preference instead of creating a new one
                logger.LogInformation($"Existing preference found with Id: {existingPreference.Id}, updating instead of creating new");
                
                existingPreference.Data = addClientPreferenceRequest.Data ?? new List<string>();
                existingPreference.UpdatedOn = DateTime.Now;

                logger.LogInformation($"Updated entity data count: {existingPreference.Data.Count}");
                logger.LogInformation($"Updated entity data: {System.Text.Json.JsonSerializer.Serialize(existingPreference.Data)}");

                careProDbContext.ClientPreferences.Update(existingPreference);
                await careProDbContext.SaveChangesAsync();
                logger.LogInformation($"SaveChangesAsync completed successfully for updated entity Id: {existingPreference.Id}");

                return existingPreference.Id.ToString();
            }

            /// CONVERT DTO TO DOMAIN OBJECT - CREATE NEW (only if no existing preference)          
            var clientPreference = new ClientPreference
            {
                Data = addClientPreferenceRequest.Data ?? new List<string>(),
                ClientId = addClientPreferenceRequest.ClientId ?? string.Empty,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                CreatedAt = DateTime.Now,
            };

            logger.LogInformation($"Created entity with Id: {clientPreference.Id}");
            logger.LogInformation($"Entity data count: {clientPreference.Data.Count}");
            logger.LogInformation($"Entity data: {System.Text.Json.JsonSerializer.Serialize(clientPreference.Data)}");

            await careProDbContext.ClientPreferences.AddAsync(clientPreference);
            logger.LogInformation("Entity added to context, calling SaveChangesAsync");

            await careProDbContext.SaveChangesAsync();
            logger.LogInformation($"SaveChangesAsync completed successfully for entity Id: {clientPreference.Id}");

            return clientPreference.Id.ToString();
        }

        public async Task<ClientPreferenceDTO> GetClientPreferenceAsync(string clientId)
        {
            logger.LogInformation($"GetClientPreferenceAsync called for ClientId: {clientId}");

            // Get the most recent preference record for this client
            // OrderByDescending ensures we get the latest if multiple records exist
            var clientPreference = await careProDbContext.ClientPreferences
                .Where(x => x.ClientId == clientId)
                .OrderByDescending(x => x.UpdatedOn ?? x.CreatedAt)
                .FirstOrDefaultAsync();

            logger.LogInformation($"Database query completed for ClientId: {clientId}");

            if (clientPreference == null)
            {
                logger.LogWarning($"No preferences found for ClientId: {clientId}");
                throw new KeyNotFoundException($"User with ID '{clientId}' preferences not found.");
            }

            logger.LogInformation($"Found preference record with Id: {clientPreference.Id}");
            logger.LogInformation($"Retrieved data count: {clientPreference.Data?.Count ?? 0}");
            logger.LogInformation($"Retrieved data: {System.Text.Json.JsonSerializer.Serialize(clientPreference.Data)}");

            var clientPreferenceDTO = new ClientPreferenceDTO()
            {
                Id = clientPreference.Id.ToString(),
                ClientId = clientPreference.ClientId,
                Data = clientPreference.Data,
                NotificationPreferences = clientPreference.NotificationPreferences != null ? new NotificationPreferencesDTO
                {
                    EmailNotifications = clientPreference.NotificationPreferences.EmailNotifications,
                    SmsNotifications = clientPreference.NotificationPreferences.SmsNotifications,
                    MarketingEmails = clientPreference.NotificationPreferences.MarketingEmails,
                    OrderUpdates = clientPreference.NotificationPreferences.OrderUpdates,
                    ServiceUpdates = clientPreference.NotificationPreferences.ServiceUpdates,
                    Promotions = clientPreference.NotificationPreferences.Promotions
                } : null,
                CreatedAt = clientPreference.CreatedAt,
                UpdatedOn = clientPreference.UpdatedOn,
            };

            logger.LogInformation($"DTO created with data count: {clientPreferenceDTO.Data?.Count ?? 0}");
            logger.LogInformation($"DTO data: {System.Text.Json.JsonSerializer.Serialize(clientPreferenceDTO.Data)}");

            return clientPreferenceDTO;
        }

        public async Task<string> UpdateClientPreferenceAsync(string preferenceId, UpdateClientPreferenceRequest updateClientPreferenceRequest)
        {
            logger.LogInformation($"UpdateClientPreferenceAsync called for preferenceId: {preferenceId}");
            logger.LogInformation($"Update data count: {updateClientPreferenceRequest.Data?.Count ?? 0}");
            logger.LogInformation($"Update data: {System.Text.Json.JsonSerializer.Serialize(updateClientPreferenceRequest.Data)}");

            if (!ObjectId.TryParse(preferenceId, out var objectId))
            {
                logger.LogError($"Invalid preference ID format: {preferenceId}");
                throw new ArgumentException("Invalid Preference ID format.");
            }

            var existingClientPreference = await careProDbContext.ClientPreferences.FindAsync(objectId);
            if (existingClientPreference == null)
            {
                logger.LogError($"Preference with ID '{preferenceId}' not found");
                throw new KeyNotFoundException($"Preference with ID '{preferenceId}' not found.");
            }

            logger.LogInformation($"Found existing preference with data count: {existingClientPreference.Data?.Count ?? 0}");

            existingClientPreference.Data = updateClientPreferenceRequest.Data ?? new List<string>();
            existingClientPreference.UpdatedOn = DateTime.Now;

            logger.LogInformation($"Updated preference data count: {existingClientPreference.Data.Count}");
            logger.LogInformation($"Updated preference data: {System.Text.Json.JsonSerializer.Serialize(existingClientPreference.Data)}");

            careProDbContext.ClientPreferences.Update(existingClientPreference);
            await careProDbContext.SaveChangesAsync();

            logger.LogInformation($"Client Preference with ID '{preferenceId}' updated successfully");

            return $"Client Preference with ID '{preferenceId}' Updated successfully.";
        }

        public async Task<NotificationPreferencesDTO> GetNotificationPreferencesAsync(string clientId)
        {
            var clientPreference = await careProDbContext.ClientPreferences
                .FirstOrDefaultAsync(x => x.ClientId == clientId);

            if (clientPreference?.NotificationPreferences == null)
            {
                // Return default preferences if none exist
                return new NotificationPreferencesDTO
                {
                    EmailNotifications = true,
                    SmsNotifications = true,
                    MarketingEmails = false,
                    OrderUpdates = true,
                    ServiceUpdates = true,
                    Promotions = false
                };
            }

            return new NotificationPreferencesDTO
            {
                EmailNotifications = clientPreference.NotificationPreferences.EmailNotifications,
                SmsNotifications = clientPreference.NotificationPreferences.SmsNotifications,
                MarketingEmails = clientPreference.NotificationPreferences.MarketingEmails,
                OrderUpdates = clientPreference.NotificationPreferences.OrderUpdates,
                ServiceUpdates = clientPreference.NotificationPreferences.ServiceUpdates,
                Promotions = clientPreference.NotificationPreferences.Promotions
            };
        }

        public async Task<NotificationPreferencesDTO> UpdateNotificationPreferencesAsync(string clientId, UpdateNotificationPreferencesRequest updateRequest)
        {
            // Validate client exists
            var clientUser = await clientService.GetClientUserAsync(clientId);
            if (clientUser == null)
            {
                throw new KeyNotFoundException("The Client ID entered is not a Valid ID");
            }

            var clientPreference = await careProDbContext.ClientPreferences
                .FirstOrDefaultAsync(x => x.ClientId == clientId);

            if (clientPreference == null)
            {
                // Create new client preference with notification preferences
                clientPreference = new ClientPreference
                {
                    Id = ObjectId.GenerateNewId(),
                    ClientId = clientId,
                    Data = new List<string>(),
                    NotificationPreferences = new Domain.Entities.NotificationPreferences
                    {
                        EmailNotifications = updateRequest.EmailNotifications,
                        SmsNotifications = updateRequest.SmsNotifications,
                        MarketingEmails = updateRequest.MarketingEmails,
                        OrderUpdates = updateRequest.OrderUpdates,
                        ServiceUpdates = updateRequest.ServiceUpdates,
                        Promotions = updateRequest.Promotions
                    },
                    CreatedAt = DateTime.Now
                };

                await careProDbContext.ClientPreferences.AddAsync(clientPreference);
            }
            else
            {
                // Update existing preferences
                if (clientPreference.NotificationPreferences == null)
                {
                    clientPreference.NotificationPreferences = new Domain.Entities.NotificationPreferences();
                }

                clientPreference.NotificationPreferences.EmailNotifications = updateRequest.EmailNotifications;
                clientPreference.NotificationPreferences.SmsNotifications = updateRequest.SmsNotifications;
                clientPreference.NotificationPreferences.MarketingEmails = updateRequest.MarketingEmails;
                clientPreference.NotificationPreferences.OrderUpdates = updateRequest.OrderUpdates;
                clientPreference.NotificationPreferences.ServiceUpdates = updateRequest.ServiceUpdates;
                clientPreference.NotificationPreferences.Promotions = updateRequest.Promotions;
                clientPreference.UpdatedOn = DateTime.Now;

                careProDbContext.ClientPreferences.Update(clientPreference);
            }

            await careProDbContext.SaveChangesAsync();

            return new NotificationPreferencesDTO
            {
                EmailNotifications = clientPreference.NotificationPreferences.EmailNotifications,
                SmsNotifications = clientPreference.NotificationPreferences.SmsNotifications,
                MarketingEmails = clientPreference.NotificationPreferences.MarketingEmails,
                OrderUpdates = clientPreference.NotificationPreferences.OrderUpdates,
                ServiceUpdates = clientPreference.NotificationPreferences.ServiceUpdates,
                Promotions = clientPreference.NotificationPreferences.Promotions
            };
        }
    }
}
