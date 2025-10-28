using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class LocationDTO
    {
        public string? Id { get; set; }
        public string Address { get; set; } = null!;
        public string City { get; set; } = null!;
        public string? State { get; set; }
        public string? Country { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SetLocationRequest
    {
        [Required]
        public string UserId { get; set; } = null!;

        [Required]
        public string UserType { get; set; } = null!; // "Client" or "Caregiver"

        [Required]
        public string Address { get; set; } = null!;
    }

    public class DistanceCalculationRequest
    {
        [Required]
        public double FromLatitude { get; set; }

        [Required]
        public double FromLongitude { get; set; }

        [Required]
        public double ToLatitude { get; set; }

        [Required]
        public double ToLongitude { get; set; }
    }

    public class ProximitySearchRequest
    {
        [Required]
        public string ClientId { get; set; } = null!;

        public string? ServiceAddress { get; set; }

        public double? ServiceLatitude { get; set; }

        public double? ServiceLongitude { get; set; }

        public double MaxDistanceKm { get; set; } = 50; // Default 50km

        public string? PreferredCity { get; set; }
    }

    public class CaregiverProximityResponse
    {
        public string CaregiverId { get; set; } = null!;
        public string CaregiverName { get; set; } = null!;
        public string? ProfileImage { get; set; }
        public double DistanceKm { get; set; }
        public bool SameCity { get; set; }
        public int ProximityScore { get; set; } // Higher = better
        public LocationDTO? Location { get; set; }
        public bool IsAvailable { get; set; }
        public string? AboutMe { get; set; }
    }

    public class GeocodeRequest
    {
        [Required]
        public string Address { get; set; } = null!;
    }

    public class GeocodeResponse
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string City { get; set; } = null!;
        public string? State { get; set; }
        public string? Country { get; set; }
        public string FormattedAddress { get; set; } = null!;
    }

    public class ReverseGeocodeRequest
    {
        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }
    }

    public class UpdateUserLocationRequest
    {
        [Required]
        public string UserId { get; set; } = null!;

        [Required]
        public string UserType { get; set; } = null!;

        public string? Address { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }

    public class DistanceResponse
    {
        public double DistanceKm { get; set; }
        public double DistanceMiles { get; set; }
        public string FormattedDistance { get; set; } = null!;
    }
}