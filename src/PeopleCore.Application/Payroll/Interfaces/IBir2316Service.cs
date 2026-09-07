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

    /// <summary>
    /// Every employee with at least one PAID run whose pay date falls in <paramref name="year"/>,
    /// for the "generate all" bulk action. Kept here rather than left for the controller to query
    /// the repository directly, so a controller never has to reach past Application for payroll
    /// data - the same layering every other controller in this codebase already follows.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetEmployeeIdsWithPaidRunsAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// Every employee with at least one PAID run whose pay date falls in <paramref name="year"/>,
    /// built with an empty <see cref="Bir2316ManualInputs"/> - the bulk equivalent of
    /// <see cref="BuildAsync"/> for the "generate all" action. Fetches the year's paid runs once
    /// and batches the employee lookup, rather than calling <see cref="BuildAsync"/> once per
    /// employee, which would re-query and re-materialise every OTHER employee's entries on every
    /// call.
    /// </summary>
    Task<IReadOnlyList<Bir2316Dto>> BuildAllAsync(int year, CancellationToken ct = default);
}
