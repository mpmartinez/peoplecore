using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Components.Audit;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

/// <summary>
/// The audit trail page lists what the API returns, sends each filter it is given, and opens an
/// entry to show its field changes and the rest of its request. The history sheet asks for one
/// employee's or one record's entries.
/// </summary>
public class AuditTrailTests : BunitContext
{
    private static readonly Guid MariaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly string Today = DateTime.Today.ToString("yyyy-MM-dd");
    private static readonly string WeekAgo = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd");

    private const string Facets = """{"entityTypes":["EmployeeCompensation","PayrollRun"],"actions":["Updated","auth.login-failed"]}""";

    private static string Entry(long id, string category, string action, string? summary, string outcome = "Success", int changes = 0) => $$"""
        {"id":{{id}},"occurredAt":"2026-10-24T01:30:00Z","category":"{{category}}","action":"{{action}}","summary":{{(summary is null ? "null" : $"\"{summary}\"")}},
         "outcome":"{{outcome}}","failureReason":null,"userId":"u1","userEmail":"hr@company.test","actorName":null,"ipAddress":"203.0.113.7",
         "entityType":"EmployeeCompensation","entityId":"c-1","subjectEmployeeId":"{{MariaId}}","subjectEmployeeName":"Maria Reyes",
         "statusCode":null,"correlationId":"req-1","changeCount":{{changes}}}
        """;

    private static string Page(params string[] entries) =>
        $$"""{"items":[{{string.Join(',', entries)}}],"totalCount":{{entries.Length}},"page":1,"pageSize":50,"totalPages":1}""";

    private readonly StubHttpHandler _api = new();

    public AuditTrailTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@company.test");
        auth.SetClaims(SeededPermissions.ClaimsFor("Admin"));
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.On(HttpMethod.Get, "/api/audit-logs/facets", HttpStatusCode.OK, Facets);
    }

    [Fact]
    public void ListsTheLastWeek_WithWhoWhatAndWhere()
    {
        _api.On(HttpMethod.Get, $"/api/audit-logs?page=1&pageSize=50&from={WeekAgo}&to={Today}", HttpStatusCode.OK,
            Page(Entry(2, "DataChange", "Updated", "EmployeeCompensation updated", changes: 1),
                 Entry(1, "Security", "auth.login-failed", "Sign-in failed", outcome: "Failure")));

        var cut = Render<AuditTrail>();

        cut.WaitForAssertion(() => cut.FindAll("[data-entry]").Should().HaveCount(2));
        cut.Markup.Should().Contain("EmployeeCompensation updated").And.Contain("1 field(s)")
            .And.Contain("hr@company.test").And.Contain("203.0.113.7").And.Contain("Maria Reyes")
            .And.Contain("Failed").And.Contain("Oct 24, 2026 9:30:00 AM");
    }

    [Fact]
    public void FilteringByKind_AsksTheApiForThatKind()
    {
        _api.On(HttpMethod.Get, $"/api/audit-logs?page=1&pageSize=50&from={WeekAgo}&to={Today}", HttpStatusCode.OK, Page());
        _api.On(HttpMethod.Get, $"/api/audit-logs?page=1&pageSize=50&from={WeekAgo}&to={Today}&category=Security", HttpStatusCode.OK,
            Page(Entry(1, "Security", "auth.login-failed", "Sign-in failed", outcome: "Failure")));

        var cut = Render<AuditTrail>();
        cut.WaitForAssertion(() => cut.Find("[data-empty]"));

        cut.Find("#audit-category").Change("Security");

        cut.WaitForAssertion(() => cut.FindAll("[data-entry]").Should().ContainSingle());
    }

    [Fact]
    public void OpeningAnEntry_ShowsItsChangesAndTheRestOfItsRequest()
    {
        _api.On(HttpMethod.Get, $"/api/audit-logs?page=1&pageSize=50&from={WeekAgo}&to={Today}", HttpStatusCode.OK,
            Page(Entry(2, "DataChange", "Updated", "EmployeeCompensation updated", changes: 1)));
        _api.On(HttpMethod.Get, "/api/audit-logs/2", HttpStatusCode.OK, $$"""
            {"entry":{{Entry(2, "DataChange", "Updated", "EmployeeCompensation updated", changes: 1)}},
             "userAgent":"Mozilla/5.0","httpMethod":"PUT","path":"/api/employee-compensation/x",
             "changes":[{"field":"BasicSalary","old":"30000.00","new":"35000"}],
             "metadata":{},
             "related":[{{Entry(3, "Action", "compensation.saved", "Saved an employee's compensation")}}]}
            """);

        var cut = Render<AuditTrail>();
        cut.WaitForAssertion(() => cut.Find("[data-entry='2']"));
        cut.Find("[data-entry='2']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-change='BasicSalary']").TextContent
            .Should().Contain("Basic salary").And.Contain("30000.00").And.Contain("35000"));
        cut.Find("[data-audit-related]").TextContent.Should().Contain("Saved an employee's compensation");
        cut.Markup.Should().Contain("Mozilla/5.0");
    }

    [Fact]
    public void ALinkToOneEmployee_ShowsAllOfTheirHistory_NotJustTheLastWeek()
    {
        _api.On(HttpMethod.Get, $"/api/audit-logs?page=1&pageSize=50&employeeId={MariaId}", HttpStatusCode.OK,
            Page(Entry(2, "DataChange", "Updated", "EmployeeCompensation updated")));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/admin/audit-trail?employeeId={MariaId}");

        var cut = Render<AuditTrail>();

        cut.WaitForAssertion(() => cut.FindAll("[data-entry]").Should().ContainSingle());
        cut.Find("[data-scope]").TextContent.Should().Contain("one employee");
    }

    [Fact]
    public void TheHistory_OfAnEmployee_AsksForEverythingThatNamesThem()
    {
        _api.On(HttpMethod.Get, $"/api/audit-logs?page=1&pageSize=25&employeeId={MariaId}", HttpStatusCode.OK,
            Page(Entry(2, "DataChange", "Updated", "EmployeeCompensation updated", changes: 1)));

        var cut = Render<RecordHistory>(p => p.Add(h => h.EmployeeId, MariaId));

        cut.WaitForAssertion(() => cut.FindAll("[data-history-entry]").Should().ContainSingle());
        cut.Markup.Should().Contain("EmployeeCompensation updated");
    }

    [Fact]
    public void TheHistory_OfARecord_AsksForThatRecord()
    {
        _api.On(HttpMethod.Get, "/api/audit-logs?page=1&pageSize=25&entityType=PayrollRun&entityId=run-1", HttpStatusCode.OK, Page());

        var cut = Render<RecordHistory>(p => p.Add(h => h.EntityType, "PayrollRun").Add(h => h.EntityId, "run-1"));

        cut.WaitForAssertion(() => cut.Find("[data-history-empty]"));
    }
}
