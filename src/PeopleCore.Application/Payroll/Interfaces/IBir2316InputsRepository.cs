using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IBir2316InputsRepository
{
    Task<Bir2316Inputs?> GetAsync(Guid employeeId, int year, CancellationToken ct = default);

    /// <summary>Every saved set for the year, keyed by employee.</summary>
    Task<IReadOnlyDictionary<Guid, Bir2316Inputs>> GetForYearAsync(int year, CancellationToken ct = default);

    /// <summary>Saves the inputs for the employee and year, replacing any saved before.</summary>
    Task SaveAsync(Guid employeeId, int year, Bir2316ManualInputs inputs, CancellationToken ct = default);
}
