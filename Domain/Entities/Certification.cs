using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Certification
    {
        public ObjectId Id { get; set; }

        public string CaregiverId { get; set; }

        public string CertificateName { get; set; }

        public string CertificateIssuer { get; set; }

        public byte[] Certificate { get; set; }

        public bool IsVerified { get; set; }

        //public string VerificationStatus { get; set; }

        public DateTime YearObtained { get; set; }

        public DateTime SubmittedOn { get; set; }
    }
}
