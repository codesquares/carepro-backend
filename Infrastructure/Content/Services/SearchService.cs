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
            throw new NotImplementedException();
        }

        //public async Task<IEnumerable<CaregiverResponse>> SearchCaregiversWithServicesAsync(string searchTerm)
        //{
        //    if (string.IsNullOrWhiteSpace(searchTerm))
        //        return Enumerable.Empty<CaregiverResponse>();

        //    searchTerm = searchTerm.Trim().ToLower();

        //    // Get all caregivers who are not deleted and active
        //    var caregivers = await careProDbContext.CareGivers
        //        .Where(c => c.Status == true && c.IsDeleted == false)
        //        .ToListAsync();

        //    var matchedCaregivers = new List<CaregiverResponse>();

        //    foreach (var caregiver in caregivers)
        //    {
        //        // Get caregiver's gigs
        //        var gigs = await careProDbContext.Gigs
        //            .Where(g => (g.Status == "Published" || g.Status == "Active") && g.CaregiverId == caregiver.Id.ToString())
        //            .ToListAsync();

        //        // Get all searchable strings for the caregiver and their services
        //        var subCategories = gigs
        //            .Where(g => !string.IsNullOrEmpty(g.SubCategory))
        //            .SelectMany(g => g.SubCategory.Split(',', StringSplitOptions.RemoveEmptyEntries))
        //            .Select(sc => sc.Trim())
        //            .ToList();

        //        var searchableStrings = new List<string>
        //{
        //    caregiver.FirstName?.ToLower() ?? "",
        //    caregiver.LastName?.ToLower() ?? ""
        //};

        //        searchableStrings.AddRange(gigs.Select(g => g.Title?.ToLower() ?? ""));
        //        searchableStrings.AddRange(gigs.Select(g => g.Category?.ToLower() ?? ""));
        //        searchableStrings.AddRange(subCategories.Select(sc => sc.ToLower()));

        //        // Check if any field contains the search term
        //        bool matches = searchableStrings.Any(s => s.Contains(searchTerm));

        //        if (!matches)
        //            continue;

        //        // Calculate total earnings
        //        var clientOrders = await careProDbContext.ClientOrders
        //            .Where(o => o.CaregiverId == caregiver.Id.ToString())
        //            .ToListAsync();

        //        decimal totalEarnings = clientOrders.Sum(o => o.Amount);
        //        int noOfOrders = clientOrders.Count;

        //        var caregiverDTO = new CaregiverResponse
        //        {
        //            Id = caregiver.Id.ToString(),
        //            FirstName = caregiver.FirstName,
        //            MiddleName = caregiver.MiddleName,
        //            LastName = caregiver.LastName,
        //            Email = caregiver.Email,
        //            PhoneNo = caregiver.PhoneNo,
        //            Role = caregiver.Role,
        //            IsDeleted = caregiver.IsDeleted,
        //            Status = caregiver.Status,
        //            HomeAddress = caregiver.HomeAddress,
        //            AboutMe = caregiver.AboutMe,
        //            AboutMeIntro = string.IsNullOrWhiteSpace(caregiver.AboutMe)
        //                ? null
        //                : caregiver.AboutMe.Length <= 150
        //                    ? caregiver.AboutMe
        //                    : caregiver.AboutMe.Substring(0, 150) + "...",
        //            Location = caregiver.Location,
        //            ReasonForDeactivation = caregiver.ReasonForDeactivation,
        //            IsAvailable = caregiver.IsAvailable,
        //            IntroVideo = caregiver.IntroVideo,
        //            Services = subCategories.Distinct().ToList(),
        //            TotalEarning = totalEarnings,
        //            NoOfOrders = noOfOrders,
        //            CreatedAt = caregiver.CreatedAt
        //        };

        //        matchedCaregivers.Add(caregiverDTO);
        //    }

        //    // Sort by caregiver full name
        //    return matchedCaregivers
        //        .OrderBy(c => (c.FirstName + " " + c.LastName).ToLower())
        //        .ToList();
        //}


        public async Task<IEnumerable<CaregiverResponse>> SearchCaregiversWithServicesAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<CaregiverResponse>();

            searchTerm = searchTerm.Trim().ToLower();

            // Fetch caregivers in bulk
            var caregivers = await careProDbContext.CareGivers
                .Where(c => c.Status == true && c.IsDeleted == false)
                .ToListAsync();

            var caregiverIds = caregivers.Select(c => c.Id.ToString()).ToList();

            // Fetch gigs in bulk for those caregivers
            var gigs = await careProDbContext.Gigs
                .Where(g => (g.Status == "Published" || g.Status == "Active") && caregiverIds.Contains(g.CaregiverId))
                .ToListAsync();

            // Fetch client orders in bulk for those caregivers
            var orders = await careProDbContext.ClientOrders
                .Where(o => caregiverIds.Contains(o.CaregiverId))
                .ToListAsync();

            // Group gigs and orders by caregiver ID
            var gigsGrouped = gigs.GroupBy(g => g.CaregiverId).ToDictionary(g => g.Key, g => g.ToList());
            var ordersGrouped = orders.GroupBy(o => o.CaregiverId).ToDictionary(o => o.Key, o => o.ToList());

            var matchedCaregivers = new List<CaregiverResponse>();

            foreach (var caregiver in caregivers)
            {
                var caregiverId = caregiver.Id.ToString();
                gigsGrouped.TryGetValue(caregiverId, out var caregiverGigs);
                ordersGrouped.TryGetValue(caregiverId, out var caregiverOrders);

                caregiverGigs ??= new List<Gig>();
                caregiverOrders ??= new List<ClientOrder>();

                var subCategories = caregiverGigs
                    .Where(g => !string.IsNullOrEmpty(g.SubCategory))
                    .SelectMany(g => g.SubCategory.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(sc => sc.Trim())
                    .ToList();

                var searchableFields = new List<string>
        {
            caregiver.FirstName?.ToLower() ?? "",
            caregiver.LastName?.ToLower() ?? ""
        };

                searchableFields.AddRange(caregiverGigs.Select(g => g.Title?.ToLower() ?? ""));
                searchableFields.AddRange(caregiverGigs.Select(g => g.Category?.ToLower() ?? ""));
                searchableFields.AddRange(subCategories.Select(sc => sc.ToLower()));

                bool matches = searchableFields.Any(field => field.Contains(searchTerm));
                if (!matches)
                    continue;

                var caregiverDTO = new CaregiverResponse
                {
                    Id = caregiver.Id.ToString(),
                    FirstName = caregiver.FirstName,
                    MiddleName = caregiver.MiddleName,
                    LastName = caregiver.LastName,
                    Email = caregiver.Email,
                    PhoneNo = caregiver.PhoneNo,
                    Role = caregiver.Role,
                    IsDeleted = caregiver.IsDeleted,
                    Status = caregiver.Status,
                    HomeAddress = caregiver.HomeAddress,
                    AboutMe = caregiver.AboutMe,
                    AboutMeIntro = string.IsNullOrWhiteSpace(caregiver.AboutMe)
                        ? null
                        : caregiver.AboutMe.Length <= 150
                            ? caregiver.AboutMe
                            : caregiver.AboutMe.Substring(0, 150) + "...",
                    Location = caregiver.Location,
                    ReasonForDeactivation = caregiver.ReasonForDeactivation,
                    IsAvailable = caregiver.IsAvailable,
                    IntroVideo = caregiver.IntroVideo,
                    Services = subCategories.Distinct().ToList(),
                    TotalEarning = caregiverOrders.Sum(o => o.Amount),
                    NoOfOrders = caregiverOrders.Count,
                    CreatedAt = caregiver.CreatedAt
                };

                matchedCaregivers.Add(caregiverDTO);
            }

            return matchedCaregivers
                .OrderBy(c => (c.FirstName + " " + c.LastName).ToLower())
                .ToList();
        }


    }
}
