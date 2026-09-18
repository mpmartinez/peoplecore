using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PeopleCore.Infrastructure.Email;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// The one row that says how the app sends mail. The password is encrypted at rest, and saving
/// without one keeps the password already stored - the settings page never sends it back.
/// </summary>
public class EmailSettingsStoreTests : DatabaseTestBase
{
    public EmailSettingsStoreTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly MailAccount Account = new(
        "smtp.example.com", 587, true, "mailer@example.com", "s3cret",
        "hr@example.com", "PeopleCore", "https://people.example.com");

    private EmailSettingsStore Store() =>
        new(NewContext(), DataProtectionProvider.Create("PeopleCore.Tests"), NullLogger<EmailSettingsStore>.Instance);

    [Fact]
    public async Task WithNothingConfigured_ThereIsNoAccount()
    {
        (await Store().GetAccountAsync()).Should().BeNull();
        (await Store().GetViewAsync()).Should().BeNull();
    }

    [Fact]
    public async Task WhatIsSaved_ComesBack()
    {
        await Store().SaveAsync(Account);

        (await Store().GetAccountAsync()).Should().BeEquivalentTo(Account);
    }

    [Fact]
    public async Task TheStoredPassword_IsNotReadableInTheDatabase()
    {
        await Store().SaveAsync(Account);

        await using var read = NewContext();
        var stored = await read.EmailSettings.SingleAsync();
        stored.PasswordProtected.Should().NotBeNullOrEmpty().And.NotContain("s3cret");
    }

    [Fact]
    public async Task SavingWithoutAPassword_KeepsTheOneAlreadyStored()
    {
        await Store().SaveAsync(Account);

        await Store().SaveAsync(Account with { Password = null, Host = "smtp2.example.com" });

        var account = await Store().GetAccountAsync();
        account!.Password.Should().Be("s3cret");
        account.Host.Should().Be("smtp2.example.com");
    }

    [Fact]
    public async Task TheViewNeverCarriesThePassword_OnlyWhetherThereIsOne()
    {
        await Store().SaveAsync(Account);

        var view = await Store().GetViewAsync();

        view!.HasPassword.Should().BeTrue();
        view.Host.Should().Be("smtp.example.com");
        view.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SavingTwice_LeavesOneRow()
    {
        await Store().SaveAsync(Account);
        await Store().SaveAsync(Account with { Host = "smtp3.example.com" });

        await using var read = NewContext();
        (await read.EmailSettings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task APasswordTheKeyRingCannotRead_IsTreatedAsNoPassword()
    {
        await Store().SaveAsync(Account);

        await using (var write = NewContext())
        {
            var row = await write.EmailSettings.SingleAsync();
            row.PasswordProtected = "not-a-protected-payload";
            await write.SaveChangesAsync();
        }

        var account = await Store().GetAccountAsync();
        account.Should().BeEquivalentTo(Account with { Password = null });
    }
}
