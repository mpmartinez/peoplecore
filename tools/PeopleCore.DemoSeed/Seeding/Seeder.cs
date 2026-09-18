using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public record SeedResult(IReadOnlyDictionary<string, int> Counts, string ClientEmail, string ClientTemporaryPassword);

/// <summary>What the switch-off after a failure managed: of the demo logins created, how many are now off, and what went wrong.</summary>
public record LoginCleanup(int Created, int SwitchedOff, IReadOnlyList<string> Errors);

/// <summary>
/// Walks the plan through the API in calendar order. Each month does its accruals first, then the
/// leave and overtime filed that month, then the attendance, then the payroll for that month. That
/// way every payroll run sees the same history a real company's would.
/// </summary>
public sealed partial class Seeder(ApiClient api, DemoPlan plan, TextWriter log)
{
    /// <summary>The client's persona: the HR Manager.</summary>
    public const int ClientPerson = 2;

    private readonly Logins _logins = new(api);
    private Guid _companyId;
    private readonly Dictionary<int, Guid> _employeeIds = new();
    private readonly Dictionary<string, Guid> _departmentIds = new();
    private readonly Dictionary<(string Department, string Title), Guid> _positionIds = new();
    private readonly Dictionary<(string Department, string Team), Guid> _teamIds = new();
    private readonly Dictionary<string, Guid> _leaveTypeIds = new();
    private readonly SortedDictionary<string, int> _counts = new();
    private readonly HashSet<int> _switchedOff = new();

    /// <summary>What the run has created so far. After a failure, this is what remains on the site.</summary>
    public IReadOnlyDictionary<string, int> CountsSoFar => _counts;

    /// <summary>True once the preflight has passed and the run has started writing.</summary>
    public bool WroteAnything { get; private set; }

    /// <summary>Set when a run that had started writing failed: how the demo logins were switched off.</summary>
    public LoginCleanup? LoginCleanup { get; private set; }

    public async Task<SeedResult> RunAsync(string adminEmail, string adminPassword)
    {
        var signIn = await _logins.AddAdminAsync(adminEmail, adminPassword);
        Say("Signed in.");

        await PreflightAsync(signIn);
        WroteAnything = true;

        try
        {
            await CreateOrganizationAsync();
            await CreateEmployeesAsync();
            await CreateHolidaysAndScheduleAsync();
            await CreateLoginsAsync();
            await CreateLeaveSetupAsync();

            foreach (var month in plan.Months)
                await RunMonthAsync(month);

            await RunReviewsAsync();
            await RunRecruitmentAsync();
            var temporaryPassword = await FinishAsync();

            return new SeedResult(_counts, plan.PersonNumber(ClientPerson).Email, temporaryPassword);
        }
        catch
        {
            await SwitchOffDemoLoginsAsync();
            throw;
        }
    }

    /// <summary>
    /// Best effort, after a failure: every demo login created so far, the client's included, is
    /// deactivated with the admin token, so none is left usable on a half-built demo. Each error is
    /// kept for the report and never replaces the error that stopped the run.
    /// </summary>
    private async Task SwitchOffDemoLoginsAsync()
    {
        var created = _logins.CreatedUserIds;
        var errors = new List<string>();
        var switchedOff = 0;

        foreach (var (person, userId) in created)
        {
            if (_switchedOff.Contains(person))
            {
                switchedOff++;
                continue;
            }

            try
            {
                await api.PostAsync($"Deactivate the login of DEMO-{person:0000} after the failure",
                    $"api/users/{userId}/deactivate", null, await AdminAsync());
                _switchedOff.Add(person);
                switchedOff++;
            }
            catch (Exception e)
            {
                errors.Add(e is SeedException ? e.Message : $"DEMO-{person:0000}: {e.GetType().Name}: {e.Message}");
            }
        }

        LoginCleanup = new LoginCleanup(created.Count, switchedOff, errors);
    }

    private Task<string> AdminAsync() => _logins.TokenAsync(Logins.Admin);

    private Task<string> TokenOfAsync(int personNumber) => _logins.TokenAsync(personNumber);

    private Guid EmployeeId(int personNumber) => _employeeIds[personNumber];

    private void Count(string what, int n = 1) => _counts[what] = _counts.GetValueOrDefault(what) + n;

    private void Say(string line) => log.WriteLine(line);

    private static Guid IdOf(System.Text.Json.Nodes.JsonNode? node) => Guid.Parse(node!["id"]!.GetValue<string>());

    // Implemented in Seeder.Months.cs, Seeder.Reviews.cs, Seeder.Recruitment.cs and Seeder.Finish.cs.
    private partial Task RunMonthAsync(int month);
    private partial Task RunReviewsAsync();
    private partial Task RunRecruitmentAsync();
    private partial Task<string> FinishAsync();
}
