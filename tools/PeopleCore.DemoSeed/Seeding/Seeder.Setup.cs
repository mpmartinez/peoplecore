using System.Text.Json.Nodes;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private static readonly (string Code, string Name)[] LeaveTypes = [("VL", "Vacation Leave"), ("SL", "Sick Leave")];

    private async Task CreateHolidaysAndScheduleAsync()
    {
        var admin = await AdminAsync();

        // A site may already carry this year's holidays. Payroll would not count a date twice (the
        // attendance bridge keys holidays by date), but a second row for the same day would clutter
        // the Holidays list the client sees, so a date already there is left alone.
        var existing = await ExistingHolidayDatesAsync(admin);
        var added = new List<Holiday>();
        foreach (var holiday in Calendar.Holidays.Where(h => !existing.Contains(h.Date)))
        {
            await api.PostAsync($"Add the holiday {holiday.Name}", "api/holidays", new
            {
                name = holiday.Name, holidayDate = holiday.Date,
                holidayType = holiday.IsRegular ? "RegularHoliday" : "SpecialNonWorking", isRecurring = false,
            }, admin);
            added.Add(holiday);
            Count("holidays");
        }

        Say(added.Count == 0
            ? "Holidays added: none; the site already had every 2026 date the demo uses."
            : $"Holidays added (company-wide, so real staff's payroll uses them too): {string.Join(", ", added.Select(h => $"{h.Name} {h.Date:d MMM}"))}.");

        var shiftId = IdOf(await api.PostAsync("Create the day shift", "api/shift-templates", new
        {
            name = "Day Shift 8-5", startTime = new TimeOnly(8, 0), endTime = new TimeOnly(17, 0),
            breakMinutes = 60, isNightShift = false,
        }, admin));
        Count("shift templates");

        foreach (var person in plan.People)
        {
            await api.PostAsync($"Assign the day shift to {person.EmployeeNumber}", "api/shift-assignments", new
            {
                employeeId = EmployeeId(person.Number), shiftTemplateId = shiftId, rotatingPatternId = (Guid?)null,
                patternStartDate = (DateOnly?)null, effectiveFrom = person.ActiveFrom, effectiveTo = (DateOnly?)null,
            }, admin);
            Count("shift assignments");
        }

        Say("The Monday-to-Friday day shift is in place.");
    }

    /// <summary>
    /// Reads the holidays already on the site. HolidaysController's GET (api/holidays, filtered by
    /// the "year" query parameter, defaulting to the current year when omitted) returns a plain array
    /// of HolidayDto, carrying holidayDate. The year is passed explicitly here as the demo's own
    /// Calendar.Start.Year, so a run in a later calendar year still checks the year the demo seeds
    /// rather than whatever year the server clock happens to be in. The paged-shape fallback below is
    /// kept only for robustness against a future change.
    /// </summary>
    private async Task<HashSet<DateOnly>> ExistingHolidayDatesAsync(string admin)
    {
        var node = await api.GetAsync("Read existing holidays", $"api/holidays?year={Calendar.Start.Year}", admin);
        var items = node is JsonArray array ? array : node?["items"]?.AsArray() ?? [];
        return items.Select(h => DateOnly.Parse(h!["holidayDate"]!.GetValue<string>()[..10])).ToHashSet();
    }

    private async Task CreateLoginsAsync()
    {
        foreach (var person in plan.People)
        {
            await _logins.CreateAsync(person.Number, EmployeeId(person.Number), person.Email,
                person.FirstName, person.LastName, person.Role);
            Count("logins");
        }
        Say($"Logins: {plan.People.Count}, one per employee.");
    }

    /// <summary>
    /// The leave plan assumes 15 days a year per type, accruing monthly from day one for everyone,
    /// with no tenure ceiling. An existing VL or SL type is reused. The accrual engine
    /// (LeaveAccrualService.RunAccrualsAsync) only ever loads <em>active</em> policies and skips one
    /// once an employee's tenure exceeds its TenureMonthsMax, so an inactive policy is invisible to it
    /// and a tenure cap would silently stop long-tenured demo staff from accruing. A policy is usable
    /// only when it is active, Monthly, at least 15 days a year, has no minimum tenure, and has no
    /// maximum tenure at all. The type itself must be paid and open to both genders
    /// (<see cref="Preflight.LeaveTypeProblem"/>). Decision:
    ///   - no active policy for the type: fine, CreateLeaveSetupAsync will create one;
    ///   - exactly one active policy and it is usable: reuse it;
    ///   - anything else (an active policy that isn't usable, or more than one active policy, which
    ///     would double-accrue): refuse before anything is written.
    /// </summary>
    private async Task CheckLeaveSetupAsync()
    {
        var admin = await AdminAsync();
        var types = (await api.GetAsync("Read leave types", "api/leave-types", admin))!.AsArray();

        foreach (var (code, _) in LeaveTypes)
        {
            var type = types.FirstOrDefault(t => string.Equals(t!["code"]!.GetValue<string>(), code, StringComparison.OrdinalIgnoreCase));
            if (type is null) continue;

            if (Preflight.LeaveTypeProblem(code, type) is { } problem)
                throw new PreflightRefusedException($"{problem} Nothing was changed.");

            _leaveTypeIds[code] = IdOf(type);
            var active = (await AccrualPoliciesForAsync(_leaveTypeIds[code], admin))
                .Where(p => p!["isActive"]!.GetValue<bool>())
                .ToList();

            if (active.Count == 0) continue;

            if (active.Count > 1)
                throw new PreflightRefusedException(
                    $"The existing {code} accrual policies include more than one active policy, which would double-accrue. Nothing was changed.");

            if (!IsUsablePolicy(active[0]!))
                throw new PreflightRefusedException(
                    $"The existing active {code} accrual policy is not monthly at 15+ days a year from day one with no tenure cap, so the demo's leave would be refused. Nothing was changed.");
        }
    }

    /// <summary>See the decision table on <see cref="CheckLeaveSetupAsync"/>.</summary>
    private static bool IsUsablePolicy(JsonNode policy) =>
        policy["accrualFrequency"]!.GetValue<string>() == "Monthly"
        && policy["daysPerYear"]!.GetValue<decimal>() >= 15
        && policy["tenureMonthsMin"]!.GetValue<int>() == 0
        && policy["tenureMonthsMax"] is null;

    private async Task CreateLeaveSetupAsync()
    {
        var admin = await AdminAsync();
        foreach (var (code, name) in LeaveTypes)
        {
            if (!_leaveTypeIds.ContainsKey(code))
            {
                _leaveTypeIds[code] = IdOf(await api.PostAsync($"Create {name}", "api/leave-types", new
                {
                    name, code, maxDaysPerYear = 15m, isPaid = true, isCarryOver = false,
                    carryOverMaxDays = (decimal?)null, genderRestriction = (string?)null, requiresDocument = false,
                }, admin));
                Count("leave types");
            }

            // CheckLeaveSetupAsync already refused any active policy that would not accrue as the
            // demo needs. An inactive policy is invisible to the accrual engine, so its mere presence
            // must never suppress creation of the active one the demo relies on.
            var hasActivePolicy = (await AccrualPoliciesForAsync(_leaveTypeIds[code], admin))
                .Any(p => p!["isActive"]!.GetValue<bool>());
            if (hasActivePolicy) continue;

            await api.PostAsync($"Create the {code} accrual policy", "api/leave-accrual-policies", new
            {
                leaveTypeId = _leaveTypeIds[code], tenureMonthsMin = 0, tenureMonthsMax = (int?)null,
                daysPerYear = 15m, accrualFrequency = "Monthly",
            }, admin);
            Count("accrual policies");
        }
        Say("Leave: Vacation and Sick Leave at 15 days a year, accruing monthly.");
    }

    /// <summary>
    /// LeaveAccrualPoliciesController's GET takes leaveTypeId as a required query parameter and
    /// filters server-side; there is no "give me every policy" shape to page through. So this asks
    /// for one leave type's policies at a time, rather than fetching everything and filtering by
    /// leaveTypeId client-side.
    /// </summary>
    private async Task<JsonArray> AccrualPoliciesForAsync(Guid leaveTypeId, string admin)
    {
        var node = await api.GetAsync("Read accrual policies", $"api/leave-accrual-policies?leaveTypeId={leaveTypeId}", admin);
        return node is JsonArray array ? array : node?["items"]?.AsArray() ?? [];
    }
}
