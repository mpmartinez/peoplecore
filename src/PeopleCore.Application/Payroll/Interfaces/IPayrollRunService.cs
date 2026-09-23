using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayrollRunService
{
    /// <summary>Creates a run and computes its entries for the requested employees in one step.</summary>
    Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default);

    /// <summary>
    /// Recomputes an existing, uncommitted run in place against current rates/settings - for
    /// when a statutory schedule is corrected after the run was first computed. Throws
    /// DomainException when the run is Approved or Paid: an approver has signed off on those
    /// figures, or (for Paid) they have already been used to retire loan balances.
    /// </summary>
    Task ComputeAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Approves a run, freezing the figures it holds. This is the gate between computing and
    /// paying: once approved, ComputeAsync refuses to run again (an approver has signed off on
    /// these numbers), and MarkPaidAsync goes on to retire loan balances against exactly what was
    /// approved. Valid from Draft, Processing or ForApproval; throws DomainException otherwise.
    /// </summary>
    Task ApproveAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Retires each active loan's balance by exactly what this run withheld (its
    /// PayrollLoanDeduction line), never by re-deriving the instalment from the loan's own
    /// schedule, and marks the run Paid. A final-pay run is refused until its separation's
    /// clearance is complete.
    /// </summary>
    Task MarkPaidAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Takes an employee off a run that isn't Approved or Paid yet - someone who left and whose
    /// pay belongs in their final pay. The run goes back to Draft, as its totals have changed. A
    /// run keeps at least one employee.
    /// </summary>
    Task<PayrollRunDto> RemoveEmployeeAsync(Guid runId, Guid employeeId, CancellationToken ct = default);

    Task<PayrollRunDto?> GetAsync(Guid runId, CancellationToken ct = default);

    /// <summary>A page of runs, summarised without their per-employee entries.</summary>
    Task<PagedResult<PayrollRunSummaryDto>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
