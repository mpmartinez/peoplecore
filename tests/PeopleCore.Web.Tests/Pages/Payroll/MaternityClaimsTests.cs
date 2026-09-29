using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace PeopleCore.Web.Tests.Pages.Payroll;

/// <summary>
/// The maternity claims page: the list and what SSS still owes, opening a claim for an approved
/// maternity leave, setting the daily allowance from the suggestion, and recording SSS's
/// reimbursement or denial.
/// </summary>
public class MaternityClaimsTests : BunitContext
{
    private static readonly Guid MariaClaimId = Guid.Parse("c1a1c1a1-0000-0000-0000-000000000001");
    private static readonly Guid AnaClaimId = Guid.Parse("c1a1c1a1-0000-0000-0000-000000000002");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");
    private static readonly Guid AnaId = Guid.Parse("0c6f9a3e-8b2d-4f71-a5c4-3e9d1b7f2a60");
    private static readonly Guid MariaLeaveId = Guid.Parse("1ea0e000-0000-0000-0000-000000000001");
    private static readonly Guid AnaLeaveId = Guid.Parse("1ea0e000-0000-0000-0000-000000000002");

    private const string ListPath = "/api/maternity-claims";

    private readonly StubHttpHandler _api = new();

    public MaternityClaimsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
    }

    private static string Claim(Guid? id = null, string name = "Maria Santos", string status = "Draft",
        decimal? allowance = null, decimal benefit = 0m, string? runNumber = null, string? advancedAt = null,
        string? reimbursedOn = null, decimal? reimbursedAmount = null, string? note = null, string? carriedBy = null,
        Guid? employeeId = null) =>
        $$"""
        {"id":"{{id ?? MariaClaimId}}","leaveRequestId":"{{MariaLeaveId}}","employeeId":"{{employeeId ?? MariaId}}","employeeName":"{{name}}",
         "leaveStart":"2026-08-10","leaveEnd":"2026-11-22","days":105,"dailyAllowance":{{Num(allowance)}},"benefit":{{benefit}},
         "status":"{{status}}","advanceRunId":{{(runNumber is null ? "null" : $"\"{Guid.NewGuid()}\"")}},
         "advanceRunNumber":{{Str(runNumber)}},"advancedAt":{{Str(advancedAt)}},
         "reimbursedOn":{{Str(reimbursedOn)}},"reimbursedAmount":{{Num(reimbursedAmount)}},"note":{{Str(note)}},
         "carriedByRunNumber":{{Str(carriedBy)}}}
        """;

    private static string Num(decimal? value) => value is null ? "null" : value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string Str(string? value) => value is null ? "null" : $"\"{value}\"";

    private static string Summary(decimal outstanding, params string[] claims) =>
        $$"""{"claims":[{{string.Join(",", claims)}}],"outstanding":{{outstanding}}}""";

    private static string Eligible(Guid leaveId, Guid employeeId, string name, string start, string end, decimal days) =>
        $$"""{"leaveRequestId":"{{leaveId}}","employeeId":"{{employeeId}}","employeeName":"{{name}}","startDate":"{{start}}","endDate":"{{end}}","days":{{days}}}""";

    private static string Suggestion(decimal? allowance, int months, bool overridden = false) =>
        $$"""{"dailyAllowance":{{Num(allowance)}},"monthsFound":{{months}},"windowFrom":"2025-04-01","windowTo":"2026-03-31","ratesOverridden":{{(overridden ? "true" : "false")}}}""";

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(string detail) =>
        Json($$"""{"title":"Business rule violation","status":400,"detail":"{{detail}}"}""", HttpStatusCode.BadRequest);

    private IRenderedComponent<MaternityClaims> RenderPage(string? summary = null)
    {
        if (summary is not null) _api.On(HttpMethod.Get, ListPath, HttpStatusCode.OK, summary);
        var cut = Render<MaternityClaims>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement Row(IRenderedComponent<MaternityClaims> cut, Guid claimId) =>
        cut.Find($"[data-claim='{claimId}']");

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    // ---------- The page and the list ----------

    [Fact]
    public void ThePage_IsAtMaternityClaims_ForPayrollManagers()
    {
        typeof(MaternityClaims).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).Should().Equal("/maternity-claims");
        typeof(MaternityClaims).GetCustomAttribute<RequirePermissionAttribute>()!.Policy.Should().Be("permission:payroll.manage");
    }

    [Fact]
    public void Claims_AreListed_WithTheirLeaveBenefitReadableStatusAndRun_AndTheOutstandingTotal()
    {
        var cut = RenderPage(Summary(70000.35m,
            Claim(status: "Advanced", allowance: 666.67m, benefit: 70000.35m, runNumber: "PR-2026-0015", advancedAt: "2026-08-05"),
            Claim(AnaClaimId, "Ana Cruz", "Reimbursed", 500m, 52500m, "PR-2026-0009", "2026-05-05", "2026-07-01", 52000m, "SSS paid less")));

        cut.Find("[data-outstanding]").TextContent.Trim().Should().Be("₱70,000.35");
        var maria = Row(cut, MariaClaimId).TextContent;
        maria.Should().Contain("Maria Santos").And.Contain("Aug 10, 2026").And.Contain("Nov 22, 2026").And.Contain("105")
            .And.Contain("₱666.67").And.Contain("₱70,000.35").And.Contain("Advanced").And.Contain("PR-2026-0015").And.Contain("Aug 5, 2026");
        var ana = Row(cut, AnaClaimId).TextContent;
        ana.Should().Contain("Reimbursed").And.Contain("₱52,000.00").And.Contain("Jul 1, 2026").And.Contain("SSS paid less");
    }

    [Theory]
    [InlineData("Draft", "Draft")]
    [InlineData("Advanced", "Advanced")]
    [InlineData("Reimbursed", "Reimbursed")]
    [InlineData("Denied", "Denied")]
    public void EachStatus_ReadsAsAWord(string status, string label)
    {
        var cut = RenderPage(Summary(0m, Claim(status: status)));

        Row(cut, MariaClaimId).QuerySelector("[data-status]")!.TextContent.Trim().Should().Be(label);
    }

    [Fact]
    public void AClaimWithNoAllowance_SaysSo()
    {
        var cut = RenderPage(Summary(0m, Claim()));

        Row(cut, MariaClaimId).TextContent.Should().Contain("Not set");
    }

    [Theory]
    [InlineData("Draft", new[] { "set-allowance" })]
    [InlineData("Advanced", new[] { "reimburse", "deny" })]
    [InlineData("Reimbursed", new string[0])]
    [InlineData("Denied", new string[0])]
    public void OnlyTheActionsTheApiAcceptsForTheStatus_AreOffered(string status, string[] expected)
    {
        var cut = RenderPage(Summary(0m, Claim(status: status, allowance: 666.67m, benefit: 70000.35m)));

        var offered = new[] { "set-allowance", "reimburse", "deny" }
            .Where(a => Row(cut, MariaClaimId).QuerySelector($"[data-{a}]") is not null);
        offered.Should().Equal(expected);
    }

    [Fact]
    public void ADraftClaimAnUnpaidRunAdvances_NamesTheRun_InsteadOfOfferingTheAllowance()
    {
        // The API refuses a new allowance while a run advances the benefit; the way on is that run.
        var cut = RenderPage(Summary(0m,
            Claim(allowance: 666.67m, benefit: 70000.35m, carriedBy: "PR-2026-0015"),
            Claim(AnaClaimId, "Ana Cruz", allowance: 600m, benefit: 63000m, employeeId: AnaId)));

        var maria = Row(cut, MariaClaimId);
        maria.QuerySelector("[data-carried-by]")!.TextContent.Trim().Should().Be("Advancing on PR-2026-0015");
        maria.QuerySelector("[data-set-allowance]").Should().BeNull();
        Row(cut, AnaClaimId).QuerySelector("[data-set-allowance]").Should().NotBeNull();
        Row(cut, AnaClaimId).QuerySelector("[data-carried-by]").Should().BeNull();
    }

    [Fact]
    public void NoClaims_SaysSo()
    {
        var cut = RenderPage(Summary(0m));

        cut.Markup.Should().Contain("No maternity claims yet.");
        cut.Find("[data-outstanding]").TextContent.Trim().Should().Be("₱0.00");
    }

    [Fact]
    public void AFailedLoad_ShowsTheApisReason_AndARetryLoadsTheList()
    {
        var attempts = 0;
        _api.On(HttpMethod.Get, ListPath, () => ++attempts == 1
            ? Problem("The claims could not be read.")
            : Json(Summary(0m, Claim())));
        var cut = RenderPage();

        cut.Find("[role=alert]").TextContent.Should().Contain("The claims could not be read.");
        cut.FindAll("[data-outstanding]").Should().BeEmpty();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Retry").Click();

        cut.WaitForAssertion(() => Row(cut, MariaClaimId).TextContent.Should().Contain("Maria Santos"));
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    // ---------- Opening a claim ----------

    [Fact]
    public void ANewClaim_IsPickedFromTheApprovedMaternityLeaveWithoutOne_AndTheListReloads()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, ListPath, () => Json(++loads == 1 ? Summary(0m) : Summary(0m, Claim(AnaClaimId, "Ana Cruz"))))
            .On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK,
                $"[{Eligible(MariaLeaveId, MariaId, "Maria Santos", "2026-08-10", "2026-11-22", 105m)},{Eligible(AnaLeaveId, AnaId, "Ana Cruz", "2026-09-01", "2026-12-14", 105m)}]")
            .On(HttpMethod.Post, $"/api/maternity-claims/{AnaLeaveId}", HttpStatusCode.Created, Claim(AnaClaimId, "Ana Cruz"));
        var cut = RenderPage();

        cut.Find("[data-new-claim]").Click();
        cut.WaitForAssertion(() => cut.FindAll("#claim-leave option").Should().HaveCount(3));
        cut.Find("#claim-leave").TextContent.Should().Contain("Maria Santos").And.Contain("Aug 10, 2026").And.Contain("105 days")
            .And.Contain("Ana Cruz");
        cut.Find("#claim-leave").Change(AnaLeaveId.ToString());
        cut.Find("[data-submit-create]").Click();

        cut.WaitForAssertion(() => Row(cut, AnaClaimId).TextContent.Should().Contain("Ana Cruz"));
        cut.FindAll("[data-create-dialog]").Should().BeEmpty();
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)].Should().BeNull();
    }

    [Fact]
    public void ANewClaim_WithNoLeaveChosen_IsRefusedWithoutCallingTheApi()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK,
            $"[{Eligible(MariaLeaveId, MariaId, "Maria Santos", "2026-08-10", "2026-11-22", 105m)}]");
        var cut = RenderPage(Summary(0m));

        cut.Find("[data-new-claim]").Click();
        cut.WaitForAssertion(() => cut.FindAll("#claim-leave option").Should().HaveCount(2));
        cut.Find("[data-submit-create]").Click();

        cut.Find("[data-create-error]").TextContent.Should().Contain("Choose an approved maternity leave.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void NoLeaveWaitingForAClaim_SaysSo()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK, "[]");
        var cut = RenderPage(Summary(0m));

        cut.Find("[data-new-claim]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-create-dialog]").TextContent.Should()
            .Contain("No approved maternity leave is waiting for a claim."));
        cut.FindAll("[data-submit-create]").Should().ContainSingle().Which.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ARefusedClaim_ShowsTheApisReason_AndKeepsTheDialogOpen()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK,
                $"[{Eligible(MariaLeaveId, MariaId, "Maria Santos", "2026-08-10", "2026-11-22", 105m)}]")
            .On(HttpMethod.Post, $"/api/maternity-claims/{MariaLeaveId}", () => Problem("Maria Santos already has a maternity claim for this leave."));
        var cut = RenderPage(Summary(0m));

        cut.Find("[data-new-claim]").Click();
        cut.WaitForAssertion(() => cut.FindAll("#claim-leave option").Should().HaveCount(2));
        cut.Find("#claim-leave").Change(MariaLeaveId.ToString());
        cut.Find("[data-submit-create]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-create-error]").TextContent.Should()
            .Contain("Maria Santos already has a maternity claim for this leave."));
        cut.Find("[data-submit-create]").HasAttribute("disabled").Should().BeFalse("the user can pick another and retry");
    }

    [Fact]
    public void AClaimBeingOpened_CannotBeSentTwice()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK,
            $"[{Eligible(MariaLeaveId, MariaId, "Maria Santos", "2026-08-10", "2026-11-22", 105m)}]");
        var gate = _api.OnGated(HttpMethod.Post, $"/api/maternity-claims/{MariaLeaveId}");
        var cut = RenderPage(Summary(0m));

        cut.Find("[data-new-claim]").Click();
        cut.WaitForAssertion(() => cut.FindAll("#claim-leave option").Should().HaveCount(2));
        cut.Find("#claim-leave").Change(MariaLeaveId.ToString());
        cut.Find("[data-submit-create]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-submit-create]").HasAttribute("disabled").Should().BeTrue());
        gate.SetResult(Json(Claim()));
        cut.WaitForAssertion(() => cut.FindAll("[data-create-dialog]").Should().BeEmpty());
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }

    [Fact]
    public async Task AClaimOpenedAfterItsFormWasClosed_DoesNotCloseTheFormOnScreenNow()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK,
                $"[{Eligible(MariaLeaveId, MariaId, "Maria Santos", "2026-08-10", "2026-11-22", 105m)}]")
            .On(HttpMethod.Get, $"/api/maternity-claims/{AnaClaimId}/suggestion", HttpStatusCode.OK, Suggestion(500m, 4));
        var gate = _api.OnGated(HttpMethod.Post, $"/api/maternity-claims/{MariaLeaveId}");
        var cut = RenderPage(Summary(0m, Claim(AnaClaimId, "Ana Cruz", employeeId: AnaId)));

        cut.Find("[data-new-claim]").Click();
        cut.WaitForAssertion(() => cut.FindAll("#claim-leave option").Should().HaveCount(2));
        cut.Find("#claim-leave").Change(MariaLeaveId.ToString());
        cut.Find("[data-submit-create]").Click();
        cut.WaitForAssertion(() => CancelButton(cut).HasAttribute("disabled").Should().BeTrue("the claim is being opened"));
        CloseButton(cut).Click();
        Row(cut, AnaClaimId).QuerySelector("[data-set-allowance]")!.Click();
        cut.WaitForAssertion(() => cut.Find("[data-suggestion]").TextContent.Should().Contain("₱500.00"));

        gate.SetResult(Json(Claim(), HttpStatusCode.Created));
        await Task.Delay(100); // let the late answer land, if it is going to

        cut.FindAll("[data-allowance-dialog]").Should().ContainSingle("Ana's form is still open");
    }

    // ---------- The allowance ----------

    private IRenderedComponent<MaternityClaims> OpenAllowance(string suggestion, string? claim = null)
    {
        _api.On(HttpMethod.Get, $"/api/maternity-claims/{MariaClaimId}/suggestion", HttpStatusCode.OK, suggestion);
        var cut = RenderPage(Summary(0m, claim ?? Claim()));
        Row(cut, MariaClaimId).QuerySelector("[data-set-allowance]")!.Click();
        cut.WaitForAssertion(() => cut.Find("[data-suggestion]").TextContent.Should().NotContain("Working out"));
        return cut;
    }

    [Fact]
    public void TheAllowanceForm_ShowsTheSuggestion_AndTheMonthsItCameFrom()
    {
        var cut = OpenAllowance(Suggestion(666.67m, 6));

        cut.Find("[data-suggestion]").TextContent.Trim().Should()
            .Be("Suggested: ₱666.67 a day, from 6 months of paid payroll in Apr 2025–Mar 2026.");
    }

    [Fact]
    public void ASuggestionFromOneMonth_SaysMonth()
    {
        var cut = OpenAllowance(Suggestion(111.11m, 1));

        cut.Find("[data-suggestion]").TextContent.Trim().Should()
            .Be("Suggested: ₱111.11 a day, from 1 month of paid payroll in Apr 2025–Mar 2026.");
    }

    [Fact]
    public void NoPaidPayrollInTheWindow_AsksForTheSssApprovedAllowance()
    {
        var cut = OpenAllowance(Suggestion(null, 0));

        cut.Find("[data-suggestion]").TextContent.Trim().Should()
            .Be("No paid payroll found in the SSS window (Apr 2025–Mar 2026); enter the SSS-approved allowance.");
        cut.Find("#claim-allowance").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void OverriddenSssRates_SayNoSuggestionIsPossible()
    {
        var cut = OpenAllowance(Suggestion(null, 0, overridden: true));

        cut.Find("[data-suggestion]").TextContent.Trim().Should()
            .Be("SSS rates are overridden in payroll settings, so no suggestion is possible; enter the SSS-approved allowance.");
    }

    [Fact]
    public void TheSuggestion_FillsAnEmptyAllowance_AndSavingPutsIt_AndReloadsTheList()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, ListPath, () => Json(++loads == 1
                ? Summary(0m, Claim())
                : Summary(0m, Claim(allowance: 666.67m, benefit: 70000.35m))))
            .On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/allowance", HttpStatusCode.OK, Claim(allowance: 666.67m, benefit: 70000.35m));
        var cut = OpenAllowance(Suggestion(666.67m, 6));

        cut.Find("#claim-allowance").GetAttribute("value").Should().Be("666.67");
        cut.Find("[data-benefit-preview]").TextContent.Should().Contain("₱70,000.35").And.Contain("105 days");
        cut.Find("[data-submit-allowance]").Click();

        cut.WaitForAssertion(() => Row(cut, MariaClaimId).TextContent.Should().Contain("₱70,000.35"));
        BodyOf(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/allowance").GetProperty("dailyAllowance").GetDecimal().Should().Be(666.67m);
        cut.FindAll("[data-allowance-dialog]").Should().BeEmpty();
    }

    [Fact]
    public void AnAllowanceAlreadySet_IsWhatTheFormStartsFrom_NotTheSuggestion()
    {
        var cut = OpenAllowance(Suggestion(666.67m, 6), Claim(allowance: 600m, benefit: 63000m));

        cut.Find("#claim-allowance").GetAttribute("value").Should().Be("600");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    public void AnAllowanceThatIsNotAboveZero_IsRefusedWithoutCallingTheApi(string typed)
    {
        var cut = OpenAllowance(Suggestion(null, 0));

        cut.Find("#claim-allowance").Input(typed);
        cut.Find("[data-submit-allowance]").Click();

        cut.Find("[data-allowance-error]").TextContent.Should().Contain("Enter the SSS daily maternity allowance.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void ARefusedAllowance_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/allowance",
            () => Problem("PR-2026-0015 advances this benefit; discard it or pay it first."));
        var cut = OpenAllowance(Suggestion(666.67m, 6));

        cut.Find("[data-submit-allowance]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-allowance-error]").TextContent.Should()
            .Contain("PR-2026-0015 advances this benefit; discard it or pay it first."));
    }

    [Fact]
    public async Task ALateSuggestionForAnEarlierClaim_DoesNotReplaceTheOneOnScreen()
    {
        var mariaGate = _api.OnGated(HttpMethod.Get, $"/api/maternity-claims/{MariaClaimId}/suggestion");
        _api.On(HttpMethod.Get, $"/api/maternity-claims/{AnaClaimId}/suggestion", HttpStatusCode.OK, Suggestion(500m, 4));
        var cut = RenderPage(Summary(0m, Claim(), Claim(AnaClaimId, "Ana Cruz")));

        Row(cut, MariaClaimId).QuerySelector("[data-set-allowance]")!.Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        Row(cut, AnaClaimId).QuerySelector("[data-set-allowance]")!.Click();
        cut.WaitForAssertion(() => cut.Find("[data-suggestion]").TextContent.Should().Contain("₱500.00"));

        mariaGate.SetResult(Json(Suggestion(666.67m, 6)));
        await Task.Delay(100); // let the late answer land, if it is going to

        cut.Find("[data-suggestion]").TextContent.Should().Contain("₱500.00").And.NotContain("666.67");
        cut.Find("#claim-allowance").GetAttribute("value").Should().Be("500");
    }

    [Fact]
    public void AFailedSuggestion_SaysSo_AndTheAllowanceCanStillBeEntered()
    {
        _api.On(HttpMethod.Get, $"/api/maternity-claims/{MariaClaimId}/suggestion", () => Problem("Payroll history could not be read."))
            .On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/allowance", HttpStatusCode.OK, Claim(allowance: 650m, benefit: 68250m));
        var cut = RenderPage(Summary(0m, Claim()));

        Row(cut, MariaClaimId).QuerySelector("[data-set-allowance]")!.Click();
        cut.WaitForAssertion(() => cut.Find("[data-suggestion]").TextContent.Should().Contain("Payroll history could not be read."));
        cut.Find("#claim-allowance").Input("650");
        cut.Find("[data-submit-allowance]").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
    }

    // ---------- Reimbursement and denial ----------

    private static IElement CancelButton(IRenderedComponent<MaternityClaims> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel");

    /// <summary>The dialog's own close (X) button.</summary>
    private static IElement CloseButton(IRenderedComponent<MaternityClaims> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Close");

    [Fact]
    public async Task ARefusalAnsweredAfterItsFormWasClosed_DoesNotLandOnTheNextForm()
    {
        var gate = _api.OnGated(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/deny");
        var cut = RenderPage(Summary(140000.70m,
            Claim(status: "Advanced", allowance: 666.67m, benefit: 70000.35m, runNumber: "PR-2026-0015", advancedAt: "2026-08-05"),
            Claim(AnaClaimId, "Ana Cruz", "Advanced", 666.67m, 70000.35m, "PR-2026-0015", "2026-08-05", employeeId: AnaId)));

        Row(cut, MariaClaimId).QuerySelector("[data-deny]")!.Click();
        cut.Find("#deny-note").Input("No MAT-1 on file");
        cut.Find("[data-submit-deny]").Click();
        cut.WaitForAssertion(() => CancelButton(cut).HasAttribute("disabled").Should().BeTrue("the denial is being sent"));
        CloseButton(cut).Click();
        Row(cut, AnaClaimId).QuerySelector("[data-deny]")!.Click();

        gate.SetResult(Problem("Only an advanced claim can be denied."));
        await Task.Delay(100); // let the late answer land, if it is going to

        cut.Find("[data-deny-dialog]").TextContent.Should().Contain("Ana Cruz");
        cut.FindAll("[data-deny-error]").Should().BeEmpty();
        CancelButton(cut).HasAttribute("disabled").Should().BeFalse();
    }

    private IRenderedComponent<MaternityClaims> RenderAdvanced(string action)
    {
        var cut = RenderPage(Summary(70000.35m, Claim(status: "Advanced", allowance: 666.67m, benefit: 70000.35m,
            runNumber: "PR-2026-0015", advancedAt: "2026-08-05")));
        Row(cut, MariaClaimId).QuerySelector($"[data-{action}]")!.Click();
        return cut;
    }

    [Fact]
    public void AReimbursementOfTheBenefit_NeedsNoNote_AndPutsTheDateAndAmount()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/reimburse", HttpStatusCode.OK,
            Claim(status: "Reimbursed", allowance: 666.67m, benefit: 70000.35m, reimbursedOn: "2026-10-01", reimbursedAmount: 70000.35m));
        var cut = RenderAdvanced("reimburse");

        cut.Find("#reimbursed-amount").GetAttribute("value").Should().Be("70000.35", "it starts at the benefit");
        cut.Find("#reimbursed-on").Input("2026-10-01");
        cut.Find("[data-submit-reimburse]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-reimburse-dialog]").Should().BeEmpty());
        var body = BodyOf(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/reimburse");
        body.GetProperty("reimbursedOn").GetString().Should().Be("2026-10-01");
        body.GetProperty("reimbursedAmount").GetDecimal().Should().Be(70000.35m);
        body.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void AReimbursementThatDiffersFromTheBenefit_NeedsANote()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/reimburse", HttpStatusCode.OK,
            Claim(status: "Reimbursed", allowance: 666.67m, benefit: 70000.35m));
        var cut = RenderAdvanced("reimburse");

        cut.Find("#reimbursed-amount").Input("70000");
        cut.Find("[data-submit-reimburse]").Click();

        cut.Find("[data-reimburse-error]").TextContent.Should().Contain("Explain why the reimbursement differs from the benefit.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);

        cut.Find("#reimburse-note").Input("  SSS rounded down  ");
        cut.Find("[data-submit-reimburse]").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
        var body = BodyOf(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/reimburse");
        body.GetProperty("reimbursedAmount").GetDecimal().Should().Be(70000m);
        body.GetProperty("note").GetString().Should().Be("SSS rounded down");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.004")]
    public void AReimbursementOfNothing_IsRefusedWithoutCallingTheApi(string typed)
    {
        var cut = RenderAdvanced("reimburse");

        cut.Find("#reimbursed-amount").Input(typed);
        cut.Find("[data-submit-reimburse]").Click();

        cut.Find("[data-reimburse-error]").TextContent.Should().Contain("Enter the amount SSS reimbursed.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void ARefusedReimbursement_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/reimburse", () => Problem("Only an advanced claim can be reimbursed."));
        var cut = RenderAdvanced("reimburse");

        cut.Find("[data-submit-reimburse]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-reimburse-error]").TextContent.Should().Contain("Only an advanced claim can be reimbursed."));
    }

    [Fact]
    public void ADenial_NeedsANote_AndPutsIt()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, ListPath, () => Json(++loads == 1
                ? Summary(70000.35m, Claim(status: "Advanced", allowance: 666.67m, benefit: 70000.35m, runNumber: "PR-2026-0015", advancedAt: "2026-08-05"))
                : Summary(0m, Claim(status: "Denied", allowance: 666.67m, benefit: 70000.35m, note: "No MAT-1 on file"))))
            .On(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/deny", HttpStatusCode.OK,
                Claim(status: "Denied", allowance: 666.67m, benefit: 70000.35m, note: "No MAT-1 on file"));
        var cut = RenderPage();
        Row(cut, MariaClaimId).QuerySelector("[data-deny]")!.Click();

        cut.Find("#deny-note").Input("   ");
        cut.Find("[data-submit-deny]").Click();
        cut.Find("[data-deny-error]").TextContent.Should().Contain("Explain why SSS denied the claim.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);

        cut.Find("#deny-note").Input("No MAT-1 on file");
        cut.Find("[data-submit-deny]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-outstanding]").TextContent.Trim().Should().Be("₱0.00"));
        Row(cut, MariaClaimId).QuerySelector("[data-status]")!.TextContent.Trim().Should().Be("Denied");
        BodyOf(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/deny").GetProperty("note").GetString().Should().Be("No MAT-1 on file");
    }

    [Fact]
    public void ADenialInFlight_CannotBeSentTwice()
    {
        var gate = _api.OnGated(HttpMethod.Put, $"/api/maternity-claims/{MariaClaimId}/deny");
        var cut = RenderAdvanced("deny");

        cut.Find("#deny-note").Input("No MAT-1 on file");
        cut.Find("[data-submit-deny]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-submit-deny]").HasAttribute("disabled").Should().BeTrue());

        gate.SetResult(Json(Claim(status: "Denied")));
        cut.WaitForAssertion(() => cut.FindAll("[data-deny-dialog]").Should().BeEmpty());
        _api.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(1);
    }
}
