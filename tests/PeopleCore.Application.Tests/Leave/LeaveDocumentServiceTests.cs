using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// A leave request's supporting document: PDF, JPEG or PNG of at most 10 MB, attached by the
/// request's owner while it is Pending, stored under <c>leave-requests/{requestId}/{Guid}{extension}</c>
/// in the documents bucket, and opened through a 300-second presigned link.
/// </summary>
public class LeaveDocumentServiceTests
{
    private const long TenMegabytes = 10 * 1024 * 1024;
    private const string Bucket = "tenant-documents";

    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SomeoneElse = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly LeaveRequest _request;
    private readonly LeaveDocumentService _sut;

    public LeaveDocumentServiceTests()
    {
        _request = new LeaveRequest
        {
            EmployeeId = Owner,
            LeaveTypeId = Guid.NewGuid(),
            LeaveType = new LeaveType { Name = "Paternity Leave", Code = "PL", RequiresDocument = true },
            StartDate = new DateOnly(2026, 10, 5),
            EndDate = new DateOnly(2026, 10, 9),
            TotalDays = 5,
            Status = LeaveStatus.Pending
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(_request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_request);

        _sut = new LeaveDocumentService(
            _leaveRepo.Object, _storage.Object,
            new DocumentStorageOptions { BucketName = Bucket },
            NullLogger<LeaveDocumentService>.Instance);
    }

    private Task<Application.Leave.DTOs.LeaveRequestDto> Upload(
        string contentType = "application/pdf", long length = 1024, Guid? uploader = null, string fileName = "birth-certificate.pdf")
        => _sut.UploadAsync(_request.Id, uploader ?? Owner, new MemoryStream(new byte[16]), fileName, contentType, length);

    // ---- accepted types --------------------------------------------------------------------

    [Theory]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    public async Task Upload_AcceptsPdfJpegAndPng_AndStoresUnderTheLeaveRequestKey(string contentType, string extension)
    {
        string? storedKey = null;
        _storage.Setup(s => s.UploadAsync(Bucket, It.IsAny<string>(), It.IsAny<Stream>(), contentType, It.IsAny<CancellationToken>()))
                .Callback((string _, string key, Stream _, string _, CancellationToken _) => storedKey = key)
                .ReturnsAsync("stored");

        var dto = await Upload(contentType);

        storedKey.Should().MatchRegex($@"^leave-requests/{_request.Id}/[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}\{extension}$");
        _request.DocumentStorageKey.Should().Be(storedKey);
        _request.DocumentContentType.Should().Be(contentType);
        dto.HasDocument.Should().BeTrue();
        _leaveRepo.Verify(r => r.UpdateAsync(_request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_RecordsTheFileAndWhoUploadedIt()
    {
        var before = DateTime.UtcNow;

        var dto = await Upload(length: 2048, fileName: "birth-certificate.pdf");

        _request.DocumentFileName.Should().Be("birth-certificate.pdf");
        _request.DocumentSizeBytes.Should().Be(2048);
        _request.DocumentUploadedBy.Should().Be(Owner);
        _request.DocumentUploadedAt.Should().BeOnOrAfter(before);
        dto.DocumentFileName.Should().Be("birth-certificate.pdf");
    }

    [Fact]
    public async Task Upload_OfExactlyTenMegabytes_IsAccepted()
    {
        await Upload(length: TenMegabytes);

        _request.DocumentSizeBytes.Should().Be(TenMegabytes);
    }

    [Fact]
    public async Task Upload_KeepsOnlyTheFileName_NotAnyPathTheClientSent()
    {
        await Upload(fileName: @"C:\Users\juan\Documents\birth-certificate.pdf");

        _request.DocumentFileName.Should().Be("birth-certificate.pdf");
    }

    // ---- refused files ---------------------------------------------------------------------

    [Theory]
    [InlineData("image/gif")]
    [InlineData("text/html")]
    [InlineData("application/msword")]
    [InlineData("")]
    public async Task Upload_RefusesOtherTypes(string contentType)
    {
        var act = () => Upload(contentType);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Attach a PDF, JPG or PNG of at most 10 MB.");
        VerifyNothingStored();
    }

    [Theory]
    [InlineData(TenMegabytes + 1)]
    [InlineData(0)]
    public async Task Upload_RefusesAFileOverTenMegabytesOrEmpty(long length)
    {
        var act = () => Upload(length: length);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Attach a PDF, JPG or PNG of at most 10 MB.");
        VerifyNothingStored();
    }

    // ---- refused requests ------------------------------------------------------------------

    [Theory]
    [InlineData(LeaveStatus.Approved)]
    [InlineData(LeaveStatus.Rejected)]
    [InlineData(LeaveStatus.Cancelled)]
    public async Task Upload_RefusesARequestThatIsNotPending(LeaveStatus status)
    {
        _request.Status = status;

        var act = () => Upload();

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only a pending request's document can be replaced.");
        VerifyNothingStored();
    }

    [Fact]
    public async Task Upload_RefusesAnUploaderWhoIsNotTheOwner()
    {
        var act = () => Upload(uploader: SomeoneElse);

        await act.Should().ThrowAsync<DomainException>().WithMessage("You can only attach documents to your own leave requests.");
        VerifyNothingStored();
    }

    [Fact]
    public async Task Upload_ForAnUnknownRequest_IsNotFound()
    {
        var act = () => _sut.UploadAsync(Guid.NewGuid(), Owner, new MemoryStream(), "a.pdf", "application/pdf", 10);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---- replacing -------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ReplacingAFile_OverwritesTheFields_AndRemovesTheOldObject()
    {
        _request.DocumentFileName = "old.png";
        _request.DocumentStorageKey = $"leave-requests/{_request.Id}/{Guid.NewGuid()}.png";
        _request.DocumentContentType = "image/png";
        _request.DocumentSizeBytes = 99;
        _request.DocumentUploadedBy = Owner;
        _request.DocumentUploadedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldKey = _request.DocumentStorageKey;

        await Upload("application/pdf", 4096, fileName: "new.pdf");

        _request.DocumentFileName.Should().Be("new.pdf");
        _request.DocumentStorageKey.Should().NotBe(oldKey).And.EndWith(".pdf");
        _request.DocumentContentType.Should().Be("application/pdf");
        _request.DocumentSizeBytes.Should().Be(4096);
        _request.DocumentUploadedAt.Should().BeAfter(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _storage.Verify(s => s.DeleteAsync(Bucket, oldKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_WhenSavingFails_RemovesTheNewObject_AndKeepsTheOldOne()
    {
        var oldKey = $"leave-requests/{_request.Id}/{Guid.NewGuid()}.png";
        _request.DocumentStorageKey = oldKey;
        string? newKey = null;
        _storage.Setup(s => s.UploadAsync(Bucket, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string key, Stream _, string _, CancellationToken _) => newKey = key)
                .ReturnsAsync("stored");
        _leaveRepo.Setup(r => r.UpdateAsync(_request, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => Upload();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _storage.Verify(s => s.DeleteAsync(Bucket, newKey!, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(Bucket, oldKey, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_WhenRemovingTheOldObjectFails_StillSucceeds()
    {
        _request.DocumentStorageKey = $"leave-requests/{_request.Id}/{Guid.NewGuid()}.png";
        _storage.Setup(s => s.DeleteAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("storage hiccup"));

        var dto = await Upload();

        dto.HasDocument.Should().BeTrue();
    }

    // ---- opening ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDownloadUrl_IsAPresignedLinkValidFor300Seconds()
    {
        _request.DocumentStorageKey = $"leave-requests/{_request.Id}/{Guid.NewGuid()}.pdf";
        _storage.Setup(s => s.GetPresignedUrlAsync(Bucket, _request.DocumentStorageKey, 300, It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://storage/signed");

        var url = await _sut.GetDownloadUrlAsync(_request.Id);

        url.Should().Be("https://storage/signed");
    }

    [Fact]
    public async Task GetDownloadUrl_ForARequestWithNoDocument_IsNotFound()
    {
        var act = () => _sut.GetDownloadUrlAsync(_request.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private void VerifyNothingStored()
    {
        _storage.VerifyNoOtherCalls();
        _leaveRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _request.DocumentStorageKey.Should().BeNull();
    }
}
