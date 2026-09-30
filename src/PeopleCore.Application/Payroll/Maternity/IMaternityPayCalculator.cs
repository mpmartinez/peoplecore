using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>
/// One employee's maternity figures for a regular run: the SSS benefit the entry advances, the part
/// of regular pay SSS covers (taken off regular pay), what HR still has to do, and the claim advanced.
/// </summary>
/// <param name="ClaimId">The claim <paramref name="Advance"/> advances; null when nothing is advanced.</param>
public sealed record MaternityPay(decimal Advance, decimal Offset, IReadOnlyList<string> Warnings, Guid? ClaimId,
    decimal Differential = 0m)
{
    public static readonly MaternityPay None = new(0m, 0m, [], null);
}

/// <summary>
/// Works out the maternity advance and offset for a regular payroll run (RA 11210), the warnings the
/// run shows, and settles the advanced claims at Mark Paid. A final pay gets no maternity handling.
/// </summary>
public interface IMaternityPayCalculator
{
    /// <summary>
    /// One employee's figures. Refuses an advance the employee can't have (a
    /// <see cref="Domain.Exceptions.DomainException"/>).
    /// </summary>
    /// <param name="regularPayBeforeOffset">The entry's regular pay after absences and tardiness, before the offset.</param>
    /// <param name="exempt">The employer is exempt from the salary differential (<see cref="PayrollSettings.ExemptFromMaternityDifferential"/>).</param>
    Task<MaternityPay> ForAsync(PayrollRun run, Guid employeeId, bool advanceRequested, decimal regularPayBeforeOffset,
        bool exempt, CancellationToken ct = default);

    /// <summary>
    /// Loads what <see cref="ForAsync"/> reads for many employees at once, so computing a run is a
    /// handful of queries rather than a few per employee.
    /// </summary>
    Task<MaternityRun> LoadAsync(PayrollRun run, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>
    /// The run's warnings as things stand now, rebuilt on every load of the run: its entries' figures
    /// are stored, but a claim set up or advanced since changes what HR still has to do. None for a final pay.
    /// </summary>
    Task<IReadOnlyList<string>> WarningsAsync(PayrollRun run, CancellationToken ct = default);

    /// <summary>
    /// For approval: refuses when a claim the run advances is no longer the one it was computed with
    /// (its benefit changed, or it was advanced elsewhere), while the run can still be recomputed.
    /// Changes nothing.
    /// </summary>
    Task EnsureAdvancesCurrentAsync(PayrollRun run, CancellationToken ct = default);

    /// <summary>
    /// For Mark Paid: every claim the run advances, marked <see cref="Domain.Enums.MaternityClaimStatus.Advanced"/>
    /// with the run and its pay date, for the caller to save with the run. Refuses when a claim is no
    /// longer the one the run was computed with. Changes nothing for a final pay.
    /// </summary>
    Task<IReadOnlyList<MaternityClaim>> SettleAdvancesAsync(PayrollRun run, CancellationToken ct = default);
}
