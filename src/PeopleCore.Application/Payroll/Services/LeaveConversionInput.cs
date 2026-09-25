namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Unused leave converted to cash on an entry, already priced and split (see
/// <c>FinalPayMath.LeaveConversion</c>). <see cref="PayrollComputationService.Compute"/> records it
/// the same way whether it comes from a final pay or a year-end conversion.
/// </summary>
/// <param name="DeMinimis">
/// The de minimis (non-taxable) part: vacation-type days up to the 10 a tax year allows, less
/// those earlier conversions in the pay year used (see <c>LeavePayout.DeMinimisDaysLeft</c>).
/// </param>
/// <param name="OtherBenefits">
/// The rest: "other benefits", exempt with the 13th month up to the year's 90,000 and taxable past
/// it - never part of the per-period withholding base.
/// </param>
public sealed record LeaveConversionInput(decimal DeMinimis, decimal OtherBenefits);
