using Microsoft.Extensions.Configuration;

namespace PeopleCore.Application.Tests.Api;

/// <summary>Enough configuration for AuthController to sign a real token.</summary>
public static class TestJwtConfiguration
{
    public static IConfiguration Create() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "a-test-signing-key-that-is-comfortably-over-32-bytes",
            ["Jwt:Issuer"] = "peoplecore-tests",
            ["Jwt:Audience"] = "peoplecore-tests",
            ["Jwt:ExpiryMinutes"] = "60"
        })
        .Build();
}
