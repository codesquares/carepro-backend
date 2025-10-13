using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    //[Route("api/[controller]")]
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly FlutterwaveService _flutterwaveService;

        public PaymentsController(FlutterwaveService flutterwaveService)
        {
            _flutterwaveService = flutterwaveService;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiatePayment([FromBody] PaymentRequest request)
        {
            request.Currency = "NGN";
            var txRef = Guid.NewGuid().ToString(); // Unique transaction reference
            var response = await _flutterwaveService.InitiatePayment(request.Amount, request.Email, request.Currency, txRef, request.RedirectUrl);
            return Ok(response);
        }

        [HttpGet("verify/{transactionId}")]
        public async Task<IActionResult> VerifyPayment(string transactionId)
        {
            var response = await _flutterwaveService.VerifyPayment(transactionId);
            return Ok(response);
        }
    }


    public class PaymentRequest
    {
        public decimal Amount { get; set; }
        public string Email { get; set; }
        public string Currency { get; set; }
        public string RedirectUrl { get; set; }
    }
}
