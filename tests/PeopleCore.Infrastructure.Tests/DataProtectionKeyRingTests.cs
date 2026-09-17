using System.Security.Cryptography;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Infrastructure.DataProtection;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// The key ring lives in the database, not in the container. A password reset link issued before a
/// restart has to still work after it - which is what building a second provider over the same
/// database stands in for here. The keys are encrypted with a secret held outside the database, so
/// the database on its own is not enough to read them.
/// </summary>
public class DataProtectionKeyRingTests : DatabaseTestBase
{
    private const string Secret = "a-key-ring-secret-that-is-comfortably-over-32-bytes";

    public DataProtectionKeyRingTests(PostgresFixture fixture) : base(fixture) { }

    private ServiceProvider NewHost(string secret = Secret) =>
        new ServiceCollection()
            .AddSingleton(NewContext())
            .AddDataProtection()
            .SetApplicationName("PeopleCore")
            .PersistKeysToDbContext<AppDbContext>()
            .ProtectKeysWithKeyRingEncryptionKey(KeyRingEncryptionKey.FromSecret(secret))
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

    // Unencrypted, the framework writes the master key as <masterKey><value>base64</value></masterKey>.
    // Encrypted, that whole element is replaced by an <encryptedSecret> naming the decryptor.
    [Fact]
    public async Task TheStoredKey_CarriesNoPlaintextMasterKey()
    {
        await using var host = NewHost();
        host.GetRequiredService<IDataProtectionProvider>().CreateProtector("PeopleCore.Test").Protect("x");

        await using var read = NewContext();
        var stored = await read.DataProtectionKeys.AsNoTracking().ToListAsync();

        stored.Should().NotBeEmpty();
        foreach (var row in stored)
        {
            var xml = XElement.Parse(row.Xml!);
            xml.Descendants().Where(e => e.Name.LocalName == "masterKey").Should().BeEmpty();
            xml.Descendants().Where(e => e.Name.LocalName == "value").Should().BeEmpty();
            xml.Descendants().Should().ContainSingle(e => e.Name.LocalName == "encryptedSecret")
                .Which.Attribute("decryptorType")!.Value.Should().Contain(nameof(AesGcmXmlDecryptor));
            row.Xml.Should().NotContain("unencrypted form");
        }
    }

    // A database whose keys were written before encryption was switched on, such as a developer's.
    [Fact]
    public async Task AKeyWrittenBeforeEncryption_StillLoads()
    {
        await using var before = new ServiceCollection()
            .AddSingleton(NewContext())
            .AddDataProtection()
            .SetApplicationName("PeopleCore")
            .PersistKeysToDbContext<AppDbContext>()
            .Services
            .BuildServiceProvider();
        var protectedText = before.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PeopleCore.Test").Protect("reset-token");

        await using var after = NewHost();
        var read = after.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PeopleCore.Test").Unprotect(protectedText);

        read.Should().Be("reset-token");
    }

    [Fact]
    public async Task AHostWithADifferentSecret_CannotReadThePayload()
    {
        await using var first = NewHost();
        var protectedText = first.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PeopleCore.Test").Protect("reset-token");

        await using var second = NewHost("a-different-secret-that-is-also-over-32-bytes-long");
        var read = () => second.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PeopleCore.Test").Unprotect(protectedText);

        // The GCM tag check fails and the framework lets that exception through. It derives from
        // CryptographicException, which is what EmailSettingsStore catches.
        read.Should().ThrowExactly<AuthenticationTagMismatchException>()
            .Which.Should().BeAssignableTo<CryptographicException>();
    }

    // Rotating the secret costs the outstanding links and the stored SMTP password, not the app: the
    // unreadable key is passed over and a new one is created.
    [Fact]
    public async Task AHostWithADifferentSecret_StillProtectsNewPayloads()
    {
        await using var first = NewHost();
        first.GetRequiredService<IDataProtectionProvider>().CreateProtector("PeopleCore.Test").Protect("old");

        await using var second = NewHost("a-different-secret-that-is-also-over-32-bytes-long");
        var protector = second.GetRequiredService<IDataProtectionProvider>().CreateProtector("PeopleCore.Test");

        protector.Unprotect(protector.Protect("new")).Should().Be("new");
    }

    private static AesGcmXmlDecryptor NewDecryptor() =>
        new(new ServiceCollection().AddSingleton(KeyRingEncryptionKey.FromSecret(Secret)).BuildServiceProvider());

    // Corrupted storage - a bad row, a truncated backup - must not surface as anything other than
    // CryptographicException: EmailSettingsStore.Reveal only catches that, and the forgot-password
    // and password-reset-available endpoints depend on it degrading rather than 500ing.
    [Fact]
    public void InvalidBase64_IsReportedAsCryptographicException()
    {
        var element = new XElement("aesGcmEncryptedSecret",
            new XElement("nonce", "not-valid-base64!!"),
            new XElement("tag", Convert.ToBase64String(new byte[16])),
            new XElement("ciphertext", Convert.ToBase64String(new byte[16])));

        var decrypt = () => NewDecryptor().Decrypt(element);

        decrypt.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ATruncatedNonce_IsReportedAsCryptographicException()
    {
        var element = new XElement("aesGcmEncryptedSecret",
            new XElement("nonce", Convert.ToBase64String(new byte[4])),
            new XElement("tag", Convert.ToBase64String(new byte[16])),
            new XElement("ciphertext", Convert.ToBase64String(new byte[16])));

        var decrypt = () => NewDecryptor().Decrypt(element);

        decrypt.Should().Throw<CryptographicException>();
    }
}
