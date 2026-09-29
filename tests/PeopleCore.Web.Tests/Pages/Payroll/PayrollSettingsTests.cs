using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Payroll;

/// <summary>
/// The payroll settings page's maternity differential exemption. The API's PUT replaces the whole
/// settings record, so the rates the page doesn't show go back exactly as they were read.
/// </summary>
public class PayrollSettingsTests : BunitContext
{
    private static readonly Guid CompanyId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000001");

    private readonly StubHttpHandler _api = new();

    public PayrollSettingsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
    }

    // The row payroll computes from; the page never has to know which company it belongs to.
    private const string SettingsPath = "/api/payroll-settings/default";

    private static string Settings(bool exempt) => $$"""
        {"companyId":"{{CompanyId}}","philHealthRate":0.05,"philHealthMinShare":500,"philHealthMaxShare":5000,
         "pagIbigEmployeeRate":0.02,"pagIbigLowEmployeeRate":0.01,"pagIbigLowRateThreshold":1500,
         "pagIbigEmployerRate":0.02,"pagIbigMaxFundSalary":10000,"dailyRateFactor":261,
         "sssEmployeeRate":null,"sssEmployerRate":0.1,"exemptFromMaternityDifferential":{{(exempt ? "true" : "false")}}}
        """;

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private IRenderedComponent<PayrollSettings> RenderPage(bool exempt = false)
    {
        _api.On(HttpMethod.Get, SettingsPath, HttpStatusCode.OK, Settings(exempt));
        var cut = Render<PayrollSettings>();
        cut.WaitForAssertion(() => cut.FindAll("[data-exempt-switch]").Should().ContainSingle());
        return cut;
    }

    [Fact]
    public void ThePage_IsAtPayrollSettings_ForPayrollManagers()
    {
        typeof(PayrollSettings).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).Should().Equal("/payroll-settings");
        typeof(PayrollSettings).GetCustomAttribute<RequirePermissionAttribute>()!.Policy.Should().Be("permission:payroll.manage");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSwitch_ShowsTheStoredSetting_WithItsHint(bool exempt)
    {
        var cut = RenderPage(exempt);

        var toggle = cut.Find("[data-exempt-switch]");
        toggle.GetAttribute("role").Should().Be("switch");
        toggle.HasAttribute("aria-checked").Should().Be(exempt);
        toggle.ParentElement!.TextContent.Should().Contain("Exempt from the maternity salary differential");
        cut.Find("[data-exempt-hint]").TextContent.Trim().Should()
            .Be("For distressed, small (10 or fewer workers), micro (BMBE) or already-compliant employers under RA 11210.");
    }

    [Fact]
    public void Saving_PutsTheWholeRecord_WithOnlyTheSwitchChanged()
    {
        _api.On(HttpMethod.Put, SettingsPath, HttpStatusCode.NoContent);
        var cut = RenderPage(exempt: false);

        cut.Find("[data-exempt-switch]").Click();
        cut.Find("[data-save-settings]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-settings-result]").TextContent.Should().Contain("Saved"));
        var index = _api.Requests.FindIndex(r => r.Method == HttpMethod.Put);
        var sent = JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
        var read = JsonDocument.Parse(Settings(exempt: true)).RootElement;
        sent.EnumerateObject().Select(p => (p.Name, p.Value.GetRawText()))
            .Should().Equal(read.EnumerateObject().Select(p => (p.Name, p.Value.GetRawText())));
    }

    [Fact]
    public void ARefusedSave_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Put, SettingsPath, HttpStatusCode.BadRequest,
            """{"title":"Business rule violation","status":400,"detail":"Payroll settings could not be saved."}""");
        var cut = RenderPage();

        cut.Find("[data-exempt-switch]").Click();
        cut.Find("[data-save-settings]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-settings-error]").TextContent.Should().Contain("Payroll settings could not be saved."));
        cut.FindAll("[data-settings-result]").Should().BeEmpty();
    }

    [Fact]
    public void ASaveInFlight_CannotBeSentTwice()
    {
        var gate = _api.OnGated(HttpMethod.Put, SettingsPath);
        var cut = RenderPage();

        cut.Find("[data-save-settings]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-save-settings]").HasAttribute("disabled").Should().BeTrue());
        cut.Find("[data-exempt-switch]").HasAttribute("disabled").Should().BeTrue();

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        cut.WaitForAssertion(() => cut.Find("[data-save-settings]").HasAttribute("disabled").Should().BeFalse());
        _api.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(1);
    }

    [Fact]
    public void AFailedLoad_ShowsTheApisReason_AndOffersNoSwitch()
    {
        _api.On(HttpMethod.Get, SettingsPath, () => Json("""{"status":400,"detail":"Settings unavailable."}""", HttpStatusCode.BadRequest));

        var cut = Render<PayrollSettings>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Settings unavailable."));
        cut.FindAll("[data-exempt-switch]").Should().BeEmpty();
    }

    [Fact]
    public void ThePage_ReadsTheRowPayrollUses_WithoutGuessingACompany()
    {
        var cut = RenderPage();

        _api.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal(SettingsPath);
        cut.FindAll("[data-exempt-switch]").Should().ContainSingle();
    }
}
