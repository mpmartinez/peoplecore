using System.Net;
using System.Security.Claims;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.ESS;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.ESS;

public class MyLeaveTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.Parse("3f6a9d21-8c4b-4e0f-a7d2-5b1e9c3f7a01");
    private static readonly Guid VacationTypeId = Guid.Parse("a1c0e5d7-2b3f-4c8e-9d1a-6f4b7e2c0d11");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    private string _balancesJson = "[]";
    private string _requestsJson = Paged();

    public MyLeaveTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("maria@company.test");
    }

    private static string BalancesPath(Guid employeeId) => $"/api/leave-balances/{employeeId}";

    private static string RequestsPath(Guid employeeId) => $"/api/leave-requests?page=1&pageSize=20&employeeId={employeeId}";

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static string VacationBalance(int total, int used) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{EmployeeId}}","employeeName":"Maria Santos","leaveTypeId":"{{VacationTypeId}}",
         "leaveTypeName":"Vacation Leave","year":2026,"totalDays":{{total}},"usedDays":{{used}},"carriedOverDays":0,"remainingDays":{{total - used}}}
        """;

    private static string Request(string start, string end, int days, string status) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{EmployeeId}}","employeeName":"Maria Santos","leaveTypeName":"Vacation Leave",
         "startDate":"{{start}}","endDate":"{{end}}","totalDays":{{days}},"status":"{{status}}","reason":null}
        """;

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Serves the signed-in employee's balances and history from fields a test can change mid-flight.</summary>
    private void ServeOwnLeave()
    {
        _api.On(HttpMethod.Get, BalancesPath(EmployeeId), () => Json(_balancesJson))
            .On(HttpMethod.Get, RequestsPath(EmployeeId), () => Json(_requestsJson));
    }

    private IRenderedComponent<MyLeave> RenderPage()
    {
        var cut = Render<MyLeave>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private IRenderedComponent<MyLeave> RenderAsLinkedEmployee()
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        ServeOwnLeave();
        return RenderPage();
    }

    private static IElement SubmitButton(IRenderedComponent<MyLeave> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Submit Request");

    private string? PostedBody() =>
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)];

    [Fact]
    public void OnlyTheSignedInEmployeesOwnBalancesAndHistory_AreRequested()
    {
        var cut = RenderAsLinkedEmployee();

        cut.Markup.Should().Contain("No leave balances found.").And.Contain("No leave requests yet.");
        _api.Requests.Select(r => r.RequestUri!.PathAndQuery)
            .Should().BeEquivalentTo([BalancesPath(EmployeeId), RequestsPath(EmployeeId)]);
    }

    [Fact]
    public void AnAccountWithoutAnEmployeeLink_NeverAsksForEveryonesLeaveRequests()
    {
        // The leave-requests endpoint lists the whole company when employeeId is omitted, so a
        // missing claim must still produce a filtered query rather than an unfiltered one.
        _api.On(HttpMethod.Get, BalancesPath(Guid.Empty), HttpStatusCode.OK, "[]")
            .On(HttpMethod.Get, RequestsPath(Guid.Empty), HttpStatusCode.OK, Paged());

        RenderPage();

        _api.Requests.Should().NotBeEmpty()
            .And.OnlyContain(r => !r.RequestUri!.AbsolutePath.StartsWith("/api/leave-requests")
                                  || r.RequestUri.Query.Contains("employeeId="));
    }

    [Fact]
    public void BalancesAndHistory_AreShown_AndTheTypePickerSaysWhatIsLeft()
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        _requestsJson = Paged(Request("2026-08-03", "2026-08-05", 3, "Approved"), Request("2026-09-21", "2026-09-21", 1, "Pending"));

        var cut = RenderAsLinkedEmployee();

        var balanceCells = cut.FindAll("table")[0].QuerySelectorAll("tbody td").Select(td => td.TextContent.Trim());
        balanceCells.Should().Equal("Vacation Leave", "15", "3", "12");

        cut.Find($"#leaveType option[value='{VacationTypeId}']").TextContent.Should().Be("Vacation Leave (12 days left)");

        var history = cut.FindAll("table")[1].QuerySelectorAll("tbody tr")
            .Select(tr => string.Join("|", tr.QuerySelectorAll("td").Select(td => td.TextContent.Trim())));
        history.Should().Equal("Vacation Leave|2026-08-03|2026-08-05|3|Approved", "Vacation Leave|2026-09-21|2026-09-21|1|Pending");
    }

    [Fact]
    public void SubmittingWithoutALeaveType_IsRefusedWithoutCallingTheApi()
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        var cut = RenderAsLinkedEmployee();

        SubmitButton(cut).Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("Please select a leave type.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("Family trip to Baguio", "\"Family trip to Baguio\"")]
    [InlineData("   ", "null")]
    public void AValidRequest_IsFiledForTheSignedInEmployee_AndTheBalancesAndHistoryRefresh(string reason, string expectedReasonJson)
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
        {
            // What the server would now report: the days are reserved and the request is on file.
            _balancesJson = $"[{VacationBalance(total: 15, used: 6)}]";
            _requestsJson = Paged(Request("2026-10-05", "2026-10-07", 3, "Pending"));
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(Request("2026-10-05", "2026-10-07", 3, "Pending"), Encoding.UTF8, "application/json")
            };
        });
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        cut.Find("#leaveFrom").Input("2026-10-05");
        cut.Find("#leaveTo").Input("2026-10-07");
        cut.Find("#leaveReason").Input(reason);
        SubmitButton(cut).Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Leave request submitted successfully."));
        PostedBody().Should().Be(
            $$"""{"employeeId":"{{EmployeeId}}","leaveTypeId":"{{VacationTypeId}}","startDate":"2026-10-05","endDate":"2026-10-07","reason":{{expectedReasonJson}}}""");
        cut.WaitForAssertion(() =>
        {
            cut.Find($"#leaveType option[value='{VacationTypeId}']").TextContent.Should().Be("Vacation Leave (9 days left)");
            cut.FindAll("table")[1].QuerySelectorAll("tbody tr").Should().ContainSingle();
        });
    }

    [Fact]
    public void ARejectedRequest_ShowsTheFailureInline_AndLetsTheEmployeeTryAgain()
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", HttpStatusCode.BadRequest);
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("400"));
        cut.Markup.Should().NotContain("Leave request submitted successfully.");
        SubmitButton(cut).HasAttribute("disabled").Should().BeFalse();
    }
}
