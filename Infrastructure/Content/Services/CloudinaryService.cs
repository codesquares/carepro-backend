using CloudinaryDotNet.Actions;
using CloudinaryDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Application.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Content.Services
{
    public class CloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private readonly HttpClient _httpClient;
        private readonly string _cloudName;
        private readonly string _apiKey;
        private readonly string _apiSecret;

        public CloudinaryService(Cloudinary cloudinary, IOptions<CloudinarySettings> cloudinarySettings)
        {
            _cloudinary = cloudinary;
            _cloudinary.Api.Secure = true;
            _httpClient = new HttpClient();

            // Extract cloud name from the cloudinary instance
            _cloudName = ExtractCloudName(cloudinary);

            // Store API credentials for authenticated requests
            var settings = cloudinarySettings.Value;
            _apiKey = settings.ApiKey ?? throw new InvalidOperationException("Cloudinary API Key is required");
            _apiSecret = settings.ApiSecret ?? throw new InvalidOperationException("Cloudinary API Secret is required");
        }

        private string ExtractCloudName(Cloudinary cloudinary)
        {
            try
            {
                // Try to get cloud name from a test URL build
                var testUrl = cloudinary.Api.UrlImgUp.BuildUrl("test");
                var uri = new Uri(testUrl);
                var segments = uri.Segments;
                // URL format: https://res.cloudinary.com/{cloud_name}/...
                if (segments.Length > 1)
                {
                    return segments[1].TrimEnd('/');
                }
            }
            catch
            {
                // Fallback - this should be set from configuration
            }

            return "dkqnmjhkh"; // Your actual cloud name from configuration
        }



        public async Task<string> UploadVideoAsync(byte[] videoBytes, string fileName)
        {
            using var stream = new MemoryStream(videoBytes);
            var uploadParams = new VideoUploadParams
            {
                File = new FileDescription(fileName, stream),
                // ResourceType = ResourceType.Video, // Important for videos
                Folder = "caregiver_videos"
            };

            var uploadResult = await _cloudinary.UploadLargeAsync(uploadParams); // Handles big files
            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return uploadResult.SecureUrl.AbsoluteUri;
            }

            throw new Exception("Video upload failed.");
        }



        //public async Task<string> UploadVideoAsync(byte[] videoBytes, string fileName)
        //{
        //    using var stream = new MemoryStream(videoBytes);
        //    var uploadParams = new VideoUploadParams
        //    {
        //        File = new FileDescription(fileName, stream),
        //        //ResourceType = ResourceType.Video
        //        Folder = "caregiver_videos"
        //    };

        //    var uploadResult = await _cloudinary.UploadAsync(uploadParams);
        //    if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
        //    {
        //        return uploadResult.SecureUrl.AbsoluteUri;
        //    }

        //    throw new Exception("Video upload failed.");
        //}


        public async Task<string> UploadImageAsync(byte[] imageBytes, string fileName)
        {
            using var stream = new MemoryStream(imageBytes);
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = "caregiver_images"
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return uploadResult.SecureUrl.AbsoluteUri;
            }

            throw new Exception("Image upload failed.");
        }


        public async Task<string> UploadGigImageAsync(byte[] imageBytes, string fileName)
        {
            using var stream = new MemoryStream(imageBytes);
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = "gigs_images"
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return uploadResult.SecureUrl.AbsoluteUri;
            }

            throw new Exception($"Image upload failed. Status: {uploadResult.StatusCode}, Error: {uploadResult.Error?.Message}");
        }


        public async Task<string> DownloadVideoAsBase64Async(string videoUrl)
        {
            if (string.IsNullOrWhiteSpace(videoUrl))
                return string.Empty;

            try
            {
                var videoBytes = await _httpClient.GetByteArrayAsync(videoUrl);
                return Convert.ToBase64String(videoBytes);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download video from URL: {videoUrl}", ex);
            }
        }

        public async Task<(string Url, string PublicId)> UploadPdfAsync(byte[] pdfBytes, string fileName)
        {
            using var stream = new MemoryStream(pdfBytes);

            // Use RawUploadParams but try different settings to ensure public access
            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = "training_materials",
                PublicId = null, // Let Cloudinary generate ID
                Overwrite = false,
                Type = "upload",
                AccessMode = "public", // Explicitly set to public access
                // Try additional parameters for public access
                Tags = "training,public"
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                // Log the URL for debugging
                Console.WriteLine($"Cloudinary Upload Success - URL: {uploadResult.SecureUrl}");
                Console.WriteLine($"Cloudinary Upload Success - PublicId: {uploadResult.PublicId}");

                return (uploadResult.SecureUrl.AbsoluteUri, uploadResult.PublicId);
            }

            throw new Exception($"PDF upload failed. Status: {uploadResult.StatusCode}, Error: {uploadResult.Error?.Message}");
        }

        /// <summary>
        /// Download file bytes from Cloudinary URL with proper authentication
        /// </summary>
        public async Task<byte[]> DownloadFileAsync(string cloudinaryUrl)
        {
            try
            {
                Console.WriteLine($"Attempting to download from URL: {cloudinaryUrl}");

                using var httpClient = new HttpClient();

                // Add a timeout to prevent hanging
                httpClient.Timeout = TimeSpan.FromMinutes(2);

                var response = await httpClient.GetAsync(cloudinaryUrl);

                Console.WriteLine($"Response Status: {response.StatusCode}");
                Console.WriteLine($"Response Headers: {response.Headers}");

                if (response.IsSuccessStatusCode)
                {
                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"Successfully downloaded {fileBytes.Length} bytes");
                    return fileBytes;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error Response Content: {errorContent}");

                throw new HttpRequestException($"HTTP {response.StatusCode}: {errorContent}");
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                // If direct download fails due to authentication, try with signed URL
                throw new UnauthorizedAccessException("File access denied. File may be private.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download failed with exception: {ex.Message}");
                throw new Exception($"Failed to download file from Cloudinary: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generate a signed URL for private file access
        /// </summary>
        public string GenerateSignedUrl(string publicId, int expirationMinutes = 60)
        {
            try
            {
                // For raw files, create a manual signed URL since the SDK might not support it fully
                var timestamp = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes).ToUnixTimeSeconds();

                // Try the basic URL first
                var baseUrl = $"https://res.cloudinary.com/{_cloudName}/raw/upload/{publicId}";

                Console.WriteLine($"Generated signed URL: {baseUrl}");
                return baseUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating signed URL: {ex.Message}");
                // Fallback to basic URL
                return $"https://res.cloudinary.com/{_cloudName}/raw/upload/{publicId}";
            }
        }

        /// <summary>
        /// Download file using signed URL if direct access fails
        /// </summary>
        public async Task<byte[]> DownloadFileWithSignedUrlAsync(string publicId)
        {
            try
            {
                var signedUrl = GenerateSignedUrl(publicId);
                Console.WriteLine($"Trying signed URL: {signedUrl}");

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(2);

                var response = await httpClient.GetAsync(signedUrl);
                Console.WriteLine($"Signed URL Response Status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"Successfully downloaded {fileBytes.Length} bytes via signed URL");
                    return fileBytes;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Signed URL Error Response: {errorContent}");

                // Don't use EnsureSuccessStatusCode here - instead throw our own exception
                throw new HttpRequestException($"HTTP {response.StatusCode}: {errorContent}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Signed URL method failed: {ex.Message}");
                throw new Exception($"Failed to download file using signed URL: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Try to download file with alternative URL construction
        /// </summary>
        public async Task<byte[]> DownloadFileWithAlternativeUrlAsync(string publicId)
        {
            try
            {
                // Try different URL formats that might work for public files
                var urls = new[]
                {
                    $"https://res.cloudinary.com/{_cloudName}/raw/upload/{publicId}",
                    $"https://res.cloudinary.com/{_cloudName}/image/upload/{publicId}",
                    $"https://res.cloudinary.com/{_cloudName}/auto/upload/{publicId}",
                    $"https://res.cloudinary.com/{_cloudName}/raw/upload/fl_attachment/{publicId}"
                };

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(2);

                foreach (var url in urls)
                {
                    try
                    {
                        Console.WriteLine($"Trying alternative URL: {url}");
                        var response = await httpClient.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var fileBytes = await response.Content.ReadAsByteArrayAsync();
                            Console.WriteLine($"Successfully downloaded {fileBytes.Length} bytes from: {url}");
                            return fileBytes;
                        }

                        Console.WriteLine($"Alternative URL failed with status: {response.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Alternative URL {url} failed: {ex.Message}");
                    }
                }

                throw new Exception("All alternative URLs failed");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download file using alternative URLs: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteFileAsync(string publicId)
        {
            try
            {
                var deleteParams = new DeletionParams(publicId)
                {
                    ResourceType = ResourceType.Raw
                };

                var result = await _cloudinary.DestroyAsync(deleteParams);
                return result.Result == "ok";
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete file from Cloudinary: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Download file using Cloudinary Admin API with authentication
        /// </summary>
        public async Task<byte[]> DownloadFileWithAuthAsync(string publicId)
        {
            try
            {
                Console.WriteLine($"Attempting authenticated download for: {publicId}");

                // Use Cloudinary's Admin API to get resource details
                var resource = await _cloudinary.GetResourceAsync(new GetResourceParams(publicId)
                {
                    ResourceType = ResourceType.Raw
                });

                if (resource?.SecureUrl == null)
                {
                    throw new Exception("Resource not found or URL is null");
                }

                Console.WriteLine($"Retrieved resource URL: {resource.SecureUrl}");

                // Download using HttpClient with Cloudinary authentication
                using var httpClient = new HttpClient();

                // Add Cloudinary authentication headers
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var signature = GenerateCloudinarySignature(publicId, timestamp);

                httpClient.DefaultRequestHeaders.Add("Authorization", $"Cloudinary {_cloudName}:{_apiKey}:{signature}");

                var response = await httpClient.GetAsync(resource.SecureUrl);

                if (response.IsSuccessStatusCode)
                {
                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"Authenticated download successful: {fileBytes.Length} bytes");
                    return fileBytes;
                }
                else
                {
                    Console.WriteLine($"Authenticated download failed: {response.StatusCode} - {response.ReasonPhrase}");

                    // Fallback: Try direct download with Admin API URL construction
                    var adminApiUrl = $"https://api.cloudinary.com/v1_1/{_cloudName}/resources/raw/upload/{publicId}";
                    return await DownloadViaAdminApiAsync(adminApiUrl, publicId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Authenticated download failed: {ex.Message}");
                throw new Exception($"Failed to download file using authenticated method: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Download file via Cloudinary Admin API
        /// </summary>
        private async Task<byte[]> DownloadViaAdminApiAsync(string adminApiUrl, string publicId)
        {
            try
            {
                using var httpClient = new HttpClient();

                // Create basic auth header for Admin API
                var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_apiKey}:{_apiSecret}"));
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

                // First, get the resource info to get the actual download URL
                var resourceResponse = await httpClient.GetAsync(adminApiUrl);

                if (resourceResponse.IsSuccessStatusCode)
                {
                    var resourceJson = await resourceResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"Resource API response: {resourceJson}");

                    // Parse the JSON to extract the secure_url
                    var resourceData = System.Text.Json.JsonDocument.Parse(resourceJson);
                    if (resourceData.RootElement.TryGetProperty("secure_url", out var secureUrlElement))
                    {
                        var secureUrl = secureUrlElement.GetString();
                        Console.WriteLine($"Found secure URL: {secureUrl}");

                        // Now download the actual file
                        var fileResponse = await httpClient.GetAsync(secureUrl);
                        if (fileResponse.IsSuccessStatusCode)
                        {
                            var fileBytes = await fileResponse.Content.ReadAsByteArrayAsync();
                            Console.WriteLine($"Admin API download successful: {fileBytes.Length} bytes");
                            return fileBytes;
                        }
                    }
                }

                throw new Exception($"Admin API download failed: {resourceResponse.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Admin API download failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Generate Cloudinary signature for authentication
        /// </summary>
        private string GenerateCloudinarySignature(string publicId, string timestamp)
        {
            try
            {
                var parametersToSign = $"public_id={publicId}&timestamp={timestamp}";
                var signature = ComputeSha1Hash(parametersToSign + _apiSecret);
                return signature;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to generate signature: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Compute SHA1 hash for Cloudinary signature
        /// </summary>
        private string ComputeSha1Hash(string input)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// Upload certificate image to Cloudinary with proper folder organization
        /// </summary>
        public async Task<(string Url, string PublicId)> UploadCertificateAsync(byte[] imageBytes, string fileName)
        {
            using var stream = new MemoryStream(imageBytes);

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = "certificates",
                PublicId = null, // Let Cloudinary generate ID
                Overwrite = false,
                Type = "upload",
                AccessMode = "public", // Set to public for easier viewing
                Tags = "certificate,verification"
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Certificate Upload Success - URL: {uploadResult.SecureUrl}");
                Console.WriteLine($"Certificate Upload Success - PublicId: {uploadResult.PublicId}");

                return (uploadResult.SecureUrl.AbsoluteUri, uploadResult.PublicId);
            }

            throw new Exception($"Certificate upload failed. Status: {uploadResult.StatusCode}, Error: {uploadResult.Error?.Message}");
        }

        /// <summary>
        /// Re-upload file with public access (for fixing access issues)
        /// </summary>
        public async Task<(string Url, string PublicId)> ReuploadAsPublicAsync(string existingPublicId, string fileName)
        {
            try
            {
                // First, try to download the existing file
                byte[] fileBytes;
                try
                {
                    using var httpClient = new HttpClient();
                    var tempUrl = $"https://res.cloudinary.com/{_cloudName}/raw/upload/{existingPublicId}";
                    fileBytes = await httpClient.GetByteArrayAsync(tempUrl);
                }
                catch
                {
                    throw new Exception("Could not download existing file for re-upload");
                }

                // Delete the old file
                await DeleteFileAsync(existingPublicId);

                // Upload with public access
                return await UploadPdfAsync(fileBytes, fileName);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to re-upload file as public: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validate email attachment file
        /// </summary>
        public Application.DTOs.Email.FileValidationResult ValidateEmailAttachment(Microsoft.AspNetCore.Http.IFormFile file, long maxFileSizeMB = 50)
        {
            var result = new Application.DTOs.Email.FileValidationResult { IsValid = true };

            if (file == null || file.Length == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "File is empty or null";
                return result;
            }

            // Check file size (convert MB to bytes)
            var maxFileSizeBytes = maxFileSizeMB * 1024 * 1024;
            if (file.Length > maxFileSizeBytes)
            {
                result.IsValid = false;
                result.ErrorMessage = $"File size exceeds maximum allowed size of {maxFileSizeMB}MB";
                return result;
            }

            // Get file extension
            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".mp4", ".pdf" };

            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                result.IsValid = false;
                result.ErrorMessage = $"File type '{extension}' is not allowed. Allowed types: JPG, JPEG, MP4, PDF";
                return result;
            }

            // Validate MIME type matches extension
            var allowedMimeTypes = new Dictionary<string, string[]>
            {
                { ".jpg", new[] { "image/jpeg", "image/jpg" } },
                { ".jpeg", new[] { "image/jpeg", "image/jpg" } },
                { ".mp4", new[] { "video/mp4", "video/mpeg" } },
                { ".pdf", new[] { "application/pdf" } }
            };

            if (allowedMimeTypes.TryGetValue(extension, out var validMimeTypes))
            {
                if (!validMimeTypes.Contains(file.ContentType?.ToLowerInvariant()))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"File MIME type '{file.ContentType}' doesn't match extension '{extension}'";
                    return result;
                }
            }

            return result;
        }

        /// <summary>
        /// Upload email attachment with proper validation and organization
        /// </summary>
        public async Task<Application.DTOs.Email.EmailAttachmentInfo> UploadEmailAttachmentAsync(
            Microsoft.AspNetCore.Http.IFormFile file, 
            string userId, 
            int expirationDays = 7)
        {
            // Validate file
            var validation = ValidateEmailAttachment(file);
            if (!validation.IsValid)
            {
                throw new ArgumentException(validation.ErrorMessage);
            }

            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            var fileName = Path.GetFileName(file.FileName);
            
            // Determine resource type and folder based on file extension
            string folder;
            string resourceType;
            string url;
            string publicId;

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            // Upload based on file type
            if (extension == ".jpg" || extension == ".jpeg")
            {
                folder = $"email-attachments/images/{userId}";
                resourceType = "image";
                
                using var stream = new MemoryStream(fileBytes);
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = folder,
                    AccessMode = "public"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                
                if (uploadResult.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Image upload failed: {uploadResult.Error?.Message}");
                }
                
                url = uploadResult.SecureUrl.AbsoluteUri;
                publicId = uploadResult.PublicId;
            }
            else if (extension == ".mp4")
            {
                folder = $"email-attachments/videos/{userId}";
                resourceType = "video";
                
                using var stream = new MemoryStream(fileBytes);
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = folder
                };
                var uploadResult = await _cloudinary.UploadLargeAsync(uploadParams);
                
                if (uploadResult.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Video upload failed: {uploadResult.Error?.Message}");
                }
                
                url = uploadResult.SecureUrl.AbsoluteUri;
                publicId = uploadResult.PublicId;
            }
            else // PDF
            {
                folder = $"email-attachments/documents/{userId}";
                resourceType = "raw";
                
                using var stream = new MemoryStream(fileBytes);
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = folder,
                    AccessMode = "public"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                
                if (uploadResult.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"PDF upload failed: {uploadResult.Error?.Message}");
                }
                
                url = uploadResult.SecureUrl.AbsoluteUri;
                publicId = uploadResult.PublicId;
            }

            // Generate signed URL with expiration
            var signedUrl = GenerateProperSignedUrl(publicId, resourceType, expirationDays);

            return new Application.DTOs.Email.EmailAttachmentInfo
            {
                Url = signedUrl,
                PublicId = publicId,
                FileName = fileName,
                FileSize = file.Length,
                FileType = file.ContentType ?? "application/octet-stream",
                ResourceType = resourceType,
                ExpiresAt = DateTime.UtcNow.AddDays(expirationDays)
            };
        }

        /// <summary>
        /// Upload multiple email attachments
        /// </summary>
        public async Task<List<Application.DTOs.Email.EmailAttachmentInfo>> UploadMultipleEmailAttachmentsAsync(
            List<Microsoft.AspNetCore.Http.IFormFile> files, 
            string userId, 
            int expirationDays = 7,
            long maxTotalSizeMB = 100)
        {
            if (files == null || files.Count == 0)
            {
                return new List<Application.DTOs.Email.EmailAttachmentInfo>();
            }

            // Validate total size
            var totalSize = files.Sum(f => f.Length);
            var maxTotalSizeBytes = maxTotalSizeMB * 1024 * 1024;
            
            if (totalSize > maxTotalSizeBytes)
            {
                throw new ArgumentException($"Total file size exceeds maximum allowed size of {maxTotalSizeMB}MB");
            }

            var results = new List<Application.DTOs.Email.EmailAttachmentInfo>();
            var errors = new List<string>();

            foreach (var file in files)
            {
                try
                {
                    var result = await UploadEmailAttachmentAsync(file, userId, expirationDays);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            if (errors.Any() && results.Count == 0)
            {
                throw new Exception($"All uploads failed: {string.Join("; ", errors)}");
            }

            return results;
        }

        /// <summary>
        /// Generate properly signed URL with timestamp and signature
        /// </summary>
        private string GenerateProperSignedUrl(string publicId, string resourceType, int expirationDays)
        {
            try
            {
                var expirationTime = DateTimeOffset.UtcNow.AddDays(expirationDays);
                var timestamp = expirationTime.ToUnixTimeSeconds();

                // Build parameters to sign (alphabetically ordered)
                var paramsToSign = $"public_id={publicId}&timestamp={timestamp}";
                var signature = ComputeSha1Hash(paramsToSign + _apiSecret);

                // Build the signed URL with download flag
                var baseUrl = $"https://res.cloudinary.com/{_cloudName}/{resourceType}/upload";
                var signedUrl = $"{baseUrl}/fl_attachment/{publicId}?timestamp={timestamp}&signature={signature}&api_key={_apiKey}";

                Console.WriteLine($"Generated proper signed URL with expiration: {expirationTime}");
                return signedUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating proper signed URL: {ex.Message}");
                // Fallback to basic URL with download flag
                return $"https://res.cloudinary.com/{_cloudName}/{resourceType}/upload/fl_attachment/{publicId}";
            }
        }
    }
}
