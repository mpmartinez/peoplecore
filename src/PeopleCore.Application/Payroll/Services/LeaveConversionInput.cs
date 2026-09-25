namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Unused leave converted to cash on an entry, already priced and split (see
/// <c>FinalPayMath.LeaveConversion</c>). <see cref="PayrollComputationService.Compute"/> records it
/// the same way whether it comes from a final pay or a year-end conversion.
/// </summary>
/// <param name="DeMinimis">The de minimis (non-taxable) part: the first 10 vacation-type days.</param>
/// <param name="OtherBenefits">
/// The rest: "other benefits", exempt with the 13th month up to the year's 90,000 and taxable past
/// it - never part of the per-period withholding base.
/// </param>
public sealed record LeaveConversionInput(decimal DeMinimis, decimal OtherBenefits);
