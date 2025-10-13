using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class VerificationDTO
    {
        public ObjectId VerificationId { get; set; }
        public string UserId { get; set; }
        public string VerifiedFirstName { get; set; }
        public string VerifiedLastName { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationStatus { get; set; }
        public DateTime VerifiedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }


    public class AddVerificationRequest
    {       
        public string UserId { get; set; }
        public string VerifiedFirstName { get; set; }
        public string VerifiedLastName { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationNo { get; set; }

        public string VerificationStatus { get; set; } 
        
    }

    public class VerificationResponse
    {
        public string VerificationId { get; set; }
        public string UserId { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationNo { get; set; }
        public bool IsVerified { get; set; }
        public string VerificationStatus { get; set; }
        public DateTime VerifiedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }


    public class UpdateVerificationRequest
    {
        public string VerificationMode { get; set; }
        public string VerificationStatus { get; set; }

    }
}
