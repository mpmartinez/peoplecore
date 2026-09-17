using FluentAssertions;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PeopleCore.API.Extensions;
using PeopleCore.Infrastructure.DataProtection;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The key ring is encrypted with DataProtection:KeyEncryptionKey. Without it the keys would be
/// written in plain text beside the data they protect, so the API refuses to start instead.
/// </summary>
public class DataProtectionConfigurationTests
{
    private static IConfiguration Configuration(string? keyEncryptionKey) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=peoplecore;Username=postgres;Password=postgres",
            ["Jwt:Key"] = new string('k', 32),
            ["DataProtection:KeyEncryptionKey"] = keyEncryptionKey,
            ["Storage:Provider"] = "Minio",
            ["Minio:Endpoint"] = "localhost:9000",
            ["Minio:AccessKey"] = "minioadmin",
            ["Minio:SecretKey"] = "minioadmin",
            ["Minio:UseSSL"] = "false"
        }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingKeyEncryptionKey_StopsStartup_AndSaysHowToSetIt(string? value)
    {
        var build = () => new ServiceCollection().AddLogging().AddInfrastructure(Configuration(value));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*DataProtection:KeyEncryptionKey*")
            .WithMessage("*dotnet user-secrets set*")
            .WithMessage("*DataProtection__KeyEncryptionKey*");
    }

    [Fact]
    public void AShortKeyEncryptionKey_StopsStartup()
    {
        var build = () => new ServiceCollection().AddLogging().AddInfrastructure(Configuration(new string('k', 31)));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*DataProtection:KeyEncryptionKey must be at least 32 bytes*31*");
    }

    [Fact]
    public void AKeyEncryptionKeyOf32Bytes_IsAccepted()
    {
        var build = () => new ServiceCollection().AddLogging().AddInfrastructure(Configuration(new string('k', 32)));

        build.Should().NotThrow();
    }

    [Fact]
    public void TheKeyRing_IsEncryptedBeforeItIsWritten()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddInfrastructure(Configuration("a-key-ring-secret-that-is-comfortably-over-32-bytes"))
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor
            .Should().BeOfType<AesGcmXmlEncryptor>();
        provider.GetService<KeyRingEncryptionKey>().Should().NotBeNull();
    }
}
