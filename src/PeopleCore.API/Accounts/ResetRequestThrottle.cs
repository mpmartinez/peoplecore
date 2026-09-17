namespace PeopleCore.API.Accounts;

/// <summary>
/// Three requests per address and ten per caller an hour, counted in memory. The application runs
/// as one container; a restart forgetting the counts is not worth a table. Both limits are needed:
/// the first keeps one mailbox from being flooded, the second keeps one caller from walking a list
/// of addresses to see which ones exist.
/// </summary>
public class ResetRequestThrottle : IResetRequestThrottle
{
    private const int PerEmailAnHour = 3;
    private const int PerAddressAnHour = 10;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _byEmail = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _byIp = new();

    public ResetRequestThrottle(TimeProvider time) => _time = time;

    public bool TryRequest(string email, string ipAddress)
    {
        var now = _time.GetUtcNow();
        var key = email.Trim().ToLowerInvariant();

        // Both counters are checked before either is incremented, so a request refused by one limit
        // does not use up room under the other. All access to either dictionary happens inside this
        // single lock, which is what makes the plain (non-concurrent) dictionaries safe to share.
        lock (_gate)
        {
            if (Count(_byEmail, key, now) >= PerEmailAnHour) return false;
            if (Count(_byIp, ipAddress, now) >= PerAddressAnHour) return false;

            Add(_byEmail, key, now);
            Add(_byIp, ipAddress, now);
            return true;
        }
    }

    private static void Add(Dictionary<string, List<DateTimeOffset>> counts, string key, DateTimeOffset now)
    {
        if (!counts.TryGetValue(key, out var times))
        {
            times = [];
            counts[key] = times;
        }

        times.Add(now);
    }

    private static int Count(Dictionary<string, List<DateTimeOffset>> counts, string key, DateTimeOffset now)
    {
        if (!counts.TryGetValue(key, out var times)) return 0;

        times.RemoveAll(at => now - at >= Window);
        if (times.Count == 0) counts.Remove(key);
        return times.Count;
    }
}
