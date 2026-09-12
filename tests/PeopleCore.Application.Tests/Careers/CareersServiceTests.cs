using FluentAssertions;
using Moq;
using PeopleCore.Application.Careers.DTOs;
using PeopleCore.Application.Careers.Interfaces;
using PeopleCore.Application.Careers.Services;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Careers;

public class CareersServiceTests
{
    private readonly Mock<IJobPostingRepository> _jobPostingRepo = new();
    private readonly Mock<IApplicantRepository> _applicantRepo = new();
    private readonly Mock<IStorageService> _storageService = new();

    private ICareersService CreateSut()
    {
        return new CareersService(
            _jobPostingRepo.Object,
            _applicantRepo.Object,
            _storageService.Object,
            new DocumentStorageOptions());
    }

    private static JobPosting MakeOpenJobPosting(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = "Software Engineer",
        Description = "Build great software",
        Requirements = "3+ years C#",
        Status = JobPostingStatus.Open,
        Vacancies = 2,
        PostedAt = DateTime.UtcNow.AddDays(-5),
        Department = new Department { Name = "Engineering" },
        Position = new Position { Title = "Senior Developer" }
    };

    private static JobApplicationRequest MakeRequest() => new(
        FirstName: "Juan",
        LastName: "Dela Cruz",
        Email: "juan@example.com",
        Phone: "+639171234567",
        ResumeBase64: Convert.ToBase64String("fake-pdf-content"u8.ToArray()),
        ResumeFileName: "resume.pdf");

    [Fact]
    public async Task Apply_ValidRequest_CreatesApplicantWithAppliedStatus()
    {
        // Arrange
        var jobPosting = MakeOpenJobPosting();
        var request = MakeRequest();

        _jobPostingRepo.Setup(r => r.GetByIdAsync(jobPosting.Id, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(jobPosting);

        // No existing applicant with same email for this job
        _applicantRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<Applicant>());

        _storageService.Setup(s => s.UploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("resumes/some-key.pdf");

        Applicant? capturedApplicant = null;
        _applicantRepo.Setup(r => r.AddAsync(It.IsAny<Applicant>(), It.IsAny<CancellationToken>()))
                      .Callback<Applicant, CancellationToken>((a, _) => capturedApplicant = a)
                      .ReturnsAsync((Applicant a, CancellationToken _) =>
                      {
                          a.Id = Guid.NewGuid();
                          return a;
                      });

        var sut = CreateSut();

        // Act
        var response = await sut.ApplyAsync(jobPosting.Id, request);

        // Assert
        _applicantRepo.Verify(r => r.AddAsync(
            It.Is<Applicant>(a => a.Status == ApplicantStatus.Applied),
            It.IsAny<CancellationToken>()), Times.Once);

        response.ApplicationId.Should().NotBeEmpty();
        response.Message.Should().NotBeNullOrWhiteSpace();
        capturedApplicant.Should().NotBeNull();
        capturedApplicant!.Email.Should().Be(request.Email);
        capturedApplicant.FirstName.Should().Be(request.FirstName);
        capturedApplicant.ResumeStorageKey.Should().Be("resumes/some-key.pdf");
    }

    [Fact]
    public async Task Apply_DuplicateEmail_ThrowsDomainException()
    {
        // Arrange
        var jobPosting = MakeOpenJobPosting();
        var request = MakeRequest();

        _jobPostingRepo.Setup(r => r.GetByIdAsync(jobPosting.Id, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(jobPosting);

        // Existing applicant with the same email for this job posting
        var existingApplicant = new Applicant
        {
            Id = Guid.NewGuid(),
            JobPostingId = jobPosting.Id,
            FirstName = "Juan",
            LastName = "Dela Cruz",
            Email = request.Email,
            Status = ApplicantStatus.Applied,
            AppliedAt = DateTime.UtcNow.AddDays(-1)
        };

        _applicantRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<Applicant> { existingApplicant });

        var sut = CreateSut();

        // Act
        var act = () => sut.ApplyAsync(jobPosting.Id, request);

        // Assert
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Apply_ClosedJob_ThrowsDomainException()
    {
        // Arrange
        var jobPosting = MakeOpenJobPosting();
        jobPosting.Status = JobPostingStatus.Closed;
        var request = MakeRequest();

        _jobPostingRepo.Setup(r => r.GetByIdAsync(jobPosting.Id, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(jobPosting);

        var sut = CreateSut();

        // Act
        var act = () => sut.ApplyAsync(jobPosting.Id, request);

        // Assert
        await act.Should().ThrowAsync<DomainException>();
    }
}
