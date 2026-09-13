using FluentAssertions;
using PeopleCore.Web.Services;

namespace PeopleCore.Web.Tests.Services;

public class ToastServiceTests
{
    private readonly ToastService _service = new();
    private readonly List<ToastMessage> _shown = [];

    public ToastServiceTests() => _service.OnShow += _shown.Add;

    [Fact]
    public void Success_Info_AndWarning_StayUpForFourSeconds()
    {
        _service.ShowSuccess("saved");
        _service.ShowInfo("fyi");
        _service.ShowWarning("careful");

        _shown.Select(t => t.Type).Should().Equal(ToastType.Success, ToastType.Info, ToastType.Warning);
        _shown.Should().OnlyContain(t => t.Duration == TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Errors_StayUpLonger()
    {
        _service.ShowError("could not save");

        _shown.Single().Type.Should().Be(ToastType.Error);
        _shown.Single().Duration.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void AnExplicitDuration_WinsOverTheDefault()
    {
        _service.ShowError("brief", TimeSpan.FromSeconds(1));

        _shown.Single().Duration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Show_ReturnsTheIdThatRemoveCancels()
    {
        Guid? removed = null;
        _service.OnRemove += id => removed = id;

        var id = _service.Show("working...");
        _service.Remove(id);

        _shown.Single().Id.Should().Be(id);
        removed.Should().Be(id);
    }

    [Fact]
    public void Show_WithNoSubscriber_DoesNotThrow()
    {
        var act = () => new ToastService().ShowError("nobody is listening");

        act.Should().NotThrow();
    }
}
