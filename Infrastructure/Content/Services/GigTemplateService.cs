using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    public class GigTemplateService : IGigTemplateService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ILogger<GigTemplateService> _logger;

        public GigTemplateService(CareProDbContext dbContext, ILogger<GigTemplateService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<GigTemplateResponseDTO> GetAllTemplatesAsync()
        {
            try
            {
                var categories = await _dbContext.GigTemplateCategories
                    .OrderBy(c => c.SortOrder)
                    .ToListAsync();

                return MapToResponse(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving gig templates");
                throw;
            }
        }

        public async Task<GigTemplateResponseDTO> GetTemplatesByCategoryAsync(string category)
        {
            try
            {
                var categories = await _dbContext.GigTemplateCategories
                    .Where(c => c.Name == category)
                    .OrderBy(c => c.SortOrder)
                    .ToListAsync();

                return MapToResponse(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving gig templates for category {Category}", category);
                throw;
            }
        }

        public async Task SeedTemplatesAsync(List<GigTemplateCategory> categories)
        {
            try
            {
                var existingCount = await _dbContext.GigTemplateCategories.CountAsync();
                if (existingCount > 0)
                {
                    _logger.LogInformation("Gig templates already seeded ({Count} categories). Skipping.", existingCount);
                    return;
                }

                await _dbContext.GigTemplateCategories.AddRangeAsync(categories);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Seeded {Count} gig template categories", categories.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding gig templates");
                throw;
            }
        }

        private static GigTemplateResponseDTO MapToResponse(List<GigTemplateCategory> categories)
        {
            return new GigTemplateResponseDTO
            {
                Categories = categories.Select(c => new GigTemplateCategoryDTO
                {
                    Name = c.Name,
                    CategoryTags = c.CategoryTags,
                    Subcategories = c.Subcategories.Select(s => new GigTemplateSubcategoryDTO
                    {
                        Name = s.Name,
                        Description = s.Description,
                        Tags = s.Tags,
                        SampleTasks = s.SampleTasks.Select(t => new GigTemplateSampleTaskDTO
                        {
                            Id = t.Id,
                            Text = t.Text
                        }).ToList()
                    }).ToList()
                }).ToList()
            };
        }
    }
}
