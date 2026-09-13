using System.Globalization;
using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages;

public class DashboardTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.Parse("2d4f6a8c-1e3b-4d5f-9a7c-8e6b4d2f0a1c");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public DashboardTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("maria@company.test");
    }

    private string BalancesPath => $"/api/leave-balances/{EmployeeId}";
    private string AttendancePath => $"/api/attendance?page=1&pageSize=1&employeeId={EmployeeId}";
    private string MyRequestsPath => $"/api/leave-requests?page=1&pageSize=100&employeeId={EmployeeId}";

    private static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":100,"totalPages":1}""";

    private static string Balance(string leaveType, decimal remaining) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{EmployeeId}}","employeeName":"Maria Santos","leaveTypeId":"{{Guid.NewGuid()}}",
         "leaveTypeName":"{{leaveType}}","year":2026,"totalDays":15,"usedDays":0,"carriedOverDays":0,"remainingDays":{{remaining.ToString(CultureInfo.InvariantCulture)}}}
        """;

    private static string Attendance(string date, string? timeIn, string? timeOut, int lateMinutes) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","attendanceDate":"{{date}}","timeIn":{{(timeIn is null ? "null" : $"\"{timeIn}\"")}},
         "timeOut":{{(timeOut is null ? "null" : $"\"{timeOut}\"")}},"lateMinutes":{{lateMinutes}},"undertimeMinutes":0,"isPresent":true}
        """;

    private static string LeaveRequest(string status) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{EmployeeId}}","employeeName":"Maria Santos","leaveTypeName":"Vacation Leave",
         "startDate":"2026-10-05","endDate":"2026-10-06","totalDays":2,"status":"{{status}}","reason":null}
        """;

    private void SignedInAsAnEmployee(string balances = "[]", string attendance = "", string requests = "")
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, BalancesPath, HttpStatusCode.OK, balances)
            .On(HttpMethod.Get, AttendancePath, HttpStatusCode.OK, attendance == "" ? Paged() : attendance)
            .On(HttpMethod.Get, MyRequestsPath, HttpStatusCode.OK, requests == "" ? Paged() : requests);
    }

    private IRenderedComponent<Dashboard> RenderPage()
    {
        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Loading..."));
        return cut;
    }

    [Fact]
    public void ShowsTheFirstThreeLeaveBalances_WithTheDaysRemaining()
    {
        SignedInAsAnEmployee(balances:
            $"[{Balance("Vacation Leave", 12.5m)},{Balance("Sick Leave", 7)},{Balance("Emergency Leave", 3)},{Balance("Solo Parent Leave", 7)}]");

        var cut = RenderPage();

        cut.FindAll("tbody tr").Select(r => r.TextContent.Trim().Replace("\n", " "))
            .Select(t => string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Should().Equal("Vacation Leave 12.5 days", "Sick Leave 7 days", "Emergency Leave 3 days");
    }

    [Fact]
    public void TodaysAttendance_ShowsTheClockTimes_AndHowLateTheEmployeeWas()
    {
        SignedInAsAnEmployee(attendance: Paged(Attendance(Today, "08:17", null, 17)));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("08:17"));
        cut.Markup.Should().Contain("--:--").And.Contain("Late by 17 minutes");
        cut.Markup.Should().NotContain("Not clocked in yet today.");
    }

    [Fact]
    public void OnTime_ShowsNoLateBadge()
    {
        SignedInAsAnEmployee(attendance: Paged(Attendance(Today, "07:55", "17:02", 0)));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("07:55"));
        cut.Markup.Should().Contain("17:02").And.NotContain("Late by");
    }

    [Fact]
    public void ALatestRecordFromAnEarlierDay_DoesNotCountAsClockedInToday()
    {
        // Yesterday's time-in must not be passed off as today's, which would hide that the
        // employee has not clocked in.
        var yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        SignedInAsAnEmployee(
            attendance: Paged(Attendance(yesterday, "08:00", "17:00", 30)),
            requests: Paged(LeaveRequest("Pending")));

        var cut = RenderPage();

        // The pending count is the last thing loaded, so once it shows, attendance has been read.
        cut.WaitForAssertion(() => cut.Find(".text-3xl").TextContent.Trim().Should().Be("1"));
        cut.Markup.Should().Contain("Not clocked in yet today.");
        cut.Markup.Should().NotContain("08:00").And.NotContain("Late by");
    }

    [Fact]
    public void PendingRequests_AreCounted_AndFlaggedForAction()
    {
        SignedInAsAnEmployee(requests: Paged(LeaveRequest("Pending"), LeaveRequest("Approved"), LeaveRequest("Pending"), LeaveRequest("Rejected")));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find(".text-3xl").TextContent.Trim().Should().Be("2"));
        cut.Markup.Should().Contain("Action Required").And.NotContain("All Clear");
    }

    [Fact]
    public void WithNothingPending_ItIsAllClear()
    {
        SignedInAsAnEmployee(requests: Paged(LeaveRequest("Approved")));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("All Clear"));
        cut.Find(".text-3xl").TextContent.Trim().Should().Be("0");
        cut.Markup.Should().NotContain("Action Required");
    }

    [Fact]
    public void AnAccountWithNoEmployeeRecord_AsksForNoPersonalData_AndCountsAllRequests()
    {
        // An HR or admin login is not necessarily an employee. Asking for balances or attendance
        // without an id would fail; the pending count is the organisation's instead.
        _api.On(HttpMethod.Get, "/api/leave-requests?page=1&pageSize=100", HttpStatusCode.OK,
            Paged(LeaveRequest("Pending"), LeaveRequest("Pending"), LeaveRequest("Pending")));

        var cut = Render<Dashboard>();

        cut.WaitForAssertion(() => cut.Find(".text-3xl").TextContent.Trim().Should().Be("3"));
        _api.Requests.Select(r => r.RequestUri!.PathAndQuery)
            .Should().Equal("/api/leave-requests?page=1&pageSize=100");
        cut.Markup.Should().Contain("Not clocked in yet today.");
    }
}
