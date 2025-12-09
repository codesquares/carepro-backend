using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Authentication;
using Domain.Entities;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminsController : ControllerBase
    {
        private readonly IAdminUserService adminUserService;
        private readonly IClientOrderService clientOrderService;
        private readonly ICareGiverService careGiverService;
        private readonly IClientService clientService;
        private readonly IEmailService emailService;
        private readonly ICertificationService certificationService;
        private readonly ILogger<AdminsController> logger;

        public AdminsController(
            IAdminUserService adminUserService, 
            IClientOrderService clientOrderService, 
            ICareGiverService careGiverService,
            IClientService clientService,
            IEmailService emailService,
            ICertificationService certificationService,
            ILogger<AdminsController> logger)
        {
            this.adminUserService = adminUserService;
            this.clientOrderService = clientOrderService;
            this.careGiverService = careGiverService;
            this.clientService = clientService;
            this.emailService = emailService;
            this.certificationService = certificationService;
            this.logger = logger;
        }

        /// ENDPOINT TO CREATE  ADMIN USERS TO THE DATABASE        
        [HttpPost]
        // [Route("AdminUser")]
        public async Task<IActionResult> AddAdminUserAsync([FromBody] AddAdminUserRequest addAdminUserRequest)
        {
            try
            {
                // Pass Domain Object to Repository to Persist this
                var adminUser = await adminUserService.CreateAdminUserAsync(addAdminUserRequest);

                // Send DTO response back to ClientUser
                return Ok(adminUser);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message }); // Or BadRequest
            }
            catch (AuthenticationException authEx)
            {
                // Handle authentication-related exceptions
                return BadRequest(new { StatusCode = 400, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database update-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }


        [HttpGet]
        [Route("AllAdminUsers")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetAllCaregiverAsync()
        {
            try
            {
                var adminUsers = await adminUserService.GetAllAdminUsersAsync();
                return Ok(adminUsers);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message }); // Or BadRequest
            }
            catch (AuthenticationException authEx)
            {
                // Handle authentication-related exceptions
                return BadRequest(new { StatusCode = 400, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database update-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }

        }


        [HttpGet]
        [Route("{adminUserId}")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetAdminUserAsync(string adminUserId)
        {
            try
            {
                var adminUser = await adminUserService.GetAdminUserByIdAsync(adminUserId);
                return Ok(adminUser);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        /// <summary>
        /// GET ALL ORDERS - Admin endpoint to retrieve all orders in the system
        /// </summary>
        [HttpGet]
        [Route("AllOrders")]
        //[Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GetAllOrdersAsync()
        {
            try
            {
                logger.LogInformation("Admin retrieving all orders");
                var orders = await clientOrderService.GetAllOrdersAsync();
                
                return Ok(new
                {
                    success = true,
                    count = orders.Count(),
                    data = orders
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving all orders");
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Send custom email to a specific user
        /// </summary>
        [HttpPost]
        [Route("SendEmail")]
        //[Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> SendCustomEmailAsync([FromForm] SendCustomEmailRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                logger.LogInformation($"Admin sending email to {request.RecipientEmail}");

                // Upload attachments to Cloudinary if any
                var attachments = new List<Application.DTOs.Email.EmailAttachmentInfo>();
                
                if (request.Attachments != null && request.Attachments.Any())
                {
                    try
                    {
                        // Validate attachment count (max 5)
                        if (request.Attachments.Count > 5)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message = "Maximum 5 attachments allowed per email"
                            });
                        }

                        // Upload attachments using CloudinaryService
                        var cloudinaryService = HttpContext.RequestServices.GetRequiredService<CloudinaryService>();
                        
                        // Use recipient email as userId for organization
                        var userId = request.RecipientEmail.Replace("@", "_").Replace(".", "_");
                        
                        attachments = await cloudinaryService.UploadMultipleEmailAttachmentsAsync(
                            request.Attachments, 
                            userId, 
                            expirationDays: 7,
                            maxTotalSizeMB: 100
                        );

                        logger.LogInformation($"Successfully uploaded {attachments.Count} attachments for email to {request.RecipientEmail}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error uploading attachments");
                        return BadRequest(new
                        {
                            success = false,
                            message = "Failed to upload attachments",
                            error = ex.Message
                        });
                    }
                }

                await emailService.SendCustomEmailToUserAsync(
                    request.RecipientEmail, 
                    request.RecipientName, 
                    request.Subject, 
                    request.Message,
                    attachments);

                return Ok(new
                {
                    success = true,
                    message = $"Email sent successfully to {request.RecipientEmail}",
                    attachmentCount = attachments.Count
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending custom email");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to send email",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Send bulk emails to multiple users (all caregivers, all clients, or specific users)
        /// </summary>
        [HttpPost]
        [Route("SendBulkEmail")]
        //[Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> SendBulkEmailAsync([FromBody] SendBulkEmailRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                logger.LogInformation($"Admin sending bulk email to {request.RecipientType}");

                var recipients = new List<(string Email, string FirstName)>();
                var errors = new List<string>();

                // Determine recipients based on type
                switch (request.RecipientType.ToLower())
                {
                    case "all":
                        // Get all caregivers
                        var allCaregivers = await careGiverService.GetAllCaregiverUserAsync();
                        recipients.AddRange(allCaregivers.Select(c => (c.Email, c.FirstName)));

                        // Get all clients
                        var allClients = await clientService.GetAllClientUserAsync();
                        recipients.AddRange(allClients.Select(c => (c.Email ?? "", c.FirstName ?? "")));
                        break;

                    case "caregivers":
                        var caregivers = await careGiverService.GetAllCaregiverUserAsync();
                        recipients.AddRange(caregivers.Select(c => (c.Email, c.FirstName)));
                        break;

                    case "clients":
                        var clients = await clientService.GetAllClientUserAsync();
                        recipients.AddRange(clients.Select(c => (c.Email ?? "", c.FirstName ?? "")));
                        break;

                    case "specific":
                        if (request.SpecificUserIds == null || !request.SpecificUserIds.Any())
                        {
                            return BadRequest(new { message = "SpecificUserIds is required when RecipientType is 'Specific'" });
                        }

                        foreach (var userId in request.SpecificUserIds)
                        {
                            try
                            {
                                // Try to get as caregiver first
                                var caregiver = await careGiverService.GetCaregiverUserAsync(userId);
                                if (caregiver != null)
                                {
                                    recipients.Add((caregiver.Email, caregiver.FirstName));
                                    continue;
                                }
                            }
                            catch { }

                            try
                            {
                                // Try to get as client
                                var client = await clientService.GetClientUserAsync(userId);
                                if (client != null)
                                {
                                    recipients.Add((client.Email ?? "", client.FirstName ?? ""));
                                    continue;
                                }
                            }
                            catch { }

                            errors.Add($"User with ID {userId} not found");
                        }
                        break;

                    default:
                        return BadRequest(new { message = "Invalid RecipientType. Use 'All', 'Caregivers', 'Clients', or 'Specific'" });
                }

                // Remove duplicates and empty emails
                recipients = recipients
                    .Where(r => !string.IsNullOrEmpty(r.Email))
                    .GroupBy(r => r.Email)
                    .Select(g => g.First())
                    .ToList();

                if (!recipients.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No valid recipients found",
                        errors
                    });
                }

                // Send bulk emails
                var successCount = 0;
                var failCount = 0;

                try
                {
                    await emailService.SendBulkCustomEmailAsync(recipients, request.Subject, request.Message);
                    successCount = recipients.Count;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error sending bulk emails");
                    errors.Add($"Bulk email error: {ex.Message}");
                    failCount = recipients.Count;
                }

                return Ok(new BulkEmailResponse
                {
                    Success = successCount > 0,
                    Message = $"Bulk email process completed. {successCount} sent successfully, {failCount} failed.",
                    TotalRecipients = recipients.Count,
                    SuccessfulSends = successCount,
                    FailedSends = failCount,
                    Errors = errors
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in bulk email process");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to process bulk email",
                    error = ex.Message
                });
            }
        }

        #region Certificate Management Endpoints

        /// <summary>
        /// Get all certificates system-wide with caregiver details
        /// </summary>
        [HttpGet("Certificates/All")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllCertificatesAsync()
        {
            try
            {
                var certificates = await certificationService.GetAllCertificatesAsync();
                
                return Ok(new
                {
                    success = true,
                    message = "All certificates retrieved successfully",
                    count = certificates.Count(),
                    data = certificates
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching all certificates");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to retrieve certificates",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get certificates requiring manual review
        /// </summary>
        [HttpGet("Certificates/PendingReview")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetCertificatesPendingReviewAsync()
        {
            try
            {
                var certificates = await certificationService.GetCertificatesByStatusAsync(DocumentVerificationStatus.ManualReviewRequired);
                
                return Ok(new
                {
                    success = true,
                    message = "Certificates pending review retrieved successfully",
                    count = certificates.Count(),
                    data = certificates
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching certificates pending review");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to retrieve certificates pending review",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get certificates by specific verification status
        /// </summary>
        [HttpGet("Certificates/ByStatus/{status}")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetCertificatesByStatusAsync(DocumentVerificationStatus status)
        {
            try
            {
                var certificates = await certificationService.GetCertificatesByStatusAsync(status);
                
                return Ok(new
                {
                    success = true,
                    message = $"Certificates with status '{status}' retrieved successfully",
                    count = certificates.Count(),
                    status = status.ToString(),
                    data = certificates
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error fetching certificates with status {status}");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Failed to retrieve certificates with status '{status}'",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get detailed information about a specific certificate
        /// </summary>
        [HttpGet("Certificates/{certificateId}/Details")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetCertificateDetailsAsync(string certificateId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { success = false, message = "Certificate ID is required" });
                }

                var certificate = await certificationService.GetCertificateDetailsAsync(certificateId);
                
                return Ok(new
                {
                    success = true,
                    message = "Certificate details retrieved successfully",
                    data = certificate
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error fetching certificate details for {certificateId}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to retrieve certificate details",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Manually approve a certificate
        /// </summary>
        [HttpPost("Certificates/{certificateId}/Approve")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> ApproveCertificateAsync(string certificateId, [FromBody] ManualApprovalRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { success = false, message = "Certificate ID is required" });
                }

                if (string.IsNullOrWhiteSpace(request?.AdminId))
                {
                    return BadRequest(new { success = false, message = "Admin ID is required" });
                }

                var result = await certificationService.ManuallyApproveCertificateAsync(
                    certificateId, 
                    request.AdminId, 
                    request.ApprovalNotes);
                
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error approving certificate {certificateId}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to approve certificate",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Manually reject a certificate
        /// </summary>
        [HttpPost("Certificates/{certificateId}/Reject")]
        // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> RejectCertificateAsync(string certificateId, [FromBody] ManualRejectionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(certificateId))
                {
                    return BadRequest(new { success = false, message = "Certificate ID is required" });
                }

                if (string.IsNullOrWhiteSpace(request?.AdminId))
                {
                    return BadRequest(new { success = false, message = "Admin ID is required" });
                }

                if (string.IsNullOrWhiteSpace(request.RejectionReason))
                {
                    return BadRequest(new { success = false, message = "Rejection reason is required" });
                }

                var result = await certificationService.ManuallyRejectCertificateAsync(
                    certificateId, 
                    request.AdminId, 
                    request.RejectionReason);
                
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error rejecting certificate {certificateId}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to reject certificate",
                    error = ex.Message
                });
            }
        }

        #endregion

    }
}
