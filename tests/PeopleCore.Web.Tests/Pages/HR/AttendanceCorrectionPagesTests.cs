using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.ESS;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Pages.Management;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

/// <summary>
/// The three ways a time-in or time-out gets corrected - HR editing a day, an employee asking, an
/// approver deciding - each send what the API needs and show what it says back, including a refusal
/// for a day already paid.
/// </summary>
public class AttendanceCorrectionPagesTests : BunitContext
{
    private static readonly Guid JuanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CorrectionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly string Today = DateTime.Today.ToString("yyyy-MM-dd");
    private static readonly string TwoWeeksAgo = DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd");

    private static readonly string Record = $$"""
        {"items":[{"id":"{{Guid.NewGuid()}}","attendanceDate":"2026-03-10","timeIn":"2026-03-10T09:30:00Z","timeOut":"2026-03-10T16:00:00Z",
                   "lateMinutes":90,"undertimeMinutes":60,"isPresent":true,"employeeId":"{{JuanId}}","employeeName":"Juan Cruz","overtimeMinutes":0}],
         "totalCount":1,"page":1,"pageSize":50,"totalPages":1}
        """;

    private static readonly string Correction = $$"""
        {"id":"{{CorrectionId}}","employeeId":"{{JuanId}}","employeeName":"Juan Cruz","employeeNumber":"EMP-001","attendanceDate":"2026-03-10",
         "previousTimeIn":"2026-03-10T09:30:00Z","previousTimeOut":null,"newTimeIn":"2026-03-10T08:00:00Z","newTimeOut":"2026-03-10T17:00:00Z",
         "reason":"Forgot to scan","source":"EmployeeRequest","status":"Pending","requestedBy":"juan@company.test",
         "requestedAt":"2026-03-11T01:00:00Z","reviewedBy":null,"reviewedAt":null,"rejectionReason":null}
        """;

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public AttendanceCorrectionPagesTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("hr@company.test");
        _auth.SetClaims([.. SeededPermissions.ClaimsFor("HRManager"), new Claim("employee_id", Guid.NewGuid().ToString())]);
    }

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    private IRenderedComponent<AttendanceRecords> RenderRecords()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100&isActive=true", HttpStatusCode.OK,
                """{"items":[],"totalCount":0,"page":1,"pageSize":100,"totalPages":1}""")
            .On(HttpMethod.Get, $"/api/attendance?page=1&pageSize=50&from={TwoWeeksAgo}&to={Today}", HttpStatusCode.OK, Record)
            .On(HttpMethod.Get, $"/api/attendance-corrections/history?employeeId={JuanId}&date=2026-03-10", HttpStatusCode.OK, "[]");
        var cut = Render<AttendanceRecords>();
        cut.WaitForElement("[data-edit]");
        return cut;
    }

    [Fact]
    public void HR_corrects_a_day_with_new_times_and_a_reason()
    {
        _api.On(HttpMethod.Post, "/api/attendance-corrections", HttpStatusCode.OK, Correction);
        var cut = RenderRecords();

        cut.Find("[data-edit]").Click();
        cut.Find("#correction-in").GetAttribute("value").Should().Be("09:30");
        cut.Find("#correction-in").Input("08:00");
        cut.Find("#correction-out").Input("17:00");
        cut.Find("#correction-reason").Input("Clock was down");
        cut.Find("form[data-correction-form]").Submit();

        cut.WaitForElement("[data-records-notice]");
        var body = BodyOf(HttpMethod.Post, "/api/attendance-corrections");
        body.GetProperty("employeeId").GetGuid().Should().Be(JuanId);
        body.GetProperty("date").GetString().Should().Be("2026-03-10");
        body.GetProperty("timeIn").GetString().Should().StartWith("08:00");
        body.GetProperty("timeOut").GetString().Should().StartWith("17:00");
        body.GetProperty("reason").GetString().Should().Be("Clock was down");
    }

    [Fact]
    public void A_day_already_paid_keeps_the_dialog_open_with_the_APIs_reason()
    {
        _api.On(HttpMethod.Post, "/api/attendance-corrections", HttpStatusCode.BadRequest,
            """{"title":"Domain rule violated","detail":"Mar 10, 2026 was paid in payroll run PR-2026-005 (Mar 1 – Mar 15, 2026), so its attendance can't be changed.","status":400}""");
        var cut = RenderRecords();

        cut.Find("[data-edit]").Click();
        cut.Find("#correction-reason").Input("Late fix");
        cut.Find("form[data-correction-form]").Submit();

        cut.WaitForElement("[data-correction-error]").TextContent.Should().Contain("PR-2026-005");
    }

    [Fact]
    public void The_form_refuses_a_time_out_before_the_time_in_without_asking_the_API()
    {
        var cut = RenderRecords();

        cut.Find("[data-edit]").Click();
        cut.Find("#correction-in").Input("17:00");
        cut.Find("#correction-out").Input("08:00");
        cut.Find("#correction-reason").Input("x");
        cut.Find("form[data-correction-form]").Submit();

        cut.Find("[data-correction-error]").TextContent.Should().Contain("later than the time-in");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void An_employee_requests_a_correction_for_their_own_day()
    {
        _auth.SetClaims(new Claim("employee_id", JuanId.ToString()));
        _api.On(HttpMethod.Get, $"/api/attendance?page=1&pageSize=20&employeeId={JuanId}", HttpStatusCode.OK, Record)
            .On(HttpMethod.Get, $"/api/attendance-corrections?page=1&pageSize=10&employeeId={JuanId}", HttpStatusCode.OK,
                """{"items":[],"totalCount":0,"page":1,"pageSize":10,"totalPages":0}""")
            .On(HttpMethod.Post, "/api/attendance-corrections/requests", HttpStatusCode.Created, Correction);

        var cut = Render<MyAttendance>();
        cut.WaitForElement("[data-request]").Click();
        cut.Find("#correction-in").Input("08:00");
        cut.Find("#correction-reason").Input("Forgot to scan");
        cut.Find("form[data-correction-form]").Submit();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("sent for approval"));
        var body = BodyOf(HttpMethod.Post, "/api/attendance-corrections/requests");
        body.GetProperty("date").GetString().Should().Be("2026-03-10");
        body.GetProperty("reason").GetString().Should().Be("Forgot to scan");
        body.TryGetProperty("employeeId", out _).Should().BeFalse("the API takes the employee from the caller's sign-in");
    }

    [Fact]
    public void An_approver_approves_a_waiting_request()
    {
        _api.On(HttpMethod.Get, "/api/attendance-corrections?page=1&pageSize=20&status=Pending", HttpStatusCode.OK,
                $$"""{"items":[{{Correction}}],"totalCount":1,"page":1,"pageSize":20,"totalPages":1}""")
            .On(HttpMethod.Put, $"/api/attendance-corrections/{CorrectionId}/approve", HttpStatusCode.OK, Correction);

        var cut = Render<AttendanceCorrectionApprovals>();
        cut.WaitForElement("[data-approve]").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.EndsWith("/approve")));
    }

    [Fact]
    public void Rejecting_asks_why_and_sends_the_reason()
    {
        _api.On(HttpMethod.Get, "/api/attendance-corrections?page=1&pageSize=20&status=Pending", HttpStatusCode.OK,
                $$"""{"items":[{{Correction}}],"totalCount":1,"page":1,"pageSize":20,"totalPages":1}""")
            .On(HttpMethod.Put, $"/api/attendance-corrections/{CorrectionId}/reject", HttpStatusCode.OK, Correction);

        var cut = Render<AttendanceCorrectionApprovals>();
        cut.WaitForElement("[data-reject]").Click();
        cut.Find("[data-confirm-reject]").HasAttribute("disabled").Should().BeTrue("a rejection needs a reason");
        cut.Find("#reject-reason").Input("The CCTV shows 9:30");
        cut.Find("[data-confirm-reject]").Click();

        cut.WaitForAssertion(() =>
            BodyOf(HttpMethod.Put, $"/api/attendance-corrections/{CorrectionId}/reject")
                .GetProperty("reason").GetString().Should().Be("The CCTV shows 9:30"));
    }
}
