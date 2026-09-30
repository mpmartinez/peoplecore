namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// One entry's maternity figures (RA 11210), already worked out by the run (see
/// <c>MaternityPayCalculator</c>). <see cref="PayrollComputationService.Compute"/> records both on the
/// entry.
/// </summary>
/// <param name="Advance">
/// The SSS maternity benefit the employer advances on this entry. It is the SSS's benefit, not
/// compensation: it joins gross pay only - never the withholding base, the contribution base, the
/// 13th-month basis or "13th month and other benefits".
/// </param>
/// <param name="Offset">
/// The part of this period's regular pay the SSS benefit covers. It comes off regular pay before
/// anything else reads it (withholding base, 13th-month basis, gross); the engine caps it at the
/// regular pay there is. Contributions stay on the monthly basic.
/// </param>
/// <param name="Differential">
/// The salary differential: the pay for the maternity days the offset leaves. It stays in regular
/// pay (and so in the 13th-month basis and gross) but, being part of the maternity benefit, it is
/// left out of the withholding base (RMC 105-2019). The engine caps it at the regular pay left.
/// </param>
public sealed record MaternityInput(decimal Advance, decimal Offset, decimal Differential = 0m)
{
    public decimal Advance { get; } = Advance >= 0m
        ? Advance
        : throw new ArgumentOutOfRangeException(nameof(Advance), Advance, "The maternity benefit advance can't be negative.");

    public decimal Offset { get; } = Offset >= 0m
        ? Offset
        : throw new ArgumentOutOfRangeException(nameof(Offset), Offset, "The maternity benefit offset can't be negative.");

    public decimal Differential { get; } = Differential >= 0m
        ? Differential
        : throw new ArgumentOutOfRangeException(nameof(Differential), Differential, "The maternity salary differential can't be negative.");
}
