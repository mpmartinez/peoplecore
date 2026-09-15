using System.Net;
using System.Security.Claims;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Management;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Management;

public class OvertimeApprovalsTests : BunitContext
{
    private const string ListPath = "/api/overtime-requests?page=1&pageSize=20";
    private const string PendingPath = ListPath + "&status=Pending";

    private static readonly Guid ApproverId = Guid.Parse("c3b1f2e4-6a5d-4e7c-9f80-2d1b3a4c5e66");
    private static readonly Guid PendingId = Guid.Parse("d4c2a3f5-7b6e-4f8d-a091-3e2c4b5d6f77");
    private static readonly Guid ApprovedId = Guid.Parse("e5d3b4a6-8c7f-4a9e-b1a2-4f3d5c6e7a88");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    private string _pendingJson = Paged(Overtime(PendingId, "Juan Cruz", 150, "Pending"));
    private bool _pendingFails;

    public OvertimeApprovalsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("manager@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("Manager"));
        _api.On(HttpMethod.Get, PendingPath, () => _pendingFails
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(_pendingJson));
    }

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static string Overtime(Guid id, string employee, int minutes, string status) =>
        $$"""
        {"id":"{{id}}","employeeId":"{{Guid.NewGuid()}}","employeeName":"{{employee}}","overtimeDate":"2026-09-10",
         "startTime":"18:00","endTime":"20:30","totalMinutes":{{minutes}},"reason":"Month-end close","status":"{{status}}","rejectionReason":null}
        """;

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private IRenderedComponent<OvertimeApprovals> RenderPage(bool linkedToEmployee = true)
    {
        if (linkedToEmployee)
            _auth.SetClaims([.. SeededPermissions.ClaimsFor("Manager"), new Claim("employee_id", ApproverId.ToString())]);

        var cut = Render<OvertimeApprovals>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement RowFor(IRenderedComponent<OvertimeApprovals> cut, string employee) =>
        cut.FindAll("tbody tr").Single(tr => tr.TextContent.Contains(employee));

    private static List<string> ActionsIn(IElement row) =>
        row.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

    private string? BodyOf(HttpMethod method, string path) =>
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.PathAndQuery == path)];

    [Fact]
    public void PendingRequests_AreWhatTheManagerSeesFirst()
    {
        var cut = RenderPage();

        _api.Requests.Should().ContainSingle().Which.RequestUri!.PathAndQuery.Should().Be(PendingPath);
        var cells = RowFor(cut, "Juan Cruz").QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList();
        cells.Take(5).Should().Equal("Juan Cruz", "2026-09-10", $"{2.5.ToString("F1")}h", "Month-end close", "Pending");
    }

    [Fact]
    public void NoRequests_ShowsTheEmptyState()
    {
        _pendingJson = Paged();

        var cut = RenderPage();

        cut.Markup.Should().Contain("No overtime requests.");
    }

    [Theory]
    [InlineData("", ListPath)]
    [InlineData("Approved", ListPath + "&status=Approved")]
    [InlineData("Rejected", ListPath + "&status=Rejected")]
    public void ChangingTheStatusFilter_ReloadsWithThatStatus(string filter, string expectedPath)
    {
        _api.On(HttpMethod.Get, expectedPath, HttpStatusCode.OK, Paged(Overtime(ApprovedId, "Rosa Lim", 60, "Approved")));
        var cut = RenderPage();

        cut.Find("select").Change(filter);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Rosa Lim"));
        _api.Requests.Last().RequestUri!.PathAndQuery.Should().Be(expectedPath);
    }

    [Fact]
    public void OnlyPendingRequests_OfferApproveAndReject()
    {
        // A decided request must not be decidable again from this screen.
        _api.On(HttpMethod.Get, ListPath, HttpStatusCode.OK,
            Paged(Overtime(PendingId, "Juan Cruz", 150, "Pending"), Overtime(ApprovedId, "Rosa Lim", 60, "Approved"),
                  Overtime(Guid.NewGuid(), "Leo Tan", 90, "Rejected")));
        var cut = RenderPage();

        cut.Find("select").Change("");

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(3));
        ActionsIn(RowFor(cut, "Juan Cruz")).Should().Equal("Approve", "Reject");
        ActionsIn(RowFor(cut, "Rosa Lim")).Should().BeEmpty();
        ActionsIn(RowFor(cut, "Leo Tan")).Should().BeEmpty();
    }

    [Fact]
    public void AManagerWithoutAnEmployeeLink_CannotDecideAnything()
    {
        // Approvals are recorded against the approver's employee id; without one there is nobody to record.
        var cut = RenderPage(linkedToEmployee: false);

        ActionsIn(RowFor(cut, "Juan Cruz")).Should().BeEmpty();
    }

    [Fact]
    public void Approving_NamesNoApprover_AndDropsTheRequestFromThePendingList()
    {
        var approvePath = $"/api/overtime-requests/{PendingId}/approve";
        _api.On(HttpMethod.Put, approvePath, () =>
        {
            _pendingJson = Paged();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var cut = RenderPage();

        RowFor(cut, "Juan Cruz").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Approve").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No overtime requests."));
        // The API approves as the signed-in manager's own employee id; a body naming one is what
        // let any manager approve as someone else.
        BodyOf(HttpMethod.Put, approvePath).Should().Be("{}");
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Rejecting_SendsTheRejectionReason_AndDropsTheRequestFromThePendingList()
    {
        var rejectPath = $"/api/overtime-requests/{PendingId}/reject";
        _api.On(HttpMethod.Put, rejectPath, () =>
        {
            _pendingJson = Paged();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var cut = RenderPage();

        RowFor(cut, "Juan Cruz").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Reject").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No overtime requests."));
        BodyOf(HttpMethod.Put, rejectPath).Should().Be("""{"rejectionReason":"Rejected by manager"}""");
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/approve"));
    }

    [Theory]
    [InlineData("Approve", "approve", "Failed to approve.")]
    [InlineData("Reject", "reject", "Failed to reject.")]
    public void AFailedDecision_IsReportedInline_AndTheRequestStaysPending(string button, string action, string expectedError)
    {
        _api.On(HttpMethod.Put, $"/api/overtime-requests/{PendingId}/{action}", HttpStatusCode.Conflict);
        var cut = RenderPage();

        RowFor(cut, "Juan Cruz").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == button).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain(expectedError));
        ActionsIn(RowFor(cut, "Juan Cruz")).Should().Equal("Approve", "Reject");
    }

    [Fact]
    public void RequestsThatFailToLoad_AreReported_InsteadOfCrashingThePage()
    {
        _pendingFails = true;
        _auth.SetClaims([.. SeededPermissions.ClaimsFor("Manager"), new Claim("employee_id", ApproverId.ToString())]);

        var cut = Render<OvertimeApprovals>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load overtime requests"));
        cut.FindAll(".animate-spin").Should().BeEmpty();
    }

    [Fact]
    public void AFilterChangeThatFails_DoesNotLeaveThePreviousListOnScreen()
    {
        _api.On(HttpMethod.Get, ListPath + "&status=Approved", HttpStatusCode.InternalServerError);
        var cut = RenderPage();

        cut.Find("select").Change("Approved");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load overtime requests"));
        cut.Markup.Should().NotContain("Juan Cruz", "those are the Pending requests, not the Approved ones");
    }

    [Fact]
    public void AnEarlierFailure_IsClearedOnceALaterDecisionSucceeds()
    {
        var rosaId = Guid.NewGuid();
        _pendingJson = Paged(Overtime(PendingId, "Juan Cruz", 150, "Pending"), Overtime(rosaId, "Rosa Lim", 60, "Pending"));
        _api.On(HttpMethod.Put, $"/api/overtime-requests/{PendingId}/approve", HttpStatusCode.Conflict)
            .On(HttpMethod.Put, $"/api/overtime-requests/{rosaId}/approve", HttpStatusCode.NoContent);
        var cut = RenderPage();

        RowFor(cut, "Juan Cruz").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Approve").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role=alert]").Should().ContainSingle());

        RowFor(cut, "Rosa Lim").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Approve").Click();

        cut.WaitForAssertion(() => cut.FindAll("[role=alert]").Should().BeEmpty());
    }
}
