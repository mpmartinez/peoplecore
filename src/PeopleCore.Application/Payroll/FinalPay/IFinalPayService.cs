using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>
/// A separated employee's final pay, as a single-employee payroll run of type
/// <see cref="Domain.Enums.PayrollRunType.FinalPay"/> computed by the ordinary payroll engine.
/// </summary>
public interface IFinalPayService
{
    /// <summary>Creates the separation's final-pay run, computed and with its tax settled for the year.</summary>
    Task<FinalPaySummaryDto> CreateAsync(Guid separationId, FinalPayRequest request, CancellationToken ct = default);

    /// <summary>
    /// Replaces HR's inputs on a Draft, For-approval or Approved final-pay run and recomputes it;
    /// an Approved one goes back to Draft, to be approved again.
    /// </summary>
    Task<FinalPaySummaryDto> UpdateAsync(Guid separationId, FinalPayRequest request, CancellationToken ct = default);

    /// <summary>The separation's final pay, or null when it has no final-pay run yet.</summary>
    Task<FinalPaySummaryDto?> GetAsync(Guid separationId, CancellationToken ct = default);

    /// <summary>
    /// Recomputes a final-pay run's entry from its stored inputs (the working days as stored,
    /// the attendance snapshot on the current entry) and settles the tax again. Called by
    /// <c>PayrollRunService.ComputeAsync</c>, which saves the result.
    /// </summary>
    Task<List<PayrollRunEmployee>> RecomputeAsync(PayrollRun run, CancellationToken ct = default);

    /// <summary>
    /// The leave a final-pay run converted to cash: each balance it drew on and the days it paid
    /// out. Refused when the balances no longer price to the entry's leave conversion - leave was
    /// taken or granted since the run was computed - so it's recomputed before it's paid.
    /// Called by <c>PayrollRunService.MarkPaidAsync</c> before anything is changed.
    /// </summary>
    Task<IReadOnlyList<LeavePaidOut>> LeavePaidOutAsync(PayrollRun run, CancellationToken ct = default);

    /// <summary>
    /// Records the paid-out days as used on their balances, so the year-end carry-over doesn't
    /// carry them forward. Called by <c>PayrollRunService.MarkPaidAsync</c> as it pays the run.
    /// </summary>
    Task RecordLeavePaidOutAsync(IReadOnlyList<LeavePaidOut> paidOut, CancellationToken ct = default);
}

/// <summary>A leave balance a final pay converted to cash, and the days it paid out.</summary>
public sealed record LeavePaidOut(LeaveBalance Balance, decimal Days);
