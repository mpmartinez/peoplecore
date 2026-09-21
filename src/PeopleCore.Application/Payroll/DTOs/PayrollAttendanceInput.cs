using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// Attendance totals for one employee over one payroll period. Phase 1 leaves this null and
/// treats the employee as fully present; Phase 2 populates it from PeopleCore's punches,
/// approved leave and approved overtime.
/// </summary>
public record PayrollAttendanceInput
{
    public decimal LateMinutes { get; init; }
    public decimal AbsenceDays { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal UndertimeMinutes { get; init; }
    public decimal HolidayRegularDays { get; init; }
    public decimal HolidaySpecialDays { get; init; }
    public decimal NightDiffHours { get; init; }
    public decimal RestDayOTHours { get; init; }

    /// <summary>
    /// The premium-bearing attendance broken down by the kind of day it fell on. When present
    /// it is what the premiums are priced from, and the five overtime, holiday and night totals
    /// above are only roll-ups of it; when empty those totals are priced instead, as they were
    /// before the breakdown existed (see <see cref="PremiumDayInput.FromTotals"/>).
    /// </summary>
    public IReadOnlyList<PremiumDayInput> PremiumDays { get; init; } = [];

    /// <summary><see cref="PremiumDays"/>, or the breakdown the totals stand for when it is empty.</summary>
    public IReadOnlyList<PremiumDayInput> ResolvePremiumDays() =>
        PremiumDays.Count > 0 ? PremiumDays : PremiumDayInput.FromTotals(this);
}

/// <summary>One kind of day's premium-bearing attendance. See PayrollRunPremiumDay.</summary>
public record PremiumDayInput(
    WorkDayType DayType,
    decimal Days = 0m,
    decimal Hours = 0m,
    decimal OvertimeHours = 0m,
    decimal NightDiffHours = 0m)
{
    /// <summary>
    /// The breakdown that collapsed totals stand for, priced exactly as they were before the
    /// breakdown existed: overtime and night hours on ordinary days, rest-day overtime at the
    /// rest-day overtime rate, and holiday days on single regular or special holidays.
    /// </summary>
    public static IReadOnlyList<PremiumDayInput> FromTotals(PayrollAttendanceInput totals) =>
        new[]
        {
            new PremiumDayInput(WorkDayType.Ordinary,
                OvertimeHours: totals.OvertimeHours, NightDiffHours: totals.NightDiffHours),
            new PremiumDayInput(WorkDayType.RestDay, OvertimeHours: totals.RestDayOTHours),
            new PremiumDayInput(WorkDayType.RegularHoliday, Days: totals.HolidayRegularDays),
            new PremiumDayInput(WorkDayType.SpecialNonWorking, Days: totals.HolidaySpecialDays)
        }
        .Where(d => d.Days != 0m || d.Hours != 0m || d.OvertimeHours != 0m || d.NightDiffHours != 0m)
        .ToList();
}
