using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;
using static PeopleCore.Web.Tests.Pages.Payroll.PayrollTestData;

namespace PeopleCore.Web.Tests.Pages.Payroll;

public class PayrollRunDetailTests : BunitContext
{
    private static readonly Guid RunId = Guid.Parse("5d2c8e61-9f4a-4b37-8c15-a7e3b0d92f48");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    public PayrollRunDetailTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true);
    }

    private string RunPath => $"/api/payroll-runs/{RunId}";

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static readonly Guid JoseId = Guid.Parse("0c6f9a3e-8b2d-4f71-a5c4-3e9d1b7f2a60");

    private static string RunJson(string status, bool withEmployee = true, int missingAttendance = 0,
        string runType = "Regular", string? employees = null, decimal totalDeductions = 0m) =>
        $$"""
        {"id":"{{RunId}}","runNumber":"PR-2026-0017","periodLabel":"Sep 1-15, 2026","periodStart":"2026-09-01",
         "periodEnd":"2026-09-15","payDate":"2026-09-20","frequency":"SemiMonthly","status":"{{status}}",
         "employeeCount":1,"totalGrossPay":0,"totalDeductions":{{totalDeductions}},"totalNetPay":0,"createdAt":"2026-09-01T00:00:00Z",
         "employeesMissingAttendance":{{missingAttendance}},"runType":"{{runType}}",
         "employees":[{{employees ?? (withEmployee ? EmployeeLine : "")}}]}
        """;

    private static string EmployeeLine =>
        $$"""{"id":"{{Guid.NewGuid()}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-0042"}""";

    private static string JoseLine =>
        $$"""{"id":"{{Guid.NewGuid()}}","employeeId":"{{JoseId}}","employeeName":"Jose Reyes","employeeNumber":"EMP-0043"}""";

    private static string FinalPayLine(decimal tax, decimal totalDeductions = 2500m) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-0042",
         "grossPay":132500,"netPay":130000,"withholdingTax":{{tax}},"totalDeductions":{{totalDeductions}},
         "leaveConversionPay":7500,"leaveConversionNonTaxable":6000,"separationPay":100000,"retirementPay":25000,
         "finalPayNonTaxable":131000}
        """;

    private IRenderedComponent<PayrollRunDetail> RenderPage()
    {
        var cut = Render<PayrollRunDetail>(p => p.Add(x => x.Id, RunId));
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IEnumerable<string> ActionButtons(IRenderedComponent<PayrollRunDetail> cut) =>
        cut.FindAll("button").Select(b => b.TextContent.Trim()).Where(t => t is "Compute" or "Approve" or "Mark Paid");

    private static IElement Button(IRenderedComponent<PayrollRunDetail> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    [Fact]
    public void TheRun_IsLoadedWithItsEmployees_AndWarnsAboutMissingSchedules()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("ForApproval", missingAttendance: 2));

        var cut = RenderPage();

        cut.Find("h1").TextContent.Should().Be("PR-2026-0017");
        cut.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("Maria Santos");
        // Without the warning, those employees' pay silently has no absences deducted.
        cut.Find("[role=alert]").TextContent.Should()
            .Contain("2 employees had no shift schedule for this period, so no absences were derived for those employees.");
    }

    [Fact]
    public void TheStatusBadge_ReadsAsWords_NotTheRawStatusName()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("ForApproval"));

        var cut = RenderPage();

        cut.Markup.Should().Contain(">For approval<").And.NotContain("ForApproval");
    }

    [Theory]
    [InlineData("Draft", new[] { "Compute", "Approve" })]
    [InlineData("Processing", new[] { "Compute", "Approve" })]
    [InlineData("ForApproval", new[] { "Compute", "Approve" })]
    [InlineData("Approved", new[] { "Mark Paid" })]
    [InlineData("Paid", new string[0])]
    public void OnlyTheActionsTheServerWillAcceptForTheStatus_AreOffered(string status, string[] expected)
    {
        // Every button outside these sets is a guaranteed DomainException from the API - and
        // recomputing a Paid run would be touching figures that have already retired loans.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson(status));

        var cut = RenderPage();

        ActionButtons(cut).Should().Equal(expected);
    }

    [Fact]
    public void AFailedLoad_ShowsTheError_AndOffersNoActions()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.InternalServerError);

        var cut = Render<PayrollRunDetail>(p => p.Add(x => x.Id, RunId));

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("500"));
        ActionButtons(cut).Should().BeEmpty();
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Download all"));
        cut.Markup.Should().NotContain("Payroll run not found.", "a server failure says nothing about whether the run exists");
    }

    [Fact]
    public void ARunThatDoesNotExist_SaysSo_AndOffersTheWayBackToTheList()
    {
        // A stale bookmark or a mistyped id is not a failure to retry; the user needs to be told
        // there is no such run and pointed back to the ones there are.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.NotFound);

        var cut = RenderPage();

        cut.Markup.Should().Contain("Payroll run not found.");
        cut.FindAll("[role=alert]").Should().BeEmpty();
        ActionButtons(cut).Should().BeEmpty();

        Button(cut, "Back to Runs").Click();

        CurrentUri.Should().Be("http://localhost/payroll-runs");
    }

    [Theory]
    [InlineData("Draft", "Compute", "compute", "ForApproval", "For approval", new[] { "Compute", "Approve" })]
    [InlineData("ForApproval", "Approve", "approve", "Approved", "Approved", new[] { "Mark Paid" })]
    [InlineData("Approved", "Mark Paid", "mark-paid", "Paid", "Paid", new string[0])]
    public void AnAction_PutsToItsEndpoint_AndReloadsTheRunIntoItsNextStatus(
        string status, string button, string endpoint, string nextStatus, string nextLabel, string[] nextActions)
    {
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson(status)))
            .On(HttpMethod.Put, $"{RunPath}/{endpoint}", () =>
            {
                status = nextStatus;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
        var cut = RenderPage();

        Button(cut, button).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain($">{nextLabel}<"));
        ActionButtons(cut).Should().Equal(nextActions);
        _api.Requests.Where(r => r.Method == HttpMethod.Put).Should().ContainSingle();
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void AFailedReloadAfterAnAction_ShowsTheError_RatherThanTheRunAsItWasBefore()
    {
        // The compute went through, so the Draft figures on screen are out of date; leaving them
        // there with the reload's error hidden would have the user approving numbers that no
        // longer exist.
        var loads = 0;
        _api.On(HttpMethod.Get, RunPath, () => ++loads == 1
                ? Json(RunJson("Draft"))
                : new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .On(HttpMethod.Put, $"{RunPath}/compute", HttpStatusCode.NoContent);
        var cut = RenderPage();

        Button(cut, "Compute").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("500"));
        cut.Markup.Should().NotContain(">Draft<");
        cut.FindAll("tbody tr").Should().BeEmpty();
        ActionButtons(cut).Should().BeEmpty();
    }

    [Fact]
    public void ARejectedAction_ShowsTheServersReason_AndLetsTheUserTryAgain()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("ForApproval"))
            .On(HttpMethod.Put, $"{RunPath}/approve", HttpStatusCode.BadRequest,
                """{"detail":"Run must be computed before approval."}""");
        var cut = RenderPage();
        var before = CurrentUri;

        Button(cut, "Approve").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Run must be computed before approval."));
        CurrentUri.Should().Be(before);
        Button(cut, "Approve").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void DownloadingOneEmployeesPayslip_SavesItUnderTheRunAndEmployeeNumber()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslip/{RunId}/{MariaId}", Pdf);
        var cut = RenderPage();

        cut.Find("button[title='Download payslip for Maria Santos']").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "Payslip-PR-2026-0017-EMP-0042.pdf", "application/pdf"));
    }

    [Fact]
    public void DownloadingEveryPayslip_SavesOneFileForTheRun()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslips/{RunId}", Pdf);
        var cut = RenderPage();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Download all")).Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "Payslips-PR-2026-0017.pdf", "application/pdf"));
    }

    [Fact]
    public void AFailedPayslipDownload_ShowsAnInlineError_AndSavesNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslip/{RunId}/{MariaId}", HttpStatusCode.InternalServerError);
        // No explanation in the body: the fallback sentence still has to reach the user.
        var cut = RenderPage();

        cut.Find("button[title='Download payslip for Maria Santos']").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("Failed to download payslip for Maria Santos. The server ran into a problem (500). Please try again."));
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }

    [Fact]
    public void AFailedDownloadOfEveryPayslip_SaysWhy_AndSavesNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslips/{RunId}", HttpStatusCode.Conflict,
                """{"detail":"Payslips are released once the run is paid."}""");
        var cut = RenderPage();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Download all")).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("Failed to download payslips for this run. Payslips are released once the run is paid."));
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }

    [Fact]
    public void ARunWithNoEmployees_SaysSo_AndOffersNothingToDownload()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", withEmployee: false));

        var cut = RenderPage();

        cut.Markup.Should().Contain("No employees on this run.");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Download all"));
    }

    // ---------- Final-pay runs ----------

    [Fact]
    public void AFinalPayRun_ShowsEachEmployeesLeaveConversionSeparationAndRetirementPay_AndTheNonTaxablePart()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", runType: "FinalPay", employees: FinalPayLine(1200m)));

        var cut = RenderPage();

        var headers = cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
        // The column leaves out the exempt share of leave beyond de minimis, so it says what it holds.
        headers.Should().Contain(["Leave Conversion", "Separation Pay", "Retirement Pay",
                                  "Non-taxable (de minimis + separation/retirement)"]);
        headers.Should().NotContain("Final-Pay Non-Taxable");
        cut.Find("[data-leave-conversion]").TextContent.Should().Contain("7,500.00");
        cut.Find("[data-separation-pay]").TextContent.Should().Contain("100,000.00");
        cut.Find("[data-retirement-pay]").TextContent.Should().Contain("25,000.00");
        cut.Find("[data-final-pay-non-taxable]").TextContent.Should().Contain("131,000.00");
        cut.Find("[data-final-pay-badge]").TextContent.Should().Be("Final pay");
    }

    [Theory]
    [InlineData("FinalPay", "Approved", new[] { "Compute", "Mark Paid" })]
    [InlineData("FinalPay", "Paid", new string[0])]
    [InlineData("Regular", "Approved", new[] { "Mark Paid" })]
    public void AnApprovedFinalPay_CanStillBeComputed_ARegularRunCannot(string runType, string status, string[] expected)
    {
        // The API recomputes an Approved final pay (and sends it back to Draft) - it's where a Mark
        // Paid refused with "recompute it before paying" leads. A regular run's approval holds.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson(status, runType: runType, employees: FinalPayLine(1200m)));

        var cut = RenderPage();

        ActionButtons(cut).Should().Equal(expected);
    }

    [Fact]
    public void ComputingAnApprovedFinalPay_AsksFirst_SayingItGoesBackForApproval_ThenRecomputes()
    {
        var status = "Approved";
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson(status, runType: "FinalPay", employees: FinalPayLine(1200m))))
            .On(HttpMethod.Put, $"{RunPath}/compute", () =>
            {
                status = "Draft";
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
        var cut = RenderPage();

        Button(cut, "Compute").Click();

        cut.WaitForElement("[data-confirm-compute]").TextContent.Should()
            .Contain("goes back to Draft").And.Contain("approval again");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);

        cut.Find("[data-confirm-compute]").QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(">Draft<"));
        _api.Requests.Where(r => r.Method == HttpMethod.Put).Should().ContainSingle()
            .Which.RequestUri!.AbsolutePath.Should().Be($"{RunPath}/compute");
        ActionButtons(cut).Should().Equal("Compute", "Approve");
    }

    [Fact]
    public void CancellingTheComputeOfAnApprovedFinalPay_LeavesItApproved()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Approved", runType: "FinalPay", employees: FinalPayLine(1200m)));
        var cut = RenderPage();

        Button(cut, "Compute").Click();
        cut.WaitForElement("[data-confirm-compute]").QuerySelectorAll("button").First().Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-confirm-compute]").Should().BeEmpty());
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void ComputingADraftRun_DoesNotAsk()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", runType: "FinalPay", employees: FinalPayLine(1200m)))
            .On(HttpMethod.Put, $"{RunPath}/compute", HttpStatusCode.NoContent);
        var cut = RenderPage();

        Button(cut, "Compute").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
        cut.FindAll("[data-confirm-compute]").Should().BeEmpty();
    }

    [Fact]
    public void ARegularRun_HasNoFinalPayColumns()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft"));

        var cut = RenderPage();

        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).Should().NotContain("Leave Conversion");
        cut.FindAll("[data-final-pay-badge]").Should().BeEmpty();
    }

    [Fact]
    public void ANegativeTax_IsShownAsARefund()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", runType: "FinalPay", employees: FinalPayLine(-1800m)));

        var cut = RenderPage();

        var tax = cut.Find("[data-withholding-tax]").TextContent;
        tax.Should().Contain("Refund").And.Contain("1,800.00").And.NotContain("-");
    }

    [Fact]
    public void ARefund_IsNotNettedIntoTheDeductions_ButShownOnItsOwn()
    {
        // Stored: 5,550 of deductions after a 1,800 refund. The deductions actually taken are
        // 7,350; the refund is shown apart from them.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", runType: "FinalPay",
            employees: FinalPayLine(-1800m, totalDeductions: 5550m), totalDeductions: 5550m));

        var cut = RenderPage();

        cut.Find("[data-employee-deductions]").TextContent.Should().Contain("7,350.00");
        cut.Find("[data-run-deductions]").TextContent.Should().Contain("7,350.00");
        cut.Find("[data-run-tax-refund]").TextContent.Should().Contain("1,800.00");
    }

    [Fact]
    public void WithoutARefund_TheDeductionsAreAsStored_AndNoRefundIsShown()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", runType: "FinalPay",
            employees: FinalPayLine(1200m, totalDeductions: 5550m), totalDeductions: 5550m));

        var cut = RenderPage();

        cut.Find("[data-employee-deductions]").TextContent.Should().Contain("5,550.00");
        cut.Find("[data-run-deductions]").TextContent.Should().Contain("5,550.00");
        cut.FindAll("[data-run-tax-refund]").Should().BeEmpty();
    }

    [Fact]
    public void MarkPaidRefusedForOutstandingClearance_ShowsTheReason()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Approved", runType: "FinalPay", employees: FinalPayLine(0m)))
            .On(HttpMethod.Put, $"{RunPath}/mark-paid", HttpStatusCode.BadRequest,
                """{"title":"Bad request","detail":"Clear Return laptop, Turn in ID before paying final pay.","status":400}""");
        var cut = RenderPage();

        Button(cut, "Mark Paid").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("Clear Return laptop, Turn in ID before paying final pay."));
    }

    // ---------- Removing an employee ----------

    [Theory]
    [InlineData("Regular", "Draft", true)]
    [InlineData("Regular", "ForApproval", true)]
    [InlineData("Regular", "Approved", true)]
    [InlineData("Regular", "Paid", false)]
    [InlineData("FinalPay", "Draft", false)]
    public void RemoveIsOfferedOnRegularRunsThatArentPaid(string runType, string status, bool offered)
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson(status, runType: runType, employees: $"{EmployeeLine},{JoseLine}"));

        var cut = RenderPage();

        cut.FindAll($"[data-remove-employee='{MariaId}']").Should().HaveCount(offered ? 1 : 0);
    }

    [Fact]
    public void RemoveIsNotOffered_WhenTheRunHasOnlyOneEmployee()
    {
        // The API refuses: a payroll run needs at least one employee.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft"));

        var cut = RenderPage();

        cut.FindAll("[data-remove-employee]").Should().BeEmpty();
    }

    [Fact]
    public void RemovingAnEmployee_AsksFirst_ThenDeletesAndShowsTheRunTheApiReturns()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", employees: $"{EmployeeLine},{JoseLine}"))
            .On(HttpMethod.Delete, $"{RunPath}/employees/{JoseId}", HttpStatusCode.OK, RunJson("Draft"));
        var cut = RenderPage();

        cut.Find($"[data-remove-employee='{JoseId}']").Click();

        var dialog = cut.WaitForElement("[data-confirm-remove]");
        dialog.TextContent.Should().Contain("Jose Reyes");
        dialog.TextContent.Should().NotContain("approval again");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);

        dialog.QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Maria Santos"));
        _api.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Delete);
        cut.FindAll("[data-confirm-remove]").Should().BeEmpty();
    }

    [Fact]
    public void RemovingFromAnApprovedRun_WarnsItGoesBackToDraftForApprovalAgain()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Approved", employees: $"{EmployeeLine},{JoseLine}"));
        var cut = RenderPage();

        cut.Find($"[data-remove-employee='{JoseId}']").Click();

        cut.WaitForElement("[data-confirm-remove]").TextContent.Should()
            .Contain("goes back to Draft").And.Contain("approval again");
    }

    [Fact]
    public void CancellingTheRemoval_LeavesTheRunAlone()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", employees: $"{EmployeeLine},{JoseLine}"));
        var cut = RenderPage();

        cut.Find($"[data-remove-employee='{JoseId}']").Click();
        cut.WaitForElement("[data-confirm-remove]").QuerySelectorAll("button").First().Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-confirm-remove]").Should().BeEmpty());
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
        cut.FindAll("tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public void ARefusedRemoval_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", employees: $"{EmployeeLine},{JoseLine}"))
            .On(HttpMethod.Delete, $"{RunPath}/employees/{JoseId}", HttpStatusCode.BadRequest,
                """{"title":"Bad request","detail":"A paid payroll run can't be changed.","status":400}""");
        var cut = RenderPage();

        cut.Find($"[data-remove-employee='{JoseId}']").Click();
        cut.WaitForElement("[data-confirm-remove]").QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("A paid payroll run can't be changed."));
        cut.FindAll("[data-confirm-remove]").Should().BeEmpty();
        cut.FindAll("tbody tr").Should().HaveCount(2);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
