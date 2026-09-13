using Bunit;
using FluentAssertions;
using PeopleCore.Web.Components.UI;

namespace PeopleCore.Web.Tests.Components;

public class PaginationTests : BunitContext
{
    private IRenderedComponent<Pagination> RenderPagination(int current, int total, Action<int>? onPageChange = null) =>
        Render<Pagination>(p => p
            .Add(x => x.CurrentPage, current)
            .Add(x => x.TotalPages, total)
            .Add(x => x.OnPageChange, onPageChange ?? (_ => { })));

    /// <summary>What the page strip shows between the arrows, with "..." for an ellipsis.</summary>
    private static List<string> VisibleLabels(IRenderedComponent<Pagination> cut)
    {
        var strip = cut.Find("nav > div.flex");
        return strip.Children
            .Skip(1).SkipLast(1) // the previous/next arrows
            .Select(e => e.TextContent.Trim())
            .ToList();
    }

    [Fact]
    public void ShowsEveryPage_WhenThereAreSevenOrFewer()
    {
        var cut = RenderPagination(current: 4, total: 7);

        VisibleLabels(cut).Should().Equal("1", "2", "3", "4", "5", "6", "7");
        cut.Find("nav").TextContent.Should().Contain("Page 4 of 7");
    }

    [Theory]
    [InlineData(1, new[] { "1", "2", "...", "20" })]
    [InlineData(3, new[] { "1", "2", "3", "4", "...", "20" })]
    [InlineData(10, new[] { "1", "...", "9", "10", "11", "...", "20" })]
    [InlineData(18, new[] { "1", "...", "17", "18", "19", "20" })]
    [InlineData(20, new[] { "1", "...", "19", "20" })]
    public void CollapsesDistantPagesIntoEllipses_WhenThereAreMany(int current, string[] expected)
    {
        var cut = RenderPagination(current, total: 20);

        VisibleLabels(cut).Should().Equal(expected);
    }

    [Fact]
    public void DisablesPrevious_OnTheFirstPage_AndNext_OnTheLast()
    {
        var first = RenderPagination(current: 1, total: 3);
        first.FindAll("button")[0].HasAttribute("disabled").Should().BeTrue();
        first.FindAll("button")[^1].HasAttribute("disabled").Should().BeFalse();

        var last = RenderPagination(current: 3, total: 3);
        last.FindAll("button")[0].HasAttribute("disabled").Should().BeFalse();
        last.FindAll("button")[^1].HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void RequestsThePageThatWasClicked()
    {
        var requested = new List<int>();
        var cut = RenderPagination(current: 2, total: 5, requested.Add);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "4").Click();
        cut.FindAll("button")[0].Click();  // previous
        cut.FindAll("button")[^1].Click(); // next

        requested.Should().Equal(4, 1, 3);
    }

    [Fact]
    public void DoesNotRequestTheCurrentPageAgain()
    {
        var requested = new List<int>();
        var cut = RenderPagination(current: 2, total: 5, requested.Add);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "2").Click();

        requested.Should().BeEmpty();
    }
}
