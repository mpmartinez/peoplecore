namespace PeopleCore.Application.Common.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct = default);
    Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600);
}
