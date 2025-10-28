using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IGeocodingService
    {
        Task<GeocodeResponse> GeocodeAsync(string address);

        Task<GeocodeResponse> ReverseGeocodeAsync(double latitude, double longitude);

        Task<string> GetCityFromAddressAsync(string address);

        Task<bool> ValidateAddressAsync(string address);

        Task<IEnumerable<GeocodeResponse>> BatchGeocodeAsync(IEnumerable<string> addresses);
    }
}