using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;
using static PeopleCore.Web.Tests.Pages.Payroll.PayrollTestData;

namespace PeopleCore.Web.Tests.Pages.Payroll;

/// <summary>
/// The opening balances page: what each employee was paid in a year before PeopleCore, by year,
/// with the double-count warnings; the form to add or edit one; and the CSV template and import.
/// </summary>
public class OpeningBalancesTests : BunitContext
{
    private static readonly Guid MariaBalanceId = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001");
    private static readonly Guid JoseBalanceId = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000002");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");
    private static readonly Guid JoseId = Guid.Parse("0c6f9a3e-8b2d-4f71-a5c4-3e9d1b7f2a60");

    private static readonly int ThisYear = DateTime.Today.Year;

    private const string BasePath = "/api/payroll-opening-balances";
    private static string ListPath(int year) => $"{BasePath}?year={year}";

    private readonly StubHttpHandler _api = new();

    public OpeningBalancesTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true).SetVoidResult();
    }

    private static string Balance(Guid? id = null, Guid? employeeId = null, string name = "Maria Santos",
        string number = "E-001", int? year = null, string? through = null, decimal basic = 90000m,
        decimal thirteenth = 1000.5m, decimal contributions = 6000m, decimal tax = 7000m, decimal leaveDays = 2.5m,
        params string[] warnings)
    {
        var y = year ?? ThisYear;
        return $$"""
            {"id":"{{id ?? MariaBalanceId}}","employeeId":"{{employeeId ?? MariaId}}","employeeName":"{{name}}","employeeNumber":"{{number}}",
             "year":{{y}},"throughDate":"{{through ?? $"{y}-03-31"}}","basicSalary":{{basic}},"thirteenthMonthPaid":{{thirteenth}},
             "otherBenefitsPaid":2000,"otherTaxablePay":3000,"deMinimis":4000,"otherNonTaxable":5000,
             "employeeContributions":{{contributions}},"taxWithheld":{{tax}},"deMinimisLeaveDays":{{leaveDays}},
             "warnings":{{JsonSerializer.Serialize(warnings)}}}
            """;
    }

    private static string List(params string[] balances) => $"[{string.Join(",", balances)}]";

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(string detail) =>
        Json($$"""{"title":"Business Rule Violation","status":400,"detail":"{{detail}}"}""", HttpStatusCode.BadRequest);

    private IRenderedComponent<OpeningBalances> RenderPage(string? list = null)
    {
        if (list is not null) _api.On(HttpMethod.Get, ListPath(ThisYear), HttpStatusCode.OK, list);
        var cut = Render<OpeningBalances>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement Row(IRenderedComponent<OpeningBalances> cut, Guid balanceId) =>
        cut.Find($"[data-balance='{balanceId}']");

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    private static IElement CancelButton(IRenderedComponent<OpeningBalances> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel");

    private static IElement CloseButton(IRenderedComponent<OpeningBalances> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Close");

    // ---------- The page and the list ----------

    [Fact]
    public void ThePage_IsAtOpeningBalances_ForPayrollManagers()
    {
        typeof(OpeningBalances).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).Should().Equal("/opening-balances");
        typeof(OpeningBalances).GetCustomAttribute<RequirePermissionAttribute>()!.Policy.Should().Be("permission:payroll.manage");
    }

    [Fact]
    public void TheList_IsThisYearsBalances_WithTheirFiguresAndWarnings()
    {
        var cut = RenderPage(List(
            Balance(warnings: $"Maria Santos's opening balance already covers pay through Mar 31, {ThisYear}; PAY-{ThisYear}-003 was paid on Mar 15, {ThisYear}."),
            Balance(JoseBalanceId, JoseId, "Jose Cruz", "E-002", through: $"{ThisYear}-02-28", basic: 50000m, thirteenth: 0m, tax: 0m)));

        cut.Find("#ob-year").GetAttribute("value").Should().Be(ThisYear.ToString());
        var maria = Row(cut, MariaBalanceId);
        maria.TextContent.Should().Contain("Maria Santos").And.Contain("E-001").And.Contain($"Mar 31, {ThisYear}")
            .And.Contain("₱90,000.00").And.Contain("₱1,000.50").And.Contain("₱7,000.00");
        maria.QuerySelectorAll("[data-balance-warnings] li").Select(li => li.TextContent.Trim()).Should().Equal(
            $"Maria Santos's opening balance already covers pay through Mar 31, {ThisYear}; PAY-{ThisYear}-003 was paid on Mar 15, {ThisYear}.");
        var jose = Row(cut, JoseBalanceId);
        jose.TextContent.Should().Contain("Jose Cruz").And.Contain($"Feb 28, {ThisYear}").And.Contain("₱50,000.00");
        jose.QuerySelector("[data-balance-warnings]").Should().BeNull();
        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).Should().StartWith(
            ["Employee", "Through", "Basic salary", "13th month paid", "Tax withheld", "Warnings"]);
    }

    [Fact]
    public void ChoosingAnotherYear_ListsThatYearsBalances()
    {
        var lastYear = ThisYear - 1;
        _api.On(HttpMethod.Get, ListPath(lastYear), HttpStatusCode.OK, List(Balance(JoseBalanceId, JoseId, "Jose Cruz", "E-002", year: lastYear)));
        var cut = RenderPage(List(Balance()));

        cut.Find("#ob-year").Change(lastYear.ToString());

        cut.WaitForAssertion(() => Row(cut, JoseBalanceId).TextContent.Should().Contain("Jose Cruz"));
        cut.FindAll($"[data-balance='{MariaBalanceId}']").Should().BeEmpty();
    }

    [Fact]
    public async Task AnEarlierYearsListAnsweringLate_DoesNotReplaceTheYearOnScreen()
    {
        var lastYear = ThisYear - 1;
        var gate = _api.OnGated(HttpMethod.Get, ListPath(ThisYear));
        _api.On(HttpMethod.Get, ListPath(lastYear), HttpStatusCode.OK, List(Balance(JoseBalanceId, JoseId, "Jose Cruz", "E-002", year: lastYear)));
        var cut = Render<OpeningBalances>();

        cut.Find("#ob-year").Change(lastYear.ToString());
        cut.WaitForAssertion(() => Row(cut, JoseBalanceId).TextContent.Should().Contain("Jose Cruz"));
        gate.SetResult(Json(List(Balance())));
        await Task.Delay(100);

        cut.FindAll($"[data-balance='{MariaBalanceId}']").Should().BeEmpty();
    }

    [Fact]
    public void NoBalancesForTheYear_SaysSo()
    {
        var cut = RenderPage(List());

        cut.Markup.Should().Contain($"No opening balances for {ThisYear}.");
    }

    [Fact]
    public void AFailedLoad_ShowsTheApisReason_AndARetryLoadsTheList()
    {
        var attempts = 0;
        _api.On(HttpMethod.Get, ListPath(ThisYear), () => ++attempts == 1 ? Problem("The balances could not be read.") : Json(List(Balance())));
        var cut = RenderPage();

        cut.Find("[data-load-error]").TextContent.Should().Contain("The balances could not be read.");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Retry").Click();

        cut.WaitForAssertion(() => Row(cut, MariaBalanceId).TextContent.Should().Contain("Maria Santos"));
        cut.FindAll("[data-load-error]").Should().BeEmpty();
    }

    // ---------- Adding one ----------

    private IRenderedComponent<OpeningBalances> OpenNew(string? list = null)
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.OK,
            EmployeePage(1, 1, Employee(MariaId, "E-001", "Maria", "Santos"), Employee(JoseId, "E-002", "Jose", "Cruz")));
        var cut = RenderPage(list ?? List());
        cut.Find("[data-new-balance]").Click();
        cut.WaitForAssertion(() => cut.FindAll("#ob-employee option").Should().HaveCount(3));
        return cut;
    }

    private static void Fill(IRenderedComponent<OpeningBalances> cut, string through = "", string basic = "90000",
        string contributions = "6000", string leaveDays = "2.5", string thirteenth = "1000.5")
    {
        cut.Find("#ob-through").Input(through.Length > 0 ? through : $"{ThisYear}-03-31");
        cut.Find("#ob-basic").Input(basic);
        cut.Find("#ob-thirteenth").Input(thirteenth);
        cut.Find("#ob-other-benefits").Input("2000");
        cut.Find("#ob-other-taxable").Input("3000");
        cut.Find("#ob-de-minimis").Input("4000");
        cut.Find("#ob-other-non-taxable").Input("5000");
        cut.Find("#ob-contributions").Input(contributions);
        cut.Find("#ob-tax").Input("7000");
        cut.Find("#ob-leave-days").Input(leaveDays);
    }

    [Fact]
    public void TheForm_HasEveryField_LabelledAsTheSpecNamesThem()
    {
        var cut = OpenNew();

        cut.Find("[data-balance-dialog]").QuerySelectorAll("label")
            .Select(l => l.TextContent.Replace("*", "").Trim())
            .Should().Equal("Employee", "Year", "Through date", "Basic salary", "13th month paid", "Other benefits paid",
                "Other taxable pay", "De minimis", "Other non-taxable", "Employee contributions", "Tax withheld",
                "De minimis leave days");
        cut.Find("#ob-employee").TextContent.Should().Contain("Cruz, Jose (E-002)").And.Contain("Santos, Maria (E-001)");
        cut.Find("#ob-form-year").GetAttribute("value").Should().Be(ThisYear.ToString(), "it starts at the year on screen");
    }

    [Fact]
    public void ANewBalance_PostsEveryField_ClosesTheForm_ReloadsTheList_AndShowsTheWarningsTheSaveReturned()
    {
        const string editWarning = "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.";
        var loads = 0;
        _api.On(HttpMethod.Get, ListPath(ThisYear), () => Json(++loads == 1 ? List() : List(Balance())))
            .On(HttpMethod.Post, BasePath, HttpStatusCode.Created, Balance(warnings: editWarning));
        var cut = OpenNew();

        cut.Find("#ob-employee").Change(MariaId.ToString());
        Fill(cut);
        cut.Find("[data-submit-balance]").Click();

        cut.WaitForAssertion(() => Row(cut, MariaBalanceId).TextContent.Should().Contain("Maria Santos"));
        cut.FindAll("[data-balance-dialog]").Should().BeEmpty();
        cut.FindAll("[data-save-warnings] li").Select(li => li.TextContent.Trim()).Should().Equal(editWarning);
        var body = BodyOf(HttpMethod.Post, BasePath);
        body.EnumerateObject().Select(p => p.Name).Should().Equal(
            "employeeId", "year", "throughDate", "basicSalary", "thirteenthMonthPaid", "otherBenefitsPaid", "otherTaxablePay",
            "deMinimis", "otherNonTaxable", "employeeContributions", "taxWithheld", "deMinimisLeaveDays");
        body.GetProperty("employeeId").GetGuid().Should().Be(MariaId);
        body.GetProperty("year").GetInt32().Should().Be(ThisYear);
        body.GetProperty("throughDate").GetString().Should().Be($"{ThisYear}-03-31");
        body.GetProperty("basicSalary").GetDecimal().Should().Be(90000m);
        body.GetProperty("thirteenthMonthPaid").GetDecimal().Should().Be(1000.5m);
        body.GetProperty("otherBenefitsPaid").GetDecimal().Should().Be(2000m);
        body.GetProperty("otherTaxablePay").GetDecimal().Should().Be(3000m);
        body.GetProperty("deMinimis").GetDecimal().Should().Be(4000m);
        body.GetProperty("otherNonTaxable").GetDecimal().Should().Be(5000m);
        body.GetProperty("employeeContributions").GetDecimal().Should().Be(6000m);
        body.GetProperty("taxWithheld").GetDecimal().Should().Be(7000m);
        body.GetProperty("deMinimisLeaveDays").GetDecimal().Should().Be(2.5m);
    }

    [Fact]
    public void ABlankAmount_IsSentAsZero()
    {
        _api.On(HttpMethod.Post, BasePath, HttpStatusCode.Created, Balance());
        var cut = OpenNew();

        cut.Find("#ob-employee").Change(MariaId.ToString());
        cut.Find("#ob-through").Input($"{ThisYear}-03-31");
        cut.Find("#ob-basic").Input("90000");
        cut.Find("[data-submit-balance]").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        var body = BodyOf(HttpMethod.Post, BasePath);
        body.GetProperty("taxWithheld").GetDecimal().Should().Be(0m);
        body.GetProperty("deMinimisLeaveDays").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public void ANewBalanceForAnotherYear_ShowsThatYearAfterSaving()
    {
        var lastYear = ThisYear - 1;
        _api.On(HttpMethod.Post, BasePath, HttpStatusCode.Created, Balance(year: lastYear))
            .On(HttpMethod.Get, ListPath(lastYear), HttpStatusCode.OK, List(Balance(year: lastYear)));
        var cut = OpenNew();

        cut.Find("#ob-employee").Change(MariaId.ToString());
        cut.Find("#ob-form-year").Input(lastYear.ToString());
        Fill(cut, through: $"{lastYear}-03-31");
        cut.Find("[data-submit-balance]").Click();

        cut.WaitForAssertion(() => Row(cut, MariaBalanceId).TextContent.Should().Contain($"Mar 31, {lastYear}"));
        cut.Find("#ob-year").GetAttribute("value").Should().Be(lastYear.ToString());
    }

    public static TheoryData<string, Action<IRenderedComponent<OpeningBalances>>> Refusals => new()
    {
        { "Choose an employee.", cut => { cut.Find("#ob-employee").Change(""); Fill(cut); } },
        { "Enter a year.", cut => { cut.Find("#ob-form-year").Input(""); Fill(cut); } },
        { "Enter a year.", cut => { cut.Find("#ob-form-year").Input("26"); Fill(cut); } },
        { $"The through date must fall in {ThisYear}.", cut => Fill(cut, through: $"{ThisYear - 1}-12-31") },
        { $"The through date must fall in {ThisYear}.", cut => { Fill(cut); cut.Find("#ob-through").Input(""); } },
        { "Amounts can't be negative.", cut => Fill(cut, thirteenth: "-1") },
        { "Enter an amount below ₱10,000,000,000.", cut => Fill(cut, basic: "10000000000", contributions: "0") },
        { "Contributions can't be more than the basic salary.", cut => Fill(cut, basic: "5000", contributions: "5000.01") },
        { "De minimis leave days must be between 0 and 10.", cut => Fill(cut, leaveDays: "10.5") },
        { "De minimis leave days must be between 0 and 10.", cut => Fill(cut, leaveDays: "-0.5") },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AFormTheApiWouldRefuse_IsRefusedWithItsMessage_WithoutCallingTheApi(
        string message, Action<IRenderedComponent<OpeningBalances>> fill)
    {
        var cut = OpenNew();
        cut.Find("#ob-employee").Change(MariaId.ToString());

        fill(cut);
        cut.Find("[data-submit-balance]").Click();

        cut.Find("[data-balance-error]").TextContent.Trim().Should().Be(message);
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void ARefusedSave_ShowsTheApisReason_AndKeepsTheFormOpen()
    {
        _api.On(HttpMethod.Post, BasePath, () => Problem($"Maria Santos already has an opening balance for {ThisYear}."));
        var cut = OpenNew();

        cut.Find("#ob-employee").Change(MariaId.ToString());
        Fill(cut);
        cut.Find("[data-submit-balance]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-balance-error]").TextContent.Should()
            .Contain($"Maria Santos already has an opening balance for {ThisYear}."));
        cut.Find("[data-submit-balance]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ABalanceBeingSaved_CannotBeSentTwice()
    {
        var gate = _api.OnGated(HttpMethod.Post, BasePath);
        var cut = OpenNew();

        cut.Find("#ob-employee").Change(MariaId.ToString());
        Fill(cut);
        cut.Find("[data-submit-balance]").Click();
        cut.WaitForAssertion(() => CancelButton(cut).HasAttribute("disabled").Should().BeTrue());
        cut.Find("[data-submit-balance]").Click();

        gate.SetResult(Json(Balance(), HttpStatusCode.Created));
        cut.WaitForAssertion(() => cut.FindAll("[data-balance-dialog]").Should().BeEmpty());
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }

    [Fact]
    public async Task ASaveAnsweredAfterItsFormWasClosed_DoesNotCloseTheFormOnScreenNow()
    {
        var gate = _api.OnGated(HttpMethod.Post, BasePath);
        var cut = OpenNew(List(Balance(JoseBalanceId, JoseId, "Jose Cruz", "E-002")));

        cut.Find("#ob-employee").Change(MariaId.ToString());
        Fill(cut);
        cut.Find("[data-submit-balance]").Click();
        cut.WaitForAssertion(() => CancelButton(cut).HasAttribute("disabled").Should().BeTrue());
        CloseButton(cut).Click();
        Row(cut, JoseBalanceId).QuerySelector("[data-edit]")!.Click();

        gate.SetResult(Json(Balance(warnings: "Late warning"), HttpStatusCode.Created));
        await Task.Delay(100);

        cut.Find("[data-balance-dialog]").TextContent.Should().Contain("Jose Cruz");
        cut.FindAll("[data-balance-error]").Should().BeEmpty();
        CancelButton(cut).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task ASaveRefusedAfterItsFormWasClosed_ShowsTheRefusalAboveTheList()
    {
        var gate = _api.OnGated(HttpMethod.Post, BasePath);
        var cut = OpenNew();

        cut.Find("#ob-employee").Change(MariaId.ToString());
        Fill(cut);
        cut.Find("[data-submit-balance]").Click();
        cut.WaitForAssertion(() => CancelButton(cut).HasAttribute("disabled").Should().BeTrue());
        CloseButton(cut).Click();

        gate.SetResult(Problem($"Maria Santos already has an opening balance for {ThisYear}."));
        await Task.Delay(100);

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should()
            .Contain($"Maria Santos already has an opening balance for {ThisYear}."));
        cut.FindAll("[data-balance-dialog]").Should().BeEmpty();
    }

    /// <summary>Saves Maria's new balance, which answers with a warning, and returns the page with it shown.</summary>
    private IRenderedComponent<OpeningBalances> SavedWithAWarning()
    {
        _api.On(HttpMethod.Post, BasePath, HttpStatusCode.Created, Balance(warnings: "Check PAY-2026-007."));
        var cut = OpenNew();
        cut.Find("#ob-employee").Change(MariaId.ToString());
        Fill(cut);
        cut.Find("[data-submit-balance]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-save-warnings]").TextContent.Should().Contain("Check PAY-2026-007."));
        return cut;
    }

    [Fact]
    public void TheSaveWarnings_GoWhenTheYearChanges()
    {
        _api.On(HttpMethod.Get, ListPath(ThisYear - 1), HttpStatusCode.OK, List());
        var cut = SavedWithAWarning();

        cut.Find("#ob-year").Change((ThisYear - 1).ToString());

        cut.WaitForAssertion(() => cut.FindAll("[data-save-warnings]").Should().BeEmpty());
    }

    [Fact]
    public void TheSaveWarnings_GoWhenAnImportStarts()
    {
        var gate = _api.OnGated(HttpMethod.Post, $"{BasePath}/import");
        var cut = SavedWithAWarning();

        Choose(cut, InputFileContent.CreateFromText("EmployeeNumber,Year\n", "balances.csv"));
        cut.Find("[data-import]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-save-warnings]").Should().BeEmpty());
        gate.SetResult(Json("""{"created":1,"updated":0,"warnings":[]}"""));
    }

    // ---------- Editing and deleting ----------

    [Fact]
    public void Editing_StartsFromTheBalance_KeepsItsEmployeeAndYear_AndPutsTheNewFigures()
    {
        const string editWarning = "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.";
        _api.On(HttpMethod.Put, $"{BasePath}/{MariaBalanceId}", HttpStatusCode.OK, Balance(tax: 8000m, warnings: editWarning));
        var cut = RenderPage(List(Balance()));

        Row(cut, MariaBalanceId).QuerySelector("[data-edit]")!.Click();

        var dialog = cut.Find("[data-balance-dialog]");
        dialog.TextContent.Should().Contain("Maria Santos").And.Contain("E-001");
        cut.FindAll("#ob-employee").Should().BeEmpty("an opening balance's employee can't change");
        cut.Find("#ob-form-year").HasAttribute("disabled").Should().BeTrue("nor its year");
        cut.Find("#ob-through").GetAttribute("value").Should().Be($"{ThisYear}-03-31");
        cut.Find("#ob-basic").GetAttribute("value").Should().Be("90000");
        cut.Find("#ob-thirteenth").GetAttribute("value").Should().Be("1000.5");
        cut.Find("#ob-leave-days").GetAttribute("value").Should().Be("2.5");

        cut.Find("#ob-tax").Input("8000");
        cut.Find("[data-submit-balance]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-balance-dialog]").Should().BeEmpty());
        cut.FindAll("[data-save-warnings] li").Select(li => li.TextContent.Trim()).Should().Equal(editWarning);
        var body = BodyOf(HttpMethod.Put, $"{BasePath}/{MariaBalanceId}");
        body.GetProperty("employeeId").GetGuid().Should().Be(MariaId);
        body.GetProperty("year").GetInt32().Should().Be(ThisYear);
        body.GetProperty("taxWithheld").GetDecimal().Should().Be(8000m);
        body.GetProperty("basicSalary").GetDecimal().Should().Be(90000m);
    }

    [Fact]
    public void Deleting_AsksFirst_ThenDeletesAndReloads()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, ListPath(ThisYear), () => Json(++loads == 1 ? List(Balance()) : List()))
            .On(HttpMethod.Delete, $"{BasePath}/{MariaBalanceId}", HttpStatusCode.NoContent);
        var cut = RenderPage();

        Row(cut, MariaBalanceId).QuerySelector("[data-delete]")!.Click();
        cut.Find("[data-confirm-delete]").TextContent.Should().Contain("Maria Santos");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
        cut.Find("[data-confirm-delete]").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Delete").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain($"No opening balances for {ThisYear}."));
        cut.FindAll("[data-confirm-delete]").Should().BeEmpty();
        _api.Requests.Count(r => r.Method == HttpMethod.Delete).Should().Be(1);
    }

    [Fact]
    public void ARefusedDelete_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Delete, $"{BasePath}/{MariaBalanceId}", () => Problem("The balance could not be deleted."));
        var cut = RenderPage(List(Balance()));

        Row(cut, MariaBalanceId).QuerySelector("[data-delete]")!.Click();
        cut.Find("[data-confirm-delete]").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Delete").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should().Contain("The balance could not be deleted."));
    }

    // ---------- The template and the import ----------

    [Fact]
    public void TheTemplate_Downloads()
    {
        _api.On(HttpMethod.Get, $"{BasePath}/template",
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("EmployeeNumber,Year\r\n"u8.ToArray()) });
        var cut = RenderPage(List());

        cut.Find("[data-template]").TextContent.Trim().Should().Be("Download template");
        cut.Find("[data-template]").Click();

        cut.WaitForAssertion(() =>
        {
            var call = JSInterop.VerifyInvoke("downloadFileFromBytes");
            call.Arguments[0].Should().Be(Convert.ToBase64String("EmployeeNumber,Year\r\n"u8.ToArray()));
            call.Arguments[1].Should().Be("opening-balances-template.csv");
            call.Arguments[2].Should().Be("text/csv");
        });
    }

    [Fact]
    public void AFailedTemplateDownload_ShowsAboveTheList_NotAsAnImportProblem()
    {
        _api.On(HttpMethod.Get, $"{BasePath}/template", () => Problem("The template could not be made."));
        var cut = RenderPage(List());

        cut.Find("[data-template]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should().Contain("The template could not be made."));
        cut.FindAll("[data-import-errors]").Should().BeEmpty();
        cut.Markup.Should().NotContain("Nothing was imported.");
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }

    [Fact]
    public void TheImport_TellsExcelUsersToKeepTheDateAndNumberColumnsAsText()
    {
        var cut = RenderPage(List());

        cut.Find("[data-import-tip]").TextContent.Trim().Should().Be(
            "In Excel, format the date and employee-number columns as Text before typing or pasting into them, so they keep their format.");
        cut.Find("[data-import-file]").GetAttribute("accept").Should().Be(".csv");
    }

    private static void Choose(IRenderedComponent<OpeningBalances> cut, InputFileContent file) =>
        cut.FindComponent<InputFile>().UploadFiles(file);

    [Fact]
    public void AnImportedFile_ShowsTheCountsAndWarnings_AndReloadsTheList()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, ListPath(ThisYear), () => Json(++loads == 1 ? List() : List(Balance())))
            .On(HttpMethod.Post, $"{BasePath}/import", HttpStatusCode.OK,
                """{"created":3,"updated":2,"warnings":["E-001 Maria Santos: PAY-2026-007 used these figures."]}""");
        var cut = RenderPage();

        Choose(cut, InputFileContent.CreateFromText("EmployeeNumber,Year\n", "balances.csv"));
        cut.Find("[data-import]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-import-result]").TextContent.Should().Contain("3 created").And.Contain("2 updated"));
        cut.FindAll("[data-import-warnings] li").Select(li => li.TextContent.Trim()).Should().Equal(
            "E-001 Maria Santos: PAY-2026-007 used these figures.");
        cut.WaitForAssertion(() => Row(cut, MariaBalanceId).TextContent.Should().Contain("Maria Santos"));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)].Should().Contain("EmployeeNumber,Year");
    }

    [Fact]
    public void ARefusedImport_ListsEveryRowError()
    {
        _api.On(HttpMethod.Post, $"{BasePath}/import", HttpStatusCode.BadRequest,
            """{"title":"File not imported","status":400,"detail":"x","errors":["Row 3: Unknown employee number E-999.","Row 4: Enter a date as yyyy-MM-dd."]}""");
        var cut = RenderPage(List());

        Choose(cut, InputFileContent.CreateFromText("EmployeeNumber,Year\n", "balances.csv"));
        cut.Find("[data-import]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-import-errors] li").Select(li => li.TextContent.Trim()).Should().Equal(
            "Row 3: Unknown employee number E-999.", "Row 4: Enter a date as yyyy-MM-dd."));
        cut.FindAll("[data-import-result]").Should().BeEmpty();
    }

    [Fact]
    public void AnImportFailingForAnotherReason_ShowsIt()
    {
        _api.On(HttpMethod.Post, $"{BasePath}/import", HttpStatusCode.Forbidden);
        var cut = RenderPage(List());

        Choose(cut, InputFileContent.CreateFromText("EmployeeNumber,Year\n", "balances.csv"));
        cut.Find("[data-import]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-import-errors] li").Select(li => li.TextContent.Trim()).Should().Equal(
            "You do not have permission to do that."));
    }

    [Theory]
    [InlineData("balances.xlsx", 10)]
    [InlineData("balances.csv", 2 * 1024 * 1024 + 1)]
    public void AFileThatIsNotACsvOfAtMost2Mb_IsRefusedWithoutSendingIt(string name, int size)
    {
        var cut = RenderPage(List());

        Choose(cut, InputFileContent.CreateFromBinary(new byte[size], name));

        cut.WaitForAssertion(() => cut.Find("[data-import-errors]").TextContent.Trim().Should().Be("Choose a CSV file of at most 2 MB."));
        cut.Find("[data-import]").HasAttribute("disabled").Should().BeTrue();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void AFileOfExactly2Mb_IsAccepted()
    {
        _api.On(HttpMethod.Post, $"{BasePath}/import", HttpStatusCode.OK, """{"created":0,"updated":0,"warnings":[]}""");
        var cut = RenderPage(List());

        Choose(cut, InputFileContent.CreateFromBinary(new byte[2 * 1024 * 1024], "BALANCES.CSV"));
        cut.Find("[data-import]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-import-result]").TextContent.Should().Contain("0 created"));
    }

    [Fact]
    public void AnImportBeingSent_CannotBeSentTwice()
    {
        var gate = _api.OnGated(HttpMethod.Post, $"{BasePath}/import");
        var cut = RenderPage(List());

        Choose(cut, InputFileContent.CreateFromText("EmployeeNumber,Year\n", "balances.csv"));
        cut.Find("[data-import]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-import]").HasAttribute("disabled").Should().BeTrue());
        cut.Find("[data-import]").Click();

        gate.SetResult(Json("""{"created":1,"updated":0,"warnings":[]}"""));
        cut.WaitForAssertion(() => cut.Find("[data-import-result]").TextContent.Should().Contain("1 created"));
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }
}
