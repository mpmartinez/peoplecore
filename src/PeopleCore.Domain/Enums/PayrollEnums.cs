namespace PeopleCore.Domain.Enums;

public enum PayFrequency { Monthly, SemiMonthly }
public enum PayrollRunStatus { Draft, Processing, ForApproval, Approved, Paid }
public enum PayrollRunType { Regular, FinalPay }
public enum AllowanceType { Transportation, Meal, HousingAllowance, Communication, Clothing, Other }
public enum LoanType { SSSLoan, PagIbigLoan, CompanyLoan, CashAdvance, CalamityLoan, Other }
/// <summary>
/// Where a maternity claim stands. Voided: a Draft claim HR withdrew (its leave was refiled, say);
/// it is never ready, never offsets and never outstanding.
/// </summary>
public enum MaternityClaimStatus { Draft, Advanced, Reimbursed, Denied, Voided }
