using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public record SeedResult(IReadOnlyDictionary<string, int> Counts, string ClientEmail, string ClientTemporaryPassword);

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

    public async Task<SeedResult> RunAsync(string adminEmail, string adminPassword)
    {
        var signIn = await _logins.AddAdminAsync(adminEmail, adminPassword);
        Say("Signed in.");

        await PreflightAsync(signIn);
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
