using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IClientPreferenceService
    {
        Task<string> CreateClientPreferenceAsync(AddClientPreferenceRequest addClientPreferenceRequest );

        Task<ClientPreferenceDTO> GetClientPreferenceAsync(string clientId);

        Task<string> UpdateClientPreferenceAsync(string preferenceId, UpdateClientPreferenceRequest updateClientPreferenceRequest );
    }
}
