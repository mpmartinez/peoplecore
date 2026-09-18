using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.ESS;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.ESS;

public class MyAttendanceTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.Parse("5d2b8e14-9f3a-4c61-b0e7-1a8c4d6f2e33");

    private static readonly string AttendancePath = $"/api/attendance?page=1&pageSize=20&employeeId={EmployeeId}";

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    private string _recordsJson = Paged();

    public MyAttendanceTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("juan@company.test");
    }

    // The page decides "already clocked in" by comparing record dates with the local calendar
    // day, so the fixtures have to be built from the same clock.
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private static string Yesterday => DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static string Record(string date, string? timeIn, string? timeOut, int late = 0, int undertime = 0) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","attendanceDate":"{{date}}","timeIn":{{JsonSerializer.Serialize(timeIn)}},
         "timeOut":{{JsonSerializer.Serialize(timeOut)}},"lateMinutes":{{late}},"undertimeMinutes":{{undertime}},"isPresent":true}
        """;

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private IRenderedComponent<MyAttendance> RenderAsLinkedEmployee()
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, AttendancePath, () => Json(_recordsJson));

        var cut = Render<MyAttendance>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement Button(IRenderedComponent<MyAttendance> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static bool IsEnabled(IElement button) => !button.HasAttribute("disabled");

    private JsonElement PostedBody(string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a POST to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    [Fact]
    public void AnAccountWithoutAnEmployeeLink_CannotClockInOrOut_AndFetchesNobodysRecords()
    {
        var cut = Render<MyAttendance>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Account not linked"));
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Time In") || b.TextContent.Contains("Time Out"));
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public void RecentAttendance_IsTheSignedInEmployeesOwn_WithGapsShownAsDashes()
    {
        _recordsJson = Paged(Record(Yesterday, "08:12", null, late: 12, undertime: 0));

        var cut = RenderAsLinkedEmployee();

        _api.Requests.Should().ContainSingle().Which.RequestUri!.PathAndQuery.Should().Be(AttendancePath);
        cut.FindAll("tbody td").Select(td => td.TextContent.Trim())
            .Should().Equal(Yesterday, "08:12", "--", "12", "0");
    }

    [Fact]
    public void ClockTimes_AsTheApiSendsThem_ShowTheManilaWallClock_NotShiftedIntoTheBrowsersZone()
    {
        // The API stores and returns Philippine wall-clock time labelled UTC: "...T08:07:00Z" is
        // 08:07 in Manila. Converting it to the browser's zone would show 16:07 in Manila.
        _recordsJson = Paged(Record(Yesterday, $"{Yesterday}T08:07:00Z", $"{Yesterday}T17:00:00Z", late: 7));

        var cut = RenderAsLinkedEmployee();

        cut.FindAll("tbody td").Select(td => td.TextContent.Trim())
            .Should().Equal(Yesterday, "08:07", "17:00", "7", "0");
    }

    [Fact]
    public void NoRecords_ShowsTheEmptyState()
    {
        var cut = RenderAsLinkedEmployee();

        cut.Markup.Should().Contain("No attendance records found.");
    }

    [Fact]
    public void NotYetClockedInToday_OnlyTimeInIsAvailable()
    {
        // Yesterday's time-in must not count: otherwise nobody could clock in on a new day.
        _recordsJson = Paged(Record(Yesterday, "08:00", "17:00"));

        var cut = RenderAsLinkedEmployee();

        IsEnabled(Button(cut, "Time In")).Should().BeTrue();
        IsEnabled(Button(cut, "Time Out")).Should().BeFalse();
    }

    [Fact]
    public void AlreadyClockedInToday_OnlyTimeOutIsAvailable()
    {
        _recordsJson = Paged(Record(Today, "08:00", null), Record(Yesterday, "08:00", "17:00"));

        var cut = RenderAsLinkedEmployee();

        IsEnabled(Button(cut, "Time In")).Should().BeFalse();
        IsEnabled(Button(cut, "Time Out")).Should().BeTrue();
    }

    [Fact]
    public void TimeIn_PostsForTheSignedInEmployee_ThenSwitchesToTimeOutAndRefreshesTheList()
    {
        _api.On(HttpMethod.Post, "/api/attendance/time-in", () =>
        {
            _recordsJson = Paged(Record(Today, "08:03", null));
            return Json(Record(Today, "08:03", null));
        });
        var cut = RenderAsLinkedEmployee();

        Button(cut, "Time In").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Clocked in successfully."));
        var body = PostedBody("/api/attendance/time-in");
        body.GetProperty("employeeId").GetGuid().Should().Be(EmployeeId);
        // The server stamps the punch with its own clock; the browser's is neither sent nor trusted.
        body.EnumerateObject().Select(p => p.Name).Should().Equal("employeeId");

        IsEnabled(Button(cut, "Time In")).Should().BeFalse();
        IsEnabled(Button(cut, "Time Out")).Should().BeTrue();
        cut.FindAll("tbody td").Select(td => td.TextContent.Trim()).Should().StartWith([Today, "08:03", "--"]);
    }

    [Fact]
    public void TimeOut_PostsForTheSignedInEmployee_AndRefreshesTheList()
    {
        _recordsJson = Paged(Record(Today, "08:00", null));
        _api.On(HttpMethod.Post, "/api/attendance/time-out", () =>
        {
            _recordsJson = Paged(Record(Today, "08:00", "17:05"));
            return Json(Record(Today, "08:00", "17:05"));
        });
        var cut = RenderAsLinkedEmployee();

        Button(cut, "Time Out").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Clocked out successfully."));
        var body = PostedBody("/api/attendance/time-out");
        body.GetProperty("employeeId").GetGuid().Should().Be(EmployeeId);
        body.EnumerateObject().Select(p => p.Name).Should().Equal("employeeId");
        cut.FindAll("tbody td").Select(td => td.TextContent.Trim()).Should().StartWith([Today, "08:00", "17:05"]);
    }

    [Fact]
    public void AFailedTimeIn_SaysSo_AndLeavesTimeInAvailableToRetry()
    {
        _api.On(HttpMethod.Post, "/api/attendance/time-in", HttpStatusCode.InternalServerError);
        var cut = RenderAsLinkedEmployee();

        Button(cut, "Time In").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("500"));
        cut.Markup.Should().NotContain("Clocked in successfully.");
        IsEnabled(Button(cut, "Time In")).Should().BeTrue();
        IsEnabled(Button(cut, "Time Out")).Should().BeFalse();
    }

    [Fact]
    public void AnAccountWithoutAnEmployeeLink_HasNoRecordsToWaitFor()
    {
        // There is nothing to fetch, so the history card must not spin forever waiting for it.
        var cut = Render<MyAttendance>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Account not linked"));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        cut.Markup.Should().Contain("No attendance to show for an account without an employee record.");
    }

    [Fact]
    public void AlreadyClockedOutToday_NeitherButtonIsAvailable()
    {
        // The API allows one time-in a day, and a second time-out would overwrite the first.
        _recordsJson = Paged(Record(Today, "08:00", "17:00"));

        var cut = RenderAsLinkedEmployee();

        IsEnabled(Button(cut, "Time In")).Should().BeFalse();
        IsEnabled(Button(cut, "Time Out")).Should().BeFalse();
        cut.Markup.Should().Contain("You have clocked out for today.");
    }

    [Fact]
    public void AfterClockingOut_TimeOutCannotBePressedAgain()
    {
        _recordsJson = Paged(Record(Today, "08:00", null));
        _api.On(HttpMethod.Post, "/api/attendance/time-out", () =>
        {
            _recordsJson = Paged(Record(Today, "08:00", "17:05"));
            return Json(Record(Today, "08:00", "17:05"));
        });
        var cut = RenderAsLinkedEmployee();

        Button(cut, "Time Out").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Clocked out successfully."));
        IsEnabled(Button(cut, "Time Out")).Should().BeFalse();
        IsEnabled(Button(cut, "Time In")).Should().BeFalse();
    }

    [Fact]
    public void HistoryThatFailsToLoad_IsReported_AndTheClockStaysUsable()
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, AttendancePath, HttpStatusCode.InternalServerError);

        var cut = Render<MyAttendance>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load your attendance"));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        IsEnabled(Button(cut, "Time In")).Should().BeTrue("a history outage must not stop anyone clocking in");
    }

    [Fact]
    public void ARefusedTimeIn_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Post, "/api/attendance/time-in", HttpStatusCode.BadRequest,
            """{"detail":"Employee has already clocked in today."}""");
        var cut = RenderAsLinkedEmployee();

        Button(cut, "Time In").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Employee has already clocked in today."));
    }
}
