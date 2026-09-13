using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages;

namespace PeopleCore.Web.Tests.Pages;

public class NotFoundTests : BunitContext
{
    [Fact]
    public void SaysThePageDoesNotExist()
    {
        var cut = Render<NotFound>();

        cut.Find("h1").TextContent.Trim().Should().Be("Page not found");
        cut.Markup.Should().Contain("Sorry, the content you are looking for does not exist.");
    }

    [Fact]
    public void BackToDashboard_ReturnsToTheHomePage()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/some/missing/page");
        var cut = Render<NotFound>();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Back to Dashboard").Click();

        nav.Uri.Should().Be("http://localhost/");
    }
}
