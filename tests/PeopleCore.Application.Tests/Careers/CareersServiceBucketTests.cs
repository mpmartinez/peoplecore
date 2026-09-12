using FluentAssertions;
using Moq;
using PeopleCore.Application.Careers.DTOs;
using PeopleCore.Application.Careers.Services;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Careers;

/// <summary>
/// The resumes bucket is deployment configuration: CareersService must upload to whatever the
/// operator configured, and — when nothing is configured — to the historical "resumes" bucket.
/// </summary>
public class CareersServiceBucketTests
{
    private readonly Mock<IJobPostingRepository> _jobPostingRepo = new();
    private readonly Mock<IApplicantRepository> _applicantRepo = new();
    private readonly Mock<IStorageService> _storageService = new();

    private CareersService CreateSut(DocumentStorageOptions options) =>
        new(_jobPostingRepo.Object, _applicantRepo.Object, _storageService.Object, options);

    private static JobApplicationRequest MakeRequest() => new(
        FirstName: "Juan",
        LastName: "Dela Cruz",
        Email: "juan@example.com",
        Phone: "+639171234567",
        ResumeBase64: Convert.ToBase64String("fake-pdf-content"u8.ToArray()),
        ResumeFileName: "resume.pdf");

    private Guid ArrangeOpenJob()
    {
        var job = new JobPosting
        {
            Id = Guid.NewGuid(),
            Title = "Software Engineer",
            Status = JobPostingStatus.Open,
            Vacancies = 1
        };

        _jobPostingRepo.Setup(r => r.GetByIdAsync(job.Id, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(job);
        _applicantRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<Applicant>());
        _applicantRepo.Setup(r => r.AddAsync(It.IsAny<Applicant>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((Applicant a, CancellationToken _) => a);
        _storageService.Setup(s => s.UploadAsync(
                           It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                           It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((string _, string key, Stream _, string _, CancellationToken _) => key);

        return job.Id;
    }

    [Fact]
    public async Task Apply_UploadsResumeToConfiguredBucket()
    {
        var jobId = ArrangeOpenJob();
        var sut = CreateSut(new DocumentStorageOptions { ResumesBucketName = "tenant-resumes" });

        await sut.ApplyAsync(jobId, MakeRequest());

        _storageService.Verify(s => s.UploadAsync(
            "tenant-resumes", It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Pins the unchanged default. Existing deployments have resumes sitting in the "resumes"
    /// bucket; moving the default would orphan every one of them.
    /// </summary>
    [Fact]
    public async Task Apply_WithDefaultOptions_UploadsResumeToResumesBucket()
    {
        var jobId = ArrangeOpenJob();
        var sut = CreateSut(new DocumentStorageOptions());

        await sut.ApplyAsync(jobId, MakeRequest());

        _storageService.Verify(s => s.UploadAsync(
            "resumes", It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_DoesNotUploadResumeToEmployeeDocumentsBucket()
    {
        var jobId = ArrangeOpenJob();
        var options = new DocumentStorageOptions();
        var sut = CreateSut(options);

        await sut.ApplyAsync(jobId, MakeRequest());

        _storageService.Verify(s => s.UploadAsync(
            options.BucketName, It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
