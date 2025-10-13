using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class SearchDTO
    {
    }

    public class SearchCareGiverAndServiceResponse
    {
        public CaregiverResponse CaregiverResponses { get; set; }
    }


    public class ClientOrderWithGig : ClientOrder
    {
        public Gig Gigs { get; set; }
    }

    public class ClientOrderWithGigCaregiver : ClientOrderWithGig
    {
        public Caregiver Caregivers { get; set; }
    }

    public class ClientOrderFull : ClientOrderWithGigCaregiver
    {
        public Client Clients { get; set; }
    }



}
