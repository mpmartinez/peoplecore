using Minio;
using Minio.DataModel.Args;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.Infrastructure.Storage;

public class MinioStorageService : IStorageService
{
    private readonly IMinioClient _minio;

    public MinioStorageService(IMinioClient minio) => _minio = minio;

    public async Task<string> UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(bucketName, ct);
        var args = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType);
        await _minio.PutObjectAsync(args, ct);
        return objectKey;
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithCallbackStream(stream => stream.CopyTo(ms));
        await _minio.GetObjectAsync(args, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct = default)
    {
        var args = new RemoveObjectArgs().WithBucket(bucketName).WithObject(objectKey);
        await _minio.RemoveObjectAsync(args, ct);
    }

    public async Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds);
        return await _minio.PresignedGetObjectAsync(args);
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        var exists = await _minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), ct);
        if (!exists)
            await _minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName), ct);
    }
}
