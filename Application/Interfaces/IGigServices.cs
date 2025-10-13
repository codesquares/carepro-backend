using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface IGigServices
    {
        Task<GigDTO> CreateGigAsync(AddGigRequest addServiceRequest);

        Task<IEnumerable<GigDTO>> GetAllCaregiverGigsAsync(string caregiverId);

        Task<IEnumerable<GigDTO>> GetAllCaregiverPausedGigsAsync(string caregiverId);

        Task<IEnumerable<GigDTO>> GetAllCaregiverDraftGigsAsync(string caregiverId);

        Task<IEnumerable<GigDTO>> GetAllGigsAsync();
        
       // Task<IEnumerable<GigDTO>> GetAllCaregiverServicesAsync(string caregiverId);

        Task<List<string>> GetAllSubCategoriesForCaregiverAsync(string caregiverId);

        Task<GigDTO> GetGigAsync(string serviceId);

        Task<string> UpdateGigStatusToPauseAsync(string gigId, UpdateGigStatusToPauseRequest updateGigStatusToPauseRequest);

        Task<string> UpdateGigAsync(string gigId, UpdateGigRequest updateGigRequest);


    }
}
