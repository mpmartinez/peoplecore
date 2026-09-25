using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

public class EmployeesTests : BunitContext
{
    private const string FirstPagePath = "/api/employees?page=1&pageSize=20";
    private static readonly Guid FinanceId = Guid.Parse("3f7a9c1e-2b4d-4e6f-8a0b-1c2d3e4f5a6b");
    private static readonly Guid LegalId = Guid.Parse("9e8d7c6b-5a49-4382-a716-f5e4d3c2b1a0");
    private static readonly Guid AccountantId = Guid.Parse("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid CounselId = Guid.Parse("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");
    private static readonly Guid JuanId = Guid.Parse("c3d4e5f6-a7b8-4c9d-8e0f-2a3b4c5d6e7f");

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    // Read when the request arrives, so a test can say which employees have logins before rendering.
    private string _linksJson = "[]";

    public EmployeesTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true).SetVoidResult();
        _auth = AddAuthorization();
        _auth.SetAuthorized("hr@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));

        _api.On(HttpMethod.Get, "/api/departments?page=1&pageSize=100", HttpStatusCode.OK,
                Paged(1, Department(FinanceId, "Finance"), Department(LegalId, "Legal")))
            .On(HttpMethod.Get, $"/api/positions?page=1&pageSize=100&departmentId={FinanceId}", HttpStatusCode.OK,
                Paged(1, Position(AccountantId, FinanceId, "Accountant")))
            .On(HttpMethod.Get, $"/api/positions?page=1&pageSize=100&departmentId={LegalId}", HttpStatusCode.OK,
                Paged(1, Position(CounselId, LegalId, "Counsel")))
            .On(HttpMethod.Get, "/api/users/employee-links", () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_linksJson, Encoding.UTF8, "application/json")
            })
            .On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK,
                """[{"name":"Employee","grantable":true,"reason":null},{"name":"Manager","grantable":true,"reason":null},{"name":"PayrollService","grantable":true,"reason":null}]""");
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static string Paged(int totalPages, params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":{{totalPages}}}""";

    private static string Employee(string number, string name, bool active = true, string? department = null, string? position = null, Guid? id = null) =>
        $$"""
        {"id":"{{id ?? Guid.NewGuid()}}","employeeNumber":"{{number}}","firstName":"{{name.Split(' ')[0]}}","lastName":"{{name.Split(' ')[1]}}",
         "fullName":"{{name}}","workEmail":"{{name.Split(' ')[0].ToLowerInvariant()}}@company.test",
         "departmentName":{{(department is null ? "null" : $"\"{department}\"")}},"positionTitle":{{(position is null ? "null" : $"\"{position}\"")}},
         "employmentStatus":"Regular","isActive":{{(active ? "true" : "false")}}}
        """;

    private static string Department(Guid id, string name) =>
        $$"""{"id":"{{id}}","companyId":"{{Guid.NewGuid()}}","parentDepartmentId":null,"parentDepartmentName":null,"name":"{{name}}","code":null,"subDepartmentCount":0}""";

    private static string Position(Guid id, Guid departmentId, string title) =>
        $$"""{"id":"{{id}}","departmentId":"{{departmentId}}","departmentName":"x","title":"{{title}}","level":null}""";

    private IRenderedComponent<Employees> RenderPage(string firstPage)
    {
        _api.On(HttpMethod.Get, FirstPagePath, HttpStatusCode.OK, firstPage);
        var cut = Render<Employees>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static List<string[]> Rows(IRenderedComponent<Employees> cut) =>
        cut.FindAll("tbody tr")
            .Select(r => r.QuerySelectorAll("td")
                .Select(td => string.Join(' ', td.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                .ToArray())
            .ToList();

    private static IElement ButtonNamed(IRenderedComponent<Employees> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    // Several of the form's text inputs carry no id of their own; the label pointing at them is
    // the stable handle.
    private static IElement FieldLabelled(IRenderedComponent<Employees> cut, string labelFor) =>
        cut.Find($"label[for={labelFor}]").ParentElement!.QuerySelector("input, select")!;

    private static IElement Pager(IRenderedComponent<Employees> cut) => cut.Find("nav:not([aria-label=breadcrumb])");

    private static void OpenForm(IRenderedComponent<Employees> cut) => ButtonNamed(cut, "Add Employee").Click();

    private static void FillRequiredFields(IRenderedComponent<Employees> cut, string? except = null)
    {
        if (except != "empNumber") FieldLabelled(cut, "empNumber").Input("EMP-0100");
        if (except != "firstName") FieldLabelled(cut, "firstName").Input("Ana");
        if (except != "lastName") FieldLabelled(cut, "lastName").Input("Reyes");
        if (except != "workEmail") FieldLabelled(cut, "workEmail").Input("ana@company.test");
        if (except != "gender") cut.Find("#gender").Change("Female");
        if (except != "empStatus") cut.Find("#empStatus").Change("Probationary");
        if (except != "empType") cut.Find("#empType").Change("Regular");
    }

    private List<string> EmployeeListRequests =>
        _api.Requests.Select(r => r.RequestUri!.PathAndQuery).Where(p => p.StartsWith("/api/employees?")).ToList();

    [Fact]
    public void ListsEmployees_WithTheirStatus_AndDashesWhereNoDepartmentOrPositionIsSet()
    {
        var cut = RenderPage(Paged(1,
            Employee("EMP-0042", "Maria Santos", department: "Finance", position: "Accountant"),
            Employee("EMP-0007", "Jose Rizal", active: false)));

        Rows(cut).Select(r => r[..6]).Should().BeEquivalentTo(
            [
                new[] { "EMP-0042", "Maria Santos maria@company.test", "Finance", "Accountant", "Active", "Regular" },
                new[] { "EMP-0007", "Jose Rizal jose@company.test", "--", "--", "Inactive", "Regular" }
            ],
            o => o.WithStrictOrdering());
        cut.FindAll("nav:not([aria-label=breadcrumb])").Should().BeEmpty("a single page needs no pager");
    }

    [Fact]
    public void NoEmployees_ShowsTheEmptyState()
    {
        var cut = RenderPage(Paged(1));

        cut.Markup.Should().Contain("No employees found.");
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void ChoosingAnotherPage_AsksTheApiForThatPage()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=2&pageSize=20", HttpStatusCode.OK,
            Paged(3, Employee("EMP-0021", "Andres Bonifacio")));
        var cut = RenderPage(Paged(3, Employee("EMP-0001", "Maria Santos")));

        ButtonNamed(cut, "2").Click();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("EMP-0021"));
        EmployeeListRequests.Should().Equal(FirstPagePath, "/api/employees?page=2&pageSize=20");
        Pager(cut).TextContent.Should().Contain("Page 2 of 3");
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HRManager", true)]
    [InlineData("PayrollService", true)]
    [InlineData("Manager", false)]
    public void TheCompensationLink_IsOnlyOfferedToRolesThatManagePay(string role, bool offered)
    {
        _auth.SetClaims(SeededPermissions.ClaimsFor(role));

        var cut = RenderPage(Paged(1, Employee("EMP-0042", "Maria Santos", id: MariaId)));

        cut.FindAll("button").Any(b => b.TextContent.Trim() == "Compensation").Should().Be(offered);
    }

    [Fact]
    public void TheCompensationLink_OpensThatEmployeesCompensation()
    {
        var cut = RenderPage(Paged(1, Employee("EMP-0042", "Maria Santos", id: MariaId)));

        ButtonNamed(cut, "Compensation").Click();

        CurrentUri.Should().Be($"http://localhost/employees/{MariaId}/compensation");
    }

    private static IElement RowNamed(IRenderedComponent<Employees> cut, string name) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(name));

    private static List<string> ButtonsIn(IElement row) =>
        row.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

    [Fact]
    public void EachEmployee_OffersToCreateALogin_OrOpensTheOneTheyHave()
    {
        _linksJson = $$"""[{"employeeId":"{{MariaId}}","userId":"u-1","isActive":true}]""";

        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId), Employee("EMP-2", "Juan Cruz", id: JuanId)));

        ButtonsIn(RowNamed(cut, "Maria Santos")).Should().Contain("Account").And.NotContain("Create login");
        ButtonsIn(RowNamed(cut, "Juan Cruz")).Should().Contain("Create login").And.NotContain("Account");
    }

    [Fact]
    public void Account_OpensTheUsersPage_SearchingForTheirEmail()
    {
        _linksJson = $$"""[{"employeeId":"{{MariaId}}","userId":"u-1","isActive":true}]""";
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Account").Click();

        CurrentUri.Should().EndWith("/admin/users?search=maria%40company.test");
    }

    [Fact]
    public void CreateLogin_StartsFromTheEmployee_AndShowsTheTemporaryPasswordOnce()
    {
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
            $$"""
            {"account":{"id":"u-9","email":"juan@company.test","firstName":"Juan","lastName":"Cruz","roles":["Employee"],
              "isActive":true,"mustChangePassword":true,"employeeId":"{{JuanId}}","employeeName":"Juan Cruz","canManage":true},
             "temporaryPassword":"Kx7mPq2RtW9zNb4s"}
            """);
        var cut = RenderPage(Paged(1, Employee("EMP-2", "Juan Cruz", id: JuanId)));

        RowNamed(cut, "Juan Cruz").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Create login").Click();
        cut.WaitForAssertion(() => cut.Find("#account-email").GetAttribute("value").Should().Be("juan@company.test"));
        cut.Find("#account-first-name").GetAttribute("value").Should().Be("Juan");
        cut.Find("#account-last-name").GetAttribute("value").Should().Be("Cruz");
        cut.Find("[data-selected-employee]").TextContent.Should().Contain("Juan Cruz");

        cut.Find("form[data-create-account]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-temporary-password]").TextContent.Trim().Should().Be("Kx7mPq2RtW9zNb4s"));
        var postIndex = _api.Requests.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/users");
        System.Text.Json.JsonDocument.Parse(_api.RequestBodies[postIndex]!).RootElement
            .GetProperty("employeeId").GetGuid().Should().Be(JuanId);
        _api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/users/employee-links").Should().Be(2,
            "the list learns that Juan now has a login");
    }

    [Theory]
    [InlineData("empNumber")]
    [InlineData("firstName")]
    [InlineData("lastName")]
    [InlineData("workEmail")]
    [InlineData("gender")]
    [InlineData("empStatus")]
    [InlineData("empType")]
    public void AMissingRequiredField_IsRefusedWithoutCallingTheApi(string missing)
    {
        var cut = RenderPage(Paged(1));
        OpenForm(cut);

        FillRequiredFields(cut, except: missing);
        ButtonNamed(cut, "Save Employee").Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("Please fill in all required fields.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void ChangingTheDepartment_OffersOnlyItsPositions_AndDropsThePositionAlreadyPicked()
    {
        // A position left over from the previous department would put the employee in a job
        // their department does not have.
        var cut = RenderPage(Paged(1));
        OpenForm(cut);

        cut.Find("#department").Change(FinanceId.ToString());
        cut.WaitForAssertion(() => cut.FindAll("#position option").Select(o => o.TextContent).Should().Equal("None", "Accountant"));
        cut.Find("#position").Change(AccountantId.ToString());

        cut.Find("#department").Change(LegalId.ToString());

        cut.WaitForAssertion(() => cut.FindAll("#position option").Select(o => o.TextContent).Should().Equal("None", "Counsel"));
        cut.Find("#position").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void AValidEmployee_IsCreated_ThenTheFormClosesAndTheListReloadsFromPageOne()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=2&pageSize=20", HttpStatusCode.OK, Paged(2, Employee("EMP-0021", "Andres Bonifacio")))
            .On(HttpMethod.Post, "/api/employees", HttpStatusCode.Created, Employee("EMP-0100", "Ana Reyes"));
        var cut = RenderPage(Paged(2, Employee("EMP-0001", "Maria Santos")));
        ButtonNamed(cut, "2").Click();
        cut.WaitForAssertion(() => Pager(cut).TextContent.Should().Contain("Page 2 of 2"));

        OpenForm(cut);
        FillRequiredFields(cut);
        FieldLabelled(cut, "mobileNumber").Input("09171234567");
        cut.Find("#dob").Input("1992-03-14");
        cut.Find("#hireDate").Input("2026-09-01");
        cut.Find("#department").Change(FinanceId.ToString());
        cut.WaitForAssertion(() => cut.FindAll("#position option").Should().HaveCount(2));
        cut.Find("#position").Change(AccountantId.ToString());
        cut.Find("#soloParentIdNumber").Input("SP-2026-0001");
        cut.Find("#soloParentIdValidUntil").Input("2027-06-30");
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Employee"));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)].Should().Be(
            $$"""{"employeeNumber":"EMP-0100","firstName":"Ana","middleName":null,"lastName":"Reyes","dateOfBirth":"1992-03-14","gender":"Female","workEmail":"ana@company.test","mobileNumber":"09171234567","departmentId":"{{FinanceId}}","positionId":"{{AccountantId}}","reportingManagerId":null,"employmentStatus":"Probationary","employmentType":"Regular","hireDate":"2026-09-01","soloParentIdNumber":"SP-2026-0001","soloParentIdValidUntil":"2027-06-30"}""");
        EmployeeListRequests.Last().Should().Be(FirstPagePath, "the new employee has to be findable, not hidden on the page the user was on");
        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("EMP-0001"));
    }

    [Fact]
    public void ARejectedSave_KeepsTheFormOpenWithAnError_AndDoesNotReload()
    {
        _api.On(HttpMethod.Post, "/api/employees", HttpStatusCode.BadRequest, """{"detail":"Employee number already exists."}""");
        var cut = RenderPage(Paged(1));
        OpenForm(cut);

        FillRequiredFields(cut);
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Failed to save employee. Employee number already exists."));
        FieldLabelled(cut, "empNumber").GetAttribute("value").Should().Be("EMP-0100", "what the user typed must survive the failure");
        ButtonNamed(cut, "Save Employee").HasAttribute("disabled").Should().BeFalse();
        EmployeeListRequests.Should().ContainSingle();
    }

    [Fact]
    public void ReopeningTheForm_StartsBlank()
    {
        var cut = RenderPage(Paged(1));
        OpenForm(cut);
        FieldLabelled(cut, "firstName").Input("Ana");
        ButtonNamed(cut, "Save Employee").Click();
        ButtonNamed(cut, "Cancel").Click();

        OpenForm(cut);

        FieldLabelled(cut, "firstName").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("[role=alert]").Should().BeEmpty("a stale error from the last attempt would be misleading");
    }

    private static IElement SearchBox(IRenderedComponent<Employees> cut) => cut.Find("input[placeholder='Search by name or number...']");

    private static IElement StatusFilter(IRenderedComponent<Employees> cut) =>
        cut.FindAll("select").Single(s => s.QuerySelector("option")!.TextContent == "All Status");

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ServerError(string detail) =>
        Json(HttpStatusCode.InternalServerError, $$"""{"detail":"{{detail}}"}""");

    /// <summary>
    /// Swaps in an API with none of the constructor's routes, for a test that needs one of them to
    /// fail: the stub answers with the first route that matches, so it cannot be overridden.
    /// </summary>
    private StubHttpHandler FreshApi()
    {
        var api = new StubHttpHandler();
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(api)));
        return api;
    }

    [Fact]
    public void AFailedLoad_ShowsWhy_InsteadOfSpinningForever()
    {
        _api.On(HttpMethod.Get, FirstPagePath, () => ServerError("The employee directory is unavailable."));

        var cut = Render<Employees>();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("The employee directory is unavailable."));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
        cut.Markup.Should().NotContain("No employees found.", "a failure is not the same as an empty directory");
    }

    [Fact]
    public void AFailedPageChange_DropsTheRowsOfThePreviousPage()
    {
        // Rows left under the error would read as page 2's employees.
        _api.On(HttpMethod.Get, "/api/employees?page=2&pageSize=20", () => ServerError("Timed out."));
        var cut = RenderPage(Paged(2, Employee("EMP-0001", "Maria Santos")));

        ButtonNamed(cut, "2").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Timed out."));
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Searching_AsksTheApiForMatches_StartingFromPageOne()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=2&pageSize=20", HttpStatusCode.OK, Paged(2, Employee("EMP-0021", "Andres Bonifacio")))
            .On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&search=Santos", HttpStatusCode.OK, Paged(1, Employee("EMP-0042", "Maria Santos")));
        var cut = RenderPage(Paged(2, Employee("EMP-0001", "Jose Rizal")));
        ButtonNamed(cut, "2").Click();
        cut.WaitForAssertion(() => Pager(cut).TextContent.Should().Contain("Page 2 of 2"));

        SearchBox(cut).Input("Santos");

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("EMP-0042"));
        EmployeeListRequests.Last().Should().Be("/api/employees?page=1&pageSize=20&search=Santos",
            "the matches start on page one, whichever page the user was on");
    }

    [Theory]
    [InlineData("true", "&isActive=true")]
    [InlineData("false", "&isActive=false")]
    [InlineData("", "")]
    public void TheStatusFilter_AsksForActiveOrInactiveEmployees_StartingFromPageOne(string choice, string expectedFilter)
    {
        _api.On(HttpMethod.Get, "/api/employees?page=2&pageSize=20", HttpStatusCode.OK, Paged(2, Employee("EMP-0021", "Andres Bonifacio")))
            .On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&isActive=true", HttpStatusCode.OK, Paged(1, Employee("EMP-0042", "Maria Santos")))
            .On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&isActive=false", HttpStatusCode.OK, Paged(1, Employee("EMP-0007", "Jose Rizal", active: false)));
        var cut = RenderPage(Paged(2, Employee("EMP-0001", "Maria Santos")));
        ButtonNamed(cut, "2").Click();
        cut.WaitForAssertion(() => Pager(cut).TextContent.Should().Contain("Page 2 of 2"));
        if (choice == "")
        {
            // Choosing "All Status" is only a change coming from another filter.
            StatusFilter(cut).Change("true");
            cut.WaitForAssertion(() => EmployeeListRequests.Last().Should().EndWith("&isActive=true"));
        }

        StatusFilter(cut).Change(choice);

        cut.WaitForAssertion(() => EmployeeListRequests.Last().Should().Be(FirstPagePath + expectedFilter));
    }

    [Fact]
    public void SearchAndStatus_AreSentTogether()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&isActive=false", HttpStatusCode.OK, Paged(1))
            .On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&search=Rizal&isActive=false", HttpStatusCode.OK,
                Paged(1, Employee("EMP-0007", "Jose Rizal", active: false)));
        var cut = RenderPage(Paged(1));

        StatusFilter(cut).Change("false");
        SearchBox(cut).Input("Rizal");

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("EMP-0007"));
    }

    [Fact]
    public void AnOlderSearchAnsweredLate_DoesNotReplaceTheNewerResults()
    {
        // Every keystroke asks again, and the answers can come back in any order. The list has to
        // show what matches the text now in the box, not whichever answer happened to arrive last.
        var release = new TaskCompletionSource();
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(
            new HoldingHandler(_api, "/api/employees?page=1&pageSize=20&search=Ma", release.Task))));
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&search=Ma", HttpStatusCode.OK,
                Paged(1, Employee("EMP-0042", "Maria Santos"), Employee("EMP-0050", "Mark Cruz")))
            .On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&search=Mar", HttpStatusCode.OK,
                Paged(1, Employee("EMP-0042", "Maria Santos")));
        var cut = RenderPage(Paged(1));

        SearchBox(cut).Input("Ma");
        SearchBox(cut).Input("Mar");
        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle());

        // The page re-renders once the held search's handler finishes, whatever it did with the answer.
        var rendersBefore = cut.RenderCount;
        release.SetResult();
        cut.WaitForState(() => cut.RenderCount > rendersBefore);

        Rows(cut).Should().ContainSingle().Which[0].Should().Be("EMP-0042");
    }

    [Fact]
    public void ASaveThatWorked_IsNotReportedAsFailed_WhenOnlyTheReloadAfterItFails()
    {
        // Telling the user the save failed invites a second attempt, which the API then refuses
        // as a duplicate employee number.
        var saved = false;
        _api.On(HttpMethod.Post, "/api/employees", () =>
            {
                saved = true;
                return Json(HttpStatusCode.Created, Employee("EMP-0100", "Ana Reyes"));
            })
            .On(HttpMethod.Get, FirstPagePath, () => saved
                ? ServerError("The employee directory is unavailable.")
                : Json(HttpStatusCode.OK, Paged(1)));
        var cut = Render<Employees>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No employees found."));
        OpenForm(cut);

        FillRequiredFields(cut);
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("The employee directory is unavailable."));
        cut.Markup.Should().NotContain("Failed to save employee");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Employee", "the form closed on the successful save");
    }

    [Fact]
    public void DepartmentsThatFailToLoad_AreExplainedInTheForm_AndTheListStillShows()
    {
        FreshApi()
            .On(HttpMethod.Get, "/api/departments?page=1&pageSize=100", () => ServerError("Departments are unavailable."))
            .On(HttpMethod.Get, FirstPagePath, HttpStatusCode.OK, Paged(1, Employee("EMP-0042", "Maria Santos")));

        var cut = Render<Employees>();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle());
        cut.FindAll("[role=alert]").Should().BeEmpty("the list itself loaded, and the form is not open");
        OpenForm(cut);
        cut.Find("[role=alert]").TextContent.Should().Contain("Departments are unavailable.");
    }

    [Fact]
    public void PositionsThatFailToLoad_AreExplainedInTheForm()
    {
        FreshApi()
            .On(HttpMethod.Get, "/api/departments?page=1&pageSize=100", HttpStatusCode.OK, Paged(1, Department(FinanceId, "Finance")))
            .On(HttpMethod.Get, $"/api/positions?page=1&pageSize=100&departmentId={FinanceId}", () => ServerError("Positions are unavailable."))
            .On(HttpMethod.Get, FirstPagePath, HttpStatusCode.OK, Paged(1));
        var cut = Render<Employees>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No employees found."));
        OpenForm(cut);

        cut.Find("#department").Change(FinanceId.ToString());

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Positions are unavailable."));
        cut.FindAll("#position option").Select(o => o.TextContent).Should().Equal("None");
    }

    [Fact]
    public void RecordSeparation_IsOfferedOnlyForActiveEmployees()
    {
        var cut = RenderPage(Paged(1,
            Employee("EMP-1", "Maria Santos", id: MariaId),
            Employee("EMP-2", "Juan Cruz", id: JuanId, active: false)));

        ButtonsIn(RowNamed(cut, "Maria Santos")).Should().Contain("Record separation");
        ButtonsIn(RowNamed(cut, "Juan Cruz")).Should().NotContain("Record separation");
    }

    [Fact]
    public void RecordSeparation_NavigatesToTheSeparationsPage_WithThatEmployeeChosen()
    {
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Record separation").Click();

        CurrentUri.Should().EndWith($"/separations?employee={MariaId}");
    }

    [Fact]
    public void AViewOnlyUser_DoesNotSeeRecordSeparationOrCertificateOfEmployment()
    {
        // employees.view-all alone, not employees.manage: someone who can look but not act.
        _auth.SetClaims([new Claim(Permissions.ClaimType, Permissions.EmployeesViewAll)]);

        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        var buttons = ButtonsIn(RowNamed(cut, "Maria Santos"));
        buttons.Should().NotContain("Record separation");
        buttons.Should().NotContain("Certificate of Employment");
    }

    [Fact]
    public void TheIncludeSalaryCheckbox_IsHiddenWithoutPayrollPermission()
    {
        // employees.manage without payroll.manage: can open the certificate, but not add pay to it.
        _auth.SetClaims([
            new Claim(Permissions.ClaimType, Permissions.EmployeesViewAll),
            new Claim(Permissions.ClaimType, Permissions.EmployeesManage)
        ]);
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Certificate of Employment").Click();

        cut.WaitForElement("[data-coe-dialog]");
        cut.FindAll("[data-include-salary]").Should().BeEmpty();
    }

    [Fact]
    public void CertificateOfEmployment_OpensWithTheDefaultPurposeAndSignatoryTitlePrefilled()
    {
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Certificate of Employment").Click();

        cut.WaitForElement("[data-coe-dialog]");
        cut.Find("#coe-purpose").TextContent.Should().Contain("issued upon the request of the employee");
        cut.Find("#coe-signatory-title").GetAttribute("value").Should().Be("HR Manager");
        cut.Find("#coe-signatory-name").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void DownloadingTheCertificate_SendsTheFormAndSavesThePdf()
    {
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));
        _api.On(HttpMethod.Post, $"/api/employees/{MariaId}/coe", () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") } }
        });
        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Certificate of Employment").Click();
        cut.WaitForElement("[data-coe-dialog]");
        cut.Find("#coe-signatory-name").Input("Ana Reyes");
        cut.Find("[data-include-salary]").Click();

        cut.Find("[data-download-coe]").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes"));
        var body = BodyOf(HttpMethod.Post, $"/api/employees/{MariaId}/coe");
        body.GetProperty("signatoryName").GetString().Should().Be("Ana Reyes");
        body.GetProperty("includeSalary").GetBoolean().Should().BeTrue();
        cut.WaitForAssertion(() => cut.FindAll("[data-coe-dialog]").Should().BeEmpty());
    }

    [Fact]
    public void AFailedCertificateDownload_ShowsTheErrorInTheDialog()
    {
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));
        _api.On(HttpMethod.Post, $"/api/employees/{MariaId}/coe", HttpStatusCode.BadRequest,
            """{"detail":"Maria Santos has no salary on record, so the salary line can't be added."}""");
        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Certificate of Employment").Click();
        cut.WaitForElement("[data-coe-dialog]");

        cut.Find("[data-download-coe]").Click();

        cut.WaitForElement("[data-coe-error]").TextContent.Should().Contain("no salary on record");
        cut.FindAll("[data-coe-dialog]").Should().ContainSingle("the dialog stays open so the fields survive the failure");
    }

    // ---- Solo parent ID, and editing an employee ----

    private static readonly Guid TeamId = Guid.Parse("d4e5f6a7-b8c9-4d0e-8f1a-3b4c5d6e7f80");

    /// <summary>An EmployeeDto as GET api/employees/{id} sends it: every field, in the record's order.</summary>
    private static string FullRecord(Guid id, string? soloParentIdNumber = "SP-0042", string? soloParentIdValidUntil = "2027-01-31") =>
        JsonSerializer.Serialize(new
        {
            id, employeeNumber = "EMP-0042", firstName = "Maria", middleName = "Luna", lastName = "Santos",
            fullName = "Maria Luna Santos", dateOfBirth = "1990-05-01", gender = "Female", civilStatus = "Married",
            workEmail = "maria@company.test", mobileNumber = "09171234567",
            departmentId = FinanceId, departmentName = "Finance", positionId = AccountantId, positionTitle = "Accountant",
            reportingManagerId = JuanId, reportingManagerName = "Juan Cruz", teamId = TeamId,
            employmentStatus = "Regular", employmentType = "Regular", hireDate = "2020-02-03", regularizationDate = "2020-08-03",
            isActive = true, is13thMonthEligible = true, separationDate = (string?)null,
            soloParentIdNumber, soloParentIdValidUntil,
            personalEmail = "maria@home.test", address = "12 Mabini Street, Quezon City",
        });

    private string? BodyOfPut(Guid id) =>
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath == $"/api/employees/{id}")];

    private IRenderedComponent<Employees> OpenEdit(string? record = null)
    {
        _api.On(HttpMethod.Get, $"/api/employees/{MariaId}", HttpStatusCode.OK, record ?? FullRecord(MariaId));
        var cut = RenderPage(Paged(1, Employee("EMP-0042", "Maria Santos", id: MariaId)));
        cut.Find($"[data-edit-employee='{MariaId}']").Click();
        cut.WaitForAssertion(() => cut.Find("#soloParentIdNumber"));
        return cut;
    }

    [Fact]
    public void TheAddForm_AsksForTheSoloParentId_AndSendsNullsWhenLeftBlank()
    {
        _api.On(HttpMethod.Post, "/api/employees", HttpStatusCode.Created, Employee("EMP-0100", "Ana Reyes"));
        var cut = RenderPage(Paged(1));
        OpenForm(cut);

        cut.Find("label[for=soloParentIdNumber]").TextContent.Trim().Should().Be("Solo parent ID number");
        cut.Find("label[for=soloParentIdValidUntil]").TextContent.Trim().Should().Be("Solo parent ID valid until");
        FillRequiredFields(cut);
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        var body = BodyOf(HttpMethod.Post, "/api/employees");
        body.GetProperty("soloParentIdNumber").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("soloParentIdValidUntil").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Edit_IsOfferedOnlyToThoseWhoManageEmployees()
    {
        _auth.SetClaims([new Claim(Permissions.ClaimType, Permissions.EmployeesViewAll)]);

        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        cut.FindAll("[data-edit-employee]").Should().BeEmpty();
    }

    [Fact]
    public void Edit_OpensTheFullRecord_WithTheSoloParentIdFilledIn()
    {
        var cut = OpenEdit();

        cut.Markup.Should().Contain("Edit Employee");
        FieldLabelled(cut, "firstName").GetAttribute("value").Should().Be("Maria");
        FieldLabelled(cut, "middleName").GetAttribute("value").Should().Be("Luna");
        cut.Find("#soloParentIdNumber").GetAttribute("value").Should().Be("SP-0042");
        cut.Find("#soloParentIdValidUntil").GetAttribute("value").Should().Be("2027-01-31");
        cut.Find("#empStatus").GetAttribute("value").Should().Be("Regular");
        cut.FindAll("label[for=empNumber]").Should().BeEmpty("an update can't change the employee number");
    }

    [Fact]
    public void SavingAnEdit_SendsTheWholeRecord_KeepingWhatTheFormDoesNotShow()
    {
        // An update replaces every field: anything left out is cleared on the server.
        var cut = OpenEdit();
        _api.On(HttpMethod.Put, $"/api/employees/{MariaId}", HttpStatusCode.OK, FullRecord(MariaId, "SP-9999", "2028-02-29"));

        cut.Find("#soloParentIdNumber").Input("SP-9999");
        cut.Find("#soloParentIdValidUntil").Input("2028-02-29");
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Employee"));
        BodyOfPut(MariaId).Should().Be(
            $$"""{"firstName":"Maria","middleName":"Luna","lastName":"Santos","civilStatus":"Married","personalEmail":"maria@home.test","mobileNumber":"09171234567","address":"12 Mabini Street, Quezon City","departmentId":"{{FinanceId}}","positionId":"{{AccountantId}}","teamId":"{{TeamId}}","reportingManagerId":"{{JuanId}}","employmentStatus":"Regular","regularizationDate":"2020-08-03","is13thMonthEligible":true,"soloParentIdNumber":"SP-9999","soloParentIdValidUntil":"2028-02-29"}""");
        EmployeeListRequests.Should().HaveCount(2, "the list reloads after the save");
    }

    [Fact]
    public void AnEditThatDoesNotTouchTheSoloParentId_SendsItBackUnchanged()
    {
        var cut = OpenEdit();
        _api.On(HttpMethod.Put, $"/api/employees/{MariaId}", HttpStatusCode.OK, FullRecord(MariaId));

        FieldLabelled(cut, "mobileNumber").Input("09998887777");
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
        var body = BodyOf(HttpMethod.Put, $"/api/employees/{MariaId}");
        body.GetProperty("mobileNumber").GetString().Should().Be("09998887777");
        body.GetProperty("soloParentIdNumber").GetString().Should().Be("SP-0042");
        body.GetProperty("soloParentIdValidUntil").GetString().Should().Be("2027-01-31");
    }

    [Fact]
    public void ClearingTheSoloParentId_SendsNulls()
    {
        var cut = OpenEdit();
        _api.On(HttpMethod.Put, $"/api/employees/{MariaId}", HttpStatusCode.OK, FullRecord(MariaId, null, null));

        cut.Find("#soloParentIdNumber").Input("   ");
        cut.Find("#soloParentIdValidUntil").Input("");
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
        var body = BodyOf(HttpMethod.Put, $"/api/employees/{MariaId}");
        body.GetProperty("soloParentIdNumber").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("soloParentIdValidUntil").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ARejectedEdit_ShowsTheApisReason_AndKeepsTheFormOpen()
    {
        var cut = OpenEdit();
        _api.On(HttpMethod.Put, $"/api/employees/{MariaId}", HttpStatusCode.BadRequest,
            """{"detail":"The solo parent ID number can't be longer than 50 characters."}""");

        cut.Find("#soloParentIdNumber").Input(new string('9', 51));
        ButtonNamed(cut, "Save Employee").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent
            .Should().Contain("Failed to save employee. The solo parent ID number can't be longer than 50 characters."));
        ButtonNamed(cut, "Save Employee").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ARecordThatFailsToLoad_CannotBeSaved()
    {
        // Saving without the record would send blanks for everything the form doesn't show.
        _api.On(HttpMethod.Get, $"/api/employees/{MariaId}", () => ServerError("The employee record is unavailable."));
        var cut = RenderPage(Paged(1, Employee("EMP-0042", "Maria Santos", id: MariaId)));

        cut.Find($"[data-edit-employee='{MariaId}']").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("The employee record is unavailable."));
        ButtonNamed(cut, "Save Employee").HasAttribute("disabled").Should().BeTrue();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void AddingAfterEditing_StartsBlank_AndCreatesRatherThanUpdates()
    {
        var cut = OpenEdit();
        ButtonNamed(cut, "Cancel").Click();

        OpenForm(cut);

        cut.Markup.Should().Contain("Add Employee");
        FieldLabelled(cut, "firstName").GetAttribute("value").Should().BeEmpty();
        cut.Find("#soloParentIdNumber").GetAttribute("value").Should().BeEmpty();
        cut.FindAll("label[for=empNumber]").Should().ContainSingle();
    }

    [Fact]
    public async Task ARecordThatArrivesAfterTheFormMovedOn_IsIgnored()
    {
        var gate = _api.OnGated(HttpMethod.Get, $"/api/employees/{MariaId}");
        var cut = RenderPage(Paged(1, Employee("EMP-0042", "Maria Santos", id: MariaId)));
        cut.Find($"[data-edit-employee='{MariaId}']").Click();
        ButtonNamed(cut, "Cancel").Click();
        OpenForm(cut);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FullRecord(MariaId), Encoding.UTF8, "application/json")
        });
        await Task.Delay(50);

        cut.Markup.Should().Contain("Add Employee");
        FieldLabelled(cut, "firstName").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public async Task PositionsAnsweredAfterTheFormWasReopened_DoNotLandOnTheNewForm()
    {
        var api = FreshApi();
        api.On(HttpMethod.Get, FirstPagePath, HttpStatusCode.OK, Paged(1))
            .On(HttpMethod.Get, "/api/departments?page=1&pageSize=100", HttpStatusCode.OK, Paged(1, Department(FinanceId, "Finance")))
            .On(HttpMethod.Get, "/api/users/employee-links", HttpStatusCode.OK, "[]");
        var gate = api.OnGated(HttpMethod.Get, $"/api/positions?page=1&pageSize=100&departmentId={FinanceId}");
        var cut = Render<Employees>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        OpenForm(cut);
        cut.Find("#department").Change(FinanceId.ToString());
        ButtonNamed(cut, "Cancel").Click();
        OpenForm(cut);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Paged(1, Position(AccountantId, FinanceId, "Accountant")), Encoding.UTF8, "application/json")
        });
        await Task.Delay(100); // let the late answer land, if it is going to

        cut.FindAll("#position option").Select(o => o.TextContent).Should().Equal("None");
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public async Task AnEarlierDepartmentsPositions_AnsweredLate_DoNotReplaceTheLaterOnes()
    {
        var api = FreshApi();
        api.On(HttpMethod.Get, FirstPagePath, HttpStatusCode.OK, Paged(1))
            .On(HttpMethod.Get, "/api/departments?page=1&pageSize=100", HttpStatusCode.OK,
                Paged(1, Department(FinanceId, "Finance"), Department(LegalId, "Legal")))
            .On(HttpMethod.Get, "/api/users/employee-links", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Get, $"/api/positions?page=1&pageSize=100&departmentId={LegalId}", HttpStatusCode.OK,
                Paged(1, Position(CounselId, LegalId, "Counsel")));
        var financeGate = api.OnGated(HttpMethod.Get, $"/api/positions?page=1&pageSize=100&departmentId={FinanceId}");
        var cut = Render<Employees>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        OpenForm(cut);
        cut.Find("#department").Change(FinanceId.ToString());
        cut.Find("#department").Change(LegalId.ToString());
        cut.WaitForAssertion(() => cut.FindAll("#position option").Select(o => o.TextContent).Should().Equal("None", "Counsel"));

        financeGate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Paged(1, Position(AccountantId, FinanceId, "Accountant")), Encoding.UTF8, "application/json")
        });
        await Task.Delay(100); // let the late answer land, if it is going to

        cut.FindAll("#position option").Select(o => o.TextContent).Should().Equal("None", "Counsel");
    }

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    /// <summary>Holds back the answer to one URL until the test releases it.</summary>
    private sealed class HoldingHandler(HttpMessageHandler inner, string heldPathAndQuery, Task release) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.PathAndQuery == heldPathAndQuery)
                await release;
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
