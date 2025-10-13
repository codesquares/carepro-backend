using CloudinaryDotNet.Actions;
using CloudinaryDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;

namespace Infrastructure.Content.Services
{
    public class CloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private readonly HttpClient _httpClient;

        public CloudinaryService(Cloudinary cloudinary)
        {
            _cloudinary = cloudinary;
            _cloudinary.Api.Secure = true;
            _httpClient = new HttpClient();
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

            throw new Exception("Image upload failed.");
        }


        public async Task<string> DownloadVideoAsBase64Async(string videoUrl)
        {
            if (string.IsNullOrWhiteSpace(videoUrl))
                return null;

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
    }
}
