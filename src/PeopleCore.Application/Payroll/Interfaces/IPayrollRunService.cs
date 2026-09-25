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
    /// figures, or (for Paid) they have already been used to retire loan balances. Two kinds of
    /// Approved run can still be recomputed, and go back to Draft: a final pay, and a regular run
    /// with the year-end leave conversion, whose leave can change after approval.
    /// </summary>
    Task ComputeAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Approves a run, freezing the figures it holds. This is the gate between computing and
    /// paying: once approved, ComputeAsync refuses to run again (an approver has signed off on
    /// these numbers), and MarkPaidAsync goes on to retire loan balances against exactly what was
    /// approved. Valid from Draft, Processing or ForApproval; throws DomainException otherwise.
    /// A run with the year-end leave conversion is refused while its leave no longer prices to
    /// what it pays - the check Mark Paid makes - so it's recomputed first.
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
    /// Takes an employee off a regular run that isn't Paid yet - someone who left and whose pay
    /// belongs in their final pay. The run goes back to Draft, as its totals have changed, so an
    /// approved run has to be approved again. A run keeps at least one employee, and a final-pay
    /// run's one employee can't be taken off.
    /// </summary>
    Task<PayrollRunDto> RemoveEmployeeAsync(Guid runId, Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// Turns a regular run's year-end leave conversion on or off and recomputes the run; an
    /// Approved or For-approval run goes back to Draft. Turning it on is allowed only when the
    /// run's period ends in December. A Paid run, a final pay, or a request that changes nothing
    /// is refused. Returns the run.
    /// </summary>
    Task<PayrollRunDto> SetLeaveConversionAsync(Guid runId, bool include, CancellationToken ct = default);

    /// <summary>
    /// Includes or leaves out the 13th month for every employee on a regular run and recomputes
    /// the run; an Approved or For-approval run goes back to Draft. Allowed in any month, so an
    /// advance can be paid (a later 13th month nets out what was paid earlier in the year), but
    /// turning it on is refused on a run paid in another year than its period ends in. A Paid
    /// run, a final pay, or a request that changes nothing is refused. Returns the run.
    /// </summary>
    Task<PayrollRunDto> SetThirteenthMonthAsync(Guid runId, bool include, CancellationToken ct = default);

    Task<PayrollRunDto?> GetAsync(Guid runId, CancellationToken ct = default);

    /// <summary>A page of runs, summarised without their per-employee entries.</summary>
    Task<PagedResult<PayrollRunSummaryDto>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
