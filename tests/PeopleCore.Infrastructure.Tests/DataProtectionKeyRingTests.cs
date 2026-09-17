using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// The key ring lives in the database, not in the container. A password reset link issued before a
/// restart has to still work after it - which is what building a second provider over the same
/// database stands in for here.
/// </summary>
public class DataProtectionKeyRingTests : DatabaseTestBase
{
    public DataProtectionKeyRingTests(PostgresFixture fixture) : base(fixture) { }

    private ServiceProvider NewHost() =>
        new ServiceCollection()
            .AddSingleton(NewContext())
            .AddDataProtection()
            .SetApplicationName("PeopleCore")
            .PersistKeysToDbContext<AppDbContext>()
            .Services
            .BuildServiceProvider();

    [Fact]
    public async Task APayloadProtectedByOneProcess_IsReadableByTheNext()
    {
        await using var first = NewHost();
        var protectedText = first.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PeopleCore.Test").Protect("reset-token");

        await using var second = NewHost();
        var read = second.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PeopleCore.Test").Unprotect(protectedText);

        read.Should().Be("reset-token");
    }

    [Fact]
    public async Task TheKeyRing_IsStoredInTheDatabase()
    {
        await using var host = NewHost();
        host.GetRequiredService<IDataProtectionProvider>().CreateProtector("PeopleCore.Test").Protect("x");

        await using var read = NewContext();
        read.DataProtectionKeys.Should().NotBeEmpty();
    }
}
