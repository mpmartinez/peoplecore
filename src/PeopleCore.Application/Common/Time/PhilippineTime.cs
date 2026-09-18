namespace PeopleCore.Application.Common.Time;

/// <summary>
/// "Now" as PeopleCore stores attendance: the Philippine wall-clock time, labelled UTC. A stored
/// <c>2026-03-12T08:07:00Z</c> means 08:07 in Manila - the convention the device sync and the CSV
/// import already write, and the one lateness, undertime and night differential are read against.
/// <para>
/// The offset is a fixed UTC+8 rather than a lookup of <c>Asia/Manila</c>. The Philippines has
/// kept UTC+8 all year without daylight saving since 1978, so the two agree for every instant this
/// app will record; the lookup would only add a way to fail - a container image without tzdata or
/// ICU throws <see cref="TimeZoneNotFoundException"/> - and a clock-in cannot be allowed to fail on
/// that. Were the offset ever to change, a tz database update alone would not be enough anyway:
/// every stored punch is wall-clock time, so the change would need a decision about history too.
/// </para>
/// </summary>
public static class PhilippineTime
{
    public static readonly TimeSpan UtcOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// The current Philippine wall-clock time from <paramref name="clock"/>, with
    /// <see cref="DateTimeKind.Utc"/> (as Npgsql requires for <c>timestamptz</c>) and to the whole
    /// second - nothing reads finer than a minute, and a second round-trips through Postgres exactly.
    /// </summary>
    public static DateTime Now(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var wallClock = clock.GetUtcNow().UtcDateTime + UtcOffset;
        return new DateTime(wallClock.Ticks - wallClock.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }
}
