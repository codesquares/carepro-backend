using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IGigTemplateService
    {
        Task<GigTemplateResponseDTO> GetAllTemplatesAsync();
        Task<GigTemplateResponseDTO> GetTemplatesByCategoryAsync(string category);
        Task SeedTemplatesAsync(List<Domain.Entities.GigTemplateCategory> categories);
    }
}
