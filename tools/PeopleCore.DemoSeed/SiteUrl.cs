namespace PeopleCore.DemoSeed;

/// <summary>
/// The site the seeder writes to. The admin password, and a password for every demo login, go over
/// this connection, so plain HTTP is allowed only to this machine, for a rehearsal against a local API.
/// </summary>
internal static class SiteUrl
{
    /// <summary>Why <paramref name="url"/> can't be used, or null when it can.</summary>
    public static string? Problem(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return $"PEOPLECORE_URL must be a full https:// address, such as https://peoplecore.example.com; got \"{url}\".";

        if (uri.Scheme == Uri.UriSchemeHttp && !IsThisMachine(uri))
            return $"PEOPLECORE_URL must start with https://; plain http:// is allowed only for localhost or 127.0.0.1, and {uri.Host} is neither.";

        return null;
    }

    private static bool IsThisMachine(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1";
}
