using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace CarePro_Api.Controllers.Content
{
    //[Route("api/[controller]")]
    [ApiController]
    [Route("api/webhook")]
    public class WebhookController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> ReceiveWebhook([FromBody] dynamic data)
        {
            // Log the received data
            Console.WriteLine(JsonConvert.SerializeObject(data));

            // Check payment status
            if (data?.status == "successful")
            {
                string transactionId = data?.id;
                // Handle successful payment (update database, send email, etc.)
            }

            return Ok();
        }
    }

}
