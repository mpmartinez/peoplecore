using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayslipService
{
    /// <summary>One employee's payslip, or null when the run or the entry does not exist.</summary>
    Task<byte[]?> GenerateAsync(Guid runId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Every payslip in the run merged into one PDF, or null when the run does not exist.</summary>
    Task<byte[]?> GenerateForRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// The self-service equivalent of <see cref="GenerateAsync"/>: identical, except it also
    /// returns null for a run that is not yet <see cref="Domain.Enums.PayrollRunStatus.Approved"/>
    /// or <see cref="Domain.Enums.PayrollRunStatus.Paid"/>. Approval is what makes a run's figures
    /// final - see <c>PayrollRunService.ApproveAsync</c> - and a Draft or ForApproval run can still
    /// be recomputed to a different net pay under the employee's feet. HR/payroll staff keep
    /// access to unapproved runs through <see cref="GenerateAsync"/> directly: proofing a run
    /// before approving it is the whole point of that stage.
    /// </summary>
    Task<byte[]?> GenerateForSelfServiceAsync(Guid runId, Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// The runs the given employee appears in, newest first, each summarised down to that
    /// employee's own net pay. Scoped to exactly one employee id by construction - there is no
    /// overload that returns another employee's rows - so the caller (a controller resolving
    /// the id from the employee_id claim) cannot be tricked into leaking someone else's
    /// compensation through this endpoint. Also scoped to Approved/Paid runs only, for the same
    /// reason as <see cref="GenerateForSelfServiceAsync"/>: an unapproved run's numbers are not
    /// final yet.
    /// </summary>
    Task<IReadOnlyList<MyPayslipSummaryDto>> GetMyPayslipsAsync(Guid employeeId, CancellationToken ct = default);
}
