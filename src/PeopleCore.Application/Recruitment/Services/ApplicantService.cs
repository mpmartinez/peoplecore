using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Recruitment.Services;

public class ApplicantService : IApplicantService
{
    private readonly IApplicantRepository _applicantRepo;
    private readonly IJobPostingRepository _jobPostingRepo;
    private readonly IEmployeeRepository _employeeRepo;

    public ApplicantService(
        IApplicantRepository applicantRepo,
        IJobPostingRepository jobPostingRepo,
        IEmployeeRepository employeeRepo)
    {
        _applicantRepo = applicantRepo;
        _jobPostingRepo = jobPostingRepo;
        _employeeRepo = employeeRepo;
    }

    public async Task<PagedResult<ApplicantDto>> GetAllAsync(
        Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _applicantRepo.GetPagedAsync(jobPostingId, status, page, pageSize, ct);
        return PagedResult<ApplicantDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<ApplicantDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Applicant {id} not found.");
        return ToDto(applicant);
    }

    public async Task<ApplicantDto> CreateAsync(CreateApplicantDto dto, CancellationToken ct = default)
    {
        var jobPosting = await _jobPostingRepo.GetByIdAsync(dto.JobPostingId, ct)
            ?? throw new KeyNotFoundException($"Job posting {dto.JobPostingId} not found.");

        if (jobPosting.Status != JobPostingStatus.Open)
            throw new DomainException("Applications can only be submitted for Open job postings.");

        var applicant = new Applicant
        {
            JobPostingId = dto.JobPostingId,
            JobPosting = jobPosting,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Phone = dto.Phone,
            Status = ApplicantStatus.Applied,
            AppliedAt = DateTime.UtcNow
        };

        var created = await _applicantRepo.AddAsync(applicant, ct);
        return ToDto(created);
    }

    public async Task<ApplicantDto> UpdateStatusAsync(Guid id, UpdateApplicantStatusDto dto, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Applicant {id} not found.");

        applicant.Status = dto.Status;
        applicant.UpdatedAt = DateTime.UtcNow;

        await _applicantRepo.UpdateAsync(applicant, ct);
        return ToDto(applicant);
    }

    public async Task<EmployeeDto> ConvertToEmployeeAsync(Guid applicantId, ConvertToEmployeeDto dto, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(applicantId, ct)
            ?? throw new KeyNotFoundException($"Applicant {applicantId} not found.");

        if (applicant.Status != ApplicantStatus.Hired)
            throw new DomainException("Applicant must be in Hired status before conversion.");

        if (applicant.ConvertedEmployeeId.HasValue)
            throw new DomainException("This applicant has already been converted to an employee.");

        if (await _employeeRepo.EmployeeNumberExistsAsync(dto.EmployeeNumber, ct))
            throw new DomainException($"Employee number '{dto.EmployeeNumber}' already exists.");

        var employee = new Employee
        {
            EmployeeNumber = dto.EmployeeNumber,
            FirstName = applicant.FirstName,
            LastName = applicant.LastName,
            WorkEmail = applicant.Email,
            MobileNumber = applicant.Phone,
            DepartmentId = dto.DepartmentId,
            PositionId = dto.PositionId,
            ReportingManagerId = dto.ReportingManagerId,
            EmploymentStatus = dto.EmploymentStatus,
            EmploymentType = "FullTime",
            HireDate = dto.HireDate,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            IsActive = true,
            Is13thMonthEligible = true
        };

        var created = await _employeeRepo.AddAsync(employee, ct);

        applicant.ConvertedEmployeeId = created.Id;
        applicant.UpdatedAt = DateTime.UtcNow;
        await _applicantRepo.UpdateAsync(applicant, ct);

        return ToEmployeeDto(created);
    }

    private static ApplicantDto ToDto(Applicant a) => new(
        a.Id,
        a.JobPostingId,
        a.JobPosting?.Title ?? string.Empty,
        a.FirstName,
        a.LastName,
        a.Email,
        a.Phone,
        a.Status,
        a.ConvertedEmployeeId,
        a.AppliedAt);

    private static EmployeeDto ToEmployeeDto(Employee e) => new(
        e.Id, e.EmployeeNumber, e.FirstName, e.MiddleName, e.LastName, e.FullName,
        e.DateOfBirth, e.Gender, e.CivilStatus, e.WorkEmail, e.MobileNumber,
        e.DepartmentId, e.Department?.Name,
        e.PositionId, e.Position?.Title,
        e.ReportingManagerId, e.ReportingManager?.FullName,
        e.TeamId,
        e.EmploymentStatus, e.EmploymentType, e.HireDate, e.RegularizationDate,
        e.IsActive, e.Is13thMonthEligible);
}
