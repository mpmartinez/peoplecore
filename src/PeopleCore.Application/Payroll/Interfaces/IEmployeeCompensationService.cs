using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IEmployeeCompensationService
{
    Task<EmployeeCompensationDto?> GetByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmployeeCompensationDto> UpsertAsync(Guid employeeId, UpsertCompensationRequest request, CancellationToken ct = default);
}
