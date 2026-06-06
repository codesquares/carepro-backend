using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/gig-price-negotiation")]
    public class GigPriceNegotiationController : ControllerBase
    {
        private readonly IGigPriceNegotiationService _negotiationService;
        private readonly ILogger<GigPriceNegotiationController> _logger;

        public GigPriceNegotiationController(
            IGigPriceNegotiationService negotiationService,
            ILogger<GigPriceNegotiationController> logger)
        {
            _negotiationService = negotiationService;
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ────────────────────────────────────────────────────────────────────

        private string? GetCurrentUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        private string? GetCurrentUserRole() =>
            User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value;

        private IActionResult MapException(Exception ex)
        {
            _logger.LogError(ex, "GigPriceNegotiationController error: {Message}", ex.Message);

            return ex switch
            {
                KeyNotFoundException => NotFound(new { success = false, message = ex.Message }),
                UnauthorizedAccessException => StatusCode(403, new { success = false, message = ex.Message }),
                ArgumentException => BadRequest(new { success = false, message = ex.Message }),
                InvalidOperationException ioe when ioe.Message.StartsWith("CONCURRENCY_CONFLICT") =>
                    StatusCode(409, new { success = false, message = ioe.Message }),
                InvalidOperationException ioe when ioe.Message.Contains("terminal state") =>
                    StatusCode(410, new { success = false, message = ioe.Message }),
                InvalidOperationException => BadRequest(new { success = false, message = ex.Message }),
                _ => StatusCode(500, new { success = false, message = "An unexpected error occurred." })
            };
        }

        // ────────────────────────────────────────────────────────────────────
        //  POST /api/gig-price-negotiation/initiate
        //  Client initiates a negotiation for a RegularGig.
        //  Can optionally include a proposed price. Idempotent per clientId+gigId.
        // ────────────────────────────────────────────────────────────────────

        [HttpPost("initiate")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Initiate([FromBody] InitiateNegotiationRequest request)
        {
            var clientId = GetCurrentUserId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.InitiateAsync(
                    clientId, request.GigId, request.ProposedPrice, request.Note);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  GET /api/gig-price-negotiation/{negotiationId}
        //  Client or Caregiver — fetch full negotiation detail.
        // ────────────────────────────────────────────────────────────────────

        [HttpGet("{negotiationId}")]
        [Authorize]
        public async Task<IActionResult> GetById(string negotiationId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.GetNegotiationByIdAsync(negotiationId, userId);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  GET /api/gig-price-negotiation/by-gig/{gigId}
        //  Client — get the active negotiation for a specific gig, if any.
        //  Returns 404 when no active negotiation exists (not an error condition).
        // ────────────────────────────────────────────────────────────────────

        [HttpGet("by-gig/{gigId}")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> GetByGig(string gigId)
        {
            var clientId = GetCurrentUserId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.GetNegotiationByGigAsync(clientId, gigId);
                if (result == null)
                    return NotFound(new { success = false, message = "No active negotiation found for this gig." });

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  GET /api/gig-price-negotiation/caregiver/pending?page=1&pageSize=20
        //  Caregiver — paginated list of negotiations waiting for their action.
        // ────────────────────────────────────────────────────────────────────

        [HttpGet("caregiver/pending")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> GetCaregiverPending(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var caregiverId = GetCurrentUserId();
            if (string.IsNullOrEmpty(caregiverId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            try
            {
                var result = await _negotiationService.GetPendingNegotiationsForCaregiverAsync(
                    caregiverId, page, pageSize);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  POST /api/gig-price-negotiation/{negotiationId}/accept
        //  Client accepts the current proposed price (caregiver's rate or counter).
        // ────────────────────────────────────────────────────────────────────

        [HttpPost("{negotiationId}/accept")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> ClientAccept(
            string negotiationId, [FromBody] ClientAcceptRequest request)
        {
            var clientId = GetCurrentUserId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.ClientAcceptAsync(
                    clientId, negotiationId, request.Version);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  POST /api/gig-price-negotiation/{negotiationId}/propose
        //  Client submits a counter-proposal (lower price).
        // ────────────────────────────────────────────────────────────────────

        [HttpPost("{negotiationId}/propose")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> ClientPropose(
            string negotiationId, [FromBody] ClientProposeRequest request)
        {
            var clientId = GetCurrentUserId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.ClientProposeAsync(
                    clientId, negotiationId, request.ProposedPrice, request.Note, request.Version);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  PUT /api/gig-price-negotiation/{negotiationId}/respond
        //  Caregiver accepts the client's price OR submits a counter-price.
        // ────────────────────────────────────────────────────────────────────

        [HttpPut("{negotiationId}/respond")]
        [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> CaregiverRespond(
            string negotiationId, [FromBody] CaregiverRespondRequest request)
        {
            var caregiverId = GetCurrentUserId();
            if (string.IsNullOrEmpty(caregiverId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.CaregiverRespondAsync(
                    caregiverId, negotiationId, request.Accept, request.CounterPrice, request.Note, request.Version);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  POST /api/gig-price-negotiation/{negotiationId}/reject
        //  Either party (Client or Caregiver) can reject to end the negotiation.
        // ────────────────────────────────────────────────────────────────────

        [HttpPost("{negotiationId}/reject")]
        [Authorize]
        public async Task<IActionResult> Reject(
            string negotiationId, [FromBody] RejectNegotiationRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            var role = GetCurrentUserRole() ?? "Unknown";

            try
            {
                var result = await _negotiationService.RejectAsync(
                    userId, role, negotiationId, request.Reason, request.Version);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  POST /api/gig-price-negotiation/reinitiate-care-request
        //  Client restarts a negotiation for a CareRequest hire after the previous
        //  negotiation was rejected or expired. The hire status is unchanged.
        //  Idempotent: returns existing non-terminal negotiation if already active.
        // ────────────────────────────────────────────────────────────────────

        [HttpPost("reinitiate-care-request")]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> ReinitiateFromCareRequest(
            [FromBody] ReinitiateFromCareRequestRequest request)
        {
            var clientId = GetCurrentUserId();
            if (string.IsNullOrEmpty(clientId))
                return Unauthorized(new { success = false, message = "User not authenticated." });

            try
            {
                var result = await _negotiationService.ReinitiateFromCareRequestAsync(
                    clientId, request.CareRequestId, request.ResponseId);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return MapException(ex);
            }
        }
    }
}
