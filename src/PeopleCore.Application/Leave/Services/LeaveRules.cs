using System.Globalization;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Leave;

namespace PeopleCore.Application.Leave.Services;

/// <summary>Everything <see cref="LeaveRules"/> needs to check one leave request.</summary>
public sealed record LeaveRuleContext(
    LeaveType Type,
    Employee Employee,
    DateOnly Start,
    DateOnly End,
    IReadOnlyDictionary<int, decimal> DaysByYear,
    MaternityCase? MaternityCase,
    int DaysAllocatedToFather,
    int ApprovedEvents,
    IReadOnlyDictionary<int, decimal> AvailableByYear);

/// <summary>
/// Pure eligibility and limit checks for a leave request, in the fixed order and with the exact
/// messages of the Global Constraints filing-checks table. Throws <see cref="DomainException"/>.
/// Check 11 (overlap) is the caller's responsibility, run between <see cref="EnsureEligible"/> and
/// <see cref="EnsureWithinLimits"/>; check 14 (document, on approval only) is the caller's too.
/// </summary>
public static class LeaveRules
{
    private const string NumberFormat = "0.##";
    private const string DateFormat = "MMM d, yyyy";

    /// <summary>Checks 1-8 of the Global Constraints, in order.</summary>
    public static void EnsureEligible(LeaveRuleContext c)
    {
        CheckInactive(c.Type);
        CheckGender(c.Type, c.Employee);
        CheckService(c.Type, c.Employee, c.Start);
        CheckMarried(c.Type, c.Employee);
        CheckSoloParentId(c.Type, c.Employee, c.Start);
        CheckMaternityCaseChosen(c.Type, c.MaternityCase);
        CheckFatherAllocation(c.Type, c.MaternityCase, c.DaysAllocatedToFather);
        CheckEventLimit(c.Type, c.ApprovedEvents);
    }

    /// <summary>Checks 9, 10, 12 and 13, in order. (Check 11, overlap, is the service's, run between the two.)</summary>
    public static void EnsureWithinLimits(LeaveRuleContext c)
    {
        CheckSpan(c.Start, c.End);
        CheckZeroDays(c.DaysByYear);
        CheckPerEventLimit(c);
        CheckBalance(c);
    }

    /// <summary>The per-event limit, or null for non-PerEvent types. Maternity follows the RA 11210 rule.</summary>
    public static decimal? PerEventLimit(
        LeaveType type, Employee employee, DateOnly start, MaternityCase? maternityCase, int daysAllocatedToFather)
    {
        if (type.EntitlementKind != LeaveEntitlementKind.PerEvent)
            return null;

        if (!type.IsMaternity)
            return type.DaysPerEvent;

        if (maternityCase == MaternityCase.MiscarriageOrEmergencyTermination)
            return StatutoryLeave.MaternityMiscarriageDays;

        var baseDays = type.DaysPerEvent ?? StatutoryLeave.MaternityLiveBirthDays;
        var soloParentExtra = employee.HasValidSoloParentId(start) ? StatutoryLeave.MaternitySoloParentExtraDays : 0m;
        return baseDays + soloParentExtra - daysAllocatedToFather;
    }

    /// <summary>True when checks 1-5 pass on the given date (used for the filing options).</summary>
    public static bool IsEligibleOn(LeaveType type, Employee employee, DateOnly on)
    {
        try
        {
            CheckInactive(type);
            CheckGender(type, employee);
            CheckService(type, employee, on);
            CheckMarried(type, employee);
            CheckSoloParentId(type, employee, on);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    // ---- Checks 1-8 ----

    private static void CheckInactive(LeaveType type)
    {
        if (!type.IsActive)
            throw new DomainException($"{type.Name} is no longer available.");
    }

    private static void CheckGender(LeaveType type, Employee employee)
    {
        if (type.GenderRestriction is not null && type.GenderRestriction != employee.Gender.ToString())
            throw new DomainException("Employee is not eligible for this leave type.");
    }

    private static void CheckService(LeaveType type, Employee employee, DateOnly start)
    {
        if (type.MinServiceMonths is not { } months)
            return;

        var qualifiesOn = employee.HireDate.AddMonths(months);
        if (qualifiesOn > start)
            throw new DomainException(
                $"{type.Name} needs {months} months of service; you'll qualify on {FormatDate(qualifiesOn)}.");
    }

    private static void CheckMarried(LeaveType type, Employee employee)
    {
        if (type.RequiresMarried && employee.CivilStatus != M2NET.Core.Enums.CivilStatus.Married)
            throw new DomainException($"{type.Name} is for married employees.");
    }

    private static void CheckSoloParentId(LeaveType type, Employee employee, DateOnly on)
    {
        if (type.RequiresSoloParentId && !employee.HasValidSoloParentId(on))
            throw new DomainException($"{type.Name} needs a valid solo parent ID on your record; ask HR to add it.");
    }

    private static void CheckMaternityCaseChosen(LeaveType type, MaternityCase? maternityCase)
    {
        if (type.IsMaternity && maternityCase is null)
            throw new DomainException("Choose whether this is a live birth or a miscarriage or emergency termination.");
    }

    private static void CheckFatherAllocation(LeaveType type, MaternityCase? maternityCase, int daysAllocatedToFather)
    {
        if (!type.IsMaternity || daysAllocatedToFather <= 0)
            return;

        var exceedsMax = daysAllocatedToFather > StatutoryLeave.MaxDaysAllocatedToFather;
        var notLiveBirth = maternityCase != PeopleCore.Domain.Enums.MaternityCase.LiveBirth;
        if (exceedsMax || notLiveBirth)
            throw new DomainException("Up to 7 days can be allocated to the father, for a live birth only.");
    }

    private static void CheckEventLimit(LeaveType type, int approvedEvents)
    {
        if (type.MaxEvents is { } max && approvedEvents >= max)
            throw new DomainException($"You've used {type.Name} {approvedEvents} times, the most allowed.");
    }

    // ---- Checks 9, 10, 12, 13 ----

    private static void CheckSpan(DateOnly start, DateOnly end)
    {
        if (end.Year - start.Year > 1)
            throw new DomainException("A leave request can't span more than two years.");
    }

    private static void CheckZeroDays(IReadOnlyDictionary<int, decimal> daysByYear)
    {
        if (daysByYear.Values.Sum() <= 0)
            throw new DomainException("There are no working days in that range.");
    }

    private static void CheckPerEventLimit(LeaveRuleContext c)
    {
        var limit = PerEventLimit(c.Type, c.Employee, c.Start, c.MaternityCase, c.DaysAllocatedToFather);
        if (limit is null)
            return;

        var totalDays = c.DaysByYear.Values.Sum();
        if (totalDays <= limit.Value)
            return;

        var n = FormatNumber(limit.Value);
        var m = FormatNumber(totalDays);

        if (c.Type.IsMaternity)
        {
            var caseText = c.MaternityCase == PeopleCore.Domain.Enums.MaternityCase.MiscarriageOrEmergencyTermination
                ? "a miscarriage or emergency termination"
                : "a live birth";
            throw new DomainException($"Maternity leave for {caseText} is up to {n} days; this request is {m}.");
        }

        throw new DomainException($"{c.Type.Name} is up to {n} days each time; this request is {m}.");
    }

    private static void CheckBalance(LeaveRuleContext c)
    {
        // PerEvent types carry no balance; AvailableByYear is empty for them.
        if (c.Type.EntitlementKind == LeaveEntitlementKind.PerEvent)
            return;

        foreach (var year in c.DaysByYear.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => kv.Key))
        {
            var days = c.DaysByYear[year];
            var available = c.AvailableByYear.GetValueOrDefault(year);
            if (available < days)
            {
                var n = FormatNumber(Math.Max(0m, available));
                throw new DomainException($"You have {n} days of {c.Type.Name} left for {year}.");
            }
        }
    }

    private static string FormatNumber(decimal value) => value.ToString(NumberFormat, CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
