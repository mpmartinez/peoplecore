using System.Net;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

/// <summary>
/// The separations list, its record-separation form, and the detail page a record opens onto -
/// marking separated, cancelling, and the clearance checklist that gates final pay.
/// </summary>
public class SeparationsTests : BunitContext
{
    private static readonly Guid MariaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JuanId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeparationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ItemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ItemId2 = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public SeparationsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("hr@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
    }

    private static string Employee(Guid id, string number, string name) =>
        $$"""
        {"id":"{{id}}","employeeNumber":"{{number}}","firstName":"{{name.Split(' ')[0]}}","lastName":"{{name.Split(' ')[1]}}",
         "fullName":"{{name}}","workEmail":"{{name.Split(' ')[0].ToLowerInvariant()}}@company.test",
         "departmentName":null,"positionTitle":null,"employmentStatus":"Regular","isActive":true}
        """;

    private static string EmployeesPage(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":100,"totalPages":1}""";

    private static string Separation(
        Guid? id = null, string type = "Resignation", string? cause = null, string status = "NoticeGiven",
        string lastWorkingDay = "2026-04-15", string finalPayDueBy = "2026-04-20", bool finalPayOverdue = false,
        int clearedCount = 0, int clearanceCount = 0, string clearanceItems = "[]",
        string? separatedBy = null, string? separatedAt = null) =>
        $$"""
        {"id":"{{id ?? SeparationId}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-001","position":"Accountant",
         "type":"{{type}}","authorizedCause":{{(cause is null ? "null" : $"\"{cause}\"")}},"noticeDate":"2026-03-15","lastWorkingDay":"{{lastWorkingDay}}",
         "reason":"Moving abroad","status":"{{status}}","recordedBy":"hr@company.test","separatedBy":{{(separatedBy is null ? "null" : $"\"{separatedBy}\"")}},
         "separatedAt":{{(separatedAt is null ? "null" : $"\"{separatedAt}\"")}},"finalPayDueBy":"{{finalPayDueBy}}","finalPayOverdue":{{(finalPayOverdue ? "true" : "false")}},
         "clearedCount":{{clearedCount}},"clearanceCount":{{clearanceCount}},"clearanceItems":{{clearanceItems}}}
        """;

    private static string ClearanceItem(Guid id, string name, string? clearedBy = null, string? clearedAt = null, string? note = null) =>
        $$"""
        {"id":"{{id}}","name":"{{name}}","clearedBy":{{(clearedBy is null ? "null" : $"\"{clearedBy}\"")}},
         "clearedAt":{{(clearedAt is null ? "null" : $"\"{clearedAt}\"")}},"note":{{(note is null ? "null" : $"\"{note}\"")}}}
        """;

    /// <summary>Wraps one or more <see cref="ClearanceItem"/> objects as the JSON array Separation's clearanceItems expects.</summary>
    private static string Items(params string[] items) => $"[{string.Join(",", items)}]";

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    // ---------- List page ----------

    private void StubEmployees() =>
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100&isActive=true", HttpStatusCode.OK,
            EmployeesPage(Employee(MariaId, "EMP-001", "Maria Santos"), Employee(JuanId, "EMP-002", "Juan Cruz")));

    private IRenderedComponent<Separations> RenderList(params string[] separations)
    {
        StubEmployees();
        _api.On(HttpMethod.Get, "/api/separations", HttpStatusCode.OK, $$"""[{{string.Join(",", separations)}}]""");
        var cut = Render<Separations>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    [Fact]
    public void ListsSeparations_WithTypeCauseLastDayStatusClearanceAndFinalPay()
    {
        var cut = RenderList(Separation(type: "AuthorizedCause", cause: "Redundancy", clearedCount: 2, clearanceCount: 5));

        var row = cut.Find("tbody tr");
        row.TextContent.Should().Contain("Maria Santos");
        row.TextContent.Should().Contain("EMP-001");
        row.TextContent.Should().Contain("Authorized cause: Redundancy");
        row.TextContent.Should().Contain("Apr 15, 2026");
        row.TextContent.Should().Contain("Notice given");
        row.TextContent.Should().Contain("2 of 5");
        row.TextContent.Should().Contain("Apr 20, 2026");
    }

    [Fact]
    public void ACauseIsShownOnlyForAuthorizedCauseSeparations()
    {
        var cut = RenderList(Separation(type: "Resignation"));

        cut.Find("tbody tr").TextContent.Should().NotContain("Redundancy");
    }

    [Fact]
    public void AnOverdueFinalPay_ShowsTheOverdueBadge()
    {
        var cut = RenderList(Separation(finalPayOverdue: true));

        cut.Find("[data-overdue]").TextContent.Should().Contain("Overdue");
    }

    [Fact]
    public void ANonOverdueFinalPay_ShowsNoBadge()
    {
        var cut = RenderList(Separation(finalPayOverdue: false));

        cut.FindAll("[data-overdue]").Should().BeEmpty();
    }

    [Fact]
    public void NoSeparations_ShowsTheEmptyState()
    {
        var cut = RenderList();

        cut.Markup.Should().Contain("No separations recorded.");
    }

    [Fact]
    public void ClickingARow_OpensItsDetailPage()
    {
        var cut = RenderList(Separation());

        cut.Find("tbody tr").Click();

        CurrentUri.Should().EndWith($"/separations/{SeparationId}");
    }

    [Fact]
    public void RecordSeparation_OpensAFormWithAnActiveEmployeePicker()
    {
        var cut = RenderList();

        cut.Find("[data-record-separation]").Click();

        cut.WaitForElement("[data-record-form]");
        cut.FindAll("#separation-employee option").Select(o => o.TextContent).Should()
            .Contain(["Maria Santos (EMP-001)", "Juan Cruz (EMP-002)"]);
    }

    [Fact]
    public void TheCauseField_OnlyAppearsWhenTheTypeIsAuthorizedCause()
    {
        var cut = RenderList();
        cut.Find("[data-record-separation]").Click();
        cut.WaitForElement("[data-record-form]");

        cut.FindAll("#separation-cause").Should().BeEmpty();

        cut.Find("#separation-type").Change("AuthorizedCause");

        cut.Find("#separation-cause").Should().NotBeNull();
    }

    [Fact]
    public void TheTypeAndCauseDropdowns_ShowReadableLabels_ButKeepTheEnumNamesAsValues()
    {
        var cut = RenderList();
        cut.Find("[data-record-separation]").Click();
        cut.WaitForElement("[data-record-form]");

        var typeOptions = cut.FindAll("#separation-type option");
        typeOptions.Select(o => o.TextContent).Should().Contain("Termination for just cause");
        typeOptions.Single(o => o.TextContent == "Termination for just cause").GetAttribute("value").Should().Be("TerminationJustCause");

        cut.Find("#separation-type").Change("AuthorizedCause");

        var causeOptions = cut.FindAll("#separation-cause option");
        causeOptions.Select(o => o.TextContent).Should().Contain("Closure (not due to losses)");
        causeOptions.Single(o => o.TextContent == "Closure (not due to losses)").GetAttribute("value").Should().Be("ClosureNotDueToLosses");
    }

    [Fact]
    public void ChangingTheTypeAwayFromAuthorizedCause_ClearsTheStaleCause_SoItIsNotSentOnSubmit()
    {
        _api.On(HttpMethod.Post, "/api/separations", HttpStatusCode.Created, Separation());
        var cut = RenderList();
        cut.Find("[data-record-separation]").Click();
        cut.WaitForElement("[data-record-form]");
        cut.Find("#separation-employee").Change(MariaId.ToString());
        cut.Find("#separation-type").Change("AuthorizedCause");
        cut.Find("#separation-cause").Change("Redundancy");

        cut.Find("#separation-type").Change("Resignation");

        cut.Find("#separation-notice-date").Input("2026-03-15");
        cut.Find("#separation-last-day").Input("2026-04-15");
        cut.Find("[data-submit-separation]").Click();

        cut.WaitForAssertion(() => CurrentUri.Should().EndWith($"/separations/{SeparationId}"));
        var body = BodyOf(HttpMethod.Post, "/api/separations");
        body.TryGetProperty("authorizedCause", out var cause).Should().BeTrue();
        cause.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void SubmittingTheForm_PostsAndNavigatesToTheNewRecordsDetailPage()
    {
        var cut = RenderList();
        _api.On(HttpMethod.Post, "/api/separations", HttpStatusCode.Created, Separation());
        cut.Find("[data-record-separation]").Click();
        cut.WaitForElement("[data-record-form]");

        cut.Find("#separation-employee").Change(MariaId.ToString());
        cut.Find("#separation-type").Change("Resignation");
        cut.Find("#separation-notice-date").Input("2026-03-15");
        cut.Find("#separation-last-day").Input("2026-04-15");
        cut.Find("[data-submit-separation]").Click();

        cut.WaitForAssertion(() => CurrentUri.Should().EndWith($"/separations/{SeparationId}"));
        var body = BodyOf(HttpMethod.Post, "/api/separations");
        body.GetProperty("employeeId").GetGuid().Should().Be(MariaId);
        body.GetProperty("type").GetString().Should().Be("Resignation");
    }

    [Fact]
    public void AnApiErrorOnRecording_ShowsInTheForm_AndDoesNotNavigate()
    {
        var cut = RenderList();
        _api.On(HttpMethod.Post, "/api/separations", HttpStatusCode.BadRequest,
            """{"detail":"Maria Santos already has an open separation."}""");
        cut.Find("[data-record-separation]").Click();
        cut.WaitForElement("[data-record-form]");
        cut.Find("#separation-employee").Change(MariaId.ToString());
        cut.Find("#separation-type").Change("Resignation");

        cut.Find("[data-submit-separation]").Click();

        cut.WaitForElement("[data-form-error]").TextContent.Should().Contain("already has an open separation");
        CurrentUri.Should().NotContain("/separations/");
    }

    [Fact]
    public void AnEmployeeQueryParameter_OpensTheFormWithThatEmployeeChosen()
    {
        StubEmployees();
        _api.On(HttpMethod.Get, "/api/separations", HttpStatusCode.OK, "[]");
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/separations?employee={JuanId}");

        var cut = Render<Separations>();

        cut.WaitForElement("[data-record-form]");
        cut.Find("#separation-employee").GetAttribute("value").Should().Be(JuanId.ToString());
    }

    // ---------- Detail page ----------

    private IRenderedComponent<SeparationDetail> RenderDetail(string separationJson)
    {
        _api.On(HttpMethod.Get, $"/api/separations/{SeparationId}", HttpStatusCode.OK, separationJson);
        var cut = Render<SeparationDetail>(ps => ps.Add(p => p.Id, SeparationId));
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    [Fact]
    public void ShowsTheRecordsFields()
    {
        var cut = RenderDetail(Separation(type: "AuthorizedCause", cause: "Redundancy"));

        cut.Markup.Should().Contain("Maria Santos");
        cut.Markup.Should().Contain("EMP-001");
        cut.Markup.Should().Contain("Accountant");
        cut.Markup.Should().Contain("Redundancy");
        cut.Markup.Should().Contain("Moving abroad");
        cut.Markup.Should().Contain("hr@company.test");
        cut.Markup.Should().Contain("Mar 15, 2026");
        cut.Markup.Should().Contain("Apr 15, 2026");
    }

    [Fact]
    public void TheStatusBadge_ShowsAReadableLabel_NotTheRawEnumName()
    {
        var cut = RenderDetail(Separation(status: "NoticeGiven"));

        cut.Markup.Should().Contain("Notice given");
        cut.Markup.Should().NotContain("NoticeGiven");
    }

    [Fact]
    public void TheFinalPayNote_NamesTheDueDate()
    {
        var cut = RenderDetail(Separation(finalPayDueBy: "2026-04-20"));

        cut.Find("[data-final-pay-note]").TextContent.Should().Contain("Final pay is due by Apr 20, 2026");
    }

    [Fact]
    public void NoticeGiven_OffersMarkSeparatedAndCancel()
    {
        var cut = RenderDetail(Separation(status: "NoticeGiven"));

        cut.FindAll("[data-mark-separated]").Should().ContainSingle();
        cut.FindAll("[data-cancel]").Should().ContainSingle();
    }

    [Fact]
    public void Separated_OffersNeitherMarkSeparatedNorCancel()
    {
        var cut = RenderDetail(Separation(status: "Separated", separatedBy: "hr@company.test", separatedAt: "2026-04-15T09:00:00Z"));

        cut.FindAll("[data-mark-separated]").Should().BeEmpty();
        cut.FindAll("[data-cancel]").Should().BeEmpty();
    }

    [Fact]
    public void MarkingSeparated_AsksForConfirmation_BeforeCallingTheApi()
    {
        var cut = RenderDetail(Separation(status: "NoticeGiven", lastWorkingDay: "2026-03-03"));
        _api.On(HttpMethod.Post, $"/api/separations/{SeparationId}/mark-separated", HttpStatusCode.OK,
            Separation(status: "Separated", separatedBy: "hr@company.test", separatedAt: "2026-04-15T09:00:00Z"));

        cut.Find("[data-mark-separated]").Click();

        var dialog = cut.WaitForElement("[data-confirm-mark-separated]");
        dialog.TextContent.Should().Contain("Mark Maria Santos separated as of Mar 3, 2026?")
            .And.Contain("They'll be deactivated, and this can't be undone.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/mark-separated"));

        dialog.QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-mark-separated]").Should().BeEmpty());
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/mark-separated"));
    }

    [Fact]
    public void MarkingSeparated_CancellingTheConfirmation_LeavesItUnseparated()
    {
        var cut = RenderDetail(Separation(status: "NoticeGiven"));

        cut.Find("[data-mark-separated]").Click();
        cut.WaitForElement("[data-confirm-mark-separated]").QuerySelectorAll("button").First().Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-confirm-mark-separated]").Should().BeEmpty());
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/mark-separated"));
        cut.FindAll("[data-mark-separated]").Should().ContainSingle();
    }

    [Fact]
    public void MarkingSeparatedTooEarly_ShowsTheApisReasonOnThePage()
    {
        var cut = RenderDetail(Separation(status: "NoticeGiven"));
        _api.On(HttpMethod.Post, $"/api/separations/{SeparationId}/mark-separated", HttpStatusCode.BadRequest,
            """{"detail":"You can mark them separated on or after Apr 15, 2026."}""");

        cut.Find("[data-mark-separated]").Click();
        cut.Find("[data-confirm-mark-separated]").QuerySelectorAll("button").Last().Click();

        cut.WaitForElement("[data-page-error]").TextContent.Should().Contain("on or after Apr 15, 2026");
    }

    [Fact]
    public void CancellingAsksForConfirmation_ThenNavigatesBackToTheList()
    {
        var cut = RenderDetail(Separation(status: "NoticeGiven"));
        _api.On(HttpMethod.Post, $"/api/separations/{SeparationId}/cancel", HttpStatusCode.NoContent);

        cut.Find("[data-cancel]").Click();
        cut.WaitForElement("[data-confirm-cancel]");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/cancel"));

        cut.Find("[data-confirm-cancel]").QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => CurrentUri.Should().EndWith("/separations"));
    }

    [Fact]
    public void TheClearanceChecklist_ShowsEachItemsState()
    {
        var items = Items(
            ClearanceItem(ItemId, "Return laptop"),
            ClearanceItem(ItemId2, "Turn in ID", clearedBy: "hr@company.test", clearedAt: "2026-04-10T08:00:00Z", note: "Returned in person"));
        var cut = RenderDetail(Separation(clearedCount: 1, clearanceCount: 2, clearanceItems: items));

        var clearance = cut.Find("[data-clearance]");
        clearance.TextContent.Should().Contain("Return laptop");
        clearance.TextContent.Should().Contain("Turn in ID");
        clearance.TextContent.Should().Contain("Returned in person");
        cut.Find($"[data-clear='{ItemId}']").Should().NotBeNull();
        cut.Find($"[data-remove='{ItemId}']").Should().NotBeNull();
        cut.Find($"[data-undo='{ItemId2}']").Should().NotBeNull();
        cut.FindAll($"[data-clear='{ItemId2}']").Should().BeEmpty();
        cut.FindAll($"[data-remove='{ItemId2}']").Should().BeEmpty();
    }

    [Fact]
    public void AllItemsCleared_ShowsClearanceComplete()
    {
        var items = Items(ClearanceItem(ItemId, "Return laptop", clearedBy: "hr@company.test", clearedAt: "2026-04-10T08:00:00Z"));
        var cut = RenderDetail(Separation(clearedCount: 1, clearanceCount: 1, clearanceItems: items));

        cut.Find("[data-clearance-complete]").Should().NotBeNull();
    }

    [Fact]
    public void NotEveryItemCleared_ShowsNoClearanceComplete()
    {
        var items = Items(ClearanceItem(ItemId, "Return laptop"));
        var cut = RenderDetail(Separation(clearedCount: 0, clearanceCount: 1, clearanceItems: items));

        cut.FindAll("[data-clearance-complete]").Should().BeEmpty();
    }

    [Fact]
    public void AddingAClearanceItem_SendsItsName_AndRefreshesFromTheResponse()
    {
        var cut = RenderDetail(Separation(clearedCount: 0, clearanceCount: 0, clearanceItems: "[]"));
        _api.On(HttpMethod.Post, $"/api/separations/{SeparationId}/clearance", HttpStatusCode.OK,
            Separation(clearedCount: 0, clearanceCount: 1, clearanceItems: Items(ClearanceItem(ItemId, "Return laptop"))));

        cut.Find("[data-add-item]").Input("Return laptop");
        cut.Find("[data-add-item-button]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-clearance]").TextContent.Should().Contain("Return laptop"));
        BodyOf(HttpMethod.Post, $"/api/separations/{SeparationId}/clearance").GetProperty("name").GetString().Should().Be("Return laptop");
    }

    [Fact]
    public void ClearingAnItem_SendsTheOptionalNote()
    {
        var cut = RenderDetail(Separation(clearedCount: 0, clearanceCount: 1, clearanceItems: Items(ClearanceItem(ItemId, "Return laptop"))));
        _api.On(HttpMethod.Post, $"/api/separations/{SeparationId}/clearance/{ItemId}/clear", HttpStatusCode.OK,
            Separation(clearedCount: 1, clearanceCount: 1,
                clearanceItems: Items(ClearanceItem(ItemId, "Return laptop", clearedBy: "hr@company.test", clearedAt: "2026-04-10T08:00:00Z", note: "OK"))));

        cut.Find($"[data-clear-note='{ItemId}']").Input("OK");
        cut.Find($"[data-clear='{ItemId}']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-clearance]").TextContent.Should().Contain("OK"));
        BodyOf(HttpMethod.Post, $"/api/separations/{SeparationId}/clearance/{ItemId}/clear").GetProperty("note").GetString().Should().Be("OK");
    }

    [Fact]
    public void UndoingAClearedItem_RestoresItToUncleared()
    {
        var cut = RenderDetail(Separation(clearedCount: 1, clearanceCount: 1,
            clearanceItems: Items(ClearanceItem(ItemId, "Return laptop", clearedBy: "hr@company.test", clearedAt: "2026-04-10T08:00:00Z"))));
        _api.On(HttpMethod.Post, $"/api/separations/{SeparationId}/clearance/{ItemId}/undo", HttpStatusCode.OK,
            Separation(clearedCount: 0, clearanceCount: 1, clearanceItems: Items(ClearanceItem(ItemId, "Return laptop"))));

        cut.Find($"[data-undo='{ItemId}']").Click();

        cut.WaitForAssertion(() => cut.FindAll($"[data-clear='{ItemId}']").Should().ContainSingle());
    }

    [Fact]
    public void RemovingAnUnclearedItem_TakesItOffTheList()
    {
        var cut = RenderDetail(Separation(clearedCount: 0, clearanceCount: 1, clearanceItems: Items(ClearanceItem(ItemId, "Return laptop"))));
        _api.On(HttpMethod.Delete, $"/api/separations/{SeparationId}/clearance/{ItemId}", HttpStatusCode.OK,
            Separation(clearedCount: 0, clearanceCount: 0, clearanceItems: "[]"));

        cut.Find($"[data-remove='{ItemId}']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-clearance]").TextContent.Should().NotContain("Return laptop"));
    }

    [Fact]
    public void ANotFoundSeparation_SaysSo()
    {
        _api.On(HttpMethod.Get, $"/api/separations/{SeparationId}", HttpStatusCode.NotFound);

        var cut = Render<SeparationDetail>(ps => ps.Add(p => p.Id, SeparationId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Separation not found."));
    }
}
