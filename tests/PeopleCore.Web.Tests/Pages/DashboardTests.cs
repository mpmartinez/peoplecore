using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
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

    // Only the count is shown, so one row is enough; the API's total says how many there are.
    private string MyPendingPath => $"/api/leave-requests?page=1&pageSize=1&employeeId={EmployeeId}&status=Pending";
    private const string AllPendingPath = "/api/leave-requests?page=1&pageSize=1&status=Pending";

    private static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":100,"totalPages":1}""";

    /// <summary>The first page of a pending-only query whose total is <paramref name="total"/>.</summary>
    private static string Pending(int total) =>
        $$"""{"items":[{{(total == 0 ? "" : LeaveRequest("Pending"))}}],"totalCount":{{total}},"page":1,"pageSize":1,"totalPages":{{total}}}""";

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

    private static HttpResponseMessage ServerError(string detail) =>
        new(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$"""{"detail":"{{detail}}"}""", Encoding.UTF8, "application/json")
        };

    private void SignedInAsAnEmployee(string balances = "[]", string attendance = "", string requests = "")
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, BalancesPath, HttpStatusCode.OK, balances)
            .On(HttpMethod.Get, AttendancePath, HttpStatusCode.OK, attendance == "" ? Paged() : attendance)
            .On(HttpMethod.Get, MyPendingPath, HttpStatusCode.OK, requests == "" ? Pending(0) : requests);
    }

    /// <summary>
    /// Signed in as an employee whose request to <paramref name="failingPath"/> fails. The stub
    /// answers with the first route that matches, so the failure is registered first.
    /// </summary>
    private void SignedInAsAnEmployeeWhereThisFails(string failingPath, string detail)
    {
        _api.On(HttpMethod.Get, failingPath, () => ServerError(detail));
        SignedInAsAnEmployee(balances: $"[{Balance("Vacation Leave", 12)}]", requests: Pending(4));
    }

    private IRenderedComponent<Dashboard> RenderPage()
    {
        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Loading..."));
        return cut;
    }

    /// <summary>The text of the card with this title, whitespace collapsed.</summary>
    private static string CardText(IRenderedComponent<Dashboard> cut, string title)
    {
        var card = cut.FindAll("h3").Single(h => h.TextContent.Trim() == title).ParentElement!.ParentElement!;
        return string.Join(' ', card.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
    public void NoLeaveBalances_SaysSo_InsteadOfAnEmptyTable()
    {
        SignedInAsAnEmployee(balances: "[]");

        var cut = RenderPage();

        CardText(cut, "Leave Balances").Should().Be("Leave Balances No leave balances yet.");
        cut.FindAll("table").Should().BeEmpty();
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
            requests: Pending(1));

        var cut = RenderPage();

        cut.Markup.Should().Contain("Not clocked in yet today.");
        cut.Markup.Should().NotContain("08:00").And.NotContain("Late by");
    }

    [Fact]
    public void PendingRequests_AreCountedFromTheApisTotal_AndFlaggedForAction()
    {
        // The total, not the rows that came back: counting a page of rows undercounts as soon as
        // there are more pending requests than fit on it.
        SignedInAsAnEmployee(requests: Pending(137));

        var cut = RenderPage();

        cut.Find(".text-3xl").TextContent.Trim().Should().Be("137");
        cut.Markup.Should().Contain("Action Required").And.NotContain("All Clear");
    }

    [Fact]
    public void WithNothingPending_ItIsAllClear()
    {
        SignedInAsAnEmployee(requests: Pending(0));

        var cut = RenderPage();

        cut.Markup.Should().Contain("All Clear");
        cut.Find(".text-3xl").TextContent.Trim().Should().Be("0");
        cut.Markup.Should().NotContain("Action Required");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HRManager")]
    [InlineData("Manager")]
    public void ALeaveStaffAccountWithNoEmployeeRecord_AsksForNoPersonalData_SaysSo_AndCountsAllPendingRequests(string role)
    {
        // An HR or admin login is not necessarily an employee. Asking for balances or attendance
        // without an id would fail, and waiting for them would spin forever; the pending count is
        // the organisation's instead.
        _auth.SetRoles(role);
        _api.On(HttpMethod.Get, AllPendingPath, HttpStatusCode.OK, Pending(3));

        var cut = RenderPage();

        cut.Find(".text-3xl").TextContent.Trim().Should().Be("3");
        _api.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().Equal(AllPendingPath);
        CardText(cut, "Leave Balances").Should().Contain("not linked to an employee record");
        CardText(cut, "Today's Attendance").Should().Contain("not linked to an employee record")
            .And.NotContain("Not clocked in yet today.", "there is no one to clock in");
    }

    [Fact]
    public void AnUnprivilegedAccountWithNoEmployeeRecord_AsksForNothing_AndSaysSoInEveryCard()
    {
        // The API refuses the organisation's leave requests to anyone but leave staff, so asking
        // would only put a 403 in the pending card. There is nothing of the caller's own to count.
        _auth.SetRoles("Employee");

        var cut = RenderPage();

        _api.Requests.Should().BeEmpty();
        cut.FindAll(".text-3xl").Should().BeEmpty("a zero would read as nothing to approve");
        CardText(cut, "Pending Leave Requests").Should().Contain("not linked to an employee record")
            .And.NotContain("All Clear");
        CardText(cut, "Leave Balances").Should().Contain("not linked to an employee record");
    }

    [Fact]
    public void BalancesThatFailToLoad_AreExplainedInTheirCard_AndTheRestStillShows()
    {
        SignedInAsAnEmployeeWhereThisFails(BalancesPath, "Balances are unavailable.");

        var cut = RenderPage();

        CardText(cut, "Leave Balances").Should().Contain("Balances are unavailable.");
        cut.Find(".text-3xl").TextContent.Trim().Should().Be("4");
        CardText(cut, "Today's Attendance").Should().Contain("Not clocked in yet today.");
    }

    [Fact]
    public void AttendanceThatFailsToLoad_IsExplained_NotReportedAsNotClockedIn()
    {
        // "Not clocked in" would be a claim about the employee that nobody checked.
        SignedInAsAnEmployeeWhereThisFails(AttendancePath, "Attendance is unavailable.");

        var cut = RenderPage();

        CardText(cut, "Today's Attendance").Should().Contain("Attendance is unavailable.").And.NotContain("Not clocked in");
        CardText(cut, "Leave Balances").Should().Contain("Vacation Leave");
        cut.Find(".text-3xl").TextContent.Trim().Should().Be("4");
    }

    [Fact]
    public void APendingCountThatFailsToLoad_IsExplained_NotReportedAsAllClear()
    {
        SignedInAsAnEmployeeWhereThisFails(MyPendingPath, "Leave requests are unavailable.");

        var cut = RenderPage();

        var card = CardText(cut, "Pending Leave Requests");
        card.Should().Contain("Leave requests are unavailable.").And.NotContain("All Clear").And.NotContain("Action Required");
        cut.FindAll(".text-3xl").Should().BeEmpty("a zero would read as nothing to approve");
        CardText(cut, "Leave Balances").Should().Contain("Vacation Leave");
    }
}
