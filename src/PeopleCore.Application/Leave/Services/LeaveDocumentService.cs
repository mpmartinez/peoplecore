using Microsoft.Extensions.Logging;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Leave.Services;

/// <inheritdoc cref="ILeaveDocumentService"/>
public class LeaveDocumentService : ILeaveDocumentService
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;
    public const int LinkExpirySeconds = 300;
    public const string FileRefusal = "Attach a PDF, JPG or PNG of at most 10 MB.";

    /// <summary>
    /// The accepted content types, matched ignoring case, with the type each is stored as and the
    /// extension it is stored under. Some clients send the non-standard <c>image/jpg</c>; it is JPEG.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string ContentType, string Extension)> Accepted =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ("application/pdf", ".pdf"),
            ["image/jpeg"] = ("image/jpeg", ".jpg"),
            ["image/jpg"] = ("image/jpeg", ".jpg"),
            ["image/png"] = ("image/png", ".png"),
        };

    /// <summary>The column's length (LeaveRequestConfiguration).</summary>
    private const int MaxFileNameLength = 255;

    private readonly ILeaveRequestRepository _leaveRepo;
    private readonly IStorageService _storage;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly ILogger<LeaveDocumentService> _logger;

    public LeaveDocumentService(
        ILeaveRequestRepository leaveRepo,
        IStorageService storage,
        DocumentStorageOptions storageOptions,
        ILogger<LeaveDocumentService> logger)
    {
        _leaveRepo = leaveRepo;
        _storage = storage;
        _storageOptions = storageOptions;
        _logger = logger;
    }

    public async Task<LeaveRequestDto> UploadAsync(Guid requestId, Guid uploaderEmployeeId, Stream content,
        string fileName, string contentType, long length, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(requestId, ct)
            ?? throw new KeyNotFoundException($"Leave request {requestId} not found.");

        if (request.EmployeeId != uploaderEmployeeId)
            throw new DomainException("You can only attach documents to your own leave requests.");

        if (request.Status != LeaveStatus.Pending)
            throw new DomainException("Only a pending request's document can be replaced.");

        // The extension comes from the (checked) content type, never from the client's file name.
        if (!Accepted.TryGetValue(contentType?.Trim() ?? string.Empty, out var accepted) || length <= 0 || length > MaxSizeBytes)
            throw new DomainException(FileRefusal);

        var (normalizedType, extension) = accepted;
        var bucket = _storageOptions.BucketName;
        var objectKey = $"leave-requests/{requestId}/{Guid.NewGuid()}{extension}";
        await _storage.UploadAsync(bucket, objectKey, content, normalizedType, ct);

        var previousKey = request.DocumentStorageKey;
        request.DocumentFileName = CleanFileName(fileName, extension);
        request.DocumentStorageKey = objectKey;
        request.DocumentContentType = normalizedType;
        request.DocumentSizeBytes = length;
        request.DocumentUploadedBy = uploaderEmployeeId;
        request.DocumentUploadedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _leaveRepo.UpdateAsync(request, ct);
        }
        catch
        {
            // Nothing points at the new object; the old one is still the request's document.
            await _storage.DeleteAsync(bucket, objectKey, CancellationToken.None);
            throw;
        }

        // One file per request: the replaced object is no longer referenced. The new one is saved,
        // so a failure here only leaves an orphan behind - it must not fail the upload.
        if (previousKey is not null)
        {
            try
            {
                await _storage.DeleteAsync(bucket, previousKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove the replaced document {ObjectKey} of leave request {RequestId}.",
                    previousKey, requestId);
            }
        }

        return LeaveRequestService.ToDto(request);
    }

    public async Task<string> GetDownloadUrlAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(requestId, ct)
            ?? throw new KeyNotFoundException($"Leave request {requestId} not found.");

        if (request.DocumentStorageKey is null)
            throw new KeyNotFoundException("This leave request has no document.");

        return await _storage.GetPresignedUrlAsync(_storageOptions.BucketName, request.DocumentStorageKey, LinkExpirySeconds, ct);
    }

    /// <summary>The file name without any client path, fitted to the column; a blank one gets a plain name.</summary>
    private static string CleanFileName(string? fileName, string extension)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (name.Length == 0)
            return "document" + extension;

        return name.Length <= MaxFileNameLength ? name : name[^MaxFileNameLength..];
    }
}
