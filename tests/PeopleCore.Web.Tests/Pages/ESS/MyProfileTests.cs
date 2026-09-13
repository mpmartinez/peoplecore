using System.Net;
using System.Security.Claims;
using System.Text;
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

    private const string ProfileRoute = "/api/profile";

    // Read when the request arrives, so a test can change what GET api/profile answers before rendering.
    private HttpStatusCode _profileStatus = HttpStatusCode.OK;
    private string _profileJson = """{"firstName":"Ana","lastName":"Reyes","email":"ana@company.test"}""";

    public MyProfileTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("ana@company.test");
        _api.On(HttpMethod.Get, ProfileRoute, () => new HttpResponseMessage(_profileStatus)
        {
            Content = new StringContent(_profileJson, Encoding.UTF8, "application/json")
        });
    }

    private List<string> RequestedPaths() => _api.Requests.Select(r => r.RequestUri!.AbsolutePath).ToList();

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

        RequestedPaths().Should().BeEquivalentTo([ProfileRoute, $"/api/employees/{EmployeeId}"]);
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

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No employee record is linked to this account."));
        RequestedPaths().Should().Equal(ProfileRoute);
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
        cut.Markup.Should().NotContain("No employee record is linked");
    }

    // --- Change password ---------------------------------------------------------------------

    private const string ChangePasswordRoute = "/api/auth/change-password";

    /// <summary>An account with no employee record: the password form must not depend on one.</summary>
    private IRenderedComponent<MyProfile> RenderUnlinked()
    {
        var cut = Render<MyProfile>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No employee record is linked"));
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

    // --- Account details ---------------------------------------------------------------------

    private IRenderedComponent<MyProfile> RenderAccount()
    {
        var cut = Render<MyProfile>();
        cut.WaitForAssertion(() => cut.FindAll("form#account-details").Should().ContainSingle());
        return cut;
    }

    private string? ProfileUpdateBody() =>
        _api.Requests.Select((r, i) => (r, i))
            .Where(x => x.r.Method == HttpMethod.Put && x.r.RequestUri!.AbsolutePath == ProfileRoute)
            .Select(x => _api.RequestBodies[x.i]).SingleOrDefault();

    [Fact]
    public void EveryAccount_SeesItsFirstNameLastNameAndEmail_EvenWithoutAnEmployeeRecord()
    {
        var cut = RenderAccount();

        cut.Find("#first-name").GetAttribute("value").Should().Be("Ana");
        cut.Find("#last-name").GetAttribute("value").Should().Be("Reyes");
        cut.Find("[data-account-email]").TextContent.Should().Contain("Email").And.Contain("ana@company.test");
        cut.Markup.Should().Contain("No employee record is linked to this account.");
    }

    [Fact]
    public void TheEmail_IsShownButCannotBeEdited()
    {
        var cut = RenderAccount();

        cut.Find("[data-account-email]").QuerySelectorAll("input").Should().BeEmpty();
    }

    [Fact]
    public void AnAccountThatHasNoNamesYet_OpensWithBlankNameFields()
    {
        _profileJson = """{"firstName":null,"lastName":null,"email":"admin@peoplecore.local"}""";

        var cut = RenderAccount();

        cut.Find("#first-name").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find("#last-name").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find("[data-account-email]").TextContent.Should().Contain("admin@peoplecore.local");
    }

    [Fact]
    public void SavingNames_SendsOnlyTheTrimmedNames_AndShowsWhatTheApiSaved()
    {
        _api.On(HttpMethod.Put, ProfileRoute, HttpStatusCode.OK,
            """{"firstName":"Annie","lastName":"Reyes-Cruz","email":"ana@company.test"}""");
        var cut = RenderAccount();

        cut.Find("#first-name").Input("  Annie ");
        cut.Find("#last-name").Input("Reyes-Cruz ");
        cut.Find("form#account-details").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[data-account-result]").TextContent.Should().Contain("Your details have been saved."));
        ProfileUpdateBody().Should().Be("""{"firstName":"Annie","lastName":"Reyes-Cruz"}""");
        cut.Find("#first-name").GetAttribute("value").Should().Be("Annie");
        cut.Find("#last-name").GetAttribute("value").Should().Be("Reyes-Cruz");
    }

    [Theory]
    [InlineData("", "Reyes", "first-name", "Enter your first name.")]
    [InlineData("Ana", "   ", "last-name", "Enter your last name.")]
    public void AMissingName_SaysSoBesideTheField_WithoutCallingTheApi(string first, string last, string field, string message)
    {
        var cut = RenderAccount();

        cut.Find("#first-name").Input(first);
        cut.Find("#last-name").Input(last);
        cut.Find("form#account-details").Submit();

        FieldError(cut, field).Should().Be(message);
        ProfileUpdateBody().Should().BeNull();
    }

    [Fact]
    public void ARejectedSave_ShowsTheApisReason_AndKeepsWhatWasTyped()
    {
        _api.On(HttpMethod.Put, ProfileRoute, HttpStatusCode.BadRequest,
            """{"title":"Profile not saved","detail":"First name must be 100 characters or fewer.","status":400}""");
        var cut = RenderAccount();

        cut.Find("#first-name").Input("Annie");
        cut.Find("form#account-details").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[data-account-result]").TextContent.Should().Contain("First name must be 100 characters or fewer."));
        cut.Find("#first-name").GetAttribute("value").Should().Be("Annie");
        cut.Find("form#account-details button[type=submit]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AccountDetailsThatFailToLoad_SaySo_WithoutHidingTheRestOfThePage()
    {
        _profileStatus = HttpStatusCode.InternalServerError;
        _profileJson = "{}";

        var cut = Render<MyProfile>();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load your account details"));
        cut.FindAll("form#account-details").Should().BeEmpty();
        cut.FindAll("form#change-password").Should().ContainSingle();
        cut.Markup.Should().Contain("No employee record is linked to this account.");
    }
}
