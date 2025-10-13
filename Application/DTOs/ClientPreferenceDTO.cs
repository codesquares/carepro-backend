using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class ClientPreferenceDTO
    {
        public string Id { get; set; }
        public string ClientId { get; set; }
        public List<string> Data { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }

    public class AddClientPreferenceRequest
    {        
        public string ClientId { get; set; }
        public List<string> Data { get; set; }       
    }

    public class UpdateClientPreferenceRequest
    {        
        public List<string> Data { get; set; }        
    }
}
