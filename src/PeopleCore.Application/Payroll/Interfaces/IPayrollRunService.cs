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
    /// Retires each active loan's balance by exactly what this run withheld (its
    /// PayrollLoanDeduction line), never by re-deriving the instalment from the loan's own
    /// schedule, and marks the run Paid.
    /// </summary>
    Task MarkPaidAsync(Guid runId, CancellationToken ct = default);

    Task<PayrollRunDto?> GetAsync(Guid runId, CancellationToken ct = default);

    /// <summary>A page of runs, summarised without their per-employee entries.</summary>
    Task<PagedResult<PayrollRunSummaryDto>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
