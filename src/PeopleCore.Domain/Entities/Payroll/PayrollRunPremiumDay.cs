using PeopleCore.Domain.Payroll;

namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// One kind of day's attendance within one payroll entry - the per-day-type snapshot the entry's
/// overtime, holiday, rest-day and night premiums were priced from, so a recompute reprices
/// every hour at the rate of the day it was worked rather than from collapsed totals.
/// </summary>
public class PayrollRunPremiumDay : AuditableEntity
{
    public Guid PayrollRunEmployeeId { get; set; }
    public PayrollRunEmployee PayrollRunEmployee { get; set; } = null!;

    public WorkDayType DayType { get; set; }

    /// <summary>Scheduled working days of this type the employee worked, priced per day.</summary>
    public decimal Days { get; set; }

    /// <summary>
    /// The first eight hours of work on a rest day of this type, priced per hour - a rest day
    /// has no shift, so its work comes from approved overtime rather than a day of attendance.
    /// </summary>
    public decimal Hours { get; set; }

    /// <summary>Approved overtime on days of this type, past the shift or the eighth hour.</summary>
    public decimal OvertimeHours { get; set; }

    /// <summary>Hours worked between 10 p.m. and 6 a.m. on days of this type.</summary>
    public decimal NightDiffHours { get; set; }
}
