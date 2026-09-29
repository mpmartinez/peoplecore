using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>A maternity claim as HR sees it: the leave it covers, the benefit, and where it stands with SSS.</summary>
public record MaternityClaimDto(Guid Id, Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly LeaveStart, DateOnly LeaveEnd, decimal Days, decimal? DailyAllowance, decimal Benefit,
    MaternityClaimStatus Status, Guid? AdvanceRunId, string? AdvanceRunNumber, DateOnly? AdvancedAt,
    DateOnly? ReimbursedOn, decimal? ReimbursedAmount, string? Note);

/// <summary>
/// The SSS daily maternity allowance worked out from paid payroll: the 6 highest monthly salary
/// credits in the window, over 180. Null when no month in the window has one.
/// </summary>
public record SuggestedAllowanceDto(decimal? DailyAllowance, int MonthsFound, DateOnly WindowFrom, DateOnly WindowTo);

public record SetAllowanceRequest(decimal DailyAllowance);

public record ReimburseRequest(DateOnly ReimbursedOn, decimal ReimbursedAmount, string? Note);

public record DenyRequest(string Note);

/// <summary>Every claim, newest first, and the benefit advanced but not yet reimbursed or denied.</summary>
public record MaternityClaimsSummaryDto(IReadOnlyList<MaternityClaimDto> Claims, decimal Outstanding);

/// <summary>An approved maternity leave request with no claim yet, which HR can open one for.</summary>
public record EligibleMaternityLeaveDto(Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly StartDate, DateOnly EndDate, decimal Days);
