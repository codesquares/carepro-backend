using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IClientRecommendationService
    {
        Task<ClientRecommendationDTO> GetClientRecommendationAsync(string clientId);
        
        Task<ClientRecommendationResponse> CreateClientRecommendationAsync(string clientId, CreateClientRecommendationRequest request);
        
        Task<ClientRecommendationResponse> UpdateClientRecommendationAsync(string clientId, UpdateClientRecommendationRequest request);
        
        Task<bool> DeleteClientRecommendationAsync(string recommendationId);
    }
}
