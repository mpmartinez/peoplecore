using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Application.Recruitment.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Recruitment;

public class ApplicantServiceTests
{
    private readonly Mock<IApplicantRepository> _applicantRepo = new();
    private readonly Mock<IJobPostingRepository> _jobPostingRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly ApplicantService _sut;

    public ApplicantServiceTests()
    {
        _sut = new ApplicantService(_applicantRepo.Object, _jobPostingRepo.Object, _employeeRepo.Object);
    }

    private static Applicant MakeApplicant(ApplicantStatus status = ApplicantStatus.Hired, Guid? convertedEmployeeId = null) => new()
    {
        Id = Guid.NewGuid(),
        JobPostingId = Guid.NewGuid(),
        JobPosting = new JobPosting { Title = "Software Engineer" },
        FirstName = "Maria",
        LastName = "Santos",
        Email = "maria@example.com",
        Status = status,
        ConvertedEmployeeId = convertedEmployeeId,
        AppliedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task ConvertToEmployeeAsync_WhenApplicantNotHired_ThrowsDomainException()
    {
        var applicant = MakeApplicant(status: ApplicantStatus.Interview);
        _applicantRepo.Setup(r => r.GetByIdAsync(applicant.Id, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(applicant);

        var dto = new ConvertToEmployeeDto("EMP-100", null, null, null, EmploymentStatus.Probationary, new DateOnly(2025, 1, 1));

        var act = () => _sut.ConvertToEmployeeAsync(applicant.Id, dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*Hired status*");
    }

    [Fact]
    public async Task ConvertToEmployeeAsync_WhenAlreadyConverted_ThrowsDomainException()
    {
        var applicant = MakeApplicant(status: ApplicantStatus.Hired, convertedEmployeeId: Guid.NewGuid());
        _applicantRepo.Setup(r => r.GetByIdAsync(applicant.Id, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(applicant);

        var dto = new ConvertToEmployeeDto("EMP-100", null, null, null, EmploymentStatus.Probationary, new DateOnly(2025, 1, 1));

        var act = () => _sut.ConvertToEmployeeAsync(applicant.Id, dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already been converted*");
    }

    [Fact]
    public async Task ConvertToEmployeeAsync_WhenValid_CreatesEmployeeWithApplicantData()
    {
        var applicant = MakeApplicant(status: ApplicantStatus.Hired);
        _applicantRepo.Setup(r => r.GetByIdAsync(applicant.Id, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(applicant);
        _employeeRepo.Setup(r => r.EmployeeNumberExistsAsync("EMP-100", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);
        _employeeRepo.Setup(r => r.AddAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((Employee e, CancellationToken _) => e);
        _applicantRepo.Setup(r => r.UpdateAsync(It.IsAny<Applicant>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);

        var dto = new ConvertToEmployeeDto("EMP-100", null, null, null, EmploymentStatus.Probationary, new DateOnly(2025, 1, 1));

        var result = await _sut.ConvertToEmployeeAsync(applicant.Id, dto);

        result.FirstName.Should().Be(applicant.FirstName);
        result.LastName.Should().Be(applicant.LastName);
        result.WorkEmail.Should().Be(applicant.Email);
        result.EmployeeNumber.Should().Be("EMP-100");
    }
}
