using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Components.UI;
using PeopleCore.Web.Services;

namespace PeopleCore.Web.Tests.Components;

public class ToastProviderTests : BunitContext
{
    private readonly ToastService _toasts = new();

    public ToastProviderTests() => Services.AddSingleton(_toasts);

    [Fact]
    public async Task RendersAToast_WhenOneIsRaisedAnywhereInTheApp()
    {
        var cut = Render<ToastProvider>();

        await cut.InvokeAsync(() => _toasts.ShowSuccess("Employee saved."));

        cut.Find("[role=status]").TextContent.Should().Contain("Employee saved.");
    }

    [Fact]
    public async Task AnnouncesErrorsAsAlerts()
    {
        // role=alert is what makes a screen reader interrupt; only errors should do that.
        var cut = Render<ToastProvider>();

        await cut.InvokeAsync(() => _toasts.ShowError("Could not save."));

        cut.Find("[role=alert]").TextContent.Should().Contain("Could not save.");
    }

    [Fact]
    public async Task StacksToasts_AndDismissesOnlyTheOneClosed()
    {
        var cut = Render<ToastProvider>();
        await cut.InvokeAsync(() =>
        {
            _toasts.ShowInfo("first");
            _toasts.ShowInfo("second");
        });

        cut.FindAll("[role=status]")[0].QuerySelector("button[title=Dismiss]")!.Click();

        cut.FindAll("[role=status]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("second");
    }

    [Fact]
    public async Task DismissesAToastByItself_WhenItsDurationRunsOut()
    {
        var cut = Render<ToastProvider>();

        await cut.InvokeAsync(() => _toasts.ShowInfo("brief", TimeSpan.FromMilliseconds(50)));
        cut.FindAll("[role=status]").Should().ContainSingle();

        cut.WaitForAssertion(() => cut.FindAll("[role=status]").Should().BeEmpty(), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RemovesAToast_WhenTheServiceSupersedesIt()
    {
        var cut = Render<ToastProvider>();
        Guid id = default;
        await cut.InvokeAsync(() => id = _toasts.Show("Generating payslips..."));

        await cut.InvokeAsync(() => _toasts.Remove(id));

        cut.FindAll("[role=status]").Should().BeEmpty();
    }

    [Fact]
    public async Task StopsListening_OnceDisposed()
    {
        var cut = Render<ToastProvider>();

        await DisposeComponentsAsync();

        // A disposed provider still subscribed would call StateHasChanged on a dead component.
        var act = () => _toasts.ShowError("after teardown");
        act.Should().NotThrow();
    }
}
