using Application.Interfaces.Common;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.Common
{
    public class OriginValidationService : IOriginValidationService
    {
        private readonly IConfiguration configuration;

        public OriginValidationService(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public bool IsFrontendOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return false;

            // Get allowed frontend origins from configuration with fallback defaults
            var allowedOrigins = configuration.GetSection("AllowedFrontendOrigins").Get<string[]>() 
                ?? new[] { 
                    "localhost:5173", 
                    "localhost:5174", 
                    "onrender.com", 
                    "oncarepro.com", 
                    "awsapprunner.com", 
                    "amazonaws.com" 
                };

            return allowedOrigins.Any(allowedOrigin => 
                origin.Contains(allowedOrigin, StringComparison.OrdinalIgnoreCase));
        }
    }
}