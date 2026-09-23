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

    /// <summary>Replaces HR's inputs on a Draft or For-approval final-pay run and recomputes it.</summary>
    Task<FinalPaySummaryDto> UpdateAsync(Guid separationId, FinalPayRequest request, CancellationToken ct = default);

    /// <summary>The separation's final pay, or null when it has no final-pay run yet.</summary>
    Task<FinalPaySummaryDto?> GetAsync(Guid separationId, CancellationToken ct = default);

    /// <summary>
    /// Recomputes a final-pay run's entry from its stored inputs (the working days as stored,
    /// the attendance snapshot on the current entry) and settles the tax again. Called by
    /// <c>PayrollRunService.ComputeAsync</c>, which saves the result.
    /// </summary>
    Task<List<PayrollRunEmployee>> RecomputeAsync(PayrollRun run, CancellationToken ct = default);
}
