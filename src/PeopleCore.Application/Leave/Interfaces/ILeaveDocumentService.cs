using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

/// <summary>
/// A leave request's one supporting document. Who may open it is the caller's decision (the
/// controller holds the identity); this service only stores it and hands out the link.
/// </summary>
public interface ILeaveDocumentService
{
    /// <summary>
    /// Attaches, or replaces, the request's document: a PDF, JPEG or PNG of at most 10 MB, by the
    /// request's owner while it is Pending.
    /// </summary>
    Task<LeaveRequestDto> UploadAsync(Guid requestId, Guid uploaderEmployeeId, Stream content,
        string fileName, string contentType, long length, CancellationToken ct = default);

    /// <summary>A presigned link to the request's document, valid for 300 seconds.</summary>
    Task<string> GetDownloadUrlAsync(Guid requestId, CancellationToken ct = default);
}
