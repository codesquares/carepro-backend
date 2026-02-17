using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Domain;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    public class GigServices : IGigServices
    {
        private readonly CareProDbContext careProDbContext;
        private readonly ICareGiverService careGiverService;
        private readonly ILogger<GigServices> logger;
        private readonly CloudinaryService cloudinaryService;
        private readonly IEligibilityService eligibilityService;

        public GigServices(CareProDbContext careProDbContext, ICareGiverService careGiverService, ILogger<GigServices> logger, CloudinaryService cloudinaryService, IEligibilityService eligibilityService)
        {
            this.careProDbContext = careProDbContext;
            this.careGiverService = careGiverService;
            this.logger = logger;
            this.cloudinaryService = cloudinaryService;
            this.eligibilityService = eligibilityService;
        }

        public async Task<GigDTO> CreateGigAsync(AddGigRequest addGigRequest)
        {
            var gigExist = await careProDbContext.Gigs.FirstOrDefaultAsync(x => x.CaregiverId == addGigRequest.CaregiverId && x.Title == addGigRequest.Title && x.Category == addGigRequest.Category);
            string? imageURL = null;

            if (gigExist != null)
            {
                throw new ArgumentException("This Gig already exist");
            }

            var careGiver = await careGiverService.GetCaregiverUserAsync(addGigRequest.CaregiverId);
            if (careGiver == null)
            {
                throw new KeyNotFoundException("The CaregiverID entered is not a Valid ID");
            }

            /// Check if caregiver select a category or sub-category before creating a gig
            if (string.IsNullOrWhiteSpace(addGigRequest.Category) || addGigRequest.SubCategory == null || !addGigRequest.SubCategory.Any(s => !string.IsNullOrWhiteSpace(s)))
            {
                throw new ArgumentException("At least A Service and Sub-Category must be selected before you create a new Gig");
            }

            // Normalize category: if it matches a known ServiceRequirement (case-insensitive),
            // use the canonical name to prevent typo bypass of eligibility checks
            string? eligibilityWarning = null;
            var allRequirements = await careProDbContext.ServiceRequirements
                .Where(sr => sr.Active)
                .ToListAsync();
            var matchedRequirement = allRequirements
                .FirstOrDefault(sr => string.Equals(sr.ServiceCategory, addGigRequest.Category, StringComparison.OrdinalIgnoreCase));

            if (matchedRequirement != null)
            {
                // Normalize to canonical form so "medicalSupport" → "MedicalSupport"
                addGigRequest.Category = matchedRequirement.ServiceCategory;
            }

            // Server-side eligibility check for specialized categories
            if (addGigRequest.Status == "Published" || addGigRequest.Status == "Active")
            {
                var eligibilityError = await eligibilityService.ValidateGigEligibilityAsync(
                    addGigRequest.CaregiverId, addGigRequest.Category);

                if (eligibilityError != null)
                {
                    throw new UnauthorizedAccessException(
                        System.Text.Json.JsonSerializer.Serialize(eligibilityError));
                }
            }
            else if (addGigRequest.Status == "Draft" && matchedRequirement != null)
            {
                // For drafts in specialized categories, check eligibility and warn (don't block)
                var draftEligibility = await eligibilityService.ValidateGigEligibilityAsync(
                    addGigRequest.CaregiverId, addGigRequest.Category);

                if (draftEligibility != null)
                {
                    eligibilityWarning = draftEligibility.Message +
                        " You will need to meet these requirements before publishing.";
                }
            }


            if (addGigRequest.Image1 != null)
            {
                try
                {
                    using var memoryStream = new MemoryStream();
                    await addGigRequest.Image1.CopyToAsync(memoryStream);
                    var imageUri = memoryStream.ToArray();

                    // Now upload imageUri to Cloudinary
                    imageURL = await cloudinaryService.UploadGigImageAsync(imageUri, $"{careGiver.FirstName}{careGiver.LastName}{addGigRequest.PackageName}_gig");
                }
                catch (Exception ex)
                {
                    // Log the image upload error but don't fail the entire gig creation
                    logger.LogWarning(ex, "Failed to upload gig image, proceeding without image");
                    imageURL = null; // Proceed without image
                }
            }



            /// CONVERT DTO TO DOMAIN OBJECT            
            var gig = new Gig
            {
                Title = addGigRequest.Title,
                Category = addGigRequest.Category,
                // SubCategory = addGigRequest.SubCategory,
                SubCategory = string.Join(",", addGigRequest.SubCategory
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())),

                Tags = addGigRequest.Tags,
                PackageType = addGigRequest.PackageType,
                PackageName = addGigRequest.PackageName,

                PackageDetails = addGigRequest.PackageDetails
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                DeliveryTime = addGigRequest.DeliveryTime,
                Price = addGigRequest.Price,
                Image1 = imageURL,

                Status = addGigRequest.Status,
                CaregiverId = addGigRequest.CaregiverId,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                IsUpdatedToPause = false,
                CreatedAt = DateTime.Now,
            };

            await careProDbContext.Gigs.AddAsync(gig);

            await careProDbContext.SaveChangesAsync();


            var gigDTO = new GigDTO()
            {
                Id = gig.Id.ToString(),
                Title = gig.Title,
                Category = gig.Category,
                SubCategory = gig.SubCategory
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList(),
                Tags = gig.Tags,
                PackageType = gig.PackageType,
                PackageName = gig.PackageName,
                PackageDetails = gig.PackageDetails,
                DeliveryTime = gig.DeliveryTime,
                Price = gig.Price,
                Image1 = gig.Image1,

                Status = gig.Status,
                CaregiverId = gig.CaregiverId,
                CreatedAt = gig.CreatedAt,
                EligibilityWarning = eligibilityWarning,
            };

            return gigDTO;
        }

        public async Task<IEnumerable<GigDTO>> GetAllCaregiverDraftGigsAsync(string caregiverId)
        {
            var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);

            var gigs = await careProDbContext.Gigs
                .Where(x => x.CaregiverId == caregiverId && x.Status == "Draft" && x.IsDeleted != true)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            var gigsDTOs = new List<GigDTO>();

            foreach (var gig in gigs)
            {

                var gigDTO = new GigDTO()
                {
                    Id = gig.Id.ToString(),
                    Title = gig.Title,

                    Category = gig.Category,
                    //SubCategory = gig.SubCategory,
                    SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                    Tags = gig.Tags,
                    PackageType = gig.PackageType,
                    PackageName = gig.PackageName,
                    PackageDetails = gig.PackageDetails,
                    DeliveryTime = gig.DeliveryTime,
                    Price = gig.Price,
                    Image1 = gig.Image1,

                    VideoURL = caregiver.IntroVideo,
                    Status = gig.Status,
                    CaregiverId = gig.CaregiverId,
                    CreatedAt = gig.CreatedAt,

                };
                gigsDTOs.Add(gigDTO);
            }

            return gigsDTOs;
        }

        public async Task<IEnumerable<GigDTO>> GetAllCaregiverGigsAsync(string caregiverId)
        {
            var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);

            if (caregiver == null)
            {
                throw new KeyNotFoundException($"Caregiver with ID:{caregiverId} Not found");
            }


            var gigs = await careProDbContext.Gigs
                .Where(x => x.CaregiverId == caregiverId && x.IsDeleted != true)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            var gigsDTOs = new List<GigDTO>();

            foreach (var gig in gigs)
            {

                var gigDTO = new GigDTO()
                {
                    Id = gig.Id.ToString(),
                    Title = gig.Title,

                    Category = gig.Category,
                    //SubCategory = gig.SubCategory,
                    SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                    Tags = gig.Tags,
                    PackageType = gig.PackageType,
                    PackageName = gig.PackageName,
                    PackageDetails = gig.PackageDetails,
                    DeliveryTime = gig.DeliveryTime,
                    Price = gig.Price,
                    Image1 = gig.Image1,
                    //VideoURL = gig.VideoURL,
                    VideoURL = caregiver.IntroVideo,
                    Status = gig.Status,
                    CaregiverId = gig.CaregiverId,
                    CreatedAt = gig.CreatedAt,

                };
                gigsDTOs.Add(gigDTO);
            }

            return gigsDTOs;
        }

        public async Task<IEnumerable<GigDTO>> GetAllCaregiverPausedGigsAsync(string caregiverId)
        {
            var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);

            if (caregiver == null)
            {
                throw new KeyNotFoundException($"Caregiver with ID:{caregiverId} Not found");
            }

            var gigs = await careProDbContext.Gigs
                .Where(x => x.CaregiverId == caregiverId && x.Status == "Paused" && x.IsDeleted != true)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            var gigsDTOs = new List<GigDTO>();

            foreach (var gig in gigs)
            {

                var gigDTO = new GigDTO()
                {
                    Id = gig.Id.ToString(),
                    Title = gig.Title,

                    Category = gig.Category,
                    //SubCategory = gig.SubCategory,
                    SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                    Tags = gig.Tags,
                    PackageType = gig.PackageType,
                    PackageName = gig.PackageName,
                    PackageDetails = gig.PackageDetails,
                    DeliveryTime = gig.DeliveryTime,
                    Price = gig.Price,
                    Image1 = gig.Image1,

                    VideoURL = caregiver.IntroVideo,
                    Status = gig.Status,
                    CaregiverId = gig.CaregiverId,
                    CreatedAt = gig.CreatedAt,
                    UpdatedOn = gig.UpdatedOn,
                    IsUpdatedToPause = gig.IsUpdatedToPause,

                };
                gigsDTOs.Add(gigDTO);
            }

            return gigsDTOs;
        }

        public async Task<IEnumerable<GigDTO>> GetAllGigsAsync()
        {
            var gigs = await careProDbContext.Gigs
                .Where(x => (x.Status == "Published" || x.Status == "Active") && x.IsDeleted != true)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            var gigDTOs = new List<GigDTO>();

            foreach (var gig in gigs)
            {
                var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);

                if (caregiver == null)
                {
                    continue;
                    // throw new KeyNotFoundException($"Caregiver with ID:{caregiverId} Not found");
                }

                var serviceDTO = new GigDTO()
                {
                    Id = gig.Id.ToString(),
                    Title = gig.Title,
                    Category = gig.Category,
                    //SubCategory = gig.SubCategory,
                    SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                    Tags = gig.Tags,
                    PackageType = gig.PackageType,
                    PackageName = gig.PackageName,
                    PackageDetails = gig.PackageDetails,
                    DeliveryTime = gig.DeliveryTime,
                    Price = gig.Price,
                    Image1 = gig.Image1,

                    Status = gig.Status,
                    CaregiverId = gig.CaregiverId,
                    UpdatedOn = gig.UpdatedOn,
                    IsUpdatedToPause = gig.IsUpdatedToPause,
                    CreatedAt = gig.CreatedAt,
                };
                gigDTOs.Add(serviceDTO);
            }

            return gigDTOs;
        }



        public async Task<List<string>> GetAllSubCategoriesForCaregiverAsync(string caregiverId)
        {
            var subCategories = await careProDbContext.Gigs
                .Where(x => (x.Status == "Published" || x.Status == "Active") && x.CaregiverId == caregiverId && x.IsDeleted != true)
                .Select(x => x.SubCategory)
                .ToListAsync();

            var allSubCategories = subCategories
                .Where(sc => !string.IsNullOrEmpty(sc))
                .SelectMany(sc => sc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(sc => sc.Trim())
                .Distinct()
                .ToList();

            return allSubCategories;
        }


        public async Task<GigDTO> GetGigAsync(string gigId)
        {


            var gig = await careProDbContext.Gigs.FirstOrDefaultAsync(x => x.Id.ToString() == gigId && x.IsDeleted != true);

            if (gig == null)
            {
                throw new KeyNotFoundException($"Gig with ID '{gigId}' not found.");
            }

            var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);

            if (caregiver == null)
            {
                throw new KeyNotFoundException($"Caregiver with ID:{gig.CaregiverId} Not found");
            }



            var gigDTO = new GigDTO()
            {
                Id = gig.Id.ToString(),
                Title = gig.Title,
                Category = gig.Category,
                //SubCategory = gig.SubCategory,
                SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                Tags = gig.Tags,
                PackageType = gig.PackageType,
                PackageName = gig.PackageName,
                PackageDetails = gig.PackageDetails,
                DeliveryTime = gig.DeliveryTime,
                Price = gig.Price,
                Image1 = gig.Image1,

                VideoURL = caregiver.IntroVideo,
                Status = gig.Status,
                CaregiverId = gig.CaregiverId,
                CaregiverName = caregiver.FirstName + " " + caregiver.LastName,
                UpdatedOn = gig.UpdatedOn,
                IsUpdatedToPause = gig.IsUpdatedToPause,
                CreatedAt = gig.CreatedAt,
            };

            return gigDTO;
        }

        public async Task<string> UpdateGigStatusToPauseAsync(string gigId, UpdateGigStatusToPauseRequest updateGigStatusToPauseRequest)
        {
            logger.LogInformation($"Attempting to update gig status. GigId: {gigId}, Requested Status: {updateGigStatusToPauseRequest?.Status}, CaregiverId: {updateGigStatusToPauseRequest?.CaregiverId}");

            try
            {
                // Validate request object
                if (updateGigStatusToPauseRequest == null)
                {
                    throw new ArgumentNullException(nameof(updateGigStatusToPauseRequest), "Update request cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(updateGigStatusToPauseRequest.CaregiverId))
                {
                    throw new ArgumentException("CaregiverId is required.");
                }

                if (string.IsNullOrWhiteSpace(updateGigStatusToPauseRequest.Status))
                {
                    throw new ArgumentException("Status is required.");
                }

                // Validate gigId format
                if (!ObjectId.TryParse(gigId, out var objectId))
                {
                    logger.LogWarning($"Invalid Gig ID format provided: {gigId}");
                    throw new ArgumentException($"Invalid Gig ID format: '{gigId}'. Expected a valid MongoDB ObjectId.");
                }

                // Validate and normalize status
                var normalizedStatus = NormalizeGigStatus(updateGigStatusToPauseRequest.Status);
                if (string.IsNullOrEmpty(normalizedStatus))
                {
                    logger.LogWarning($"Invalid status value provided: {updateGigStatusToPauseRequest.Status}");
                    throw new ArgumentException($"Invalid status value '{updateGigStatusToPauseRequest.Status}'. Allowed values are: 'published', 'draft', 'paused', 'active' (case-insensitive).");
                }

                logger.LogDebug($"Status normalized from '{updateGigStatusToPauseRequest.Status}' to '{normalizedStatus}'");

                // Verify caregiver exists
                var caregiver = await careGiverService.GetCaregiverUserAsync(updateGigStatusToPauseRequest.CaregiverId);
                if (caregiver == null)
                {
                    logger.LogWarning($"Caregiver not found: {updateGigStatusToPauseRequest.CaregiverId}");
                    throw new KeyNotFoundException($"Caregiver with ID '{updateGigStatusToPauseRequest.CaregiverId}' not found.");
                }

                // Find the gig
                var existingGig = await careProDbContext.Gigs.FindAsync(objectId);

                if (existingGig == null)
                {
                    logger.LogWarning($"Gig not found: {gigId}");
                    throw new KeyNotFoundException($"Gig with ID '{gigId}' not found.");
                }

                // Check if gig is deleted
                if (existingGig.IsDeleted == true)
                {
                    logger.LogWarning($"Attempted to update deleted gig: {gigId}");
                    throw new InvalidOperationException($"Cannot update gig with ID '{gigId}' because it has been deleted.");
                }

                // Verify ownership
                if (existingGig.CaregiverId != updateGigStatusToPauseRequest.CaregiverId)
                {
                    logger.LogWarning($"Caregiver {updateGigStatusToPauseRequest.CaregiverId} attempted to update gig {gigId} owned by {existingGig.CaregiverId}");
                    throw new UnauthorizedAccessException($"You do not have permission to update this gig. This gig belongs to a different caregiver.");
                }

                // Log the status change
                var oldStatus = existingGig.Status;
                logger.LogInformation($"Updating gig {gigId} status from '{oldStatus}' to '{normalizedStatus}'");

                // Update the gig
                existingGig.Status = normalizedStatus;
                existingGig.UpdatedOn = DateTime.UtcNow;
                existingGig.IsUpdatedToPause = normalizedStatus == "Paused" || normalizedStatus == "Draft";

                careProDbContext.Gigs.Update(existingGig);
                await careProDbContext.SaveChangesAsync();

                logger.LogInformation($"Successfully updated gig {gigId} status to '{normalizedStatus}'");
                LogAuditEvent($"Gig Status updated from '{oldStatus}' to '{normalizedStatus}' (ID: {gigId})", updateGigStatusToPauseRequest.CaregiverId);
                
                return $"Gig with ID '{gigId}' updated successfully to {normalizedStatus}.";
            }
            catch (ArgumentNullException)
            {
                throw; // Re-throw null argument exceptions as-is (must come before ArgumentException)
            }
            catch (ArgumentException)
            {
                throw; // Re-throw validation exceptions as-is
            }
            catch (KeyNotFoundException)
            {
                throw; // Re-throw not found exceptions as-is
            }
            catch (UnauthorizedAccessException)
            {
                throw; // Re-throw authorization exceptions as-is
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw invalid operation exceptions as-is
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Unexpected error updating gig status. GigId: {gigId}, Status: {updateGigStatusToPauseRequest?.Status}");
                throw new Exception($"An unexpected error occurred while updating the gig status: {ex.Message}", ex);
            }
        }

        private string? NormalizeGigStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            var normalizedStatus = status.Trim().ToLowerInvariant();

            return normalizedStatus switch
            {
                "published" => "Published",
                "draft" => "Draft",
                "paused" => "Paused",
                "active" => "Active",
                _ => null
            };
        }


        public async Task<string> UpdateGigAsync(string gigId, UpdateGigRequest updateGigRequest)
        {
            if (!ObjectId.TryParse(gigId, out var objectId))
            {
                throw new ArgumentException("Invalid Gig ID format.");
            }

            var existingGig = await careProDbContext.Gigs.FindAsync(objectId);

            if (existingGig == null)
            {
                throw new KeyNotFoundException($"Gig with ID '{gigId}' not found.");
            }

            var careGiver = await careGiverService.GetCaregiverUserAsync(existingGig.CaregiverId);
            if (careGiver == null)
            {
                throw new KeyNotFoundException("The CaregiverID entered is not a Valid ID");
            }


            /// Check if caregiver select a category or sub-category before creating a gig
            if (string.IsNullOrWhiteSpace(updateGigRequest.Category) || updateGigRequest.SubCategory == null || !updateGigRequest.SubCategory.Any(s => !string.IsNullOrWhiteSpace(s)))
            {
                throw new ArgumentException("At least A Service and Sub-Category must be selected before you update this Gig");
            }

            // Normalize category against known ServiceRequirements
            var allReqs = await careProDbContext.ServiceRequirements
                .Where(sr => sr.Active)
                .ToListAsync();
            var matchedReq = allReqs
                .FirstOrDefault(sr => string.Equals(sr.ServiceCategory, updateGigRequest.Category, StringComparison.OrdinalIgnoreCase));
            if (matchedReq != null)
            {
                updateGigRequest.Category = matchedReq.ServiceCategory;
            }

            // Server-side eligibility check for specialized categories on publish/active
            if (updateGigRequest.Status == "Published" || updateGigRequest.Status == "Active")
            {
                var eligibilityError = await eligibilityService.ValidateGigEligibilityAsync(
                    existingGig.CaregiverId, updateGigRequest.Category);

                if (eligibilityError != null)
                {
                    throw new UnauthorizedAccessException(
                        System.Text.Json.JsonSerializer.Serialize(eligibilityError));
                }
            }



            if (updateGigRequest.Image1 != null)
            {
                using var memoryStream = new MemoryStream();
                await updateGigRequest.Image1.CopyToAsync(memoryStream);
                var imageUri = memoryStream.ToArray();

                // Now upload imageUri to Cloudinary
                //imageURL = await cloudinaryService.UploadGigImageAsync(imageUri, $"{careGiver.FirstName}{careGiver.LastName}{addGigRequest.PackageName}_gig");
                existingGig.Image1 = await cloudinaryService.UploadGigImageAsync(imageUri, $"{careGiver.FirstName}{careGiver.LastName}{existingGig.PackageName}_gig");

            }


            //// Convert the Base 64 string to a byte array
            //var imageUri = Convert.FromBase64String(updateGigRequest.Image1);



            existingGig.Category = updateGigRequest.Category;
            existingGig.SubCategory = string.Join(",", updateGigRequest.SubCategory
                                        .Where(x => !string.IsNullOrWhiteSpace(x))
                                        .Select(x => x.Trim()));
            existingGig.Tags = updateGigRequest.Tags;
            existingGig.PackageType = updateGigRequest.PackageType;
            existingGig.PackageName = updateGigRequest.PackageName;
            existingGig.PackageDetails = updateGigRequest.PackageDetails
                                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(x => x.Trim())
                                                .ToList();
            existingGig.DeliveryTime = updateGigRequest.DeliveryTime;
            existingGig.Price = updateGigRequest.Price;
            existingGig.Status = updateGigRequest.Status;
            // existingGig.Image1 = imageUri;
            existingGig.UpdatedOn = DateTime.Now;


            careProDbContext.Gigs.Update(existingGig);
            await careProDbContext.SaveChangesAsync();

            LogAuditEvent($"Gig with (ID: {gigId}) successfully updated", updateGigRequest.CaregiverId);
            return $"Gig with ID '{gigId}' updated successfully.";

        }

        public async Task<string> SoftDeleteGigAsync(string gigId, string caregiverId)
        {
            try
            {
                if (!ObjectId.TryParse(gigId, out var objectId))
                {
                    throw new ArgumentException("Invalid Gig ID format.");
                }

                var gig = await careProDbContext.Gigs.FindAsync(objectId);

                if (gig == null)
                {
                    throw new KeyNotFoundException($"Gig with ID '{gigId}' not found.");
                }

                // Verify that the caregiver owns this gig
                if (gig.CaregiverId != caregiverId)
                {
                    throw new UnauthorizedAccessException($"Caregiver with ID '{caregiverId}' is not authorized to delete this gig.");
                }

                // Check if already deleted
                if (gig.IsDeleted == true)
                {
                    throw new InvalidOperationException($"Gig with ID '{gigId}' is already deleted.");
                }

                gig.IsDeleted = true;
                gig.DeletedOn = DateTime.UtcNow;

                careProDbContext.Gigs.Update(gig);
                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Gig soft deleted (ID: {gigId})", caregiverId);
                return $"Gig with ID '{gigId}' has been successfully deleted.";
            }
            catch (Exception ex)
            {
                LogException(ex);
                throw;
            }
        }

        public string GetImageFormat(byte[] imageData)
        {
            // Basic detection of common image formats based on header bytes
            if (imageData.Length >= 4)
            {
                // PNG: 89 50 4E 47
                if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
                    return "png";

                // JPEG/JPG: FF D8 FF
                if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
                    return "jpeg";

                // GIF: 47 49 46
                if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
                    return "gif";
            }
            return "jpeg";  // Default to jpeg if format is not identifiable
        }


        private void LogException(Exception ex)
        {
            logger.LogError(ex, "Exception occurred");
        }

        private void LogAuditEvent(object message, string? caregiverId)
        {
            logger.LogInformation($"Audit Event: {message}. User ID: {caregiverId}. Timestamp: {DateTime.UtcNow}");
        }


    }
}