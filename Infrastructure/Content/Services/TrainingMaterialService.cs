using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class TrainingMaterialService : ITrainingMaterialService
    {
        private readonly CareProDbContext _context;
        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<TrainingMaterialService> _logger;

        private readonly string[] _allowedFileTypes = { ".pdf", ".doc", ".docx", ".mp4", ".avi", ".mov" };
        private readonly long _maxFileSize = 50 * 1024 * 1024; // 50MB

        public TrainingMaterialService(
            CareProDbContext context,
            CloudinaryService cloudinaryService,
            ILogger<TrainingMaterialService> logger)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        public async Task<TrainingMaterialUploadResponse> UploadTrainingMaterialAsync(AddTrainingMaterialRequest request)
        {
            try
            {
                // Validate file
                ValidateFile(request.File);

                // Determine file type
                var fileExtension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
                var fileType = GetFileType(fileExtension);

                // Read file bytes
                using var memoryStream = new MemoryStream();
                await request.File.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                // Upload to Cloudinary
                string cloudinaryUrl;
                string publicId;

                if (fileType == TrainingMaterialFileType.PDF || fileType == TrainingMaterialFileType.Document)
                {
                    (cloudinaryUrl, publicId) = await _cloudinaryService.UploadPdfAsync(fileBytes, request.File.FileName);
                }
                else if (fileType == TrainingMaterialFileType.Video)
                {
                    cloudinaryUrl = await _cloudinaryService.UploadVideoAsync(fileBytes, request.File.FileName);
                    publicId = ExtractPublicIdFromUrl(cloudinaryUrl); // Helper method to extract public ID
                }
                else
                {
                    throw new ArgumentException($"Unsupported file type: {fileExtension}");
                }

                // Create entity
                var trainingMaterial = new TrainingMaterial
                {
                    Id = ObjectId.GenerateNewId(),
                    Title = request.Title,
                    UserType = request.UserType,
                    FileType = fileType,
                    CloudinaryUrl = cloudinaryUrl,
                    FileName = request.File.FileName,
                    FileSize = request.File.Length,
                    Description = request.Description,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    UploadedBy = request.UploadedBy,
                    CloudinaryPublicId = publicId
                };

                // Save to database
                await _context.TrainingMaterials.AddAsync(trainingMaterial);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Training material '{Title}' uploaded successfully with ID: {Id}",
                    request.Title, trainingMaterial.Id);

                return new TrainingMaterialUploadResponse
                {
                    Id = trainingMaterial.Id.ToString(),
                    Title = trainingMaterial.Title,
                    FileName = trainingMaterial.FileName,
                    FileSize = trainingMaterial.FileSize,
                    CloudinaryUrl = trainingMaterial.CloudinaryUrl,
                    Success = true,
                    Message = "Training material uploaded successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading training material: {Title}", request.Title);

                return new TrainingMaterialUploadResponse
                {
                    Success = false,
                    Message = $"Upload failed: {ex.Message}"
                };
            }
        }

        public async Task<TrainingMaterialDTO?> GetTrainingMaterialByIdAsync(string id)
        {
            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                {
                    return null;
                }

                var material = await _context.TrainingMaterials
                    .FirstOrDefaultAsync(tm => tm.Id == objectId);

                return material != null ? MapToDTO(material) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting training material by ID: {Id}", id);
                throw;
            }
        }

        public async Task<List<TrainingMaterialDTO>> GetAllTrainingMaterialsAsync()
        {
            try
            {
                var materials = await _context.TrainingMaterials
                    .OrderByDescending(tm => tm.CreatedAt)
                    .ToListAsync();

                return materials.Select(MapToDTO).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all training materials");
                throw;
            }
        }

        public async Task<TrainingMaterialListResponse> GetTrainingMaterialsByUserTypeAsync(string userType, bool activeOnly = true)
        {
            try
            {
                var query = _context.TrainingMaterials.AsQueryable();

                // Filter by user type (include "Both" as well)
                query = query.Where(tm => tm.UserType == userType || tm.UserType == TrainingMaterialUserType.Both);

                if (activeOnly)
                {
                    query = query.Where(tm => tm.IsActive);
                }

                var materials = await query
                    .OrderByDescending(tm => tm.CreatedAt)
                    .ToListAsync();

                return new TrainingMaterialListResponse
                {
                    Materials = materials.Select(MapToDTO).ToList(),
                    TotalCount = materials.Count,
                    UserType = userType
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting training materials for user type: {UserType}", userType);
                throw;
            }
        }

        public async Task<TrainingMaterialDTO?> GetActiveTrainingMaterialAsync(string userType, string materialType = "PDF")
        {
            try
            {
                var material = await _context.TrainingMaterials
                    .Where(tm => (tm.UserType == userType || tm.UserType == TrainingMaterialUserType.Both)
                                 && tm.FileType == materialType
                                 && tm.IsActive)
                    .OrderByDescending(tm => tm.CreatedAt)
                    .FirstOrDefaultAsync();

                return material != null ? MapToDTO(material) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active training material for user type: {UserType}, material type: {MaterialType}",
                    userType, materialType);
                throw;
            }
        }

        public async Task<bool> UpdateTrainingMaterialAsync(UpdateTrainingMaterialRequest request)
        {
            try
            {
                if (!ObjectId.TryParse(request.Id, out var objectId))
                {
                    throw new ArgumentException("Invalid training material ID format");
                }

                var material = await _context.TrainingMaterials
                    .FirstOrDefaultAsync(tm => tm.Id == objectId);

                if (material == null)
                {
                    throw new KeyNotFoundException($"Training material with ID '{request.Id}' not found");
                }

                // Update fields if provided
                if (!string.IsNullOrEmpty(request.Title))
                    material.Title = request.Title;

                if (!string.IsNullOrEmpty(request.UserType))
                    material.UserType = request.UserType;

                if (request.Description != null)
                    material.Description = request.Description;

                if (request.IsActive.HasValue)
                    material.IsActive = request.IsActive.Value;

                // Handle file replacement
                if (request.File != null)
                {
                    ValidateFile(request.File);

                    // Delete old file from Cloudinary
                    if (!string.IsNullOrEmpty(material.CloudinaryPublicId))
                    {
                        await _cloudinaryService.DeleteFileAsync(material.CloudinaryPublicId);
                    }

                    // Upload new file
                    var fileExtension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
                    var fileType = GetFileType(fileExtension);

                    using var memoryStream = new MemoryStream();
                    await request.File.CopyToAsync(memoryStream);
                    var fileBytes = memoryStream.ToArray();

                    string cloudinaryUrl;
                    string publicId;

                    if (fileType == TrainingMaterialFileType.PDF || fileType == TrainingMaterialFileType.Document)
                    {
                        (cloudinaryUrl, publicId) = await _cloudinaryService.UploadPdfAsync(fileBytes, request.File.FileName);
                    }
                    else if (fileType == TrainingMaterialFileType.Video)
                    {
                        cloudinaryUrl = await _cloudinaryService.UploadVideoAsync(fileBytes, request.File.FileName);
                        publicId = ExtractPublicIdFromUrl(cloudinaryUrl);
                    }
                    else
                    {
                        throw new ArgumentException($"Unsupported file type: {fileExtension}");
                    }

                    material.FileType = fileType;
                    material.CloudinaryUrl = cloudinaryUrl;
                    material.FileName = request.File.FileName;
                    material.FileSize = request.File.Length;
                    material.CloudinaryPublicId = publicId;
                }

                material.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Training material updated successfully: {Id}", request.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating training material: {Id}", request.Id);
                throw;
            }
        }

        public async Task<bool> DeleteTrainingMaterialAsync(string id)
        {
            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                {
                    throw new ArgumentException("Invalid training material ID format");
                }

                var material = await _context.TrainingMaterials
                    .FirstOrDefaultAsync(tm => tm.Id == objectId);

                if (material == null)
                {
                    throw new KeyNotFoundException($"Training material with ID '{id}' not found");
                }

                // Delete from Cloudinary
                if (!string.IsNullOrEmpty(material.CloudinaryPublicId))
                {
                    await _cloudinaryService.DeleteFileAsync(material.CloudinaryPublicId);
                }

                // Remove from database
                _context.TrainingMaterials.Remove(material);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Training material deleted successfully: {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting training material: {Id}", id);
                throw;
            }
        }

        public async Task<List<TrainingMaterialDTO>> SearchTrainingMaterialsAsync(string searchTerm)
        {
            try
            {
                var materials = await _context.TrainingMaterials
                    .Where(tm => tm.Title.Contains(searchTerm)
                                 || (tm.Description != null && tm.Description.Contains(searchTerm))
                                 || tm.FileName.Contains(searchTerm))
                    .OrderByDescending(tm => tm.CreatedAt)
                    .ToListAsync();

                return materials.Select(MapToDTO).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching training materials with term: {SearchTerm}", searchTerm);
                throw;
            }
        }

        public async Task<bool> ToggleActiveStatusAsync(string id, bool isActive)
        {
            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                {
                    throw new ArgumentException("Invalid training material ID format");
                }

                var material = await _context.TrainingMaterials
                    .FirstOrDefaultAsync(tm => tm.Id == objectId);

                if (material == null)
                {
                    throw new KeyNotFoundException($"Training material with ID '{id}' not found");
                }

                material.IsActive = isActive;
                material.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Training material active status toggled: {Id} -> {IsActive}", id, isActive);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active status for training material: {Id}", id);
                throw;
            }
        }

        // Helper methods
        private void ValidateFile(Microsoft.AspNetCore.Http.IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is required and cannot be empty");
            }

            if (file.Length > _maxFileSize)
            {
                throw new ArgumentException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedFileTypes.Contains(fileExtension))
            {
                throw new ArgumentException($"File type '{fileExtension}' is not allowed. Allowed types: {string.Join(", ", _allowedFileTypes)}");
            }
        }

        private string GetFileType(string fileExtension)
        {
            return fileExtension switch
            {
                ".pdf" => TrainingMaterialFileType.PDF,
                ".doc" or ".docx" => TrainingMaterialFileType.Document,
                ".mp4" or ".avi" or ".mov" => TrainingMaterialFileType.Video,
                _ => throw new ArgumentException($"Unsupported file extension: {fileExtension}")
            };
        }

        private string ExtractPublicIdFromUrl(string cloudinaryUrl)
        {
            // This is a simple implementation - you might need to adjust based on Cloudinary URL structure
            // For now, we'll return a placeholder since video uploads might handle public ID differently
            var uri = new Uri(cloudinaryUrl);
            var pathSegments = uri.Segments;
            if (pathSegments.Length > 0)
            {
                var fileName = pathSegments.Last();
                return Path.GetFileNameWithoutExtension(fileName);
            }
            return string.Empty;
        }

        private TrainingMaterialDTO MapToDTO(TrainingMaterial material)
        {
            return new TrainingMaterialDTO
            {
                Id = material.Id.ToString(),
                Title = material.Title,
                UserType = material.UserType,
                FileType = material.FileType,
                CloudinaryUrl = material.CloudinaryUrl,
                FileName = material.FileName,
                FileSize = material.FileSize,
                Description = material.Description,
                IsActive = material.IsActive,
                CreatedAt = material.CreatedAt,
                UpdatedAt = material.UpdatedAt,
                UploadedBy = material.UploadedBy,
                CloudinaryPublicId = material.CloudinaryPublicId
            };
        }
    }
}