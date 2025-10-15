using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Application.Interfaces.Content;
using Application.DTOs;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly IContractService _contractService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(IContractService contractService, ILogger<WebhookController> logger)
        {
            _contractService = contractService;
            _logger = logger;
        }

        [HttpPost("flutterwave")]
        public async Task<IActionResult> ReceiveFlutterwaveWebhook([FromBody] dynamic data)
        {
            try
            {
                // Log the received data
                            _logger.LogInformation("Received webhook with headers: {Headers}", string.Join(", ", Request.Headers.Select(h => $"{h.Key}: {h.Value}")));

                // Check payment status
                if (data?.status == "successful")
                {
                    string transactionId = data?.id;
                    string txRef = data?.tx_ref; // This should contain our contract generation data
                    
                    // Parse contract generation data from tx_ref or metadata
                    var contractData = await ExtractContractDataFromPayment(transactionId, txRef);
                    
                    if (contractData != null)
                    {
                        // Generate and send contract
                        var contract = await _contractService.GenerateContractAsync(contractData);
                        await _contractService.SendContractToCaregiverAsync(contract.Id);
                        
                        _logger.LogInformation("Contract {ContractId} generated and sent for transaction {TransactionId}", 
                            contract.Id, transactionId);
                    }
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Flutterwave webhook");
                return StatusCode(500, "Webhook processing failed");
            }
        }

        [HttpPost("contract-payment")]
        public async Task<IActionResult> HandleContractPayment([FromBody] ContractPaymentWebhookDTO paymentData)
        {
            try
            {
                if (paymentData.Status == "successful")
                {
                    var contractData = new ContractGenerationRequestDTO
                    {
                        GigId = paymentData.GigId,
                        ClientId = paymentData.ClientId,
                        CaregiverId = paymentData.CaregiverId,
                        PaymentTransactionId = paymentData.TransactionId,
                        SelectedPackage = paymentData.SelectedPackage,
                        Tasks = paymentData.Tasks
                    };

                    // Generate and send contract
                    var contract = await _contractService.GenerateContractAsync(contractData);
                    await _contractService.SendContractToCaregiverAsync(contract.Id);

                    return Ok(new { success = true, contractId = contract.Id });
                }

                return BadRequest("Payment not successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing contract payment webhook");
                return StatusCode(500, "Contract generation failed");
            }
        }

        private async Task<ContractGenerationRequestDTO> ExtractContractDataFromPayment(string transactionId, string txRef)
        {
            // Implementation to extract contract data from payment metadata
            // This would typically involve parsing the tx_ref or querying payment metadata
            // For now, return null - this needs to be implemented based on your payment flow
            return null;
        }
    }

}
