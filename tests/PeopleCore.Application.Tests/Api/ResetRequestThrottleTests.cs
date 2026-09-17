using FluentAssertions;
using PeopleCore.API.Accounts;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// How many reset links one address, and one caller, can ask for. Both limits matter: the first
/// stops a mailbox being flooded, the second stops someone walking a list of addresses.
/// </summary>
public class ResetRequestThrottleTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));
    private readonly ResetRequestThrottle _sut;

    public ResetRequestThrottleTests() => _sut = new ResetRequestThrottle(_clock);

    private int AllowedInARow(string email, string ip, int attempts) =>
        Enumerable.Range(0, attempts).Count(_ => _sut.TryRequest(email, ip));

    [Fact]
    public void AnAddress_MayAskThreeTimesAnHour()
    {
        AllowedInARow("ana@company.test", "10.0.0.1", 5).Should().Be(3);
    }

    [Fact]
    public void AnHourLater_ItMayAskAgain()
    {
        AllowedInARow("ana@company.test", "10.0.0.1", 3);

        _clock.Advance(TimeSpan.FromMinutes(61));

        _sut.TryRequest("ana@company.test", "10.0.0.1").Should().BeTrue();
    }

    [Fact]
    public void OneAddressHittingItsLimit_DoesNotStopAnother()
    {
        AllowedInARow("ana@company.test", "10.0.0.1", 4);

        _sut.TryRequest("ben@company.test", "10.0.0.2").Should().BeTrue();
    }

    [Fact]
    public void OneCaller_MayAskTenTimesAnHour_AcrossAddresses()
    {
        var allowed = Enumerable.Range(0, 20).Count(i => _sut.TryRequest($"user{i}@company.test", "10.0.0.9"));

        allowed.Should().Be(10);
    }

    [Fact]
    public void TheAddressIsMatchedIgnoringCaseAndSpace()
    {
        _sut.TryRequest("ana@company.test", "10.0.0.1");
        _sut.TryRequest(" ANA@company.test ", "10.0.0.2");
        _sut.TryRequest("Ana@Company.Test", "10.0.0.3");

        _sut.TryRequest("ana@company.test", "10.0.0.4").Should().BeFalse();
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;

        public TestClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
