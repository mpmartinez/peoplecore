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

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public void AProfileThatFailsToLoad_SaysSo_InsteadOfCrashingThePage(HttpStatusCode status)
    {
        // A 404 is what a claim pointing at a deleted employee record gets back.
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, $"/api/employees/{EmployeeId}", status);

        var cut = Render<MyProfile>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load your profile"));
        cut.Markup.Should().NotContain("Loading...").And.NotContain("Profile not linked");
    }

    // --- Change password ---------------------------------------------------------------------

    private const string ChangePasswordRoute = "/api/auth/change-password";

    /// <summary>An account with no employee record: the password form must not depend on one.</summary>
    private IRenderedComponent<MyProfile> RenderUnlinked()
    {
        var cut = Render<MyProfile>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Profile not linked"));
        return cut;
    }

    private static void FillAndSubmit(IRenderedComponent<MyProfile> cut, string current, string next, string confirm)
    {
        cut.Find("#current-password").Input(current);
        cut.Find("#new-password").Input(next);
        cut.Find("#confirm-password").Input(confirm);
        cut.Find("form#change-password").Submit();
    }

    private static string? FieldError(IRenderedComponent<MyProfile> cut, string inputId) =>
        cut.Find($"#{inputId}").Closest(".space-y-2")!.QuerySelector("p.text-destructive")?.TextContent.Trim();

    private List<string?> ChangePasswordBodies() =>
        _api.Requests.Select((r, i) => (r, i))
            .Where(x => x.r.RequestUri!.AbsolutePath == ChangePasswordRoute)
            .Select(x => _api.RequestBodies[x.i]).ToList();

    [Fact]
    public void EveryPasswordField_OnTheForm_HasAShowPasswordToggle()
    {
        var cut = RenderUnlinked();

        foreach (var id in new[] { "current-password", "new-password", "confirm-password" })
        {
            cut.Find($"#{id}").GetAttribute("type").Should().Be("password");
            cut.Find($"[data-password-toggle][aria-controls={id}]").Click();
            cut.Find($"#{id}").GetAttribute("type").Should().Be("text", $"the toggle beside #{id} reveals it");
        }
    }

    [Fact]
    public void AValidChange_IsSentToTheApi_ConfirmedAndTheFieldsCleared()
    {
        _api.On(HttpMethod.Post, ChangePasswordRoute, HttpStatusCode.NoContent);
        var cut = RenderUnlinked();

        FillAndSubmit(cut, "OldPassw0rd", "NewPassw0rd", "NewPassw0rd");

        cut.WaitForAssertion(() =>
            cut.Find("[data-password-result]").TextContent.Should().Contain("Your password has been changed."));
        ChangePasswordBodies().Should().ContainSingle()
            .Which.Should().Be("""{"currentPassword":"OldPassw0rd","newPassword":"NewPassw0rd"}""");
        foreach (var id in new[] { "current-password", "new-password", "confirm-password" })
            cut.Find($"#{id}").GetAttribute("value").Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "NewPassw0rd", "NewPassw0rd", "current-password", "Enter your current password.")]
    [InlineData("OldPassw0rd", "", "", "new-password", "Enter a new password.")]
    [InlineData("OldPassw0rd", "Sh0rt", "Sh0rt", "new-password", "Use at least 8 characters.")]
    [InlineData("OldPassw0rd", "NoDigitsHere", "NoDigitsHere", "new-password", "Include at least one number.")]
    [InlineData("OldPassw0rd", "OldPassw0rd", "OldPassw0rd", "new-password", "Choose a password different from your current one.")]
    [InlineData("OldPassw0rd", "NewPassw0rd", "NewPassw0rdd", "confirm-password", "The passwords don't match.")]
    public void AnInvalidForm_SaysWhatIsWrong_BesideTheField_WithoutCallingTheApi(
        string current, string next, string confirm, string field, string message)
    {
        var cut = RenderUnlinked();

        FillAndSubmit(cut, current, next, confirm);

        FieldError(cut, field).Should().Be(message);
        ChangePasswordBodies().Should().BeEmpty();
        cut.FindAll("[data-password-result]").Should().BeEmpty();
    }

    [Fact]
    public void ARejectedChange_ShowsTheApisReason_AndKeepsWhatWasTyped()
    {
        _api.On(HttpMethod.Post, ChangePasswordRoute, HttpStatusCode.BadRequest,
            """{"title":"Password not changed","detail":"Your current password is incorrect.","status":400}""");
        var cut = RenderUnlinked();

        FillAndSubmit(cut, "WrongPassw0rd", "NewPassw0rd", "NewPassw0rd");

        cut.WaitForAssertion(() =>
            cut.Find("[data-password-result]").TextContent.Should().Contain("Your current password is incorrect."));
        cut.Markup.Should().NotContain("Your password has been changed.");
        cut.Find("#new-password").GetAttribute("value").Should().Be("NewPassw0rd");
        cut.Find("button[type=submit]").HasAttribute("disabled").Should().BeFalse("the user has to be able to try again");
    }

    [Fact]
    public void TheChangePasswordForm_IsAvailable_AlongsideALinkedEmployeeRecord()
    {
        var cut = RenderLinked(Employee("Finance", "Accountant", isActive: true));

        cut.Markup.Should().Contain("Ana Reyes");
        cut.FindAll("form#change-password").Should().ContainSingle();
    }
}
