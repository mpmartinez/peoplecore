using System.Net;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;
using static PeopleCore.Web.Tests.Pages.Payroll.PayrollTestData;

namespace PeopleCore.Web.Tests.Pages.Payroll;

public class Bir2316Tests : BunitContext
{
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");
    private static readonly Guid JoseId = Guid.Parse("0c6f9a3e-8b2d-4f71-a5c4-3e9d1b7f2a60");

    private readonly StubHttpHandler _api = new();

    public Bir2316Tests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true);
    }

    private static string YearsPath(Guid employeeId) => $"/api/reports/2316/years/{employeeId}";

    private static string PreviewPath(Guid employeeId, int year) => $"/api/reports/2316/preview/{employeeId}?year={year}";

    private static string Preview(int year) =>
        $$"""
        {"year":{{year}},"periodFrom":"01/01/{{year}}","periodTo":"12/31/{{year}}","employeeTin":"123-456-789-000",
         "employeeLastName":"Santos","employeeFirstName":"Maria","employeeMiddleName":"Cruz","rdoCode":"050"}
        """;

    private IRenderedComponent<Bir2316> RenderPage()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.OK,
            EmployeePage(1, 1, Employee(MariaId, "EMP-0042", "Maria", "Santos"), Employee(JoseId, "EMP-0043", "Jose", "Reyes")));
        var cut = Render<Bir2316>();
        cut.WaitForAssertion(() => cut.FindAll("#employee").Should().ContainSingle());
        return cut;
    }

    /// <summary>Renders the page with Maria selected and her 2025 preview on screen.</summary>
    private IRenderedComponent<Bir2316> RenderWithMariasPreview()
    {
        _api.On(HttpMethod.Get, YearsPath(MariaId), HttpStatusCode.OK, "[2025,2024]")
            .On(HttpMethod.Get, PreviewPath(MariaId, 2025), HttpStatusCode.OK, Preview(2025))
            .On(HttpMethod.Get, PreviewPath(MariaId, 2024), HttpStatusCode.OK, Preview(2024));
        var cut = RenderPage();
        cut.Find("#employee").Change(MariaId.ToString());
        cut.WaitForAssertion(() => cut.FindAll("#prevTin").Should().ContainSingle());
        return cut;
    }

    private static IElement Button(IRenderedComponent<Bir2316> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith(text));

    [Fact]
    public void ThePicker_OffersEveryEmployeeAcrossPages_SortedByLastName()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.OK,
                EmployeePage(1, 2, Employee(MariaId, "EMP-0042", "Maria", "Santos")))
            .On(HttpMethod.Get, "/api/employees?page=2&pageSize=100", HttpStatusCode.OK,
                EmployeePage(2, 2, Employee(JoseId, "EMP-0043", "Jose", "Reyes")));

        var cut = Render<Bir2316>();

        cut.WaitForAssertion(() => cut.FindAll("#employee option").Select(o => o.TextContent).Should()
            .Equal("Select an employee", "Jose Reyes (EMP-0043)", "Maria Santos (EMP-0042)"));
        cut.Markup.Should().Contain("Select an employee first.");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Download all"));
    }

    [Fact]
    public void AFailedEmployeeLoad_ShowsTheError()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.InternalServerError);

        var cut = Render<Bir2316>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("500"));
    }

    [Fact]
    public void ChoosingAnEmployee_LoadsTheirMostRecentYearsPreview()
    {
        var cut = RenderWithMariasPreview();

        cut.FindAll("#year option").Select(o => o.TextContent).Should().Equal("2025", "2024");
        cut.Markup.Should().Contain("123-456-789-000").And.Contain("Santos, Maria Cruz");
        Button(cut, "Download all for 2025");
        _api.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery == PreviewPath(MariaId, 2024));
    }

    [Fact]
    public void AnEmployeeWithNoPaidRuns_SaysSo_AndOffersNoForm()
    {
        _api.On(HttpMethod.Get, YearsPath(JoseId), HttpStatusCode.OK, "[]");
        var cut = RenderPage();

        cut.Find("#employee").Change(JoseId.ToString());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No paid runs for this employee."));
        cut.FindAll("#prevTin").Should().BeEmpty();
        cut.FindAll("#year").Should().BeEmpty();
    }

    [Fact]
    public void AYearWithNoPreview_SaysSoRatherThanShowingAnError()
    {
        _api.On(HttpMethod.Get, YearsPath(JoseId), HttpStatusCode.OK, "[2025]")
            .On(HttpMethod.Get, PreviewPath(JoseId, 2025), HttpStatusCode.NotFound);
        var cut = RenderPage();

        cut.Find("#employee").Change(JoseId.ToString());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No paid runs found for this employee in 2025."));
        cut.FindAll("[role=alert]").Should().BeEmpty();
        cut.FindAll("#prevTin").Should().BeEmpty();
    }

    [Fact]
    public void APreviewErrorForOneEmployee_DoesNotLingerOverTheNext()
    {
        // The next employee has no paid runs, so no year gets selected: only the employee change
        // itself is left to clear the previous employee's error.
        _api.On(HttpMethod.Get, YearsPath(JoseId), HttpStatusCode.OK, "[2025]")
            .On(HttpMethod.Get, PreviewPath(JoseId, 2025), HttpStatusCode.InternalServerError)
            .On(HttpMethod.Get, YearsPath(MariaId), HttpStatusCode.OK, "[]");
        var cut = RenderPage();

        cut.Find("#employee").Change(JoseId.ToString());
        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Failed to load 2316 preview (500)."));

        cut.Find("#employee").Change(MariaId.ToString());

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No paid runs for this employee."));
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Generate_SubmitsOnlyTheManualInputs_AndDownloadsTheCertificate()
    {
        _api.On(HttpMethod.Post, $"/api/reports/2316/generate/{MariaId}?year=2025", Pdf);
        var cut = RenderWithMariasPreview();

        cut.Find("#prevTin").Input("987-654-321-000");
        cut.Find("#prevName").Input("Acme Trading");
        cut.Find("#item22").Input("150000.50");
        cut.Find("#item25b").Input("12000");
        Button(cut, "Generate").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "BIR2316-2025-SantosMaria.pdf", "application/pdf"));
        // Not one derived figure: the server recomputes those, and anything else sent here is a
        // tax-certificate value the operator could have tampered with.
        var body = _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)];
        body.Should().Be(
            """{"prevEmployerTin":"987-654-321-000","prevEmployerName":"Acme Trading","prevEmployerAddress":null,"prevEmployerZipCode":null,"item22_PrevTaxableCompensation":150000.50,"item25B_PrevTaxWithheld":12000,"item27_PeraTaxCredit":0,"item35_DeMinimis":0,"item33_HazardPayMwe":0}""");
    }

    [Fact]
    public void SwitchingYears_ClearsTheManualInputs()
    {
        // A previous employer is a fact about one tax year; carrying it into another year's
        // certificate would misstate that year's taxable compensation.
        var cut = RenderWithMariasPreview();
        cut.Find("#prevTin").Input("987-654-321-000");
        cut.Find("#item22").Input("150000.50");

        cut.Find("#year").Change("2024");

        cut.WaitForAssertion(() => Button(cut, "Download all for 2024"));
        cut.Find("#prevTin").GetAttribute("value").Should().BeEmpty();
        cut.Find("#item22").GetAttribute("value").Should().Be("0");
    }

    [Fact]
    public void AFailedGenerate_ShowsAnInlineError_AndSavesNothing()
    {
        _api.On(HttpMethod.Post, $"/api/reports/2316/generate/{MariaId}?year=2025", HttpStatusCode.InternalServerError);
        var cut = RenderWithMariasPreview();

        Button(cut, "Generate").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Failed to generate the 2316. Please try again."));
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }

    [Fact]
    public void DownloadAll_SavesEveryCertificateForTheYear()
    {
        _api.On(HttpMethod.Post, "/api/reports/2316/generate-all?year=2025", Pdf);
        var cut = RenderWithMariasPreview();

        Button(cut, "Download all for 2025").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "BIR2316-2025-All.pdf", "application/pdf"));
    }

    [Fact]
    public void DownloadAllForAYearWithNoPaidRuns_SaysThereIsNothingToDownload()
    {
        // A 404 is a legitimate empty year; telling the user to retry would be telling them to
        // repeat something that cannot change.
        _api.On(HttpMethod.Post, "/api/reports/2316/generate-all?year=2025", HttpStatusCode.NotFound);
        var cut = RenderWithMariasPreview();

        Button(cut, "Download all for 2025").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("No employee had a paid run in 2025. Nothing to download."));
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }
}
