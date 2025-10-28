using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ILocationService
    {
        Task<LocationDTO> SetUserLocationAsync(SetLocationRequest request);

        Task<LocationDTO?> GetUserLocationAsync(string userId, string userType);

        Task<LocationDTO> UpdateUserLocationAsync(UpdateUserLocationRequest request);

        Task<bool> DeleteUserLocationAsync(string userId, string userType);

        Task<DistanceResponse> CalculateDistanceAsync(DistanceCalculationRequest request);

        Task<IEnumerable<CaregiverProximityResponse>> FindNearbyCaregivers(ProximitySearchRequest request);

        Task<string> ExtractCityFromAddressAsync(string address);

        Task<(double Latitude, double Longitude)> GeocodeAddressAsync(string address);

        Task<IEnumerable<CaregiverProximityResponse>> GetCaregiversByCity(string city, int maxResults = 50);

        Task<IEnumerable<LocationDTO>> GetUserLocationHistory(string userId, string userType);
    }
}