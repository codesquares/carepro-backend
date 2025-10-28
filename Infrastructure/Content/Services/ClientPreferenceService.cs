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
            var clientUser = await clientService.GetClientUserAsync(addClientPreferenceRequest.ClientId);
            if (clientUser == null)
            {
                throw new KeyNotFoundException("The Client ID entered is not a Valid ID");
            }



            /// CONVERT DTO TO DOMAIN OBJECT            
            var clientPreference = new ClientPreference
            {
                Data = addClientPreferenceRequest.Data,
                ClientId = addClientPreferenceRequest.ClientId,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                CreatedAt = DateTime.Now,
            };

            await careProDbContext.ClientPreferences.AddAsync(clientPreference);

            await careProDbContext.SaveChangesAsync();

            return clientPreference.Id.ToString();
        }

        public async Task<ClientPreferenceDTO> GetClientPreferenceAsync(string clientId)
        {
            var clientPreference = await careProDbContext.ClientPreferences.FirstOrDefaultAsync(x => x.ClientId.ToString() == clientId);

            if (clientPreference == null)
            {
                throw new KeyNotFoundException($"User with ID '{clientId}' preferences not found.");
            }


            var clientPreferenceDTO = new ClientPreferenceDTO()
            {
                Id = clientPreference.Id.ToString(),
                ClientId = clientPreference.ClientId,
                Data = clientPreference.Data,
                CreatedAt = clientPreference.CreatedAt,
                UpdatedOn = clientPreference.UpdatedOn,

            };

            return clientPreferenceDTO;
        }

        public async Task<string> UpdateClientPreferenceAsync(string preferenceId, UpdateClientPreferenceRequest updateClientPreferenceRequest)
        {
            if (!ObjectId.TryParse(preferenceId, out var objectId))
            {
                throw new ArgumentException("Invalid Preference ID format.");
            }

            var existingClientPreference = await careProDbContext.ClientPreferences.FindAsync(objectId);
            if (existingClientPreference == null)
            {
                throw new KeyNotFoundException($"Verification with ID '{preferenceId}' not found.");
            }
            existingClientPreference.Data = updateClientPreferenceRequest.Data;
            existingClientPreference.UpdatedOn = DateTime.Now;

            careProDbContext.ClientPreferences.Update(existingClientPreference);
            await careProDbContext.SaveChangesAsync();

            return $"Client Preference with ID '{preferenceId}' Updated successfully.";
        }
    }
}
