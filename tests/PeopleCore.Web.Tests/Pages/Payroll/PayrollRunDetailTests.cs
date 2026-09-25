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
        string runType = "Regular", string? employees = null, decimal totalDeductions = 0m,
        string periodEnd = "2026-09-15", bool includesLeaveConversion = false, bool includesThirteenthMonth = false) =>
        $$"""
        {"id":"{{RunId}}","runNumber":"PR-2026-0017","periodLabel":"Sep 1-15, 2026","periodStart":"2026-09-01",
         "periodEnd":"{{periodEnd}}","payDate":"2026-09-20","frequency":"SemiMonthly","status":"{{status}}",
         "employeeCount":1,"totalGrossPay":0,"totalDeductions":{{totalDeductions}},"totalNetPay":0,"createdAt":"2026-09-01T00:00:00Z",
         "employeesMissingAttendance":{{missingAttendance}},"runType":"{{runType}}",
         "employees":[{{employees ?? (withEmployee ? EmployeeLine : "")}}],
         "includesLeaveConversion":{{(includesLeaveConversion ? "true" : "false")}},
         "includesThirteenthMonth":{{(includesThirteenthMonth ? "true" : "false")}}}
        """;

    private const string December = "2026-12-31";

    private static string YearEndLine =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-0042",
         "grossPay":40100,"netPay":36000,"leaveConversionPay":3600,"leaveConversionNonTaxable":3600,"finalPayNonTaxable":3600}
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

    // ---------- Year-end leave conversion ----------

    private string LeaveConversionPath => $"{RunPath}/leave-conversion";

    private static IElement? LeaveConversionToggle(IRenderedComponent<PayrollRunDetail> cut) =>
        cut.FindAll("[data-leave-conversion-toggle]").SingleOrDefault();

    private static IElement ConfirmButton(IRenderedComponent<PayrollRunDetail> cut) =>
        cut.Find("[data-confirm-leave-conversion]").QuerySelectorAll("button").Last();

    [Fact]
    public void AFlaggedRun_ShowsTheYearEndBadge_AndEachEmployeesLeaveConversion()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK,
            RunJson("Draft", periodEnd: December, includesLeaveConversion: true, employees: YearEndLine));

        var cut = RenderPage();

        cut.Find("[data-leave-conversion-badge]").TextContent.Trim().Should().Be("Year-end leave conversion");
        var headers = cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
        headers.Should().Contain("Leave conversion");
        // Not a final pay: none of its other columns.
        headers.Should().NotContain(["Separation Pay", "Retirement Pay", "Non-taxable (de minimis + separation/retirement)"]);
        cut.Find("[data-leave-conversion]").TextContent.Should().Contain("3,600.00");
        cut.FindAll("[data-final-pay-badge]").Should().BeEmpty();
    }

    [Fact]
    public void AnUnflaggedDecemberRun_HasNoLeaveConversionBadgeOrColumn()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December));

        var cut = RenderPage();

        cut.FindAll("[data-leave-conversion-badge]").Should().BeEmpty();
        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).Should().NotContain("Leave conversion");
        cut.FindAll("[data-leave-conversion]").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Regular", "Draft", December, false, "Convert unused leave")]
    [InlineData("Regular", "ForApproval", December, false, "Convert unused leave")]
    [InlineData("Regular", "Approved", December, true, "Stop converting unused leave")]
    [InlineData("Regular", "Draft", December, true, "Stop converting unused leave")]
    [InlineData("Regular", "Paid", December, true, null)]
    [InlineData("Regular", "Draft", "2026-11-30", false, null)]
    [InlineData("FinalPay", "Draft", December, false, null)]
    public void TheConversionToggle_IsOfferedOnRegularDecemberRunsThatArentPaid(
        string runType, string status, string periodEnd, bool flagged, string? expected)
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK,
            RunJson(status, runType: runType, periodEnd: periodEnd, includesLeaveConversion: flagged));

        var cut = RenderPage();

        if (expected is null) LeaveConversionToggle(cut).Should().BeNull();
        else LeaveConversionToggle(cut)!.TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void TurningTheConversionOn_AsksFirst_ThenPutsIt_AndReloadsTheRun()
    {
        var flagged = false;
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson("Draft", periodEnd: December, includesLeaveConversion: flagged,
                employees: flagged ? YearEndLine : EmployeeLine)))
            .On(HttpMethod.Put, LeaveConversionPath, () =>
            {
                flagged = true;
                return Json(RunJson("Draft", periodEnd: December, includesLeaveConversion: true, employees: YearEndLine));
            });
        var cut = RenderPage();

        LeaveConversionToggle(cut)!.Click();

        var dialog = cut.WaitForElement("[data-confirm-leave-conversion]");
        dialog.TextContent.Should().Contain("Convert unused leave").And.NotContain("approval again");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);

        ConfirmButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[data-leave-conversion-badge]").TextContent.Trim().Should().Be("Year-end leave conversion"));
        var put = _api.Requests.FindIndex(r => r.Method == HttpMethod.Put);
        _api.Requests[put].RequestUri!.AbsolutePath.Should().Be(LeaveConversionPath);
        _api.RequestBodies[put].Should().Be("""{"include":true}""");
        _api.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(2, "the run is reloaded after the change");
        cut.FindAll("[data-confirm-leave-conversion]").Should().BeEmpty();
        LeaveConversionToggle(cut)!.TextContent.Trim().Should().Be("Stop converting unused leave");
        cut.Find("[data-leave-conversion]").TextContent.Should().Contain("3,600.00");
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("ForApproval")]
    public void TurningTheConversionOff_OnARunAwaitingOrPastApproval_WarnsItGoesBackToDraftForApprovalAgain(string status)
    {
        var current = status;
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson(current, periodEnd: December, includesLeaveConversion: current != "Draft",
                employees: YearEndLine)))
            .On(HttpMethod.Put, LeaveConversionPath, () =>
            {
                current = "Draft";
                return Json(RunJson("Draft", periodEnd: December));
            });
        var cut = RenderPage();

        LeaveConversionToggle(cut)!.Click();

        var dialog = cut.WaitForElement("[data-confirm-leave-conversion]");
        dialog.TextContent.Should().Contain("Stop converting unused leave")
            .And.Contain("goes back to Draft").And.Contain("approval again");

        ConfirmButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(">Draft<"));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Put)].Should().Be("""{"include":false}""");
        cut.FindAll("[data-leave-conversion-badge]").Should().BeEmpty();
        LeaveConversionToggle(cut)!.TextContent.Trim().Should().Be("Convert unused leave");
    }

    [Fact]
    public void CancellingTheConversionChange_SendsNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December));
        var cut = RenderPage();

        LeaveConversionToggle(cut)!.Click();
        cut.WaitForElement("[data-confirm-leave-conversion]").QuerySelectorAll("button").First().Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-confirm-leave-conversion]").Should().BeEmpty());
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData("A paid payroll run can't be changed.")]
    [InlineData("Year-end leave conversion goes on a December payroll.")]
    [InlineData("A final pay's leave conversion can't be changed here.")]
    [InlineData("This payroll already converts unused leave.")]
    [InlineData("This payroll already doesn't convert unused leave.")]
    [InlineData("Maria Santos's leave for 2026 was already converted in PR-2026-0023.")]
    public void ARefusedConversionChange_ShowsTheApisReason(string reason)
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December))
            .On(HttpMethod.Put, LeaveConversionPath, HttpStatusCode.BadRequest,
                System.Text.Json.JsonSerializer.Serialize(new { title = "Business rule violation", status = 400, detail = reason }));
        var cut = RenderPage();

        LeaveConversionToggle(cut)!.Click();
        cut.WaitForElement("[data-confirm-leave-conversion]");
        ConfirmButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain(reason));
        cut.FindAll("[data-confirm-leave-conversion]").Should().BeEmpty();
        LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeFalse("the user can try again");
    }

    [Fact]
    public void WhileTheConversionChangeIsInFlight_TheRunsActionsAreDisabled_AndASecondConfirmSendsNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December));
        var gate = _api.OnGated(HttpMethod.Put, LeaveConversionPath);
        var cut = RenderPage();

        LeaveConversionToggle(cut)!.Click();
        cut.WaitForElement("[data-confirm-leave-conversion]");
        ConfirmButton(cut).Click();

        cut.WaitForAssertion(() => LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeTrue());
        Button(cut, "Compute").HasAttribute("disabled").Should().BeTrue();
        Button(cut, "Approve").HasAttribute("disabled").Should().BeTrue();

        ConfirmButton(cut).Click();
        _api.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(1);

        gate.SetResult(Json(RunJson("Draft", periodEnd: December, includesLeaveConversion: true)));
        cut.WaitForAssertion(() => LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeFalse());
    }

    [Fact]
    public void WhileAnEmployeeIsBeingRemoved_TheRunsOtherActionsAreDisabled()
    {
        // Removing recomputes the run too; a Compute, Approve or conversion change sent alongside it
        // would race it.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December, employees: $"{EmployeeLine},{JoseLine}"));
        var gate = _api.OnGated(HttpMethod.Delete, $"{RunPath}/employees/{JoseId}");
        var cut = RenderPage();

        cut.Find($"[data-remove-employee='{JoseId}']").Click();
        cut.WaitForElement("[data-confirm-remove]").QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeTrue());
        Button(cut, "Compute").HasAttribute("disabled").Should().BeTrue();
        Button(cut, "Approve").HasAttribute("disabled").Should().BeTrue();

        ThirteenthMonthToggle(cut)!.HasAttribute("disabled").Should().BeTrue();

        gate.SetResult(Json(RunJson("Draft", periodEnd: December)));
        cut.WaitForAssertion(() => LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeFalse());
        Button(cut, "Compute").HasAttribute("disabled").Should().BeFalse();
        ThirteenthMonthToggle(cut)!.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AnApprovalRefusedBecauseTheConvertibleLeaveChanged_SaysSo_AndOffersRecompute()
    {
        const string reason = "Maria Santos's convertible leave has changed since this payroll was computed; recompute it before paying.";
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK,
                RunJson("ForApproval", periodEnd: December, includesLeaveConversion: true, employees: YearEndLine))
            .On(HttpMethod.Put, $"{RunPath}/approve", HttpStatusCode.BadRequest,
                System.Text.Json.JsonSerializer.Serialize(new { title = "Business rule violation", status = 400, detail = reason }));
        var cut = RenderPage();

        Button(cut, "Approve").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain(reason));
        cut.Markup.Should().Contain(">For approval<");
        ActionButtons(cut).Should().Equal("Compute", "Approve");
        Button(cut, "Compute").HasAttribute("disabled").Should().BeFalse("recomputing is the way on");
    }

    [Fact]
    public void AnApprovedFlaggedRun_CanBeRecomputed_AfterAskingFirst()
    {
        // The API recomputes an Approved run that converts leave (and sends it back to Draft): it's
        // where an approval or Mark Paid refused with "recompute it before paying" leads.
        var status = "Approved";
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson(status, periodEnd: December, includesLeaveConversion: true, employees: YearEndLine)))
            .On(HttpMethod.Put, $"{RunPath}/compute", () =>
            {
                status = "Draft";
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
        var cut = RenderPage();

        ActionButtons(cut).Should().Equal("Compute", "Mark Paid");
        Button(cut, "Compute").Click();

        var dialog = cut.WaitForElement("[data-confirm-compute]");
        dialog.TextContent.Should().Contain("goes back to Draft").And.Contain("approval again").And.NotContain("final pay");
        dialog.QuerySelectorAll("button").Last().Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(">Draft<"));
        ActionButtons(cut).Should().Equal("Compute", "Approve");
    }

    // ---------- The 13th month ----------

    private string ThirteenthMonthPath => $"{RunPath}/thirteenth-month";

    private static IElement? ThirteenthMonthToggle(IRenderedComponent<PayrollRunDetail> cut) =>
        cut.FindAll("[data-thirteenth-month-toggle]").SingleOrDefault();

    private static IElement ConfirmThirteenthMonthButton(IRenderedComponent<PayrollRunDetail> cut) =>
        cut.Find("[data-confirm-thirteenth-month]").QuerySelectorAll("button").Last();

    private static string ThirteenthMonthLine =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-0042",
         "grossPay":39541.67,"netPay":36000,"thirteenthMonth":3041.67}
        """;

    [Fact]
    public void ARunWithThe13thMonth_ShowsTheBadge_AndEachEmployees13thMonth()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK,
            RunJson("Draft", includesThirteenthMonth: true, employees: $"{ThirteenthMonthLine},{JoseLine}"));

        var cut = RenderPage();

        cut.Find("[data-thirteenth-month-badge]").TextContent.Trim().Should().Be("13th month");
        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).Should().Contain("13th month");
        var cells = cut.FindAll("[data-thirteenth-month]").Select(c => c.TextContent.Trim()).ToList();
        cells.Should().HaveCount(2);
        cells[0].Should().EndWith("3,041.67");
        cells[1].Should().EndWith("0.00");
    }

    [Fact]
    public void ARunWithout13thMonthPay_HasNoBadgeOrColumn()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft"));

        var cut = RenderPage();

        cut.FindAll("[data-thirteenth-month-badge]").Should().BeEmpty();
        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).Should().NotContain("13th month");
        cut.FindAll("[data-thirteenth-month]").Should().BeEmpty();
    }

    [Fact]
    public void ARunIncludingThe13thMonth_ThatPaysNoneOfIt_ShowsTheBadgeButNoColumn()
    {
        // Everyone on it is ineligible, say: the run includes it, but nobody's 13th month is above zero.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", includesThirteenthMonth: true));

        var cut = RenderPage();

        cut.FindAll("[data-thirteenth-month-badge]").Should().ContainSingle();
        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).Should().NotContain("13th month");
    }

    [Theory]
    [InlineData("Regular", "Draft", "2026-09-15", false, "Include 13th month")]
    [InlineData("Regular", "ForApproval", "2026-06-15", false, "Include 13th month")]
    [InlineData("Regular", "Approved", December, true, "Leave out the 13th month")]
    [InlineData("Regular", "Draft", "2026-11-30", true, "Leave out the 13th month")]
    [InlineData("Regular", "Paid", December, true, null)]
    [InlineData("Regular", "Paid", "2026-09-15", false, null)]
    [InlineData("FinalPay", "Draft", December, true, null)]
    public void The13thMonthToggle_IsOfferedOnRegularRunsThatArentPaid_InAnyMonth(
        string runType, string status, string periodEnd, bool includes, string? expected)
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK,
            RunJson(status, runType: runType, periodEnd: periodEnd, includesThirteenthMonth: includes));

        var cut = RenderPage();

        if (expected is null) ThirteenthMonthToggle(cut).Should().BeNull();
        else ThirteenthMonthToggle(cut)!.TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void Including13thMonth_AsksFirst_ThenPutsIt_AndReloadsTheRun()
    {
        var includes = false;
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson("Draft", includesThirteenthMonth: includes,
                employees: includes ? ThirteenthMonthLine : EmployeeLine)))
            .On(HttpMethod.Put, ThirteenthMonthPath, () =>
            {
                includes = true;
                return Json(RunJson("Draft", includesThirteenthMonth: true, employees: ThirteenthMonthLine));
            });
        var cut = RenderPage();

        ThirteenthMonthToggle(cut)!.Click();

        var dialog = cut.WaitForElement("[data-confirm-thirteenth-month]");
        dialog.TextContent.Should().Contain("Include 13th month").And.NotContain("approval again");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);

        ConfirmThirteenthMonthButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[data-thirteenth-month-badge]").TextContent.Trim().Should().Be("13th month"));
        var put = _api.Requests.FindIndex(r => r.Method == HttpMethod.Put);
        _api.Requests[put].RequestUri!.AbsolutePath.Should().Be(ThirteenthMonthPath);
        _api.RequestBodies[put].Should().Be("""{"include":true}""");
        _api.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(2, "the run is reloaded after the change");
        cut.FindAll("[data-confirm-thirteenth-month]").Should().BeEmpty();
        ThirteenthMonthToggle(cut)!.TextContent.Trim().Should().Be("Leave out the 13th month");
        cut.Find("[data-thirteenth-month]").TextContent.Should().Contain("3,041.67");
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("ForApproval")]
    public void LeavingOutThe13thMonth_OnARunAwaitingOrPastApproval_WarnsItGoesBackToDraftForApprovalAgain(string status)
    {
        var current = status;
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson(current, includesThirteenthMonth: current != "Draft",
                employees: current != "Draft" ? ThirteenthMonthLine : EmployeeLine)))
            .On(HttpMethod.Put, ThirteenthMonthPath, () =>
            {
                current = "Draft";
                return Json(RunJson("Draft"));
            });
        var cut = RenderPage();

        ThirteenthMonthToggle(cut)!.Click();

        var dialog = cut.WaitForElement("[data-confirm-thirteenth-month]");
        dialog.TextContent.Should().Contain("Leave out the 13th month")
            .And.Contain("goes back to Draft").And.Contain("approval again");

        ConfirmThirteenthMonthButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(">Draft<"));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Put)].Should().Be("""{"include":false}""");
        cut.FindAll("[data-thirteenth-month-badge]").Should().BeEmpty();
        cut.FindAll("[data-thirteenth-month]").Should().BeEmpty();
        ThirteenthMonthToggle(cut)!.TextContent.Trim().Should().Be("Include 13th month");
    }

    [Fact]
    public void CancellingThe13thMonthChange_SendsNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft"));
        var cut = RenderPage();

        ThirteenthMonthToggle(cut)!.Click();
        cut.WaitForElement("[data-confirm-thirteenth-month]").QuerySelectorAll("button").First().Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-confirm-thirteenth-month]").Should().BeEmpty());
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData("A paid payroll run can't be changed.")]
    [InlineData("A final pay's 13th month can't be changed here.")]
    [InlineData("This payroll already includes the 13th month.")]
    [InlineData("This payroll already leaves out the 13th month.")]
    [InlineData("Maria Santos's leave for 2026 was already converted in PR-2026-0023.")]
    public void ARefused13thMonthChange_ShowsTheApisReason(string reason)
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December))
            .On(HttpMethod.Put, ThirteenthMonthPath, HttpStatusCode.BadRequest,
                System.Text.Json.JsonSerializer.Serialize(new { title = "Business rule violation", status = 400, detail = reason }));
        var cut = RenderPage();

        ThirteenthMonthToggle(cut)!.Click();
        cut.WaitForElement("[data-confirm-thirteenth-month]");
        ConfirmThirteenthMonthButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain(reason));
        cut.FindAll("[data-confirm-thirteenth-month]").Should().BeEmpty();
        ThirteenthMonthToggle(cut)!.HasAttribute("disabled").Should().BeFalse("the user can try again");
    }

    [Fact]
    public void WhileThe13thMonthChangeIsInFlight_TheRunsActionsAreDisabled_AndASecondConfirmSendsNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", periodEnd: December));
        var gate = _api.OnGated(HttpMethod.Put, ThirteenthMonthPath);
        var cut = RenderPage();

        ThirteenthMonthToggle(cut)!.Click();
        cut.WaitForElement("[data-confirm-thirteenth-month]");
        ConfirmThirteenthMonthButton(cut).Click();

        cut.WaitForAssertion(() => ThirteenthMonthToggle(cut)!.HasAttribute("disabled").Should().BeTrue());
        LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeTrue();
        Button(cut, "Compute").HasAttribute("disabled").Should().BeTrue();
        Button(cut, "Approve").HasAttribute("disabled").Should().BeTrue();

        ConfirmThirteenthMonthButton(cut).Click();
        _api.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(1);

        gate.SetResult(Json(RunJson("Draft", periodEnd: December, includesThirteenthMonth: true)));
        cut.WaitForAssertion(() => ThirteenthMonthToggle(cut)!.HasAttribute("disabled").Should().BeFalse());
        LeaveConversionToggle(cut)!.HasAttribute("disabled").Should().BeFalse();
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
