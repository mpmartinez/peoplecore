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
public sealed record MaternityInput(decimal Advance, decimal Offset)
{
    public decimal Advance { get; } = Advance >= 0m
        ? Advance
        : throw new ArgumentOutOfRangeException(nameof(Advance), Advance, "The maternity benefit advance can't be negative.");

    public decimal Offset { get; } = Offset >= 0m
        ? Offset
        : throw new ArgumentOutOfRangeException(nameof(Offset), Offset, "The maternity benefit offset can't be negative.");
}
