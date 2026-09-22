using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    /// <summary>
    /// Phase 4 — a client's own view of their requests for a specific pre-priced package.
    /// Client-facing read-only: creation is no longer exposed here (Option A) — the only
    /// way a PackageRequest is created is the admin-initiated payment-link flow completing
    /// via the Flutterwave webhook (see <c>AdminPackagePaymentsController</c> and
    /// <c>PaymentsController</c>'s CAREPRO-PKG- route), never a direct client POST.
    /// Fulfilment is by internal assignment; the client sees a confirmed caregiver only
    /// after the assigned caregiver accepts (see <see cref="PackageRequestDTO.ConfirmedCaregiver"/>).
    /// </summary>
    [Route("api/client/package-requests")]
    [ApiController]
    [Authorize(Roles = "Client")]
    public class PackageRequestsController : ControllerBase
    {
        private readonly IPackageRequestService _service;
        private readonly IPackageContractService _contractService;
        private readonly ILogger<PackageRequestsController> _logger;

        public PackageRequestsController(
            IPackageRequestService service,
            IPackageContractService contractService,
            ILogger<PackageRequestsController> logger)
        {
            _service = service;
            _contractService = contractService;
            _logger = logger;
        }

        private string? CurrentClientId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private IActionResult HandleException(Exception ex, string action)
        {
            switch (ex)
            {
                case ArgumentException ae:
                    return BadRequest(new { message = ae.Message });
                case InvalidOperationException ioe:
                    return Conflict(new { message = ioe.Message });
                case KeyNotFoundException knf:
                    return NotFound(new { message = knf.Message });
                case UnauthorizedAccessException ua:
                    return StatusCode(403, new { message = ua.Message });
                default:
                    _logger.LogError(ex, "Unexpected error during {Action}", action);
                    return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        /// <summary>All of the caller's own package requests, newest first.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Unauthorized(new { message = "Client identity not found in token." });
            try
            {
                var data = await _service.GetAllForClientAsync(clientId);
                return Ok(new { success = true, data });
            }
            catch (Exception ex) { return HandleException(ex, "GetAllPackageRequestsForClient"); }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Unauthorized(new { message = "Client identity not found in token." });
            try
            {
                var dto = await _service.GetForClientAsync(clientId, id);
                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex) { return HandleException(ex, "GetPackageRequest"); }
        }

        /// <summary>The auto-generated care agreement for this request (available once confirmed).</summary>
        [HttpGet("{id}/contract")]
        public async Task<IActionResult> GetContract(string id)
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Unauthorized(new { message = "Client identity not found in token." });
            try
            {
                // Authorisation: confirms the request belongs to this client (throws otherwise).
                await _service.GetForClientAsync(clientId, id);

                var contract = await _contractService.GetByPackageRequestAsync(id);
                if (contract == null)
                    return NotFound(new { message = "No contract has been generated for this request yet." });

                return Ok(new { success = true, data = contract });
            }
            catch (Exception ex) { return HandleException(ex, "GetPackageRequestContract"); }
        }

        [HttpGet("{id}/contract/pdf")]
        public async Task<IActionResult> GetContractPdf(string id)
        {
            var clientId = CurrentClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Unauthorized(new { message = "Client identity not found in token." });
            try
            {
                await _service.GetForClientAsync(clientId, id);

                var contract = await _contractService.GetByPackageRequestAsync(id);
                if (contract == null)
                    return NotFound(new { message = "No contract has been generated for this request yet." });

                var pdf = await _contractService.GeneratePdfAsync(contract.Id);
                return File(pdf, "application/pdf", $"CarePro-Agreement-{contract.Id}.pdf");
            }
            catch (Exception ex) { return HandleException(ex, "GetPackageRequestContractPdf"); }
        }
    }
}
