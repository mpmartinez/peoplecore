namespace PeopleCore.Domain.Enums;

public enum PayFrequency { Monthly, SemiMonthly }
public enum PayrollRunStatus { Draft, Processing, ForApproval, Approved, Paid }
public enum PayrollRunType { Regular, FinalPay }
public enum AllowanceType { Transportation, Meal, HousingAllowance, Communication, Clothing, Other }
public enum LoanType { SSSLoan, PagIbigLoan, CompanyLoan, CashAdvance, CalamityLoan, Other }
/// <summary>
/// Where a maternity claim stands. Voided: a Draft claim HR withdrew (its leave was refiled, say);
/// it is never ready, never offsets and never outstanding. NotQualified: she doesn't qualify for the
/// SSS benefit, so her leave days are paid and taxed as ordinary salary - no advance, offset,
/// differential or deferral, even for an exempt employer; never ready or outstanding.
/// </summary>
public enum MaternityClaimStatus { Draft, Advanced, Reimbursed, Denied, Voided, NotQualified }
