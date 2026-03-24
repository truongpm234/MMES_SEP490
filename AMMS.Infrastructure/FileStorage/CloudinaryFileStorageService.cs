using AMMS.Infrastructure.Configurations;
using AMMS.Infrastructure.Interfaces;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace AMMS.Infrastructure.FileStorage
{
    public class CloudinaryFileStorageService : ICloudinaryFileStorageService
    {
        private readonly Cloudinary _cloudinary;
        private readonly string _cloudName;

        public CloudinaryFileStorageService(IOptions<CloudinaryOptions> options)
        {
            var account = new Account(
                options.Value.CloudName,
                options.Value.ApiKey,
                options.Value.ApiSecret);

            _cloudinary = new Cloudinary(account);
            _cloudName = options.Value.CloudName;
        }

        public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder)
        {
            if (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var imageParams = new ImageUploadParams
                {
                    File = new FileDescription(fileName, fileStream),
                    Folder = folder,
                    UseFilename = true,
                    UniqueFilename = true,
                    Overwrite = false
                };

                var result = await _cloudinary.UploadAsync(imageParams);
                if (result.Error != null)
                    throw new Exception(result.Error.Message);

                return result.SecureUrl?.ToString() ?? throw new Exception("Upload image failed");
            }

            var rawParams = new RawUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var rawResult = await _cloudinary.UploadAsync(rawParams);
            if (rawResult.Error != null)
                throw new Exception(rawResult.Error.Message);

            return rawResult.SecureUrl?.ToString() ?? throw new Exception("Upload raw failed");
        }

        public async Task<string> UploadRawWithPublicIdAsync(
            Stream fileStream,
            string fileName,
            string contentType,
            string publicId)
        {
            if (fileStream.CanSeek)
                fileStream.Position = 0;

            var rawParams = new RawUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                PublicId = publicId,
                UseFilename = false,
                UniqueFilename = false,
                Overwrite = true
            };

            var result = await _cloudinary.UploadAsync(rawParams);
            if (result.Error != null)
                throw new Exception(result.Error.Message);

            return result.SecureUrl?.ToString() ?? throw new Exception("Upload raw failed");
        }

        public async Task<string> UploadImageWithPublicIdAsync(
            Stream fileStream,
            string fileName,
            string publicId)
        {
            if (fileStream.CanSeek)
                fileStream.Position = 0;

            var imageParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                PublicId = publicId,
                UseFilename = false,
                UniqueFilename = false,
                Overwrite = true
            };

            var result = await _cloudinary.UploadAsync(imageParams);
            if (result.Error != null)
                throw new Exception(result.Error.Message);

            return result.SecureUrl?.ToString() ?? throw new Exception("Upload image failed");
        }

        public string BuildImageUrl(string publicId, string format = "png")
        {
            format = string.IsNullOrWhiteSpace(format) ? "png" : format.Trim().ToLowerInvariant();
            return _cloudinary.Api.UrlImgUp.Secure(true).BuildUrl($"{publicId}.{format}");
        }
    }
}