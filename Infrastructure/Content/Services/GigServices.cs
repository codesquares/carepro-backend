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

            // If gig is published/active, requeue unmatched care requests in the same category
            if (gig.Status == "Published" || gig.Status == "Active")
            {
                await RequeueUnmatchedCareRequestsAsync(gig.Category);
            }


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
                IsSpecialGig = gig.IsSpecialGig,
                CareRequestId = gig.CareRequestId,
                ScopedClientId = gig.ScopedClientId,
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
                    IsSpecialGig = gig.IsSpecialGig,
                    CareRequestId = gig.CareRequestId,
                    ScopedClientId = gig.ScopedClientId,

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
                    IsSpecialGig = gig.IsSpecialGig,
                    CareRequestId = gig.CareRequestId,
                    ScopedClientId = gig.ScopedClientId,

                };
                gigsDTOs.Add(gigDTO);
            }

            return gigsDTOs;
        }

        public async Task<IEnumerable<GigDTO>> GetAllGigsAsync()
        {
            var gigs = await careProDbContext.Gigs
                .Where(x => (x.Status == "Published" || x.Status == "Active") && x.IsDeleted != true && x.IsSpecialGig != true)
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
                    IsSpecialGig = gig.IsSpecialGig,
                    CareRequestId = gig.CareRequestId,
                    ScopedClientId = gig.ScopedClientId,
                };
                gigDTOs.Add(serviceDTO);
            }

            return gigDTOs;
        }

        public async Task<PaginatedResponse<GigDTO>> GetAllGigsPaginatedAsync(int page = 1, int pageSize = 20, string? status = null, string? search = null, string? category = null)
        {
            var query = careProDbContext.Gigs.Where(x => x.IsDeleted != true);

            // Exclude special gigs (created from care request hires) from marketplace
            query = query.Where(x => x.IsSpecialGig != true);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);
            else
                query = query.Where(x => x.Status == "Published" || x.Status == "Active");

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(x => x.Category == category);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.Title.ToLower().Contains(search.ToLower()));

            var totalCount = await query.CountAsync();

            var gigs = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var gigDTOs = new List<GigDTO>();

            foreach (var gig in gigs)
            {
                var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);
                if (caregiver == null) continue;

                var serviceDTO = new GigDTO()
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
                    UpdatedOn = gig.UpdatedOn,
                    IsUpdatedToPause = gig.IsUpdatedToPause,
                    CreatedAt = gig.CreatedAt,
                    IsSpecialGig = gig.IsSpecialGig,
                    CareRequestId = gig.CareRequestId,
                    ScopedClientId = gig.ScopedClientId,
                };
                gigDTOs.Add(serviceDTO);
            }

            return new PaginatedResponse<GigDTO>
            {
                Items = gigDTOs,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = (page * pageSize) < totalCount,
            };
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
                IsSpecialGig = gig.IsSpecialGig,
                CareRequestId = gig.CareRequestId,
                ScopedClientId = gig.ScopedClientId,
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

                // If gig became active/published, requeue unmatched care requests in the same category
                if ((normalizedStatus == "Published" || normalizedStatus == "Active")
                    && oldStatus != normalizedStatus)
                {
                    await RequeueUnmatchedCareRequestsAsync(existingGig.Category);
                }

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

            if (existingGig == null || existingGig.IsDeleted == true)
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

            // If gig became active/published, requeue unmatched care requests in the same category
            if (existingGig.Status == "Published" || existingGig.Status == "Active")
            {
                await RequeueUnmatchedCareRequestsAsync(existingGig.Category);
            }

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

                // ── GDPR Phase 2: Block deletion if active obligations exist ──

                // Block if any active contracts exist for this gig
                var activeContractStatuses = new[]
                {
                    ContractStatus.Draft, ContractStatus.PendingClientApproval,
                    ContractStatus.ClientReviewRequested, ContractStatus.Revised,
                    ContractStatus.Approved, ContractStatus.Generated,
                    ContractStatus.Sent, ContractStatus.Pending,
                    ContractStatus.Accepted, ContractStatus.ReviewRequested,
                    ContractStatus.UnderReview
                };
                var hasActiveContracts = await careProDbContext.Contracts
                    .AnyAsync(c => c.GigId == gigId && activeContractStatuses.Contains(c.Status));
                if (hasActiveContracts)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete gig '{gigId}' because it has active contracts. Please complete or terminate all contracts first.");
                }

                // Block if any active subscriptions exist
                var activeSubStatuses = new[]
                {
                    SubscriptionStatus.Active, SubscriptionStatus.PendingCancellation,
                    SubscriptionStatus.PastDue, SubscriptionStatus.Charging,
                    SubscriptionStatus.Paused
                };
                var hasActiveSubscriptions = await careProDbContext.Subscriptions
                    .AnyAsync(s => s.GigId == gigId && activeSubStatuses.Contains(s.Status));
                if (hasActiveSubscriptions)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete gig '{gigId}' because it has active subscriptions. Please cancel all subscriptions first.");
                }

                // Block if any in-progress or disputed orders exist
                var hasActiveOrders = await careProDbContext.ClientOrders
                    .AnyAsync(o => o.GigId == gigId
                        && o.ClientOrderStatus != null
                        && o.ClientOrderStatus != "Completed");
                if (hasActiveOrders)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete gig '{gigId}' because it has active orders. Please complete or resolve all orders first.");
                }

                // ── Cascade: Cancel draft/pending OrderTasks ──
                var pendingOrderTasks = await careProDbContext.OrderTasks
                    .Where(ot => ot.GigId == gigId
                        && ot.Status != OrderTasksStatus.Completed
                        && ot.Status != OrderTasksStatus.Cancelled
                        && ot.Status != OrderTasksStatus.Expired)
                    .ToListAsync();
                foreach (var ot in pendingOrderTasks)
                {
                    ot.Status = OrderTasksStatus.Cancelled;
                    careProDbContext.OrderTasks.Update(ot);
                }

                // ── Cascade: Expire pending PendingPayments ──
                var pendingPayments = await careProDbContext.PendingPayments
                    .Where(pp => pp.GigId == gigId && pp.Status == PendingPaymentStatus.Pending)
                    .ToListAsync();
                foreach (var pp in pendingPayments)
                {
                    pp.Status = PendingPaymentStatus.Expired;
                    careProDbContext.PendingPayments.Update(pp);
                }

                // ── Cascade: Expire pending BookingCommitments ──
                var pendingCommitments = await careProDbContext.BookingCommitments
                    .Where(bc => bc.GigId == gigId && bc.Status == BookingCommitmentStatus.Pending)
                    .ToListAsync();
                foreach (var bc in pendingCommitments)
                {
                    bc.Status = BookingCommitmentStatus.Expired;
                    careProDbContext.BookingCommitments.Update(bc);
                }

                // ── Soft-delete the gig ──
                gig.IsDeleted = true;
                gig.DeletedOn = DateTime.UtcNow;
                careProDbContext.Gigs.Update(gig);

                await careProDbContext.SaveChangesAsync();

                logger.LogInformation(
                    "Gig {GigId} soft-deleted by caregiver {CaregiverId}. Cascaded: {OrderTasksCancelled} order tasks cancelled, {PaymentsExpired} pending payments expired, {CommitmentsExpired} booking commitments expired",
                    gigId, caregiverId, pendingOrderTasks.Count, pendingPayments.Count, pendingCommitments.Count);

                LogAuditEvent($"Gig soft deleted (ID: {gigId}). Cascaded: {pendingOrderTasks.Count} order tasks cancelled, {pendingPayments.Count} payments expired, {pendingCommitments.Count} commitments expired", caregiverId);
                return $"Gig with ID '{gigId}' has been successfully deleted.";
            }
            catch (Exception ex)
            {
                LogException(ex);
                throw;
            }
        }

        public async Task<AdminBulkDeleteResult> AdminBulkSoftDeleteGigsAsync(List<string>? gigIds, bool deleteAll, string adminUserId)
        {
            var result = new AdminBulkDeleteResult();

            // Fetch target gigs using IgnoreQueryFilters to include all records,
            // then filter to only non-deleted gigs (handles existing records where IsDeleted is null or false)
            List<Gig> targetGigs;
            if (deleteAll)
            {
                targetGigs = await careProDbContext.Gigs
                    .Where(g => g.IsDeleted != true)
                    .ToListAsync();
            }
            else
            {
                if (gigIds == null || !gigIds.Any())
                {
                    throw new ArgumentException("No gig IDs provided.");
                }

                // Validate all IDs are valid ObjectId format before querying
                var validIds = new List<string>();
                foreach (var id in gigIds)
                {
                    if (!ObjectId.TryParse(id, out _))
                    {
                        result.SkippedCount++;
                        result.SkippedGigIds.Add(id);
                        result.SkippedReasons.Add($"Invalid ID format: {id}");
                        continue;
                    }
                    validIds.Add(id);
                }

                targetGigs = await careProDbContext.Gigs
                    .Where(g => validIds.Contains(g.Id.ToString()) && g.IsDeleted != true)
                    .ToListAsync();

                // Track IDs that weren't found (already deleted or never existed)
                var foundIds = targetGigs.Select(g => g.Id.ToString()).ToHashSet();
                foreach (var id in validIds.Where(id => !foundIds.Contains(id)))
                {
                    result.SkippedCount++;
                    result.SkippedGigIds.Add(id);
                    result.SkippedReasons.Add($"Not found or already deleted: {id}");
                }
            }

            if (!targetGigs.Any())
            {
                result.Message = "No eligible gigs found to delete.";
                return result;
            }

            logger.LogWarning(
                "ADMIN BULK DELETE initiated by {AdminUserId}: {Count} gigs targeted",
                adminUserId, targetGigs.Count);

            foreach (var gig in targetGigs)
            {
                var gigId = gig.Id.ToString();

                try
                {
                    // Check for active obligations — skip gigs that cannot be safely deleted
                    var activeContractStatuses = new[]
                    {
                        ContractStatus.Draft, ContractStatus.PendingClientApproval,
                        ContractStatus.ClientReviewRequested, ContractStatus.Revised,
                        ContractStatus.Approved, ContractStatus.Generated,
                        ContractStatus.Sent, ContractStatus.Pending,
                        ContractStatus.Accepted, ContractStatus.ReviewRequested,
                        ContractStatus.UnderReview
                    };
                    var hasActiveContracts = await careProDbContext.Contracts
                        .AnyAsync(c => c.GigId == gigId && activeContractStatuses.Contains(c.Status));

                    var activeSubStatuses = new[]
                    {
                        SubscriptionStatus.Active, SubscriptionStatus.PendingCancellation,
                        SubscriptionStatus.PastDue, SubscriptionStatus.Charging,
                        SubscriptionStatus.Paused
                    };
                    var hasActiveSubscriptions = await careProDbContext.Subscriptions
                        .AnyAsync(s => s.GigId == gigId && activeSubStatuses.Contains(s.Status));

                    var hasActiveOrders = await careProDbContext.ClientOrders
                        .AnyAsync(o => o.GigId == gigId
                            && o.ClientOrderStatus != null
                            && o.ClientOrderStatus != "Completed");

                    if (hasActiveContracts || hasActiveSubscriptions || hasActiveOrders)
                    {
                        result.SkippedCount++;
                        result.SkippedGigIds.Add(gigId);
                        var reason = hasActiveContracts ? "active contracts"
                            : hasActiveSubscriptions ? "active subscriptions"
                            : "active orders";
                        result.SkippedReasons.Add($"Skipped {gigId}: has {reason}");
                        continue;
                    }

                    // Cascade: Cancel pending OrderTasks
                    var pendingOrderTasks = await careProDbContext.OrderTasks
                        .Where(ot => ot.GigId == gigId
                            && ot.Status != OrderTasksStatus.Completed
                            && ot.Status != OrderTasksStatus.Cancelled
                            && ot.Status != OrderTasksStatus.Expired)
                        .ToListAsync();
                    foreach (var ot in pendingOrderTasks)
                    {
                        ot.Status = OrderTasksStatus.Cancelled;
                        careProDbContext.OrderTasks.Update(ot);
                    }

                    // Cascade: Expire pending PendingPayments
                    var pendingPayments = await careProDbContext.PendingPayments
                        .Where(pp => pp.GigId == gigId && pp.Status == PendingPaymentStatus.Pending)
                        .ToListAsync();
                    foreach (var pp in pendingPayments)
                    {
                        pp.Status = PendingPaymentStatus.Expired;
                        careProDbContext.PendingPayments.Update(pp);
                    }

                    // Cascade: Expire pending BookingCommitments
                    var pendingCommitments = await careProDbContext.BookingCommitments
                        .Where(bc => bc.GigId == gigId && bc.Status == BookingCommitmentStatus.Pending)
                        .ToListAsync();
                    foreach (var bc in pendingCommitments)
                    {
                        bc.Status = BookingCommitmentStatus.Expired;
                        careProDbContext.BookingCommitments.Update(bc);
                    }

                    // Soft-delete the gig
                    gig.IsDeleted = true;
                    gig.DeletedOn = DateTime.UtcNow;
                    careProDbContext.Gigs.Update(gig);

                    result.DeletedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    logger.LogError(ex, "Admin bulk delete: Failed to process gig {GigId}", gigId);
                }
            }

            await careProDbContext.SaveChangesAsync();

            result.Message = $"Bulk delete complete. Deleted: {result.DeletedCount}, Skipped: {result.SkippedCount}, Failed: {result.FailedCount}";

            LogAuditEvent(
                $"ADMIN BULK SOFT-DELETE: {result.DeletedCount} gigs deleted, {result.SkippedCount} skipped, {result.FailedCount} failed. DeleteAll={deleteAll}",
                adminUserId);

            return result;
        }

        public async Task<string> RestoreGigAsync(string gigId, string caregiverId)
        {
            if (!ObjectId.TryParse(gigId, out var objectId))
            {
                throw new ArgumentException("Invalid Gig ID format.");
            }

            // Must use IgnoreQueryFilters — the MongoDB EF Core provider may apply
            // global query filters even on FindAsync, unlike the relational providers.
            var gig = await careProDbContext.Gigs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(g => g.Id == objectId);

            if (gig == null)
            {
                throw new KeyNotFoundException($"Gig with ID '{gigId}' not found.");
            }

            // Verify ownership
            if (gig.CaregiverId != caregiverId)
            {
                throw new UnauthorizedAccessException($"Caregiver with ID '{caregiverId}' is not authorized to restore this gig.");
            }

            // Must actually be deleted to restore
            if (gig.IsDeleted != true)
            {
                throw new InvalidOperationException($"Gig with ID '{gigId}' is not deleted.");
            }

            // Enforce 30-day grace period — cannot restore after hard-delete window
            if (gig.DeletedOn.HasValue && gig.DeletedOn.Value.AddDays(30) < DateTime.UtcNow)
            {
                throw new InvalidOperationException(
                    $"Gig with ID '{gigId}' was deleted more than 30 days ago and can no longer be restored. Please contact support.");
            }

            // Restore the gig — set to Draft so caregiver must explicitly re-publish
            gig.IsDeleted = false;
            gig.DeletedOn = null;
            gig.Status = "Draft";
            gig.UpdatedOn = DateTime.UtcNow;

            careProDbContext.Gigs.Update(gig);
            await careProDbContext.SaveChangesAsync();

            logger.LogInformation(
                "Gig {GigId} restored by caregiver {CaregiverId}. Status set to Draft.",
                gigId, caregiverId);

            LogAuditEvent($"Gig restored (ID: {gigId}). Status set to Draft for review.", caregiverId);
            return $"Gig with ID '{gigId}' has been restored successfully. It has been set to Draft status — please review and republish.";
        }

        public async Task<IEnumerable<DeletedGigDTO>> GetDeletedGigsByCaregiverAsync(string caregiverId)
        {
            var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);
            if (caregiver == null)
            {
                throw new KeyNotFoundException($"Caregiver with ID '{caregiverId}' not found.");
            }

            var deletedGigs = await careProDbContext.Gigs
                .IgnoreQueryFilters()
                .Where(g => g.CaregiverId == caregiverId && g.IsDeleted == true && g.DeletedOn != null)
                .OrderByDescending(g => g.DeletedOn)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var dtos = new List<DeletedGigDTO>();

            foreach (var gig in deletedGigs)
            {
                var daysSinceDeletion = (now - gig.DeletedOn!.Value).Days;
                var daysRemaining = Math.Max(0, 30 - daysSinceDeletion);

                dtos.Add(new DeletedGigDTO
                {
                    Id = gig.Id.ToString(),
                    Title = gig.Title,
                    Category = gig.Category,
                    SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                    PackageType = gig.PackageType,
                    PackageName = gig.PackageName,
                    Price = gig.Price,
                    Image1 = gig.Image1,
                    CaregiverId = gig.CaregiverId,
                    CaregiverName = $"{caregiver.FirstName} {caregiver.LastName}".Trim(),
                    CreatedAt = gig.CreatedAt,
                    DeletedOn = gig.DeletedOn,
                    DaysRemaining = daysRemaining,
                    CanRestore = daysRemaining > 0,
                });
            }

            return dtos;
        }

        public async Task<PaginatedResponse<DeletedGigDTO>> GetAllDeletedGigsPaginatedAsync(int page = 1, int pageSize = 20, string? caregiverId = null)
        {
            var query = careProDbContext.Gigs
                .IgnoreQueryFilters()
                .Where(g => g.IsDeleted == true && g.DeletedOn != null);

            if (!string.IsNullOrWhiteSpace(caregiverId))
            {
                query = query.Where(g => g.CaregiverId == caregiverId);
            }

            var totalCount = await query.CountAsync();

            var deletedGigs = await query
                .OrderByDescending(g => g.DeletedOn)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var dtos = new List<DeletedGigDTO>();

            foreach (var gig in deletedGigs)
            {
                var daysSinceDeletion = (now - gig.DeletedOn!.Value).Days;
                var daysRemaining = Math.Max(0, 30 - daysSinceDeletion);

                string caregiverName = "";
                try
                {
                    var cg = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);
                    caregiverName = cg != null ? $"{cg.FirstName} {cg.LastName}".Trim() : "";
                }
                catch { }

                dtos.Add(new DeletedGigDTO
                {
                    Id = gig.Id.ToString(),
                    Title = gig.Title,
                    Category = gig.Category,
                    SubCategory = gig.SubCategory
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList(),
                    PackageType = gig.PackageType,
                    PackageName = gig.PackageName,
                    Price = gig.Price,
                    Image1 = gig.Image1,
                    CaregiverId = gig.CaregiverId,
                    CaregiverName = caregiverName,
                    CreatedAt = gig.CreatedAt,
                    DeletedOn = gig.DeletedOn,
                    DaysRemaining = daysRemaining,
                    CanRestore = daysRemaining > 0,
                });
            }

            return new PaginatedResponse<DeletedGigDTO>
            {
                Items = dtos,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = (page * pageSize) < totalCount,
            };
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

        /// <summary>
        /// Reset unmatched care requests in the same category back to pending
        /// so the background processor will re-evaluate them on its next cycle.
        /// </summary>
        private async Task RequeueUnmatchedCareRequestsAsync(string gigCategory)
        {
            var unmatchedRequests = await careProDbContext.CareRequests
                .Where(cr => cr.Status == "unmatched"
                    && cr.ServiceCategory == gigCategory)
                .ToListAsync();

            if (unmatchedRequests.Count == 0) return;

            foreach (var cr in unmatchedRequests)
            {
                cr.Status = "pending";
                cr.UpdatedAt = DateTime.UtcNow;
            }

            careProDbContext.CareRequests.UpdateRange(unmatchedRequests);
            await careProDbContext.SaveChangesAsync();

            logger.LogInformation(
                "Requeued {Count} unmatched care requests in category '{Category}' for re-matching",
                unmatchedRequests.Count, gigCategory);
        }

    }
}