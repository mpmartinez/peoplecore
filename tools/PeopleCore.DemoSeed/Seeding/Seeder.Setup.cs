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

        // A site may already carry this year's holidays. Adding a second copy of a date could count it twice.
        var existing = await ExistingHolidayDatesAsync(admin);
        foreach (var holiday in Calendar.Holidays.Where(h => !existing.Contains(h.Date)))
        {
            await api.PostAsync($"Add the holiday {holiday.Name}", "api/holidays", new
            {
                name = holiday.Name, holidayDate = holiday.Date,
                holidayType = holiday.IsRegular ? "RegularHoliday" : "SpecialNonWorking", isRecurring = false,
            }, admin);
            Count("holidays");
        }

        var shiftId = IdOf(await api.PostAsync("Create the day shift", "api/shift-templates", new
        {
            name = "Day Shift 8-5", startTime = new TimeOnly(8, 0), endTime = new TimeOnly(17, 0),
            breakMinutes = 60, isNightShift = false,
        }, admin));

        foreach (var person in plan.People)
        {
            await api.PostAsync($"Assign the day shift to {person.EmployeeNumber}", "api/shift-assignments", new
            {
                employeeId = EmployeeId(person.Number), shiftTemplateId = shiftId, rotatingPatternId = (Guid?)null,
                patternStartDate = (DateOnly?)null, effectiveFrom = person.ActiveFrom, effectiveTo = (DateOnly?)null,
            }, admin);
            Count("shift assignments");
        }

        Say("Holidays and the Monday-to-Friday day shift are in place.");
    }

    /// <summary>
    /// Reads the holidays already on the site. HolidaysController's GET (api/holidays, filtered by
    /// year, defaulting to the current year) returns a plain array of HolidayDto, carrying holidayDate.
    /// The paged-shape fallback below is kept only for robustness against a future change.
    /// </summary>
    private async Task<HashSet<DateOnly>> ExistingHolidayDatesAsync(string admin)
    {
        var node = await api.GetAsync("Read existing holidays", "api/holidays", admin);
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
    /// The leave plan assumes 15 days a year per type, accruing monthly for everyone. An existing
    /// VL or SL type is reused. Its accrual policy must match that assumption, or the run stops before
    /// anything is created.
    /// </summary>
    private async Task CheckLeaveSetupAsync()
    {
        var admin = await AdminAsync();
        var types = (await api.GetAsync("Read leave types", "api/leave-types", admin))!.AsArray();

        foreach (var (code, _) in LeaveTypes)
        {
            var type = types.FirstOrDefault(t => string.Equals(t!["code"]!.GetValue<string>(), code, StringComparison.OrdinalIgnoreCase));
            if (type is null) continue;

            _leaveTypeIds[code] = IdOf(type);
            var theirs = await AccrualPoliciesForAsync(_leaveTypeIds[code], admin);
            var usable = theirs.Any(p =>
                p!["accrualFrequency"]!.GetValue<string>() == "Monthly"
                && p["daysPerYear"]!.GetValue<decimal>() >= 15
                && p["tenureMonthsMin"]!.GetValue<int>() == 0);

            if (theirs.Count > 0 && !usable)
                throw new SeedException("Check leave setup", "GET", "api/leave-accrual-policies", 200,
                    $"The existing {code} accrual policy is not monthly at 15 days a year from day one, so the demo's leave would be refused. Nothing was changed.");
        }
    }

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

            var theirs = await AccrualPoliciesForAsync(_leaveTypeIds[code], admin);
            if (theirs.Count > 0) continue;

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
