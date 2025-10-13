using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class CertificationDTO
    {
        public string Id { get; set; }

        public string CaregiverId { get; set; }


        public string CertificateName { get; set; }

        public string CertificateIssuer { get; set; }

        public string CertificateUrl { get; set; }

        public bool IsVerified { get; set; }

        public string VerificationStatus { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime SubmittedOn { get; set; }
    }

    public class CertificationResponse
    {
        public string Id { get; set; }

        public string CaregiverId { get; set; }


        public string CertificateName { get; set; }

        public string CertificateIssuer { get; set; }

        public string Certificate { get; set; }

        public bool IsVerified { get; set; }

       // public string VerificationStatus { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime SubmittedOn { get; set; }
    }


    public class AddCertificationRequest
    {
        public string CertificateName { get; set; }

        public string CaregiverId { get; set; }

        public string CertificateIssuer { get; set; }

        public string Certificate { get; set; }

        public DateTime YearObtained { get; set; }

    }
}
