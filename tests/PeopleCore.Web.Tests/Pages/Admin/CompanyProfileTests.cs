using System.Net;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

/// <summary>Editing the employer details that payslips and BIR Form 2316 print.</summary>
public class CompanyProfileTests : BunitContext
{
    private const string Blank = """{"name":"My Company","tin":"","rdoCode":null,"address":null,"city":"","zipCode":null,"contactEmail":null,"contactPhone":null,"sssNumber":"","philHealthNumber":"","pagIbigNumber":""}""";
    private const string Filled = """{"name":"Bayanihan Trading Corp.","tin":"123-456-789-00000","rdoCode":"043","address":"12 Rizal St.","city":"Makati","zipCode":"1200","contactEmail":null,"contactPhone":null,"sssNumber":"","philHealthNumber":"","pagIbigNumber":""}""";

    private readonly StubHttpHandler _api = new();

    public CompanyProfileTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@company.test");
        auth.SetClaims(SeededPermissions.ClaimsFor("Admin"));
    }

    [Fact]
    public void The_stored_details_fill_the_form()
    {
        _api.On(HttpMethod.Get, "/api/company-profile", HttpStatusCode.OK, Filled);

        var cut = Render<CompanyProfile>();

        cut.Find("#company-name").GetAttribute("value").Should().Be("Bayanihan Trading Corp.");
        cut.Find("#company-tin").GetAttribute("value").Should().Be("123-456-789-00000");
        cut.Find("#company-zip").GetAttribute("value").Should().Be("1200");
    }

    [Fact]
    public void Without_a_TIN_it_warns_that_the_2316_prints_blank()
    {
        _api.On(HttpMethod.Get, "/api/company-profile", HttpStatusCode.OK, Blank);

        Render<CompanyProfile>().Markup.Should().Contain("2316 prints blank");
    }

    [Fact]
    public void Saving_sends_what_was_typed()
    {
        _api.On(HttpMethod.Get, "/api/company-profile", HttpStatusCode.OK, Blank)
            .On(HttpMethod.Put, "/api/company-profile", HttpStatusCode.OK, Filled);

        var cut = Render<CompanyProfile>();
        cut.Find("#company-name").Input("Bayanihan Trading Corp.");
        cut.Find("#company-tin").Input("123-456-789-00000");
        cut.Find("#company-address").Input("12 Rizal St.");
        cut.Find("form[data-company-profile]").Submit();

        _api.RequestBodies.Last().Should().Contain("123-456-789-00000").And.Contain("12 Rizal St.");
        cut.Find("[data-company-result]").TextContent.Should().Contain("Saved");
    }

    [Fact]
    public void A_refused_save_shows_the_reason()
    {
        _api.On(HttpMethod.Get, "/api/company-profile", HttpStatusCode.OK, Blank)
            .On(HttpMethod.Put, "/api/company-profile", HttpStatusCode.BadRequest,
                """{"title":"Company not saved","detail":"Enter the ZIP code as 4 digits.","status":400}""");

        var cut = Render<CompanyProfile>();
        cut.Find("form[data-company-profile]").Submit();

        cut.Find("[data-company-error]").TextContent.Should().Contain("ZIP code");
    }
}
