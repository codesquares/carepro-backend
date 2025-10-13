using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ISearchService
    {
        Task<List<string>> GetCaregiverAndServicesAsync(string? firstName, string? lastName, string? serviceName);

        Task<IEnumerable<CaregiverResponse>> SearchCaregiversWithServicesAsync(string searchTerm);
    }
}
