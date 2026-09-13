using System.Net;
using System.Security.Claims;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
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
    private readonly BunitAuthorizationContext _auth;

    // Read on every request, so a test can change what the API holds after the page has loaded
    // and see whether the page actually asks again.
    private string _requests = Paged(
        Request(PendingId, "Maria Santos", "Pending", "Family event"),
        Request(ApprovedId, "Jose Rizal", "Approved", reason: null));

    // When set, the first page of the list fails with this explanation instead.
    private string? _loadFailure;

    private bool _decided;

    public LeaveApprovalsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("manager@company.test");

        _api.On(HttpMethod.Get, RequestsPath, () => _loadFailure is null ? Json(_requests) : ServerError(_loadFailure));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ServerError(string detail) =>
        new(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$"""{"detail":"{{detail}}"}""", Encoding.UTF8, "application/json")
        };

    private static string Paged(params string[] items) => PageOf(1, 1, items);

    private static string PageOf(int page, int totalPages, params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":{{page}},"pageSize":50,"totalPages":{{totalPages}}}""";

    private static string Request(Guid id, string employee, string status, string? reason, Guid? employeeId = null) =>
        $$"""
        {"id":"{{id}}","employeeId":"{{employeeId ?? Guid.NewGuid()}}","employeeName":"{{employee}}","leaveTypeName":"Vacation Leave",
         "startDate":"2026-10-05","endDate":"2026-10-07","totalDays":3,"status":"{{status}}",
         "reason":{{(reason is null ? "null" : $"\"{reason}\"")}}}
        """;

    private static readonly Guid SignedInEmployeeId = Guid.Parse("7c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f");

    private IRenderedComponent<LeaveApprovals> RenderPage(bool linkedToEmployee = true)
    {
        if (linkedToEmployee)
            _auth.SetClaims(new Claim("employee_id", SignedInEmployeeId.ToString()));

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

    private List<string> ListRequests =>
        _api.Requests.Where(r => r.Method == HttpMethod.Get).Select(r => r.RequestUri!.PathAndQuery).ToList();

    private static IElement StatusFilter(IRenderedComponent<LeaveApprovals> cut) => cut.Find("select");

    private static IElement PagerButton(IRenderedComponent<LeaveApprovals> cut, string text) =>
        cut.Find("nav:not([aria-label=breadcrumb])").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

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
    public void AnAccountWithoutAnEmployeeLink_CannotDecideAnything()
    {
        // The API decides as the account's employee and refuses an account with none.
        var cut = RenderPage(linkedToEmployee: false);

        ButtonsIn(RowFor(cut, "Maria Santos")).Should().BeEmpty();
    }

    [Fact]
    public void TheSignedInManagersOwnRequest_CannotBeDecidedByThem()
    {
        // The API refuses anyone approving or rejecting their own leave; somebody else decides it.
        _requests = Paged(
            Request(PendingId, "Maria Santos", "Pending", "Family event"),
            Request(Guid.NewGuid(), "Signed-in Manager", "Pending", "Conference", SignedInEmployeeId));

        var cut = RenderPage();

        ButtonsIn(RowFor(cut, "Signed-in Manager")).Should().BeEmpty();
        ButtonsIn(RowFor(cut, "Maria Santos")).Should().Equal("Approve", "Reject");
    }

    [Fact]
    public void NoRequests_ShowsTheEmptyState()
    {
        // The page opens on every status, so "no pending requests" would claim more than it knows.
        _requests = Paged();

        var cut = RenderPage();

        cut.Markup.Should().Contain("No leave requests.").And.NotContain("pending");
        cut.FindAll("table").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Pending", "No pending leave requests.")]
    [InlineData("Approved", "No approved leave requests.")]
    [InlineData("Rejected", "No rejected leave requests.")]
    public void TheEmptyState_SaysWhichStatusHasNothing(string status, string expected)
    {
        _api.On(HttpMethod.Get, $"{RequestsPath}&status={status}", () => Json(Paged()));
        var cut = RenderPage();

        StatusFilter(cut).Change(status);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(expected));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public void TheStatusFilter_AsksTheApiForThatStatusOnly(string status)
    {
        _api.On(HttpMethod.Get, $"{RequestsPath}&status={status}", () =>
            Json(Paged(Request(Guid.NewGuid(), "Andres Bonifacio", status, "Filtered"))));
        var cut = RenderPage();

        StatusFilter(cut).Change(status);

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle()
            .Which.QuerySelector("td")!.TextContent.Trim().Should().Be("Andres Bonifacio"));
    }

    [Fact]
    public void ChoosingAllStatusesAgain_DropsTheStatusFromTheQuery()
    {
        _api.On(HttpMethod.Get, $"{RequestsPath}&status=Pending", () => Json(Paged()));
        var cut = RenderPage();
        StatusFilter(cut).Change("Pending");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No pending leave requests."));

        StatusFilter(cut).Change("");

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(2));
        ListRequests.Last().Should().Be(RequestsPath);
    }

    [Fact]
    public void MoreThanOnePage_OffersAPager_ThatAsksForTheChosenPage()
    {
        _requests = PageOf(1, 2, Request(PendingId, "Maria Santos", "Pending", "Family event"));
        _api.On(HttpMethod.Get, "/api/leave-requests?page=2&pageSize=50", () =>
            Json(PageOf(2, 2, Request(Guid.NewGuid(), "Andres Bonifacio", "Pending", "Checkup"))));
        var cut = RenderPage();

        PagerButton(cut, "2").Click();

        cut.WaitForAssertion(() => RowFor(cut, "Andres Bonifacio"));
        cut.Find("nav:not([aria-label=breadcrumb])").TextContent.Should().Contain("Page 2 of 2");
    }

    [Fact]
    public void ChangingTheStatus_StartsAgainFromPageOne()
    {
        _requests = PageOf(1, 2, Request(PendingId, "Maria Santos", "Pending", "Family event"));
        _api.On(HttpMethod.Get, "/api/leave-requests?page=2&pageSize=50", () =>
                Json(PageOf(2, 2, Request(Guid.NewGuid(), "Andres Bonifacio", "Pending", "Checkup"))))
            .On(HttpMethod.Get, $"{RequestsPath}&status=Approved", () => Json(Paged()));
        var cut = RenderPage();
        PagerButton(cut, "2").Click();
        cut.WaitForAssertion(() => RowFor(cut, "Andres Bonifacio"));

        StatusFilter(cut).Change("Approved");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No approved leave requests."));
        ListRequests.Last().Should().Be($"{RequestsPath}&status=Approved");
    }

    [Fact]
    public void DecidingTheLastRequestOnTheLastPage_MovesBackToAPageThatHasRequests()
    {
        // With the Pending filter on, a decided request drops out of the list. Staying on a page
        // that no longer exists would show "no pending requests" while page 1 still has some.
        _api.On(HttpMethod.Get, $"{RequestsPath}&status=Pending", () =>
                Json(PageOf(1, _decided ? 1 : 2, Request(PendingId, "Maria Santos", "Pending", "Family event"))))
            .On(HttpMethod.Get, "/api/leave-requests?page=2&pageSize=50&status=Pending", () =>
                Json(_decided ? PageOf(2, 1) : PageOf(2, 2, Request(ApprovedId, "Jose Rizal", "Pending", reason: null))))
            .On(HttpMethod.Put, $"/api/leave-requests/{ApprovedId}/approve", () =>
            {
                _decided = true;
                return Json(Request(ApprovedId, "Jose Rizal", "Approved", reason: null));
            });
        var cut = RenderPage();
        StatusFilter(cut).Change("Pending");
        cut.WaitForAssertion(() => cut.FindAll("nav").Should().NotBeEmpty());
        PagerButton(cut, "2").Click();
        cut.WaitForAssertion(() => RowFor(cut, "Jose Rizal"));

        ButtonIn(RowFor(cut, "Jose Rizal"), "Approve").Click();

        cut.WaitForAssertion(() => RowFor(cut, "Maria Santos"));
        ListRequests.Last().Should().Be($"{RequestsPath}&status=Pending");
    }

    [Fact]
    public void AFailedLoad_ShowsWhy_InsteadOfSpinningForever()
    {
        _loadFailure = "Leave requests are unavailable.";

        var cut = Render<LeaveApprovals>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Leave requests are unavailable."));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
        cut.Markup.Should().NotContain("No leave requests.", "a failure is not the same as having nothing to decide");
    }

    [Fact]
    public void AFailedReload_AfterADecision_DropsTheRowsItWasShowing_AndDoesNotBlameTheDecision()
    {
        // The approval went through. Reporting it as failed invites a second attempt; leaving the
        // old rows up would still offer Approve on a request that is already approved.
        _api.On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/approve", () =>
        {
            _loadFailure = "Leave requests are unavailable.";
            return Json(Request(PendingId, "Maria Santos", "Approved", "Family event"));
        });
        var cut = RenderPage();

        ButtonIn(RowFor(cut, "Maria Santos"), "Approve").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Leave requests are unavailable."));
        cut.FindAll("[role=alert]").Should().ContainSingle();
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
        // The API's RejectLeaveDto reads rejectionReason; under any other name the reason is dropped.
        _api.RequestBodies[PutIndex].Should().Be("""{"rejectionReason":"Rejected by manager."}""");
        _api.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.EndsWith("/approve"));
    }

    [Fact]
    public void AFailedDecision_IsReported_AndTheRequestStaysOpen_UntilALaterOneSucceeds()
    {
        // The API refuses, e.g. the employee no longer has the balance. The manager has to see
        // that nothing happened; a silent failure reads as an approval that went through.
        _api.On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/approve", HttpStatusCode.Conflict,
                """{"detail":"Insufficient leave balance. Available: 1, Requested: 3."}""")
            .On(HttpMethod.Put, $"/api/leave-requests/{PendingId}/reject", HttpStatusCode.OK,
                Request(PendingId, "Maria Santos", "Rejected", "Family event"));
        var cut = RenderPage();

        ButtonIn(RowFor(cut, "Maria Santos"), "Approve").Click();

        // The API's own reason, not a status code the manager can do nothing with.
        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Trim()
            .Should().Be("Insufficient leave balance. Available: 1, Requested: 3."));
        ButtonsIn(RowFor(cut, "Maria Santos")).Should().Equal("Approve", "Reject");
        _api.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(1, "nothing changed, so there is nothing to reload");

        ButtonIn(RowFor(cut, "Maria Santos"), "Reject").Click();

        cut.WaitForAssertion(() => cut.FindAll("[role=alert]").Should().BeEmpty());
    }
}
