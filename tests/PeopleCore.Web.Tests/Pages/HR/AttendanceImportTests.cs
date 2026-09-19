using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

/// <summary>
/// Importing a time clock export: the file is previewed first, numbers that match nobody can be
/// linked to an employee, and only then is anything imported.
/// </summary>
public class AttendanceImportTests : BunitContext
{
    private const string JuanId = "11111111-1111-1111-1111-111111111111";

    private static readonly string Preview = $$"""
        {"layout":"Time clock scans","punches":4,"matchedPeople":1,"from":"2026-03-10","to":"2026-03-11",
         "unmatched":[{"deviceId":"77","punches":2}],"errors":[],
         "employees":[{"id":"{{JuanId}}","employeeNumber":"EMP-001","fullName":"Juan Cruz","biometricId":null,"isActive":true}]}
        """;

    private static readonly string PreviewAllMatched = $$"""
        {"layout":"Time clock scans","punches":4,"matchedPeople":2,"from":"2026-03-10","to":"2026-03-11",
         "unmatched":[],"errors":[],
         "employees":[{"id":"{{JuanId}}","employeeNumber":"EMP-001","fullName":"Juan Cruz","biometricId":"77","isActive":true}]}
        """;

    private readonly StubHttpHandler _api = new();

    public AttendanceImportTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("hr@company.test");
        auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
    }

    private IRenderedComponent<AttendanceImport> RenderWithFile()
    {
        var cut = Render<AttendanceImport>();
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("AC-No.,Time\n77,2026-03-10 08:00\n", "export.csv"));
        return cut;
    }

    [Fact]
    public void Choosing_a_file_previews_it_and_imports_nothing()
    {
        _api.On(HttpMethod.Post, "/api/attendance/import?preview=true", HttpStatusCode.OK, Preview);

        var cut = RenderWithFile();

        cut.WaitForElement("[data-import-preview]").TextContent.Should().Contain("Time clock scans");
        cut.Find("[data-unmatched]").TextContent.Should().Contain("77");
        _api.Requests.Should().OnlyContain(r => r.RequestUri!.Query.Contains("preview=true"));
    }

    [Fact]
    public void Linking_a_number_saves_it_and_previews_again()
    {
        var previews = new Queue<string>([Preview, PreviewAllMatched]);
        _api.On(HttpMethod.Post, "/api/attendance/import?preview=true",
                () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(previews.Dequeue(), Encoding.UTF8, "application/json") })
            .On(HttpMethod.Put, $"/api/attendance/biometric-ids/{JuanId}", HttpStatusCode.OK,
                $$"""{"id":"{{JuanId}}","employeeNumber":"EMP-001","fullName":"Juan Cruz","biometricId":"77","isActive":true}""");

        var cut = RenderWithFile();
        cut.WaitForElement("[data-unmatched]");
        cut.Find("#link-77").Change(JuanId);
        cut.Find("[data-link='77']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-unmatched]").Should().BeEmpty());
        _api.RequestBodies.Should().Contain(b => b != null && b.Contains("\"biometricId\":\"77\""));
    }

    [Fact]
    public void Importing_shows_what_was_saved_and_what_was_skipped()
    {
        _api.On(HttpMethod.Post, "/api/attendance/import?preview=true", HttpStatusCode.OK, Preview)
            .On(HttpMethod.Post, "/api/attendance/import", HttpStatusCode.OK,
                """{"imported":2,"skipped":2,"errors":["'77' matches no employee, so its 2 punches were not imported."]}""");

        var cut = RenderWithFile();
        cut.WaitForElement("[data-import]").Click();

        var result = cut.WaitForElement("[data-import-result]").TextContent;
        result.Should().Contain("Imported 2 punches").And.Contain("skipped 2").And.Contain("'77'");
    }

    [Fact]
    public void A_file_whose_layout_is_not_recognised_says_so()
    {
        _api.On(HttpMethod.Post, "/api/attendance/import?preview=true", HttpStatusCode.OK,
            """{"layout":"Not recognised","punches":0,"matchedPeople":0,"from":null,"to":null,"unmatched":[],"errors":["The file's columns weren't recognised."],"employees":[]}""");

        var cut = RenderWithFile();

        cut.WaitForElement("[data-import-error]").TextContent.Should().Contain("weren't recognised");
        cut.FindAll("[data-import-preview]").Should().BeEmpty();
    }
}
