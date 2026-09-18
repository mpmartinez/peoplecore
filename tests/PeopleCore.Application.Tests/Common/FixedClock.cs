using System.Globalization;

namespace PeopleCore.Application.Tests.Common;

/// <summary>A <see cref="TimeProvider"/> stuck at one instant, which a test can move.</summary>
public sealed class FixedClock : TimeProvider
{
    public FixedClock(DateTimeOffset now) => Now = now;

    public FixedClock(string isoInstant) : this(DateTimeOffset.Parse(isoInstant, CultureInfo.InvariantCulture)) { }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
