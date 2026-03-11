using Amazon.S3;
using Amazon.S3.Model;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.Infrastructure.Storage;

public class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;

    public R2StorageService(IAmazonS3 s3) => _s3 = s3;

    public async Task<string> UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false
        };
        await _s3.PutObjectAsync(request, ct);
        return objectKey;
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct = default)
    {
        var response = await _s3.GetObjectAsync(bucketName, objectKey, ct);
        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct = default)
    {
        await _s3.DeleteObjectAsync(bucketName, objectKey, ct);
    }

    public async Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.AddSeconds(expirySeconds),
            Verb = HttpVerb.GET
        };
        return await Task.FromResult(_s3.GetPreSignedURL(request));
    }
}
