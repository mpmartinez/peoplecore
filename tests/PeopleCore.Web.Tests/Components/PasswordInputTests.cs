using Bunit;
using FluentAssertions;
using PeopleCore.Web.Components.UI;

namespace PeopleCore.Web.Tests.Components;

public class PasswordInputTests : BunitContext
{
    private IRenderedComponent<PasswordInput> RenderField(string value = "S3cretPass") =>
        Render<PasswordInput>(p => p.Add(x => x.Id, "pw").Add(x => x.Value, value));

    [Fact]
    public void ThePassword_IsHiddenUntilAsked()
    {
        var cut = RenderField();

        cut.Find("#pw").GetAttribute("type").Should().Be("password");
        var toggle = cut.Find("[data-password-toggle]");
        toggle.GetAttribute("aria-label").Should().Be("Show password");
        toggle.GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void TheToggle_ShowsThePassword_AndHidesItAgain()
    {
        var cut = RenderField();

        cut.Find("[data-password-toggle]").Click();
        cut.Find("#pw").GetAttribute("type").Should().Be("text");
        cut.Find("#pw").GetAttribute("value").Should().Be("S3cretPass");
        cut.Find("[data-password-toggle]").GetAttribute("aria-label").Should().Be("Hide password");
        cut.Find("[data-password-toggle]").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-password-toggle]").Click();
        cut.Find("#pw").GetAttribute("type").Should().Be("password");
    }

    [Fact]
    public void TheToggle_IsAPlainButton_KeyboardUsersCanReach()
    {
        var toggle = RenderField().Find("[data-password-toggle]");

        toggle.GetAttribute("type").Should().Be("button", "it must never submit the form it sits in");
        toggle.HasAttribute("tabindex").Should().BeFalse();
        toggle.GetAttribute("aria-controls").Should().Be("pw");
    }

    [Fact]
    public void Typing_ReportsTheNewValue()
    {
        string? reported = null;
        var cut = Render<PasswordInput>(p => p.Add(x => x.Id, "pw").Add(x => x.ValueChanged, v => reported = v));

        cut.Find("#pw").Input("typed-in");

        reported.Should().Be("typed-in");
    }
}
