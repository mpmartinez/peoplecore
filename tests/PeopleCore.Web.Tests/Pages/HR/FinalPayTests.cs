using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

/// <summary>
/// The Final pay section of a separation's page: creating the final-pay run, reading its summary,
/// changing HR's inputs while it can still change, and what still stands between it and payment.
/// </summary>
public class FinalPayTests : BunitContext
{
    private static readonly Guid MariaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeparationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ItemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RunId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public FinalPayTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("hr@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
    }

    private static string SeparationPath => $"/api/separations/{SeparationId}";
    private static string FinalPayPath => $"/api/separations/{SeparationId}/final-pay";

    private static string Separation(string type = "Resignation", string clearanceItems = "[]", string status = "Separated") =>
        $$"""
        {"id":"{{SeparationId}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-001","position":"Accountant",
         "type":"{{type}}","authorizedCause":null,"noticeDate":"2026-03-15","lastWorkingDay":"2026-04-15",
         "reason":null,"status":"{{status}}","recordedBy":"hr@company.test","separatedBy":"hr@company.test",
         "separatedAt":"2026-04-15T09:00:00Z","finalPayDueBy":"2026-05-15","finalPayOverdue":false,
         "clearedCount":0,"clearanceCount":0,"clearanceItems":{{clearanceItems}},
         "finalPayRunId":null,"finalPayRunNumber":null,"finalPayStatus":null}
        """;

    private static string UnclearedItem(string name = "Return laptop") =>
        $$"""[{"id":"{{ItemId}}","name":"{{name}}","clearedBy":null,"clearedAt":null,"note":null,"lastUndoneBy":null,"lastUndoneAt":null}]""";

    private static string ClearedItem(string name = "Return laptop") =>
        $$"""[{"id":"{{ItemId}}","name":"{{name}}","clearedBy":"hr@company.test","clearedAt":"2026-04-16T08:00:00Z","note":null,"lastUndoneBy":null,"lastUndoneAt":null}]""";

    private static string Summary(
        string status = "Draft", decimal workingDays = 10m, bool noSalaryDays = false,
        string leaveLines = """[{"leaveType":"Vacation Leave","days":5,"countsAsVacation":true}]""",
        decimal separationPay = 0m, decimal retirementPay = 0m, string computed = "null", string? overrideNote = null,
        string deductions = "[]", string loans = "[]", decimal tax = 1250.50m,
        bool clearanceComplete = true, string outstanding = "[]", string periodStart = "2026-04-01",
        string separationPayOverride = "null", string retirementPayOverride = "null", bool periodStartIsDefault = false) =>
        $$"""
        {"runId":"{{RunId}}","runNumber":"FP-2026-001","status":"{{status}}","periodStart":"{{periodStart}}","periodEnd":"2026-04-15",
         "payDate":"2026-04-30","workingDays":{{workingDays}},"noSalaryDays":{{(noSalaryDays ? "true" : "false")}},
         "periodStartIsDefault":{{(periodStartIsDefault ? "true" : "false")}},
         "leaveConversionPay":7500,"leaveConversionNonTaxable":7500,"leaveLines":{{leaveLines}},
         "separationPay":{{separationPay}},"retirementPay":{{retirementPay}},"computedSeparationOrRetirementPay":{{computed}},
         "separationPayOverride":{{separationPayOverride}},"retirementPayOverride":{{retirementPayOverride}},
         "overrideNote":{{(overrideNote is null ? "null" : $"\"{overrideNote}\"")}},"serviceYears":6,
         "deductions":{{deductions}},"loans":{{loans}},"withholdingTax":{{tax}},"grossPay":32500,"netPay":28000,
         "clearanceComplete":{{(clearanceComplete ? "true" : "false")}},"outstandingClearance":{{outstanding}}}
        """;

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private IRenderedComponent<SeparationDetail> RenderPage(string separationJson)
    {
        _api.On(HttpMethod.Get, SeparationPath, HttpStatusCode.OK, separationJson);
        var cut = Render<SeparationDetail>(ps => ps.Add(p => p.Id, SeparationId));
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private IRenderedComponent<SeparationDetail> RenderWithRun(string summary, string? separationJson = null)
    {
        _api.On(HttpMethod.Get, FinalPayPath, HttpStatusCode.OK, summary);
        var cut = RenderPage(separationJson ?? Separation());
        cut.WaitForElement("[data-final-pay-summary]");
        return cut;
    }

    private IRenderedComponent<SeparationDetail> RenderWithoutRun(string? separationJson = null)
    {
        _api.On(HttpMethod.Get, FinalPayPath, HttpStatusCode.NotFound);
        var cut = RenderPage(separationJson ?? Separation());
        cut.WaitForElement("[data-final-pay-form]");
        return cut;
    }

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindLastIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    private static string Text(IRenderedComponent<SeparationDetail> cut, string selector) => cut.Find(selector).TextContent;

    // ---------- Who sees it ----------

    [Fact]
    public void WithoutPayrollManage_ThereIsNoFinalPaySection_AndNothingIsAskedOfTheApi()
    {
        _auth.SetClaims(new System.Security.Claims.Claim(Permissions.ClaimType, Permissions.EmployeesManage));

        var cut = RenderPage(Separation());

        cut.FindAll("[data-final-pay]").Should().BeEmpty();
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == FinalPayPath);
    }

    // ---------- Creating ----------

    [Fact]
    public void WithoutARun_OffersTheCreateForm_WithTheBoxesItNeeds_AndThePayDateDefaultingToToday()
    {
        var cut = RenderWithoutRun();

        var form = cut.Find("[data-final-pay-form]");
        form.TextContent.Should().Contain("Create final pay");
        cut.Find("#final-pay-date").GetAttribute("value").Should().Be(DateTime.Today.ToString("yyyy-MM-dd"));
        cut.Find("#final-pay-period-start").GetAttribute("value").Should().BeEmpty();
        cut.Find("#final-pay-override").Should().NotBeNull();
        cut.Find("#final-pay-override-note").Should().NotBeNull();
        cut.FindAll("[data-final-pay-summary]").Should().BeEmpty();
    }

    [Fact]
    public void CreatingIt_PostsHrsInputs_ThenShowsTheSummary()
    {
        _api.On(HttpMethod.Post, FinalPayPath, HttpStatusCode.Created, Summary(separationPay: 20000m, overrideNote: "Goodwill"));
        var cut = RenderWithoutRun();

        cut.Find("#final-pay-date").Input("2026-04-30");
        cut.Find("#final-pay-period-start").Input("2026-04-01");
        cut.Find("#final-pay-override").Input("20000");
        cut.Find("#final-pay-override-note").Input("Goodwill");
        cut.Find("[data-add-deduction]").Click();
        cut.Find("[data-deduction-label='0']").Input("Unreturned phone");
        cut.Find("[data-deduction-amount='0']").Input("3500");
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-summary]");
        cut.FindAll("[data-final-pay-form]").Should().BeEmpty();
        var body = BodyOf(HttpMethod.Post, FinalPayPath);
        body.GetProperty("payDate").GetString().Should().Be("2026-04-30");
        body.GetProperty("periodStart").GetString().Should().Be("2026-04-01");
        body.GetProperty("separationPayOverride").GetDecimal().Should().Be(20000m);
        body.GetProperty("retirementPayOverride").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("overrideNote").GetString().Should().Be("Goodwill");
        var deduction = body.GetProperty("deductions").EnumerateArray().Should().ContainSingle().Subject;
        deduction.GetProperty("label").GetString().Should().Be("Unreturned phone");
        deduction.GetProperty("amount").GetDecimal().Should().Be(3500m);
    }

    [Fact]
    public void CreatingIt_TakesAwayCancelSeparation_WhichTheApiWouldNowRefuse()
    {
        _api.On(HttpMethod.Post, FinalPayPath, HttpStatusCode.Created, Summary());
        var cut = RenderWithoutRun(Separation(status: "NoticeGiven"));
        cut.FindAll("[data-cancel]").Should().ContainSingle();

        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-summary]");
        cut.FindAll("[data-cancel]").Should().BeEmpty();
    }

    [Fact]
    public void LeavingTheOptionalBoxesEmpty_SendsNulls_AndNoDeductions()
    {
        _api.On(HttpMethod.Post, FinalPayPath, HttpStatusCode.Created, Summary());
        var cut = RenderWithoutRun();

        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-summary]");
        var body = BodyOf(HttpMethod.Post, FinalPayPath);
        body.GetProperty("periodStart").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("separationPayOverride").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("retirementPayOverride").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("overrideNote").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("deductions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ForARetirement_TheOverrideIsTheRetirementPays()
    {
        _api.On(HttpMethod.Post, FinalPayPath, HttpStatusCode.Created, Summary(retirementPay: 90000m, overrideNote: "Company plan"));
        var cut = RenderWithoutRun(Separation(type: "Retirement"));

        cut.Find("[data-final-pay-form]").TextContent.Should().Contain("Retirement pay override");
        cut.Find("#final-pay-override").Input("90000");
        cut.Find("#final-pay-override-note").Input("Company plan");
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-summary]");
        var body = BodyOf(HttpMethod.Post, FinalPayPath);
        body.GetProperty("retirementPayOverride").GetDecimal().Should().Be(90000m);
        body.GetProperty("separationPayOverride").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ADeductionRow_CanBeRemoved_BeforeSubmitting()
    {
        _api.On(HttpMethod.Post, FinalPayPath, HttpStatusCode.Created, Summary());
        var cut = RenderWithoutRun();

        cut.Find("[data-add-deduction]").Click();
        cut.Find("[data-add-deduction]").Click();
        cut.Find("[data-deduction-label='0']").Input("Unreturned phone");
        cut.Find("[data-deduction-amount='0']").Input("3500");
        cut.Find("[data-deduction-label='1']").Input("Cash advance");
        cut.Find("[data-deduction-amount='1']").Input("1000");

        cut.Find("[data-remove-deduction='0']").Click();

        cut.FindAll("[data-deduction-label]").Should().ContainSingle();
        cut.Find("[data-deduction-label='0']").GetAttribute("value").Should().Be("Cash advance");
        cut.Find("[data-submit-final-pay]").Click();
        cut.WaitForElement("[data-final-pay-summary]");
        var deduction = BodyOf(HttpMethod.Post, FinalPayPath).GetProperty("deductions").EnumerateArray().Should().ContainSingle().Subject;
        deduction.GetProperty("label").GetString().Should().Be("Cash advance");
    }

    [Fact]
    public void WhileCreating_TheSubmitButtonIsDisabled()
    {
        var gate = _api.OnGated(HttpMethod.Post, FinalPayPath);
        var cut = RenderWithoutRun();

        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-submit-final-pay]").HasAttribute("disabled").Should().BeTrue());
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);

        gate.SetResult(Json(Summary(), HttpStatusCode.Created));
        cut.WaitForElement("[data-final-pay-summary]");
    }

    [Fact]
    public void ARefusedCreate_ShowsTheApisReason_AndKeepsTheForm()
    {
        _api.On(HttpMethod.Post, FinalPayPath, HttpStatusCode.BadRequest,
            """{"title":"Bad request","detail":"Maria Santos has no compensation record.","status":400}""");
        var cut = RenderWithoutRun();

        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-error]").TextContent.Should().Contain("Maria Santos has no compensation record.");
        cut.FindAll("[data-final-pay-form]").Should().ContainSingle();
        cut.Find("[data-submit-final-pay]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AFailedLoad_ShowsTheError_RatherThanACreateForm()
    {
        _api.On(HttpMethod.Get, FinalPayPath, HttpStatusCode.InternalServerError);

        var cut = RenderPage(Separation());

        cut.WaitForElement("[data-final-pay-error]").TextContent.Should().Contain("500");
        cut.FindAll("[data-final-pay-form]").Should().BeEmpty();
    }

    // ---------- The summary ----------

    [Fact]
    public void TheSummary_ShowsTheRunPeriodDaysAndMoney_AndLinksToTheRun()
    {
        var cut = RenderWithRun(Summary(status: "ForApproval",
            deductions: """[{"label":"Unreturned phone","amount":3500}]"""));

        var summary = Text(cut, "[data-final-pay-summary]");
        summary.Should().Contain("FP-2026-001").And.Contain("For approval").And.NotContain("ForApproval");
        summary.Should().Contain("Apr 1, 2026").And.Contain("Apr 15, 2026").And.Contain("Apr 30, 2026");
        Text(cut, "[data-salary-days]").Should().Contain("Salary days").And.Contain("10");
        Text(cut, "[data-leave-lines]").Should().Contain("Vacation Leave").And.Contain("5");
        summary.Should().Contain("7,500.00");
        Text(cut, "[data-final-pay-deductions]").Should().Contain("Unreturned phone").And.Contain("3,500.00");
        Text(cut, "[data-tax]").Should().Contain("Withholding tax").And.Contain("1,250.50");
        Text(cut, "[data-gross]").Should().Contain("32,500.00");
        Text(cut, "[data-net]").Should().Contain("28,000.00");
        cut.Find("[data-final-pay-run-link]").GetAttribute("href").Should().Be($"/payroll-runs/{RunId}");
        cut.FindAll("[data-final-pay-form]").Should().BeEmpty();
    }

    [Fact]
    public void AnOverriddenSeparationPay_ShowsTheComputedFigure_AndTheNote()
    {
        var cut = RenderWithRun(Summary(separationPay: 50000m, computed: "42000", overrideNote: "Per CBA", separationPayOverride: "50000"),
            Separation(type: "AuthorizedCause"));

        Text(cut, "[data-separation-pay]").Should().Contain("50,000.00");
        Text(cut, "[data-overridden]").Should().Contain("(overridden)");
        Text(cut, "[data-computed-pay]").Should().Contain("42,000.00");
        Text(cut, "[data-override-note]").Should().Contain("Per CBA");
    }

    [Fact]
    public void AnOverrideWithNoComputedFigure_IsStillMarkedAsOverridden()
    {
        // A goodwill payment on a resignation: there is no statutory separation pay to compare.
        var cut = RenderWithRun(Summary(separationPay: 20000m, overrideNote: "Goodwill", separationPayOverride: "20000"));

        cut.Find("[data-separation-pay] [data-overridden]").TextContent.Should().Contain("(overridden)");
        cut.FindAll("[data-computed-pay]").Should().BeEmpty();
        Text(cut, "[data-override-note]").Should().Contain("Goodwill");
    }

    [Fact]
    public void ANoteWithoutAStoredOverride_IsNotShown()
    {
        var cut = RenderWithRun(Summary(separationPay: 0m, overrideNote: "Left over from an earlier edit"));

        cut.FindAll("[data-override-note]").Should().BeEmpty();
        cut.FindAll("[data-overridden]").Should().BeEmpty();
    }

    [Fact]
    public void AnOverrideWithoutANote_IsCaughtBeforeCallingTheApi()
    {
        var cut = RenderWithoutRun();

        cut.Find("#final-pay-override").Input("1000");
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-error]").TextContent.Should()
            .Contain("Explain the separation or retirement pay override.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
        cut.FindAll("[data-final-pay-form]").Should().ContainSingle();
    }

    [Fact]
    public void ANotOverriddenSeparationPay_ShowsNoSeparateComputedFigure()
    {
        var cut = RenderWithRun(Summary(separationPay: 42000m, computed: "42000"), Separation(type: "AuthorizedCause"));

        Text(cut, "[data-separation-pay]").Should().Contain("42,000.00");
        cut.FindAll("[data-computed-pay]").Should().BeEmpty();
    }

    [Fact]
    public void ARetirement_ShowsRetirementPay()
    {
        var cut = RenderWithRun(Summary(retirementPay: 95000m, computed: "95000"), Separation(type: "Retirement"));

        Text(cut, "[data-retirement-pay]").Should().Contain("Retirement pay").And.Contain("95,000.00");
    }

    [Fact]
    public void ALoanTheFinalPayCantCover_IsWarnedAbout_WithAReadableLoanType()
    {
        var cut = RenderWithRun(Summary(loans: """[{"loanType":"SSSLoan","balance":30000,"deducted":28000,"uncovered":2000}]"""));

        Text(cut, "[data-loans]").Should().Contain("SSS loan").And.NotContain("SSSLoan");
        Text(cut, "[data-loan-shortfall]").Should().Contain("2,000.00").And.Contain("SSS loan");
    }

    [Fact]
    public void AFullyCoveredLoan_HasNoShortfallWarning()
    {
        var cut = RenderWithRun(Summary(loans: """[{"loanType":"CompanyLoan","balance":5000,"deducted":5000,"uncovered":0}]"""));

        Text(cut, "[data-loans]").Should().Contain("Company loan");
        cut.FindAll("[data-loan-shortfall]").Should().BeEmpty();
    }

    [Fact]
    public void ANegativeTax_IsLabelledATaxRefund()
    {
        var cut = RenderWithRun(Summary(tax: -1800m));

        var tax = Text(cut, "[data-tax]");
        tax.Should().Contain("Tax refund").And.Contain("1,800.00").And.NotContain("-1,800.00");
    }

    [Fact]
    public void NoConvertibleLeave_SaysNoLeaveTypeIsMarkedConvertible()
    {
        var cut = RenderWithRun(Summary(leaveLines: "[]"));

        Text(cut, "[data-no-convertible-leave]").Should()
            .Contain("No leave converts to cash - no leave type is marked convertible.");
    }

    [Fact]
    public void NoSalaryDays_SaysTheLastPayrollAlreadyPaidUpToTheLastDay_InsteadOfTheDays()
    {
        var cut = RenderWithRun(Summary(workingDays: 0m, noSalaryDays: true, periodStart: "2026-04-15", periodStartIsDefault: true));

        Text(cut, "[data-no-salary-days]").Should()
            .Contain("No salary days - the last payroll already paid up to the last working day.");
        cut.FindAll("[data-salary-days]").Should().BeEmpty();
    }

    // ---------- Clearance ----------

    [Fact]
    public void OutstandingClearance_IsNamed()
    {
        var cut = RenderWithRun(Summary(clearanceComplete: false, outstanding: """["Return laptop","Turn in ID"]"""),
            Separation(clearanceItems: UnclearedItem()));

        Text(cut, "[data-clearance-outstanding]").Should().Contain("Clearance outstanding: Return laptop, Turn in ID");
    }

    [Fact]
    public void ClearanceWithNoItems_AsksForItemsToBeAddedAndCleared()
    {
        var cut = RenderWithRun(Summary(clearanceComplete: false, outstanding: "[]"));

        Text(cut, "[data-clearance-outstanding]").Should()
            .Contain("Add clearance items and clear them before paying final pay.");
    }

    [Fact]
    public void CompleteClearance_ShowsNothingOutstanding()
    {
        var cut = RenderWithRun(Summary(clearanceComplete: true));

        cut.FindAll("[data-clearance-outstanding]").Should().BeEmpty();
    }

    [Fact]
    public void ClearingAnItem_RefreshesWhatTheFinalPaySaysIsOutstanding()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, FinalPayPath, () => ++loads == 1
            ? Json(Summary(clearanceComplete: false, outstanding: """["Return laptop"]"""))
            : Json(Summary(clearanceComplete: true)));
        _api.On(HttpMethod.Post, $"{SeparationPath}/clearance/{ItemId}/clear", HttpStatusCode.OK,
            Separation(clearanceItems: ClearedItem()));
        var cut = RenderPage(Separation(clearanceItems: UnclearedItem()));
        cut.WaitForElement("[data-clearance-outstanding]");

        cut.Find($"[data-clear='{ItemId}']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-clearance-outstanding]").Should().BeEmpty());
    }

    [Fact]
    public void AnEarlierSlowerLoad_DoesNotOverwriteANewerOne()
    {
        var first = new TaskCompletionSource<HttpResponseMessage>();
        var loads = 0;
        _api.OnAsync(HttpMethod.Get, FinalPayPath, () => ++loads == 1
            ? first.Task
            : Task.FromResult(Json(Summary(clearanceComplete: true))));
        _api.On(HttpMethod.Post, $"{SeparationPath}/clearance/{ItemId}/clear", HttpStatusCode.OK,
            Separation(clearanceItems: ClearedItem()));
        _api.On(HttpMethod.Get, SeparationPath, HttpStatusCode.OK, Separation(clearanceItems: UnclearedItem()));
        var cut = Render<SeparationDetail>(ps => ps.Add(p => p.Id, SeparationId));

        // The first load is still out when clearing the item starts a second, which lands first.
        cut.WaitForElement($"[data-clear='{ItemId}']").Click();
        cut.WaitForElement("[data-final-pay-summary]");

        first.SetResult(Json(Summary(clearanceComplete: false, outstanding: """["Return laptop"]""")));

        cut.WaitForAssertion(() => loads.Should().Be(2));
        cut.FindAll("[data-clearance-outstanding]").Should().BeEmpty("the older answer arrived last but is out of date");
    }

    [Fact]
    public void ASlowSave_DoesNotOverwriteAReloadThatBeganWhileItWasOut_ItReloadsInstead()
    {
        // An edit is saved; while its PUT is out, clearing an item reloads the final pay. The
        // PUT's answer was computed before that clearance, so it must not be the last word.
        var loads = 0;
        _api.On(HttpMethod.Get, FinalPayPath, () => ++loads == 1
            ? Json(Summary(clearanceComplete: false, outstanding: """["Return laptop"]"""))
            : Json(Summary(clearanceComplete: true)));
        var put = _api.OnGated(HttpMethod.Put, FinalPayPath);
        _api.On(HttpMethod.Post, $"{SeparationPath}/clearance/{ItemId}/clear", HttpStatusCode.OK,
            Separation(clearanceItems: ClearedItem()));
        var cut = RenderPage(Separation(clearanceItems: UnclearedItem()));
        cut.WaitForElement("[data-clearance-outstanding]");

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("[data-submit-final-pay]").Click();
        cut.Find($"[data-clear='{ItemId}']").Click();
        cut.WaitForAssertion(() => loads.Should().Be(2));

        put.SetResult(Json(Summary(clearanceComplete: false, outstanding: """["Return laptop"]""")));

        cut.WaitForAssertion(() => loads.Should().Be(3, "the save's answer is stale, so the page reloads"));
        cut.WaitForAssertion(() => cut.FindAll("[data-clearance-outstanding]").Should().BeEmpty());
        cut.FindAll("[data-final-pay-form]").Should().BeEmpty();
    }

    [Fact]
    public void ASaveWithNothingNewerStarted_ShowsItsOwnAnswer_WithoutReloading()
    {
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.OK, Summary(separationPay: 25000m));
        var cut = RenderWithRun(Summary());

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForAssertion(() => Text(cut, "[data-separation-pay]").Should().Contain("25,000.00"));
        _api.Requests.Count(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == FinalPayPath).Should().Be(1);
    }

    // ---------- Editing ----------

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("ForApproval", true)]
    [InlineData("Approved", false)]
    [InlineData("Paid", false)]
    public void EditIsOfferedOnlyWhileTheRunIsDraftOrForApproval(string status, bool offered)
    {
        var cut = RenderWithRun(Summary(status: status));

        cut.FindAll("[data-edit-final-pay]").Should().HaveCount(offered ? 1 : 0);
    }

    [Fact]
    public void Editing_StartsFromTheCurrentInputs_AndPutsTheChanges()
    {
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.OK, Summary(status: "Draft", separationPay: 25000m, overrideNote: "Goodwill"));
        var cut = RenderWithRun(Summary(status: "ForApproval", separationPay: 20000m, overrideNote: "Goodwill",
            deductions: """[{"label":"Unreturned phone","amount":3500}]""", separationPayOverride: "20000"));

        cut.Find("[data-edit-final-pay]").Click();

        cut.Find("#final-pay-date").GetAttribute("value").Should().Be("2026-04-30");
        cut.Find("#final-pay-period-start").GetAttribute("value").Should().Be("2026-04-01");
        cut.Find("#final-pay-override").GetAttribute("value").Should().Be("20000");
        cut.Find("#final-pay-override-note").GetAttribute("value").Should().Be("Goodwill");
        cut.Find("[data-deduction-label='0']").GetAttribute("value").Should().Be("Unreturned phone");

        cut.Find("#final-pay-override").Input("25000");
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-final-pay-form]").Should().BeEmpty());
        Text(cut, "[data-separation-pay]").Should().Contain("25,000.00");
        var body = BodyOf(HttpMethod.Put, FinalPayPath);
        body.GetProperty("separationPayOverride").GetDecimal().Should().Be(25000m);
        body.GetProperty("periodStart").GetString().Should().Be("2026-04-01");
        body.GetProperty("deductions").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("label").GetString().Should().Be("Unreturned phone");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == FinalPayPath);
    }

    [Fact]
    public void AnOverrideEqualToTheComputedFigure_StillOpensAsAnOverride()
    {
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.OK, Summary());
        var cut = RenderWithRun(Summary(separationPay: 42000m, computed: "42000", overrideNote: "Agreed",
            separationPayOverride: "42000"), Separation(type: "AuthorizedCause"));

        cut.Find("[data-computed-pay]").TextContent.Should().Contain("42,000.00");
        cut.Find("[data-edit-final-pay]").Click();

        cut.Find("#final-pay-override").GetAttribute("value").Should().Be("42000");
        cut.Find("[data-submit-final-pay]").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-final-pay-form]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, FinalPayPath).GetProperty("separationPayOverride").GetDecimal().Should().Be(42000m);
    }

    [Fact]
    public void WithoutAStoredOverride_TheEditFormLeavesTheOverrideEmpty_WhateverThePayIs()
    {
        var cut = RenderWithRun(Summary(retirementPay: 90000m, computed: "0", overrideNote: "Early retirement"),
            Separation(type: "Retirement"));

        cut.Find("[data-edit-final-pay]").Click();

        cut.Find("#final-pay-override").GetAttribute("value").Should().BeEmpty();
        cut.Find("#final-pay-override-note").GetAttribute("value").Should().Be("Early retirement");
    }

    [Fact]
    public void ARetirementOverride_OpensInTheOverrideBox()
    {
        var cut = RenderWithRun(Summary(retirementPay: 90000m, computed: "0", overrideNote: "Company plan",
            retirementPayOverride: "90000"), Separation(type: "Retirement"));

        cut.Find("[data-edit-final-pay]").Click();

        cut.Find("#final-pay-override").GetAttribute("value").Should().Be("90000");
    }

    [Fact]
    public void EditingAFinalPayOnTheDefaultStart_LeavesTheStartEmpty_AndSendsNone()
    {
        // Pinning the stored default would stop it following a later change to what's been paid.
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.OK, Summary(periodStartIsDefault: true));
        var cut = RenderWithRun(Summary(periodStartIsDefault: true));

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("#final-pay-period-start").GetAttribute("value").Should().BeEmpty();
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-final-pay-form]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, FinalPayPath).GetProperty("periodStart").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void EditingAFinalPayOnTheDefaultStart_SendsADateHrTypes()
    {
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.OK, Summary(periodStart: "2026-04-06"));
        var cut = RenderWithRun(Summary(periodStartIsDefault: true));

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("#final-pay-period-start").Input("2026-04-06");
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-final-pay-form]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, FinalPayPath).GetProperty("periodStart").GetString().Should().Be("2026-04-06");
    }

    [Fact]
    public void EditingANoSalaryFinalPay_SendsNoPeriodStart()
    {
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.OK, Summary(workingDays: 0m, noSalaryDays: true, periodStart: "2026-04-15", periodStartIsDefault: true));
        var cut = RenderWithRun(Summary(workingDays: 0m, noSalaryDays: true, periodStart: "2026-04-15", periodStartIsDefault: true));

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("#final-pay-period-start").GetAttribute("value").Should().BeEmpty();
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-final-pay-form]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, FinalPayPath).GetProperty("periodStart").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ARefusedEdit_ShowsTheApisReason_AndKeepsTheEditForm()
    {
        _api.On(HttpMethod.Put, FinalPayPath, HttpStatusCode.BadRequest,
            """{"title":"Bad request","detail":"Only draft or for-approval final pay can be changed.","status":400}""");
        var cut = RenderWithRun(Summary());

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("[data-submit-final-pay]").Click();

        cut.WaitForElement("[data-final-pay-error]").TextContent.Should().Contain("Only draft or for-approval final pay can be changed.");
        cut.FindAll("[data-final-pay-form]").Should().ContainSingle();
    }

    [Fact]
    public void CancellingAnEdit_GoesBackToTheSummary_WithoutCallingTheApi()
    {
        var cut = RenderWithRun(Summary());

        cut.Find("[data-edit-final-pay]").Click();
        cut.Find("[data-cancel-final-pay-edit]").Click();

        cut.FindAll("[data-final-pay-form]").Should().BeEmpty();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }
}
