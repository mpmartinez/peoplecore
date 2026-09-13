using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

public class LeaveApprovalsTests : BunitContext
{
    private const string RequestsPath = "/api/leave-requests?page=1&pageSize=50";
    private static readonly Guid PendingId = Guid.Parse("5a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid ApprovedId = Guid.Parse("6b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");

    private readonly StubHttpHandler _api = new();

    // Read on every request, so a test can change what the API holds after the page has loaded
    // and see whether the page actually asks again.
    private string _requests = Paged(
        Request(PendingId, "Maria Santos", "Pending", "Family event"),
        Request(ApprovedId, "Jose Rizal", "Approved", reason: null));

    public LeaveApprovalsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        AddAuthorization().SetAuthorized("manager@company.test");

        _api.On(HttpMethod.Get, RequestsPath, () => Json(_requests));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":50,"totalPages":1}""";

    private static string Request(Guid id, string employee, string status, string? reason) =>
        $$"""
        {"id":"{{id}}","employeeId":"{{Guid.NewGuid()}}","employeeName":"{{employee}}","leaveTypeName":"Vacation Leave",
         "startDate":"2026-10-05","endDate":"2026-10-07","totalDays":3,"status":"{{status}}",
         "reason":{{(reason is null ? "null" : $"\"{reason}\"")}}}
        """;

    private IRenderedComponent<LeaveApprovals> RenderPage()
    {
        var cut = Render<LeaveApprovals>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement RowFor(IRenderedComponent<LeaveApprovals> cut, string employee) =>
        cut.FindAll("tbody tr").Single(r => r.QuerySelector("td")!.TextContent.Trim() == employee);

    private static IElement ButtonIn(IElement row, string text) =>
        row.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private static List<string> ButtonsIn(IElement row) =>
        row.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

    private int PutIndex => _api.Requests.FindIndex(r => r.Method == HttpMethod.Put);

    [Fact]
    public void ListsTheRequests_AndOnlyPendingOnesCanBeDecided()
    {
        var cut = RenderPage();

        var pending = RowFor(cut, "Maria Santos");
        pending.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).Take(7)
            .Should().Equal("Maria Santos", "Vacation Leave", "2026-10-05", "2026-10-07", "3", "Family event", "Pending");
        ButtonsIn(pending).Should().Equal("Approve", "Reject");

        var decided = RowFor(cut, "Jose Rizal");
        decided.QuerySelectorAll("td")[5].TextContent.Trim().Should().Be("--");
        ButtonsIn(decided).Should().BeEmpty("a request that was already decided must not be decided again");
    }

    [Fact]
    public void NoRequests_ShowsTheEmptyState()
    {
        _requests = Paged();

        var cut = RenderPage();

        cut.Markup.Should().Contain("No pending leave requests.");
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Approving_SendsTheApprovalForThatRequest_AndRefreshesTheList()
    {
        _api.On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/approve", HttpStatusCode.OK,
            Request(PendingId, "Maria Santos", "Approved", "Family event"));
        var cut = RenderPage();

        _requests = Paged(
            Request(PendingId, "Maria Santos", "Approved", "Family event"),
            Request(ApprovedId, "Jose Rizal", "Approved", reason: null));
        ButtonIn(RowFor(cut, "Maria Santos"), "Approve").Click();

        cut.WaitForAssertion(() => ButtonsIn(RowFor(cut, "Maria Santos")).Should().BeEmpty());
        _api.Requests.Where(r => r.Method == HttpMethod.Put).Should().ContainSingle()
            .Which.RequestUri!.PathAndQuery.Should().Be($"/api/leave-requests/{PendingId}/approve");
        _api.RequestBodies[PutIndex].Should().Be("{}");
        _api.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(2);
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Rejecting_SendsARejectionWithAReason_AndRefreshesTheList()
    {
        _api.On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/reject", HttpStatusCode.OK,
            Request(PendingId, "Maria Santos", "Rejected", "Family event"));
        var cut = RenderPage();

        _requests = Paged(Request(PendingId, "Maria Santos", "Rejected", "Family event"));
        ButtonIn(RowFor(cut, "Maria Santos"), "Reject").Click();

        cut.WaitForAssertion(() => ButtonsIn(RowFor(cut, "Maria Santos")).Should().BeEmpty());
        _api.Requests[PutIndex].RequestUri!.PathAndQuery.Should().Be($"/api/leave-requests/{PendingId}/reject");
        _api.RequestBodies[PutIndex].Should().Be("""{"reason":"Rejected by manager."}""");
        _api.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.EndsWith("/approve"));
    }

    [Fact]
    public void AFailedDecision_IsReported_AndTheRequestStaysOpen_UntilALaterOneSucceeds()
    {
        // The API refuses, e.g. the employee no longer has the balance. The manager has to see
        // that nothing happened; a silent failure reads as an approval that went through.
        _api.On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/approve", HttpStatusCode.Conflict)
            .On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/reject", HttpStatusCode.OK,
                Request(PendingId, "Maria Santos", "Rejected", "Family event"));
        var cut = RenderPage();

        ButtonIn(RowFor(cut, "Maria Santos"), "Approve").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("409"));
        ButtonsIn(RowFor(cut, "Maria Santos")).Should().Equal("Approve", "Reject");
        _api.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(1, "nothing changed, so there is nothing to reload");

        ButtonIn(RowFor(cut, "Maria Santos"), "Reject").Click();

        cut.WaitForAssertion(() => cut.FindAll("[role=alert]").Should().BeEmpty());
    }
}
