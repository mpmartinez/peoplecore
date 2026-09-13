using System.Net;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages;

public class EmployeeCompensationTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    public EmployeeCompensationTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));

        _api.On(HttpMethod.Get, $"/api/employees/{EmployeeId}", HttpStatusCode.OK,
            $$"""
            {"id":"{{EmployeeId}}","employeeNumber":"EMP-0042","firstName":"Maria","lastName":"Santos",
             "fullName":"Maria Santos","workEmail":"maria@company.test","departmentName":"Finance",
             "positionTitle":"Accountant","employmentStatus":"Regular","isActive":true}
            """);
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private string CompensationPath => $"/api/employee-compensation/{EmployeeId}";

    private IRenderedComponent<EmployeeCompensation> RenderPage()
    {
        var cut = Render<EmployeeCompensation>(p => p.Add(x => x.EmployeeId, EmployeeId));
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement SaveButton(IRenderedComponent<EmployeeCompensation> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save");

    [Fact]
    public void AnEmployeeWithNoCompensationYet_GetsABlankFormWithDefaults()
    {
        _api.On(HttpMethod.Get, CompensationPath, HttpStatusCode.NotFound);

        var cut = RenderPage();

        cut.FindAll("[role=alert]").Should().BeEmpty();
        cut.Find("#basicSalary").GetAttribute("value").Should().Be("0");
        cut.Find("#payFrequency").GetAttribute("value").Should().Be("Monthly");
        cut.Find("#taxCode").GetAttribute("value").Should().Be("ME");
        cut.Markup.Should().Contain("Maria Santos (EMP-0042)");
    }

    [Fact]
    public void ExistingCompensation_IsLoadedIntoTheForm()
    {
        _api.On(HttpMethod.Get, CompensationPath, HttpStatusCode.OK,
            $$"""{"id":"{{Guid.NewGuid()}}","employeeId":"{{EmployeeId}}","basicSalary":35500.50,"payFrequency":"SemiMonthly","taxCode":"S","dependents":2}""");

        var cut = RenderPage();

        cut.Find("#basicSalary").GetAttribute("value").Should().Be("35500.50");
        cut.Find("#payFrequency").GetAttribute("value").Should().Be("SemiMonthly");
        cut.Find("#taxCode").GetAttribute("value").Should().Be("S");
        cut.Find("#dependents").GetAttribute("value").Should().Be("2");
    }

    [Fact]
    public void AFailedLoad_ShowsTheError_AndNoFormThatCouldOverwriteRealData()
    {
        _api.On(HttpMethod.Get, CompensationPath, HttpStatusCode.InternalServerError);

        var cut = RenderPage();

        cut.Find("[role=alert]").TextContent.Should().Contain("Failed to load compensation (500).");
        cut.FindAll("#basicSalary").Should().BeEmpty();
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save");
    }

    [Theory]
    [InlineData("0", "1", "Basic salary must be greater than zero.")]
    [InlineData("-100", "1", "Basic salary must be greater than zero.")]
    [InlineData("25000", "-1", "Dependents cannot be negative.")]
    public void InvalidInput_IsRefusedWithoutCallingTheApi(string salary, string dependents, string expectedError)
    {
        _api.On(HttpMethod.Get, CompensationPath, HttpStatusCode.NotFound);
        var cut = RenderPage();

        cut.Find("#basicSalary").Input(salary);
        cut.Find("#dependents").Input(dependents);
        SaveButton(cut).Click();

        cut.Find("[role=alert]").TextContent.Should().Contain(expectedError);
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void AValidSave_SendsTheFormAndReturnsToEmployees()
    {
        _api.On(HttpMethod.Get, CompensationPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Put, CompensationPath, HttpStatusCode.NoContent);
        var cut = RenderPage();

        cut.Find("#basicSalary").Input("42000.75");
        cut.Find("#payFrequency").Change("SemiMonthly");
        cut.Find("#taxCode").Input("ME1");
        cut.Find("#dependents").Input("1");
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => CurrentUri.Should().Be("http://localhost/employees"));
        var body = _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Put)];
        body.Should().Be("""{"basicSalary":42000.75,"payFrequency":"SemiMonthly","taxCode":"ME1","dependents":1}""");
    }

    [Fact]
    public void ARejectedSave_ShowsTheServersReason_AndKeepsTheUserOnThePage()
    {
        _api.On(HttpMethod.Get, CompensationPath, HttpStatusCode.NotFound)
            .On(HttpMethod.Put, CompensationPath, HttpStatusCode.BadRequest,
                """{"detail":"Tax code ME9 is not recognised."}""");
        var cut = RenderPage();
        var before = CurrentUri;

        cut.Find("#basicSalary").Input("30000");
        SaveButton(cut).Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Tax code ME9 is not recognised."));
        CurrentUri.Should().Be(before);
        SaveButton(cut).HasAttribute("disabled").Should().BeFalse();
    }
}
