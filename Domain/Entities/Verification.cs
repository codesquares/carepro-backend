using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Verification
    {
        public ObjectId VerificationId { get; set; }
        public string UserId { get; set; }
        //public string VerifiedFirstName { get; set; }
        //public string VerifiedLastName { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationNo { get; set; }
        public string VerificationStatus { get; set; }
        public bool IsVerified { get; set; }
        public DateTime VerifiedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }
}


//1.UserId
//2.StoredFirstName
//3.StoredLastName
//4.VerifiedFirstName
//5.VerifyLastName
//6.Message
//7.Method
//8.UserType
//9.VerifiedStatus
//10.VerifiedAt
//11.UpdatedAt