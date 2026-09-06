using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayslipService
{
    /// <summary>One employee's payslip, or null when the run or the entry does not exist.</summary>
    Task<byte[]?> GenerateAsync(Guid runId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Every payslip in the run merged into one PDF, or null when the run does not exist.</summary>
    Task<byte[]?> GenerateForRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// The runs the given employee appears in, newest first, each summarised down to that
    /// employee's own net pay. Scoped to exactly one employee id by construction - there is no
    /// overload that returns another employee's rows - so the caller (a controller resolving
    /// the id from the employee_id claim) cannot be tricked into leaking someone else's
    /// compensation through this endpoint.
    /// </summary>
    Task<IReadOnlyList<MyPayslipSummaryDto>> GetMyPayslipsAsync(Guid employeeId, CancellationToken ct = default);
}
