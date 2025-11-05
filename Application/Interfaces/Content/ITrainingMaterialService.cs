using Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ITrainingMaterialService
    {
        // Admin operations
        Task<TrainingMaterialUploadResponse> UploadTrainingMaterialAsync(AddTrainingMaterialRequest request);
        Task<TrainingMaterialDTO?> GetTrainingMaterialByIdAsync(string id);
        Task<List<TrainingMaterialDTO>> GetAllTrainingMaterialsAsync();
        Task<bool> UpdateTrainingMaterialAsync(UpdateTrainingMaterialRequest request);
        Task<bool> DeleteTrainingMaterialAsync(string id);

        // User operations
        Task<TrainingMaterialListResponse> GetTrainingMaterialsByUserTypeAsync(string userType, bool activeOnly = true);
        Task<TrainingMaterialDTO?> GetActiveTrainingMaterialAsync(string userType, string materialType = "PDF");
        
        // Analytics/Management
        Task<List<TrainingMaterialDTO>> SearchTrainingMaterialsAsync(string searchTerm);
        Task<bool> ToggleActiveStatusAsync(string id, bool isActive);
    }
}