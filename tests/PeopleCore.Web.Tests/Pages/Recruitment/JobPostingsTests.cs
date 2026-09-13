using System.Net;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Recruitment;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Recruitment;

public class JobPostingsTests : BunitContext
{
    private static readonly Guid DraftId = Guid.Parse("0f5d3a1e-2b7c-4c8e-9a61-3d2e5f7a8b01");
    private static readonly Guid OpenId = Guid.Parse("1a6e4b2f-3c8d-4d9f-8b72-4e3f6a8b9c02");
    private static readonly Guid ClosedId = Guid.Parse("2b7f5c3a-4d9e-4eaf-9c83-5f4a7b9cad03");

    private const string AllPath = "/api/job-postings?page=1&pageSize=20";

    private readonly StubHttpHandler _api = new();

    public JobPostingsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
    }

    private static string Posting(Guid id, string title, string status, string? department = "Engineering", int vacancies = 2) =>
        $$"""
        {"id":"{{id}}","title":"{{title}}","departmentId":null,"departmentName":{{(department is null ? "null" : $"\"{department}\"")}},
         "positionId":null,"positionTitle":null,"description":null,"requirements":null,"vacancies":{{vacancies}},
         "status":"{{status}}","postedAt":null,"closedAt":null}
        """;

    private static string Page(params string[] postings) =>
        $$"""{"items":[{{string.Join(",", postings)}}],"totalCount":{{postings.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static readonly string ThreePostings = Page(
        Posting(DraftId, "Payroll Analyst", "Draft"),
        Posting(OpenId, "Backend Developer", "Open"),
        Posting(ClosedId, "Office Clerk", "Closed", department: null));

    private IRenderedComponent<JobPostings> RenderPage()
    {
        var cut = Render<JobPostings>();
        cut.WaitForAssertion(() => cut.FindAll("svg.animate-spin").Should().BeEmpty());
        return cut;
    }

    private List<string> Gets => _api.Requests
        .Where(r => r.Method == HttpMethod.Get)
        .Select(r => r.RequestUri!.PathAndQuery)
        .ToList();

    private string? BodyOf(HttpMethod method) =>
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == method)];

    private static IElement Row(IRenderedComponent<JobPostings> cut, string title) =>
        cut.FindAll("tbody tr").Single(tr => tr.QuerySelector("td")!.TextContent.Trim() == title);

    private static List<string> ActionsIn(IElement row) =>
        row.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

    private static IElement Button(IRenderedComponent<JobPostings> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static void OpenForm(IRenderedComponent<JobPostings> cut) => Button(cut, "New Posting").Click();

    [Fact]
    public void ListsEveryPosting_WithItsDepartmentVacanciesAndStatus()
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings);

        var cut = RenderPage();

        Gets.Should().Equal(AllPath);
        cut.FindAll("tbody tr").Should().HaveCount(3);
        Row(cut, "Backend Developer").QuerySelectorAll("td").Select(td => td.TextContent.Trim()).Take(4)
            .Should().Equal("Backend Developer", "Engineering", "2", "Open");
        Row(cut, "Office Clerk").QuerySelectorAll("td")[1].TextContent.Trim().Should().Be("--");
    }

    [Fact]
    public void ChoosingAStatus_AsksTheApiForOnlyThatStatus_AndClearingItAsksForAll()
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings)
            .On(HttpMethod.Get, AllPath + "&status=Open", HttpStatusCode.OK, Page(Posting(OpenId, "Backend Developer", "Open")));
        var cut = RenderPage();

        cut.Find("select").Change("Open");
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());

        cut.Find("select").Change("");
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(3));
        Gets.Should().Equal(AllPath, AllPath + "&status=Open", AllPath);
    }

    [Fact]
    public void NoPostings_ShowsTheEmptyState()
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, Page());

        var cut = RenderPage();

        cut.Markup.Should().Contain("No job postings found.");
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void OnlyADraftCanBePublished_AndOnlyAnOpenPostingCanBeClosed()
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings);

        var cut = RenderPage();

        ActionsIn(Row(cut, "Payroll Analyst")).Should().Equal("Publish");
        ActionsIn(Row(cut, "Backend Developer")).Should().Equal("Close");
        ActionsIn(Row(cut, "Office Clerk")).Should().BeEmpty("a closed posting is final");
    }

    [Theory]
    [InlineData("Payroll Analyst", "Publish", "publish")]
    [InlineData("Backend Developer", "Close", "close")]
    public void AStatusChange_IsSentForThatPosting_AndTheFilteredListIsReloaded(string title, string action, string verb)
    {
        var id = title == "Payroll Analyst" ? DraftId : OpenId;
        var status = title == "Payroll Analyst" ? "Draft" : "Open";
        var loads = 0;
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings)
            // The reload has to keep the user's filter, and to show the posting has moved on.
            .On(HttpMethod.Get, AllPath + $"&status={status}", () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(++loads == 1 ? Page(Posting(id, title, status)) : Page(),
                    System.Text.Encoding.UTF8, "application/json")
            })
            .On(HttpMethod.Put, $"/api/job-postings/{id}/{verb}", HttpStatusCode.OK, Posting(id, title, "Whatever"));
        var cut = RenderPage();
        cut.Find("select").Change(status);
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());

        Button(cut, action).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No job postings found."));
        _api.Requests.Where(r => r.Method == HttpMethod.Put).Select(r => r.RequestUri!.PathAndQuery)
            .Should().Equal($"/api/job-postings/{id}/{verb}");
        Gets.Should().Equal(AllPath, AllPath + $"&status={status}", AllPath + $"&status={status}");
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Publish", "publish", "Failed to publish.")]
    [InlineData("Close", "close", "Failed to close.")]
    public void ARefusedStatusChange_IsReported(string action, string verb, string expectedError)
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings)
            .On(HttpMethod.Put, $"/api/job-postings/{(verb == "publish" ? DraftId : OpenId)}/{verb}", HttpStatusCode.Conflict);
        var cut = RenderPage();

        Button(cut, action).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain(expectedError));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveDraft_StaysDisabledUntilThePostingHasATitle(string title)
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings);
        var cut = RenderPage();
        OpenForm(cut);

        cut.Find("input[placeholder='Position title']").Input(title);

        Button(cut, "Save Draft").HasAttribute("disabled").Should().BeTrue();
        cut.Find("input[placeholder='Position title']").Input("Data Engineer");
        Button(cut, "Save Draft").HasAttribute("disabled").Should().BeFalse();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void SavingADraft_PostsTheForm_ClosesIt_AndReloadsTheList()
    {
        var loads = 0;
        _api.On(HttpMethod.Get, AllPath, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(++loads == 1 ? Page() : Page(Posting(Guid.NewGuid(), "Data Engineer", "Draft")),
                    System.Text.Encoding.UTF8, "application/json")
            })
            .On(HttpMethod.Post, "/api/job-postings", HttpStatusCode.Created, Posting(Guid.NewGuid(), "Data Engineer", "Draft"));
        var cut = RenderPage();
        OpenForm(cut);

        cut.Find("input[placeholder='Position title']").Input("Data Engineer");
        cut.Find("input[type=number]").Input("3");
        cut.Find("#posting-description").Input("Builds the reporting pipeline.");
        Button(cut, "Save Draft").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());
        BodyOf(HttpMethod.Post).Should().Be(
            """{"title":"Data Engineer","description":"Builds the reporting pipeline.","vacancies":3,"departmentId":null,"positionId":null,"requirements":null}""");
        cut.FindAll("input[placeholder='Position title']").Should().BeEmpty();
        Gets.Should().HaveCount(2);
    }

    [Fact]
    public void ARejectedDraft_ShowsTheError_AndKeepsWhatWasTyped()
    {
        _api.On(HttpMethod.Get, AllPath, HttpStatusCode.OK, ThreePostings)
            .On(HttpMethod.Post, "/api/job-postings", HttpStatusCode.BadRequest);
        var cut = RenderPage();
        OpenForm(cut);

        cut.Find("input[placeholder='Position title']").Input("Data Engineer");
        Button(cut, "Save Draft").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Failed to create posting."));
        cut.Find("input[placeholder='Position title']").GetAttribute("value").Should().Be("Data Engineer");
        Button(cut, "Save Draft").HasAttribute("disabled").Should().BeFalse("the user has to be able to try again");
        Gets.Should().ContainSingle("nothing was created, so there is nothing new to show");
    }
}
