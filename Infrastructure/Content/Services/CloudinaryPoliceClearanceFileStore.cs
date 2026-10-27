using Application.Interfaces.Content;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Infrastructure.Content.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Stores clearance files as Cloudinary AUTHENTICATED raw assets (type "authenticated"): the delivery URL
    /// returns 401/404 without a signature, unlike the public "upload" type used by the certificate flow.
    /// The backend downloads with a short-lived (120 s) signed private-download URL that is built, used and
    /// discarded server-side — it is never returned to a client and never logged (no URL or secret appears in
    /// any log line here; only the opaque public id on failure).
    /// </summary>
    public class CloudinaryPoliceClearanceFileStore : IPoliceClearanceFileStore
    {
        private const string Folder = "police_clearances";
        private const string AuthenticatedType = "authenticated";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

        private readonly Cloudinary _cloudinary;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CloudinaryPoliceClearanceFileStore> _logger;

        public CloudinaryPoliceClearanceFileStore(Cloudinary cloudinary, IConfiguration configuration,
            ILogger<CloudinaryPoliceClearanceFileStore> logger)
        {
            _cloudinary = cloudinary;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<StoredClearanceFile> UploadAsync(byte[] bytes, string extension)
        {
            var settings = PoliceClearanceSettings.Load(_configuration);
            using var stream = new MemoryStream(bytes);
            var publicId = $"{Folder}/{Guid.NewGuid():N}{extension}";
            var upload = new RawUploadParams
            {
                File = new FileDescription(Path.GetFileName(publicId), stream),
                PublicId = publicId,
                Type = AuthenticatedType,
                Overwrite = false,
                UseFilename = false,
                UniqueFilename = false,
                Tags = settings.AssetTag
            };
            var result = await _cloudinary.UploadAsync(upload);
            if (result.StatusCode != System.Net.HttpStatusCode.OK)
                throw new InvalidOperationException($"Clearance file upload failed (HTTP {(int)result.StatusCode}).");
            return new StoredClearanceFile(result.PublicId, "raw");
        }

        public async Task<byte[]?> DownloadAsync(string publicId, string? resourceType)
        {
            var url = _cloudinary.DownloadPrivate(publicId, attachment: false, format: "", type: AuthenticatedType,
                expiresAt: DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeSeconds(),
                resourceType: string.IsNullOrWhiteSpace(resourceType) ? "raw" : resourceType);
            using var response = await Http.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Clearance file download failed (HTTP {(int)response.StatusCode}).");
            return await response.Content.ReadAsByteArrayAsync();
        }

        public async Task<ClearanceFileDeleteOutcome> DeleteAsync(string publicId, string? resourceType)
        {
            try
            {
                var rt = string.Equals(resourceType, "image", StringComparison.OrdinalIgnoreCase) ? ResourceType.Image : ResourceType.Raw;
                var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId)
                {
                    ResourceType = rt,
                    Type = AuthenticatedType,
                    Invalidate = true
                });
                return result.Result switch
                {
                    "ok" => ClearanceFileDeleteOutcome.Deleted,
                    "not found" => ClearanceFileDeleteOutcome.NotFound,
                    _ => LogFailure(publicId, result.Result ?? result.Error?.Message)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Police clearance file deletion threw for public id {PublicId}", publicId);
                return ClearanceFileDeleteOutcome.Failed;
            }
        }

        private ClearanceFileDeleteOutcome LogFailure(string publicId, string? detail)
        {
            _logger.LogError("Police clearance file deletion failed for public id {PublicId}: {Detail}", publicId, detail);
            return ClearanceFileDeleteOutcome.Failed;
        }
    }
}
