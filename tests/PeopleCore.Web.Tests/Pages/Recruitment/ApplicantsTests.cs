using System.Net;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Recruitment;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Recruitment;

public class ApplicantsTests : BunitContext
{
    private static readonly Guid PostingId = Guid.Parse("3c8a6d4b-5eaf-4fb0-8d94-6a5b8cae0d04");
    private static readonly DateTime AppliedAt = new(2026, 3, 5, 9, 30, 0, DateTimeKind.Utc);

    private const string FirstPage = "/api/applicants?page=1&pageSize=20";

    private readonly StubHttpHandler _api = new();

    public ApplicantsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
    }

    private static string Applicant(string first, string last, string status) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","jobPostingId":"{{PostingId}}","jobPostingTitle":"Backend Developer",
         "firstName":"{{first}}","lastName":"{{last}}","email":"{{first.ToLowerInvariant()}}@mail.test","phone":null,
         "status":"{{status}}","convertedEmployeeId":null,"appliedAt":"{{AppliedAt:O}}"}
        """;

    private static string Page(int page, int totalPages, params string[] applicants) =>
        $$"""{"items":[{{string.Join(",", applicants)}}],"totalCount":{{applicants.Length}},"page":{{page}},"pageSize":20,"totalPages":{{totalPages}}}""";

    private IRenderedComponent<Applicants> RenderPage()
    {
        var cut = Render<Applicants>();
        cut.WaitForAssertion(() => cut.FindAll("svg.animate-spin").Should().BeEmpty());
        return cut;
    }

    private List<string> Requested => _api.Requests.Select(r => r.RequestUri!.PathAndQuery).ToList();

    /// <summary>The pager, told apart from the breadcrumb (also a nav) by its "Page n of m" caption.</summary>
    private static IElement? Pager(IRenderedComponent<Applicants> cut) =>
        cut.FindAll("nav").SingleOrDefault(n => n.TextContent.Contains("Page "));

    private static List<List<string>> Rows(IRenderedComponent<Applicants> cut) =>
        cut.FindAll("tbody tr")
            .Select(tr => tr.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList())
            .ToList();

    [Fact]
    public void ListsTheFirstPageOfEveryApplicant_WithWhereTheyAreInThePipeline()
    {
        _api.On(HttpMethod.Get, FirstPage, HttpStatusCode.OK,
            Page(1, 1, Applicant("Ana", "Reyes", "Interviewing"), Applicant("Ben", "Cruz", "Rejected")));

        var cut = RenderPage();

        Requested.Should().Equal(FirstPage);
        var rows = Rows(cut);
        rows.Should().HaveCount(2);
        rows[0][0].Should().Contain("Ana Reyes").And.Contain("ana@mail.test");
        rows[0].Skip(1).Should().Equal("Backend Developer", AppliedAt.ToString("MMM d, yyyy"), "Interviewing");
        rows[1][3].Should().Be("Rejected");
        Pager(cut).Should().BeNull("a single page needs no pager");
    }

    [Fact]
    public void ChoosingAStatus_AsksTheApiForOnlyThatStatus()
    {
        _api.On(HttpMethod.Get, FirstPage, HttpStatusCode.OK,
                Page(1, 1, Applicant("Ana", "Reyes", "Interviewing"), Applicant("Ben", "Cruz", "Hired")))
            .On(HttpMethod.Get, FirstPage + "&status=Hired", HttpStatusCode.OK,
                Page(1, 1, Applicant("Ben", "Cruz", "Hired")));
        var cut = RenderPage();

        cut.Find("select").Change("Hired");

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[3].Should().Be("Hired"));
        Requested.Should().Equal(FirstPage, FirstPage + "&status=Hired");
    }

    [Fact]
    public void NoApplicants_ShowsTheEmptyState()
    {
        _api.On(HttpMethod.Get, FirstPage + "&status=Offered", HttpStatusCode.OK, Page(1, 0))
            .On(HttpMethod.Get, FirstPage, HttpStatusCode.OK, Page(1, 1, Applicant("Ana", "Reyes", "Applied")));
        var cut = RenderPage();

        cut.Find("select").Change("Offered");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No applicants found."));
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void MoreThanOnePage_OffersAPager_ThatLoadsTheChosenPageWithTheSameFilter()
    {
        _api.On(HttpMethod.Get, FirstPage, HttpStatusCode.OK, Page(1, 3, Applicant("Ana", "Reyes", "Screening")))
            .On(HttpMethod.Get, FirstPage + "&status=Screening", HttpStatusCode.OK, Page(1, 3, Applicant("Ana", "Reyes", "Screening")))
            .On(HttpMethod.Get, "/api/applicants?page=2&pageSize=20&status=Screening", HttpStatusCode.OK,
                Page(2, 3, Applicant("Carla", "Lim", "Screening")));
        var cut = RenderPage();
        cut.Find("select").Change("Screening");
        cut.WaitForAssertion(() => Requested.Should().HaveCount(2));

        Pager(cut)!.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "2").Click();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Contain("Carla Lim"));
        Requested.Last().Should().Be("/api/applicants?page=2&pageSize=20&status=Screening");
        Pager(cut)!.TextContent.Should().Contain("Page 2 of 3");
    }
}
