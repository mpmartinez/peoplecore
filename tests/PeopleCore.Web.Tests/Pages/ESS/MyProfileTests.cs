using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.ESS;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.ESS;

public class MyProfileTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.Parse("9c4e1a7b-3d2f-4b8a-8e6c-0f5d2a9b7c44");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public MyProfileTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("ana@company.test");
    }

    private static string Employee(string? department, string? position, bool isActive) =>
        $$"""
        {"id":"{{EmployeeId}}","employeeNumber":"EMP-0107","firstName":"Ana","lastName":"Reyes","fullName":"Ana Reyes",
         "workEmail":"ana@company.test","departmentName":{{Quote(department)}},"positionTitle":{{Quote(position)}},
         "employmentStatus":"Probationary","isActive":{{(isActive ? "true" : "false")}}}
        """;

    private static string Quote(string? value) => value is null ? "null" : $"\"{value}\"";

    /// <summary>The value shown against each label, e.g. "Department" -> "Finance".</summary>
    private static Dictionary<string, string> Rows(IRenderedComponent<MyProfile> cut) =>
        cut.FindAll(".space-y-3 > div").ToDictionary(
            row => row.Children[0].TextContent.Trim(),
            row => row.Children[1].TextContent.Trim());

    private IRenderedComponent<MyProfile> RenderLinked(string employeeJson)
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, $"/api/employees/{EmployeeId}", HttpStatusCode.OK, employeeJson);

        var cut = Render<MyProfile>();
        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Loading..."));
        return cut;
    }

    [Fact]
    public void TheSignedInEmployeesOwnRecord_IsFetchedAndShown()
    {
        var cut = RenderLinked(Employee("Finance", "Accountant", isActive: true));

        _api.Requests.Should().ContainSingle().Which.RequestUri!.PathAndQuery.Should().Be($"/api/employees/{EmployeeId}");
        cut.Markup.Should().Contain("Ana Reyes").And.Contain("ana@company.test");
        cut.Find(".rounded-full").TextContent.Trim().Should().Be("AR");
        Rows(cut).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Employee #"] = "EMP-0107",
            ["Department"] = "Finance",
            ["Position"] = "Accountant",
            ["Employment Status"] = "Probationary",
            ["Account Status"] = "Active",
        });
    }

    [Fact]
    public void AnUnassignedInactiveEmployee_ShowsDashesAndInactive_RatherThanBlanks()
    {
        var cut = RenderLinked(Employee(department: null, position: null, isActive: false));

        var rows = Rows(cut);
        rows["Department"].Should().Be("--");
        rows["Position"].Should().Be("--");
        rows["Account Status"].Should().Be("Inactive");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public void AnAccountWithoutAUsableEmployeeLink_SaysSo_WithoutFetchingAnyRecord(string? claimValue)
    {
        if (claimValue is not null)
            _auth.SetClaims(new Claim("employee_id", claimValue));

        var cut = Render<MyProfile>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Profile not linked to an employee record."));
        _api.Requests.Should().BeEmpty();
    }
}
