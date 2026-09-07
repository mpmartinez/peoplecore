using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IBir2316Service
{
    /// <summary>Years in which the employee has at least one PAID run, newest first.</summary>
    Task<IReadOnlyList<int>> GetAvailableYearsAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// The form with every derived figure populated and the manual fields blank, or null when the
    /// employee has no paid runs in that year.
    /// </summary>
    Task<Bir2316Dto?> GetPreviewAsync(Guid employeeId, int year, CancellationToken ct = default);

    /// <summary>
    /// The form with derived figures recomputed from payroll and the manual fields taken from
    /// <paramref name="manual"/>. Derived figures are never read from caller input.
    /// </summary>
    Task<Bir2316Dto?> BuildAsync(Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default);
}
