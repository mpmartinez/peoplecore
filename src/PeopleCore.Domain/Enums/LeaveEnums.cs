namespace PeopleCore.Domain.Enums;

/// <summary>How a leave type's entitlement is tracked.</summary>
public enum LeaveEntitlementKind
{
    /// <summary>Yearly <c>LeaveBalance</c> rows built by accrual policies.</summary>
    Accrued,

    /// <summary>The year's balance is created on first filing in that year, with <c>TotalDays = MaxDaysPerYear</c>.</summary>
    YearlyAllowance,

    /// <summary>No balance; <c>DaysPerEvent</c> per request. One request is one event.</summary>
    PerEvent,
}

/// <summary>Which Expanded Maternity Leave scenario a request is for.</summary>
public enum MaternityCase
{
    LiveBirth,
    MiscarriageOrEmergencyTermination,
}
