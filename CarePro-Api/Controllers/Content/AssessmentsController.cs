using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssessmentsController : ControllerBase
    {
        private readonly IAssessmentService assessmentService;
        private readonly ICareGiverService careGiverService;
        private readonly ITrainingMaterialService trainingMaterialService;
        private readonly Infrastructure.Content.Services.CloudinaryService cloudinaryService;
        private readonly ILogger<AssessmentsController> logger;

        public AssessmentsController(
            IAssessmentService assessmentService,
            ICareGiverService careGiverService,
            ITrainingMaterialService trainingMaterialService,
            Infrastructure.Content.Services.CloudinaryService cloudinaryService,
            ILogger<AssessmentsController> logger)
        {
            this.assessmentService = assessmentService;
            this.careGiverService = careGiverService;
            this.trainingMaterialService = trainingMaterialService;
            this.cloudinaryService = cloudinaryService;
            this.logger = logger;
        }

        [HttpPost]
        // [Authorize(Roles = "Caregiver")]
        public async Task<IActionResult> AddAssessmentAsync([FromBody] AddAssessmentRequest addAssessmentRequest)
        {
            try
            {
                // Submit assessment and calculate score
                var assessmentId = await assessmentService.SubmitAssessmentAsync(addAssessmentRequest);

                // Return the assessment ID
                return Ok(assessmentId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }

        }

        [HttpGet]
        [Route("careGiverId")]
        // [Authorize(Roles = "Caregiver, Client, Admin")]
        public async Task<IActionResult> GetAssessmentAsync(string careGiverId)
        {
            try
            {
                logger.LogInformation($"Retrieving Assessment for caregiver with ID '{careGiverId}'.");

                var assessment = await assessmentService.GetAssesementAsync(careGiverId);

                return Ok(assessment);

            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ApplicationException appEx)
            {
                // Handle application-specific exceptions
                return BadRequest(new { ErrorMessage = appEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { ErrorMessage = httpEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                logger.LogError(ex, "An unexpected error occurred"); return StatusCode(500, new { ErrorMessage = "An error occurred on the server." });
            }
        }

        [HttpGet("questions/{userType}")]
        // [Authorize(Roles = "Caregiver, Cleaner")]
        public async Task<IActionResult> GetQuestionsForAssessmentAsync(string userType)
        {
            try
            {
                // Validate user type
                if (userType != "Cleaner" && userType != "Caregiver")
                {
                    return BadRequest(new { Message = "User type must be either 'Cleaner' or 'Caregiver'" });
                }

                var questions = await assessmentService.GetQuestionsForAssessmentAsync(userType);
                return Ok(questions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting questions for assessment");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("{id}")]
        // [Authorize(Roles = "Caregiver, Cleaner, Admin")]
        public async Task<IActionResult> GetAssessmentByIdAsync(string id)
        {
            try
            {
                var assessment = await assessmentService.GetAssessmentByIdAsync(id);
                return Ok(assessment);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessment by ID");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("user/{userId}")]
        // [Authorize(Roles = "Caregiver, Cleaner, Admin")]
        public async Task<IActionResult> GetAssessmentsByUserIdAsync(string userId)
        {
            try
            {
                var assessments = await assessmentService.GetAssessmentsByUserIdAsync(userId);
                return Ok(assessments);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting assessments by user ID");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost("calculate-score/{assessmentId}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CalculateAssessmentScoreAsync(string assessmentId)
        {
            try
            {
                var assessment = await assessmentService.CalculateAssessmentScoreAsync(assessmentId);
                return Ok(assessment);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating assessment score");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        #region Training Materials Endpoints

        /// <summary>
        /// Get training materials for specific user type (Caregiver, Client, or Both)
        /// </summary>
        [HttpGet("training-materials/{userType}")]
        [Authorize(Roles = "Caregiver, Client")]
        public async Task<IActionResult> GetTrainingMaterialsByUserType(string userType)
        {
            try
            {
                // Validate user type
                if (userType != "Caregiver" && userType != "Client")
                {
                    return BadRequest(new { Message = "User type must be either 'Caregiver' or 'Client'" });
                }

                var result = await trainingMaterialService.GetTrainingMaterialsByUserTypeAsync(userType, true);

                return Ok(new
                {
                    success = true,
                    data = result.Materials,
                    totalCount = result.TotalCount,
                    userType = result.UserType
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting training materials for user type: {UserType}", userType);
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get active PDF training material for user type
        /// </summary>
        [HttpGet("training-materials/{userType}/pdf")]
        [Authorize(Roles = "Caregiver, Client")]
        public async Task<IActionResult> GetActiveTrainingPdf(string userType)
        {
            try
            {
                // Validate user type
                if (userType != "Caregiver" && userType != "Client")
                {
                    return BadRequest(new { Message = "User type must be either 'Caregiver' or 'Client'" });
                }

                var material = await trainingMaterialService.GetActiveTrainingMaterialAsync(userType, "PDF");

                if (material == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"No active PDF training material found for {userType}"
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = material
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting active PDF training material for user type: {UserType}", userType);
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Get training material by ID with download URL
        /// </summary>
        [HttpGet("training-materials/download/{id}")]
        [Authorize(Roles = "Caregiver, Client")]
        public async Task<IActionResult> GetTrainingMaterialForDownload(string id)
        {
            try
            {
                var material = await trainingMaterialService.GetTrainingMaterialByIdAsync(id);

                if (material == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Training material not found"
                    });
                }

                if (!material.IsActive)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Training material is not currently available"
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        id = material.Id,
                        title = material.Title,
                        fileName = material.FileName,
                        fileType = material.FileType,
                        fileSize = material.FileSize,
                        downloadUrl = material.CloudinaryUrl,
                        description = material.Description
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting training material for download: {Id}", id);
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Download training material file directly with proper download headers
        /// </summary>
        [HttpGet("{id}/download")]
        [Authorize(Roles = "Caregiver, Client")]
        public async Task<IActionResult> DownloadTrainingMaterial(string id)
        {
            try
            {
                var trainingMaterial = await trainingMaterialService.GetTrainingMaterialByIdAsync(id);
                if (trainingMaterial == null)
                {
                    return NotFound("Training material not found");
                }

                if (string.IsNullOrEmpty(trainingMaterial.CloudinaryPublicId))
                {
                    return BadRequest("Training material has no associated file");
                }

                Console.WriteLine($"Attempting to download training material: {trainingMaterial.FileName} (CloudinaryId: {trainingMaterial.CloudinaryPublicId})");

                byte[]? fileBytes = null;

                // Try authenticated download first
                try
                {
                    Console.WriteLine("Attempting authenticated download...");
                    fileBytes = await cloudinaryService.DownloadFileWithAuthAsync(trainingMaterial.CloudinaryPublicId);
                    Console.WriteLine("Authenticated download successful");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Authenticated download failed: {ex.Message}");

                    // Try direct download
                    try
                    {
                        Console.WriteLine("Attempting direct download...");
                        fileBytes = await cloudinaryService.DownloadFileAsync(trainingMaterial.CloudinaryUrl);
                        Console.WriteLine("Direct download successful");
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"Direct download failed: {ex2.Message}");

                        // Try signed URL method
                        try
                        {
                            Console.WriteLine("Attempting signed URL download...");
                            fileBytes = await cloudinaryService.DownloadFileWithSignedUrlAsync(trainingMaterial.CloudinaryPublicId);
                            Console.WriteLine("Signed URL download successful");
                        }
                        catch (Exception ex3)
                        {
                            Console.WriteLine($"Signed URL download failed: {ex3.Message}");

                            // Try alternative URL patterns
                            try
                            {
                                Console.WriteLine("Attempting alternative URL patterns...");
                                fileBytes = await cloudinaryService.DownloadFileWithAlternativeUrlAsync(trainingMaterial.CloudinaryPublicId);
                                Console.WriteLine("Alternative URL download successful");
                            }
                            catch (Exception ex4)
                            {
                                Console.WriteLine($"All download methods failed. Auth: {ex.Message}, Direct: {ex2.Message}, Signed: {ex3.Message}, Alternative: {ex4.Message}");
                                return StatusCode(500, new
                                {
                                    error = "Unable to download file",
                                    details = new
                                    {
                                        authenticatedError = ex.Message,
                                        directError = ex2.Message,
                                        signedUrlError = ex3.Message,
                                        alternativeError = ex4.Message
                                    }
                                });
                            }
                        }
                    }
                }

                if (fileBytes == null)
                {
                    return StatusCode(500, new { error = "Unable to download file - no bytes returned" });
                }

                var contentType = "application/pdf";
                var fileName = trainingMaterial.FileName;

                Console.WriteLine($"Returning file: {fileName} ({fileBytes.Length} bytes)");
                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DownloadTrainingMaterial: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { error = "An error occurred while downloading the file", details = ex.Message });
            }
        }        /// <summary>
                 /// Search training materials for users
                 /// </summary>
        [HttpGet("training-materials/search")]
        [Authorize(Roles = "Caregiver, Client")]
        public async Task<IActionResult> SearchTrainingMaterials([FromQuery] string searchTerm, [FromQuery] string? userType = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return BadRequest(new { Message = "Search term is required" });
                }

                // If userType is provided, validate it
                if (!string.IsNullOrEmpty(userType) && userType != "Caregiver" && userType != "Client")
                {
                    return BadRequest(new { Message = "User type must be either 'Caregiver' or 'Client'" });
                }

                var allMaterials = await trainingMaterialService.SearchTrainingMaterialsAsync(searchTerm);

                // Filter by user type if provided and only show active materials
                var filteredMaterials = allMaterials.Where(m => m.IsActive);

                if (!string.IsNullOrEmpty(userType))
                {
                    filteredMaterials = filteredMaterials.Where(m =>
                        m.UserType == userType || m.UserType == "Both");
                }

                var results = filteredMaterials.ToList();

                return Ok(new
                {
                    success = true,
                    data = results,
                    searchTerm = searchTerm,
                    userType = userType,
                    count = results.Count
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error searching training materials");
                return StatusCode(500, new { ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Download training material file - alternative endpoint for frontend compatibility
        /// </summary>
        [HttpGet("training-materials/file/{id}")]
        [Authorize(Roles = "Caregiver, Client")]
        public async Task<IActionResult> DownloadTrainingMaterialFile(string id)
        {
            try
            {
                var trainingMaterial = await trainingMaterialService.GetTrainingMaterialByIdAsync(id);
                if (trainingMaterial == null)
                {
                    return NotFound("Training material not found");
                }

                if (string.IsNullOrEmpty(trainingMaterial.CloudinaryPublicId))
                {
                    return BadRequest("Training material has no associated file");
                }

                Console.WriteLine($"Attempting to download training material: {trainingMaterial.FileName} (CloudinaryId: {trainingMaterial.CloudinaryPublicId})");

                byte[]? fileBytes = null;

                // Try multiple download strategies with authentication first
                try
                {
                    Console.WriteLine("Trying authenticated download...");
                    fileBytes = await cloudinaryService.DownloadFileWithAuthAsync(trainingMaterial.CloudinaryPublicId);
                    Console.WriteLine($"Authenticated download successful: {fileBytes?.Length ?? 0} bytes");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Authenticated download failed: {ex.Message}");

                    try
                    {
                        Console.WriteLine("Trying direct download...");
                        fileBytes = await cloudinaryService.DownloadFileAsync(trainingMaterial.CloudinaryUrl);
                        Console.WriteLine($"Direct download successful: {fileBytes?.Length ?? 0} bytes");
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"Direct download failed: {ex2.Message}");

                        try
                        {
                            Console.WriteLine("Trying signed URL download...");
                            fileBytes = await cloudinaryService.DownloadFileWithSignedUrlAsync(trainingMaterial.CloudinaryPublicId);
                            Console.WriteLine($"Signed URL download successful: {fileBytes?.Length ?? 0} bytes");
                        }
                        catch (Exception ex3)
                        {
                            Console.WriteLine($"Signed URL download failed: {ex3.Message}");

                            try
                            {
                                Console.WriteLine("Trying alternative download method...");
                                fileBytes = await cloudinaryService.DownloadFileWithAlternativeUrlAsync(trainingMaterial.CloudinaryPublicId);
                                Console.WriteLine($"Alternative download successful: {fileBytes?.Length ?? 0} bytes");
                            }
                            catch (Exception ex4)
                            {
                                Console.WriteLine($"Alternative download failed: {ex4.Message}");
                                return StatusCode(500, new
                                {
                                    error = "Unable to download file from all attempted methods",
                                    details = new
                                    {
                                        authenticatedError = ex.Message,
                                        directError = ex2.Message,
                                        signedUrlError = ex3.Message,
                                        alternativeError = ex4.Message
                                    }
                                });
                            }
                        }
                    }
                }

                if (fileBytes == null)
                {
                    return StatusCode(500, new { error = "Unable to download file - no bytes returned" });
                }

                var contentType = "application/pdf";
                var fileName = trainingMaterial.FileName;

                Console.WriteLine($"Returning file: {fileName} ({fileBytes.Length} bytes)");
                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DownloadTrainingMaterialFile: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { error = "An error occurred while downloading the file", details = ex.Message });
            }
        }

        #endregion
    }
}
