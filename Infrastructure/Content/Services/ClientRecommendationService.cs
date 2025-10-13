using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ClientRecommendationService : IClientRecommendationService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly IClientService clientService;
        private readonly ILogger<ClientRecommendationService> logger;

        public ClientRecommendationService(CareProDbContext careProDbContext, IClientService clientService, ILogger<ClientRecommendationService> logger)
        {
            this.careProDbContext = careProDbContext;
            this.clientService = clientService;
            this.logger = logger;
        }

        public Task<ClientRecommendationDTO> GetClientRecommendationAsync(string clientId)
        {
            throw new NotImplementedException();
        }
    }
}
