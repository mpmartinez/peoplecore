using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Recruitment.DTOs;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IApplicantService
{
    Task<PagedResult<ApplicantDto>> GetAllAsync(Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<ApplicantDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApplicantDto> CreateAsync(CreateApplicantDto dto, CancellationToken ct = default);
    Task<ApplicantDto> UpdateStatusAsync(Guid id, UpdateApplicantStatusDto dto, CancellationToken ct = default);
    Task<EmployeeDto> ConvertToEmployeeAsync(Guid applicantId, ConvertToEmployeeDto dto, CancellationToken ct = default);
}
