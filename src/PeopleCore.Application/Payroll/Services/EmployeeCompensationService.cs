using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Services;

public class EmployeeCompensationService : IEmployeeCompensationService
{
    private readonly IEmployeeCompensationRepository _repo;

    public EmployeeCompensationService(IEmployeeCompensationRepository repo)
    {
        _repo = repo;
    }

    public async Task<EmployeeCompensationDto?> GetByEmployeeAsync(Guid employeeId, CancellationToken ct = default)
    {
        var compensation = await _repo.GetByEmployeeIdAsync(employeeId, ct);
        return compensation is null ? null : ToDto(compensation);
    }

    public async Task<EmployeeCompensationDto> UpsertAsync(
        Guid employeeId, UpsertCompensationRequest request, CancellationToken ct = default)
    {
        CompensationValidator.Validate(request);

        var compensation = await _repo.GetByEmployeeIdAsync(employeeId, ct);

        if (compensation is null)
        {
            compensation = new EmployeeCompensation
            {
                EmployeeId = employeeId,
                BasicSalary = request.BasicSalary,
                PayFrequency = request.PayFrequency,
                TaxCode = request.TaxCode,
                Dependents = request.Dependents
            };
            compensation = await _repo.AddAsync(compensation, ct);
        }
        else
        {
            compensation.BasicSalary = request.BasicSalary;
            compensation.PayFrequency = request.PayFrequency;
            compensation.TaxCode = request.TaxCode;
            compensation.Dependents = request.Dependents;
            await _repo.UpdateAsync(compensation, ct);
        }

        return ToDto(compensation);
    }

    private static EmployeeCompensationDto ToDto(EmployeeCompensation c) =>
        new(c.Id, c.EmployeeId, c.BasicSalary, c.PayFrequency, c.TaxCode, c.Dependents);
}
