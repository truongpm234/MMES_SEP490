using AMMS.Shared.DTOs.Estimates;

namespace AMMS.Infrastructure.Interfaces;

public interface ICloudinaryFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder);

    Task<string> UploadRawWithPublicIdAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string publicId);

    Task<string> UploadImageWithPublicIdAsync(
        Stream fileStream,
        string fileName,
        string publicId);

    string BuildImageUrl(string publicId, string format = "png");
}