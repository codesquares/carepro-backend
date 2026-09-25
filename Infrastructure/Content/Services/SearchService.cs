using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class SearchService : ISearchService
    {
        private readonly CareProDbContext careProDbContext;

        public SearchService(CareProDbContext careProDbContext)
        {
            this.careProDbContext = careProDbContext;
        }

        public Task<List<string>> GetCaregiverAndServicesAsync(string? firstName, string? lastName, string? serviceName)
        {
            return Task.FromResult(new List<string>());
        }

    }
}
