using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Leave;

namespace PeopleCore.Application.Leave.Services;

/// <summary>
/// The Philippine statutory leave types - Service Incentive Leave, Expanded Maternity Leave and the
/// days allocated to the father, Paternity, Solo Parent, VAWC and the Magna Carta special leave -
/// and the one action that adds the ones a site is missing.
/// </summary>
public static class StatutoryLeaveSet
{
    /// <summary>SIL's code: the one type in the set that also gets an accrual policy.</summary>
    public const string ServiceIncentiveLeaveCode = "SIL";

    /// <summary>
    /// The set, in order. Every type is paid, active and not carried over; a flag not named here
    /// is off, and only SIL counts as vacation for the de minimis ceiling. Fresh instances on each
    /// call, since they are handed to the repository to insert.
    /// </summary>
    public static IReadOnlyList<LeaveType> Definitions() =>
    [
        Type(ServiceIncentiveLeaveCode, "Service Incentive Leave", LeaveEntitlementKind.Accrued, t =>
        {
            t.MaxDaysPerYear = 5m;
            t.IsConvertibleToCash = true;
            t.CountsAsVacationForDeMinimis = true;
        }),
        Type("ML", "Maternity Leave", LeaveEntitlementKind.PerEvent, t =>
        {
            t.DaysPerEvent = StatutoryLeave.MaternityLiveBirthDays;
            t.CountsCalendarDays = true;
            t.IsMaternity = true;
            t.GenderRestriction = "Female";
            t.RequiresDocument = true;
        }),
        Type("AML", "Maternity Leave Allocated to Father", LeaveEntitlementKind.PerEvent, t =>
        {
            t.DaysPerEvent = StatutoryLeave.MaxDaysAllocatedToFather;
            t.CountsCalendarDays = true;
            t.GenderRestriction = "Male";
            t.RequiresDocument = true;
        }),
        Type("PL", "Paternity Leave", LeaveEntitlementKind.PerEvent, t =>
        {
            t.DaysPerEvent = 7m;
            t.MaxEvents = 4;
            t.GenderRestriction = "Male";
            t.RequiresMarried = true;
            t.RequiresDocument = true;
        }),
        Type("SPL", "Solo Parent Leave", LeaveEntitlementKind.YearlyAllowance, t =>
        {
            t.MaxDaysPerYear = 7m;
            t.RequiresSoloParentId = true;
            t.MinServiceMonths = 6;
        }),
        Type("VAWC", "VAWC Leave", LeaveEntitlementKind.YearlyAllowance, t =>
        {
            t.MaxDaysPerYear = 10m;
            t.GenderRestriction = "Female";
            t.IsConfidential = true;
            t.RequiresDocument = true;
        }),
        Type("SLW", "Special Leave for Women (Magna Carta)", LeaveEntitlementKind.PerEvent, t =>
        {
            t.DaysPerEvent = 60m;
            t.CountsCalendarDays = true;
            t.GenderRestriction = "Female";
            t.MinServiceMonths = 6;
            t.RequiresDocument = true;
        }),
    ];

    /// <summary>SIL's accrual: 5 days a year, monthly, from 12 months' service, with no ceiling.</summary>
    public static LeaveAccrualPolicy ServiceIncentiveLeavePolicy(Guid silId) => new()
    {
        LeaveTypeId = silId,
        TenureMonthsMin = 12,
        TenureMonthsMax = null,
        DaysPerYear = 5m,
        AccrualFrequency = AccrualFrequency.Monthly,
        IsActive = true,
    };

    /// <summary>
    /// Creates each type whose code the site doesn't have yet (codes compared trimmed and ignoring
    /// case), and SIL's policy when SIL itself is created, in the same save as SIL. A type already
    /// there is skipped untouched, policies and all, so running this twice changes nothing.
    /// </summary>
    public static async Task<StatutoryLeaveResultDto> ApplyAsync(ILeaveTypeRepository types, CancellationToken ct = default)
    {
        var existing = (await types.GetAllAsync(ct))
            .Select(t => Normalize(t.Code))
            .ToHashSet();

        var added = new List<string>();
        var skipped = new List<string>();
        foreach (var definition in Definitions())
        {
            if (!existing.Add(Normalize(definition.Code)))
            {
                skipped.Add(definition.Code);
                continue;
            }

            IReadOnlyList<LeaveAccrualPolicy> policies = definition.Code == ServiceIncentiveLeaveCode
                ? [ServiceIncentiveLeavePolicy(definition.Id)]
                : [];
            await types.AddWithPoliciesAsync(definition, policies, ct);
            added.Add(definition.Code);
        }

        return new StatutoryLeaveResultDto(added, skipped);
    }

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    private static LeaveType Type(string code, string name, LeaveEntitlementKind kind, Action<LeaveType> rules)
    {
        var type = new LeaveType
        {
            Code = code,
            Name = name,
            EntitlementKind = kind,
            MaxDaysPerYear = 0m,
            IsPaid = true,
            IsCarryOver = false,
            CarryOverMaxDays = null,
            IsActive = true,
            IsConvertibleToCash = false,
            CountsAsVacationForDeMinimis = false,
        };
        rules(type);
        return type;
    }
}
