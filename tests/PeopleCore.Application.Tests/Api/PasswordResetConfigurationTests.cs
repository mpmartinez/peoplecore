using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PeopleCore.API.Extensions;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The reset email says the link lasts one hour. That promise is kept by the API's DI configuration,
/// not by the controller, so it is checked there.
/// </summary>
public class PasswordResetConfigurationTests
{
    [Fact]
    public void AResetLink_LastsExactlyOneHour()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=peoplecore;Username=postgres;Password=postgres",
            ["Jwt:Key"] = new string('k', 32),
            ["DataProtection:KeyEncryptionKey"] = new string('d', 32),
            ["Storage:Provider"] = "Minio",
            ["Minio:Endpoint"] = "localhost:9000",
            ["Minio:AccessKey"] = "minioadmin",
            ["Minio:SecretKey"] = "minioadmin",
            ["Minio:UseSSL"] = "false"
        }).Build();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan
            .Should().Be(TimeSpan.FromHours(1));
    }
}
