# Forgot Password Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let someone who has forgotten their password ask for an emailed link and set a new one themselves, with the SMTP account configurable from an Administration page.

**Architecture:** Two anonymous endpoints on `AuthController` issue and consume ASP.NET Identity's own password reset tokens; the Data Protection key ring moves into the database so those tokens and the stored SMTP password survive a restart. Mail goes out through MailKit, using a single-row `email_settings` table edited under Administration behind a new `settings.manage` permission. The web client reuses `ChangePasswordForm` for the reset page.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core 10 / Npgsql (snake_case), MailKit, Blazor WebAssembly, xUnit + Moq + FluentAssertions, bUnit 2.10, real-Postgres fixture for Infrastructure tests.

## Global Constraints

- The answer to `forgot-password` is always, for every outcome: **"If that address has an account, we've sent a link to reset the password."**
- A bad, used or expired link says: **"This link has expired or has already been used. Ask for a new one."**
- Reset links last **one hour** and work **once**.
- A completed reset signs the account out everywhere, clears a lockout, and clears the must-change-password flag.
- A deactivated account gets no mail and no hint.
- Rate limits: **three requests per email address per hour** and **ten per IP address per hour**.
- SMTP defaults to **port 587 with STARTTLS**.
- The permission catalogue gains exactly one key, `settings.manage`, appended last.
- Refusals are `ProblemDetails`; validation failures are 400, permission refusals 403.
- Existing copy, naming and comment style: sentences, no exclamation marks, no em dashes in UI copy.
- Never log a token, a password, or the SMTP password.

---

### Task 1: The `settings.manage` permission

**Files:**
- Modify: `src/PeopleCore.Application/Common/Authorization/Permissions.cs`
- Modify: `src/PeopleCore.Web/Auth/Permissions.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/PermissionsCatalogueTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Permissions.SettingsManage` (`"settings.manage"`) in both the API catalogue and the web mirror.

- [ ] **Step 1: Update the pinned catalogue test**

In `tests/PeopleCore.Application.Tests/Api/PermissionsCatalogueTests.cs`, add the new key to the end of the expected list in `TheCatalogue_HasExactlyTheseKeys_InThisOrder`:

```csharp
        Permissions.AllKeys.Should().Equal(
            "employees.view-all", "employees.manage", "organization.manage", "organization.delete",
            "attendance.manage", "attendance.device-sync", "leave.manage", "leave.run-accruals",
            "approvals.team", "approvals.all", "performance.manage", "payroll.manage",
            "recruitment.manage", "scheduling.manage", "analytics.hr", "analytics.executive",
            "users.manage", "roles.manage", "settings.manage");
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~PermissionsCatalogueTests"`
Expected: FAIL — the catalogue has 18 keys, the test wants 19.

- [ ] **Step 3: Add the key to the API catalogue**

In `src/PeopleCore.Application/Common/Authorization/Permissions.cs`, after the `RolesManage` constant:

```csharp
    public const string SettingsManage = "settings.manage";
```

and at the end of the `All` list, after the `RolesManage` definition:

```csharp
        new(SettingsManage, "Administration", "Manage system settings",
            "The email account the app sends from, and whether a forgotten password can be reset by email."),
```

- [ ] **Step 4: Mirror it in the web client**

`src/PeopleCore.Web/Auth/Permissions.cs` is a copy of the same catalogue, kept equal by `WebPermissionsMirrorTests`. Add the identical constant and the identical `All` entry, in the same position.

- [ ] **Step 5: Run the permission tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~Permission"`
Expected: PASS, including `WebPermissionsMirrorTests`.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Common/Authorization/Permissions.cs src/PeopleCore.Web/Auth/Permissions.cs tests/PeopleCore.Application.Tests/Api/PermissionsCatalogueTests.cs
git commit -m "feat(auth): add the settings.manage permission"
```

---

### Task 2: Data Protection keys in the database

Identity's reset tokens are Data Protection payloads. A container generates a fresh key ring at
startup unless told otherwise, so without this every deploy would void every outstanding link and
make the stored SMTP password unreadable.

**Files:**
- Modify: `src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj`
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Identity/DataProtectionKeyConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Migrations/<timestamp>_AddDataProtectionKeys.cs` (generated)
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Test: `tests/PeopleCore.Infrastructure.Tests/DataProtectionKeyRingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppDbContext` implements `IDataProtectionKeyContext`; `IDataProtectionProvider` is registered and backed by the `data_protection_keys` table; Identity reset tokens expire after one hour.

- [ ] **Step 1: Write the failing test**

Create `tests/PeopleCore.Infrastructure.Tests/DataProtectionKeyRingTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --nologo --filter "FullyQualifiedName~DataProtectionKeyRingTests"`
Expected: FAIL to compile — `PersistKeysToDbContext` and `DataProtectionKeys` do not exist yet.

- [ ] **Step 3: Add the package**

```bash
dotnet add src/PeopleCore.Infrastructure package Microsoft.AspNetCore.DataProtection.EntityFrameworkCore
```

Check the version it picks matches the other `Microsoft.EntityFrameworkCore` packages in that
csproj (the 10.x line). If it resolves to an older major, pin it: `--version 10.0.*`.

- [ ] **Step 4: Let the context hold the keys**

In `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`, add the interface to the class
declaration and the `DbSet`. The class currently reads:

```csharp
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
```

Make it:

```csharp
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>, IDataProtectionKeyContext
```

with `using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;` at the top, and beside the
other `DbSet` properties:

```csharp
    /// <summary>The Data Protection key ring. In the database so reset links survive a restart.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
```

- [ ] **Step 5: Name the table in snake_case like the rest**

Create `src/PeopleCore.Infrastructure/Persistence/Configurations/Identity/DataProtectionKeyConfiguration.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Identity;

public class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.Property(k => k.Id).HasColumnName("id");
        builder.Property(k => k.FriendlyName).HasColumnName("friendly_name");
        builder.Property(k => k.Xml).HasColumnName("xml");
    }
}
```

- [ ] **Step 6: Register it in the API**

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, right after the `AddIdentity(...)
.AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();` block:

```csharp
        // The key ring protects password reset tokens and the stored SMTP password. It lives in the
        // database, and the application name is fixed, so a redeploy does not invalidate either.
        services.AddDataProtection()
            .SetApplicationName("PeopleCore")
            .PersistKeysToDbContext<AppDbContext>();

        // A reset link is worth an hour. Identity's default of a day is too generous for a link
        // that lands in a mailbox.
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(1));
```

with `using Microsoft.AspNetCore.DataProtection;` and `using Microsoft.AspNetCore.Identity;` present.

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add AddDataProtectionKeys --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API --output-dir Persistence/Migrations
```

Read the generated `Up`: it must create `data_protection_keys` and nothing else. If it contains
unrelated changes, the model snapshot was already out of date — stop and report that rather than
committing an unexpected migration.

- [ ] **Step 8: Run the test**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --nologo --filter "FullyQualifiedName~DataProtectionKeyRingTests"`
Expected: PASS, 2 tests.

- [ ] **Step 9: Commit**

```bash
git add src/PeopleCore.Infrastructure src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Infrastructure.Tests/DataProtectionKeyRingTests.cs
git commit -m "feat(identity): keep the data protection key ring in the database"
```

---

### Task 3: The email settings table and store

**Files:**
- Create: `src/PeopleCore.Domain/Entities/System/EmailSettings.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/System/EmailSettingsConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Email/IEmailSettingsStore.cs`
- Create: `src/PeopleCore.Infrastructure/Email/EmailSettingsStore.cs`
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Migrations/<timestamp>_AddEmailSettings.cs` (generated)
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Test: `tests/PeopleCore.Infrastructure.Tests/EmailSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `IDataProtectionProvider` from Task 2.
- Produces, in namespace `PeopleCore.Infrastructure.Email`:
  - `record MailAccount(string Host, int Port, bool UseStartTls, string? Username, string? Password, string FromAddress, string FromName, string AppBaseUrl)`
  - `record MailAccountView(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt)`
  - `interface IEmailSettingsStore`
    - `Task<MailAccount?> GetAccountAsync(CancellationToken ct = default)` — null when nothing is configured; decrypts the password
    - `Task<MailAccountView?> GetViewAsync(CancellationToken ct = default)` — never exposes the password
    - `Task SaveAsync(MailAccount account, CancellationToken ct = default)` — a null `Password` keeps the stored one

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Infrastructure.Tests/EmailSettingsStoreTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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
        new(NewContext(), DataProtectionProvider.Create("PeopleCore.Tests"));

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
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --nologo --filter "FullyQualifiedName~EmailSettingsStoreTests"`
Expected: FAIL to compile — none of these types exist.

- [ ] **Step 3: The entity**

Create `src/PeopleCore.Domain/Entities/System/EmailSettings.cs`:

```csharp
namespace PeopleCore.Domain.Entities.System;

/// <summary>
/// How the application sends mail. Exactly one row, so the settings page edits a thing that always
/// exists rather than a list of one. The password is stored encrypted; see EmailSettingsStore.
/// </summary>
public class EmailSettings
{
    public const int SingleRowId = 1;

    public int Id { get; set; } = SingleRowId;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? PasswordProtected { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "PeopleCore";

    /// <summary>Where reset links point. The web app's address, not the API's.</summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 4: The mapping**

Create `src/PeopleCore.Infrastructure/Persistence/Configurations/System/EmailSettingsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Infrastructure.Persistence.Configurations.System;

public class EmailSettingsConfiguration : IEntityTypeConfiguration<EmailSettings>
{
    public void Configure(EntityTypeBuilder<EmailSettings> builder)
    {
        builder.ToTable("email_settings", t =>
            t.HasCheckConstraint("ck_email_settings_single_row", "id = 1"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Host).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Username).HasMaxLength(200);
        builder.Property(s => s.PasswordProtected).HasMaxLength(2000);
        builder.Property(s => s.FromAddress).HasMaxLength(200).IsRequired();
        builder.Property(s => s.FromName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.AppBaseUrl).HasMaxLength(300).IsRequired();
    }
}
```

Add the `DbSet` to `AppDbContext` beside the others:

```csharp
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
```

with `using PeopleCore.Domain.Entities.System;`.

- [ ] **Step 5: The store**

Create `src/PeopleCore.Infrastructure/Email/IEmailSettingsStore.cs`:

```csharp
namespace PeopleCore.Infrastructure.Email;

/// <summary>The SMTP account the application sends from, with the password decrypted.</summary>
public record MailAccount(
    string Host, int Port, bool UseStartTls, string? Username, string? Password,
    string FromAddress, string FromName, string AppBaseUrl);

/// <summary>The same settings as the administration page may see them: no password, only whether one is stored.</summary>
public record MailAccountView(
    string Host, int Port, bool UseStartTls, string? Username, bool HasPassword,
    string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt);

public interface IEmailSettingsStore
{
    /// <summary>The account to send with, or null when mail has not been configured.</summary>
    Task<MailAccount?> GetAccountAsync(CancellationToken ct = default);

    /// <summary>The settings for display, never carrying the password.</summary>
    Task<MailAccountView?> GetViewAsync(CancellationToken ct = default);

    /// <summary>Saves the single row. A null <see cref="MailAccount.Password"/> keeps the stored one.</summary>
    Task SaveAsync(MailAccount account, CancellationToken ct = default);
}
```

Create `src/PeopleCore.Infrastructure/Email/EmailSettingsStore.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Email;

/// <summary>
/// Reads and writes the one email_settings row. The password is protected with the key ring, which
/// lives in the same database, so a redeploy leaves it readable.
/// </summary>
public class EmailSettingsStore : IEmailSettingsStore
{
    private const string Purpose = "PeopleCore.EmailSettings";

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;

    public EmailSettingsStore(AppDbContext db, IDataProtectionProvider protection)
    {
        _db = db;
        _protector = protection.CreateProtector(Purpose);
    }

    public async Task<MailAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        var row = await _db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Host)) return null;

        return new MailAccount(row.Host, row.Port, row.UseStartTls, row.Username, Reveal(row.PasswordProtected),
            row.FromAddress, row.FromName, row.AppBaseUrl);
    }

    public async Task<MailAccountView?> GetViewAsync(CancellationToken ct = default)
    {
        var row = await _db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null) return null;

        return new MailAccountView(row.Host, row.Port, row.UseStartTls, row.Username,
            !string.IsNullOrEmpty(row.PasswordProtected), row.FromAddress, row.FromName, row.AppBaseUrl, row.UpdatedAt);
    }

    public async Task SaveAsync(MailAccount account, CancellationToken ct = default)
    {
        var row = await _db.EmailSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new EmailSettings();
            _db.EmailSettings.Add(row);
        }

        row.Host = account.Host;
        row.Port = account.Port;
        row.UseStartTls = account.UseStartTls;
        row.Username = account.Username;
        // The settings page never sends the password back, so an absent one means "leave it alone".
        if (account.Password is not null) row.PasswordProtected = _protector.Protect(account.Password);
        row.FromAddress = account.FromAddress;
        row.FromName = account.FromName;
        row.AppBaseUrl = account.AppBaseUrl;
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A payload the current key ring cannot read - keys wiped, or a password written by another
    /// deployment - is treated as no password rather than crashing every send.
    /// </summary>
    private string? Reveal(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword)) return null;
        try
        {
            return _protector.Unprotect(protectedPassword);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 6: Register it**

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, beside `services.AddScoped<IRolePermissionReader, RolePermissionReader>();`:

```csharp
        services.AddScoped<IEmailSettingsStore, EmailSettingsStore>();
```

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add AddEmailSettings --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API --output-dir Persistence/Migrations
```

Expected `Up`: creates `email_settings` with the check constraint, nothing else.

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --nologo --filter "FullyQualifiedName~EmailSettingsStoreTests"`
Expected: PASS, 6 tests.

- [ ] **Step 9: Commit**

```bash
git add src/PeopleCore.Domain src/PeopleCore.Infrastructure src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Infrastructure.Tests/EmailSettingsStoreTests.cs
git commit -m "feat(email): store the SMTP account, with its password encrypted"
```

---

### Task 4: Sending mail

**Files:**
- Modify: `src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj`
- Create: `src/PeopleCore.Infrastructure/Email/IEmailSender.cs`
- Create: `src/PeopleCore.Infrastructure/Email/MailKitEmailSender.cs`
- Create: `src/PeopleCore.Infrastructure/Email/PasswordResetMail.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Test: `tests/PeopleCore.Application.Tests/Email/PasswordResetMailTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Email/MailMessageTests.cs`

**Interfaces:**
- Consumes: `MailAccount`, `IEmailSettingsStore` from Task 3.
- Produces, in `PeopleCore.Infrastructure.Email`:
  - `record EmailMessage(string ToAddress, string ToName, string Subject, string Html, string Text)`
  - `interface IEmailSender { Task SendAsync(EmailMessage message, CancellationToken ct = default); }`
  - `static class PasswordResetMail { public static EmailMessage For(string toAddress, string toName, string link); }`
  - `MailKitEmailSender.BuildMimeMessage(MailAccount account, EmailMessage message)` — internal, used by the tests.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Email/PasswordResetMailTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Infrastructure.Email;
using Xunit;

namespace PeopleCore.Application.Tests.Email;

/// <summary>
/// What lands in the mailbox. The link has to survive an HTML mail client, and the message has to
/// tell someone who did not ask for it that they can ignore it.
/// </summary>
public class PasswordResetMailTests
{
    private const string Link = "https://people.example.com/reset-password?email=ana%40company.test&token=abc";

    private static EmailMessage Message() => PasswordResetMail.For("ana@company.test", "Ana", Link);

    [Fact]
    public void TheSubject_SaysWhatItIs()
    {
        Message().Subject.Should().Be("Reset your PeopleCore password");
    }

    [Fact]
    public void BothBodies_CarryTheLink()
    {
        var message = Message();

        message.Text.Should().Contain(Link);
        message.Html.Should().Contain(Link);
    }

    [Fact]
    public void BothBodies_SayTheLinkLastsAnHour_AndThatItCanBeIgnored()
    {
        var message = Message();

        message.Text.Should().Contain("one hour").And.Contain("ignore");
        message.Html.Should().Contain("one hour").And.Contain("ignore");
    }

    [Fact]
    public void TheHtmlBody_EscapesTheLinkIntoItsHref()
    {
        Message().Html.Should().Contain($"href=\"{Link}\"");
    }

    [Fact]
    public void ItIsAddressedToTheAccount()
    {
        var message = Message();

        message.ToAddress.Should().Be("ana@company.test");
        message.ToName.Should().Be("Ana");
    }
}
```

Create `tests/PeopleCore.Application.Tests/Email/MailMessageTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Infrastructure.Email;
using Xunit;

namespace PeopleCore.Application.Tests.Email;

/// <summary>The account's sender identity is what the recipient sees, not the address it authenticates with.</summary>
public class MailMessageTests
{
    private static readonly MailAccount Account = new(
        "smtp.example.com", 587, true, "mailer@example.com", "s3cret",
        "hr@example.com", "PeopleCore HR", "https://people.example.com");

    [Fact]
    public void TheMessage_ComesFromTheConfiguredSender_AndGoesToTheAccount()
    {
        var mime = MailKitEmailSender.BuildMimeMessage(Account,
            new EmailMessage("ana@company.test", "Ana", "Subject", "<p>Hi</p>", "Hi"));

        mime.From.Mailboxes.Single().Address.Should().Be("hr@example.com");
        mime.From.Mailboxes.Single().Name.Should().Be("PeopleCore HR");
        mime.To.Mailboxes.Single().Address.Should().Be("ana@company.test");
        mime.Subject.Should().Be("Subject");
    }

    [Fact]
    public void TheMessage_CarriesBothATextAndAnHtmlBody()
    {
        var mime = MailKitEmailSender.BuildMimeMessage(Account,
            new EmailMessage("ana@company.test", "Ana", "Subject", "<p>Hi</p>", "Hi"));

        mime.HtmlBody.Should().Be("<p>Hi</p>");
        mime.TextBody.Should().Be("Hi");
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~Email"`
Expected: FAIL to compile — the types do not exist.

- [ ] **Step 3: Add MailKit**

```bash
dotnet add src/PeopleCore.Infrastructure package MailKit
```

- [ ] **Step 4: The interface and the message**

Create `src/PeopleCore.Infrastructure/Email/IEmailSender.cs`:

```csharp
namespace PeopleCore.Infrastructure.Email;

/// <summary>One message, ready to send: both bodies, because mail clients differ.</summary>
public record EmailMessage(string ToAddress, string ToName, string Subject, string Html, string Text);

public interface IEmailSender
{
    /// <summary>Sends through the configured account. Throws when mail is not configured or the server refuses.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
```

- [ ] **Step 5: The reset message**

Create `src/PeopleCore.Infrastructure/Email/PasswordResetMail.cs`:

```csharp
namespace PeopleCore.Infrastructure.Email;

/// <summary>
/// The one message this feature sends. Kept apart from the sender so its wording can be tested
/// without a mail server.
/// </summary>
public static class PasswordResetMail
{
    public static EmailMessage For(string toAddress, string toName, string link)
    {
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Hello," : $"Hello {toName},";

        var text =
            $"""
            {greeting}

            Someone asked to reset the password for your PeopleCore account. Open this link to choose
            a new one. It lasts one hour and works once:

            {link}

            If you did not ask for this, you can ignore this message. Your password stays as it is.
            """;

        var html =
            $"""
            <p>{greeting}</p>
            <p>Someone asked to reset the password for your PeopleCore account.
               Choose a new one here. The link lasts one hour and works once:</p>
            <p><a href="{link}">Set a new password</a></p>
            <p>If you did not ask for this, you can ignore this message. Your password stays as it is.</p>
            """;

        return new EmailMessage(toAddress, toName, "Reset your PeopleCore password", html, text);
    }
}
```

- [ ] **Step 6: The sender**

Create `src/PeopleCore.Infrastructure/Email/MailKitEmailSender.cs`:

```csharp
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PeopleCore.Infrastructure.Email;

/// <summary>
/// Sends through the account in email_settings. A missing account or a refusing server throws:
/// the caller decides whether that is worth telling the user about.
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly IEmailSettingsStore _settings;

    public MailKitEmailSender(IEmailSettingsStore settings) => _settings = settings;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var account = await _settings.GetAccountAsync(ct)
            ?? throw new InvalidOperationException("No email account is configured.");

        using var client = new SmtpClient();
        // StartTls on 587 is what nearly every provider wants, and port 25 is blocked by many hosts.
        var security = account.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(account.Host, account.Port, security, ct);

        if (!string.IsNullOrWhiteSpace(account.Username))
            await client.AuthenticateAsync(account.Username, account.Password ?? string.Empty, ct);

        await client.SendAsync(BuildMimeMessage(account, message), ct);
        await client.DisconnectAsync(true, ct);
    }

    internal static MimeMessage BuildMimeMessage(MailAccount account, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(account.FromName, account.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();
        return mime;
    }
}
```

- [ ] **Step 7: Register it**

In `ServiceExtensions.cs`, beside the settings store:

```csharp
        services.AddScoped<IEmailSender, MailKitEmailSender>();
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~Email"`
Expected: PASS, 7 tests.

- [ ] **Step 9: Commit**

```bash
git add src/PeopleCore.Infrastructure src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Application.Tests/Email
git commit -m "feat(email): send mail through the configured SMTP account"
```

---
### Task 5: The rate limiter

Without this, the endpoint is a free mail cannon aimed at any address someone cares to type, and a
way to grind through addresses looking for accounts.

**Files:**
- Create: `src/PeopleCore.API/Accounts/IResetRequestThrottle.cs`
- Create: `src/PeopleCore.API/Accounts/ResetRequestThrottle.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/ResetRequestThrottleTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, in namespace `PeopleCore.API.Accounts`:
  - `interface IResetRequestThrottle { bool TryRequest(string email, string ipAddress); }`
  - `ResetRequestThrottle(TimeProvider time)` — three per email an hour, ten per IP an hour.

Counters live in memory. The application runs as a single container, and a restart resetting them
costs nothing: the limit exists to stop a flood, not to keep a ledger.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/ResetRequestThrottleTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.API.Accounts;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// How many reset links one address, and one caller, can ask for. Both limits matter: the first
/// stops a mailbox being flooded, the second stops someone walking a list of addresses.
/// </summary>
public class ResetRequestThrottleTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));
    private readonly ResetRequestThrottle _sut;

    public ResetRequestThrottleTests() => _sut = new ResetRequestThrottle(_clock);

    private int AllowedInARow(string email, string ip, int attempts) =>
        Enumerable.Range(0, attempts).Count(_ => _sut.TryRequest(email, ip));

    [Fact]
    public void AnAddress_MayAskThreeTimesAnHour()
    {
        AllowedInARow("ana@company.test", "10.0.0.1", 5).Should().Be(3);
    }

    [Fact]
    public void AnHourLater_ItMayAskAgain()
    {
        AllowedInARow("ana@company.test", "10.0.0.1", 3);

        _clock.Advance(TimeSpan.FromMinutes(61));

        _sut.TryRequest("ana@company.test", "10.0.0.1").Should().BeTrue();
    }

    [Fact]
    public void OneAddressHittingItsLimit_DoesNotStopAnother()
    {
        AllowedInARow("ana@company.test", "10.0.0.1", 4);

        _sut.TryRequest("ben@company.test", "10.0.0.2").Should().BeTrue();
    }

    [Fact]
    public void OneCaller_MayAskTenTimesAnHour_AcrossAddresses()
    {
        var allowed = Enumerable.Range(0, 20).Count(i => _sut.TryRequest($"user{i}@company.test", "10.0.0.9"));

        allowed.Should().Be(10);
    }

    [Fact]
    public void TheAddressIsMatchedIgnoringCaseAndSpace()
    {
        _sut.TryRequest("ana@company.test", "10.0.0.1");
        _sut.TryRequest(" ANA@company.test ", "10.0.0.2");
        _sut.TryRequest("Ana@Company.Test", "10.0.0.3");

        _sut.TryRequest("ana@company.test", "10.0.0.4").Should().BeFalse();
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;

        public TestClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~ResetRequestThrottleTests"`
Expected: FAIL to compile — `ResetRequestThrottle` does not exist.

- [ ] **Step 3: The interface**

Create `src/PeopleCore.API/Accounts/IResetRequestThrottle.cs`:

```csharp
namespace PeopleCore.API.Accounts;

/// <summary>How often a password reset may be asked for, by address and by caller.</summary>
public interface IResetRequestThrottle
{
    /// <summary>True if this request is within both limits, and counts it. False to ignore the request.</summary>
    bool TryRequest(string email, string ipAddress);
}
```

- [ ] **Step 4: The implementation**

Create `src/PeopleCore.API/Accounts/ResetRequestThrottle.cs`:

```csharp
using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _byEmail = new();
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _byIp = new();

    public ResetRequestThrottle(TimeProvider time) => _time = time;

    public bool TryRequest(string email, string ipAddress)
    {
        var now = _time.GetUtcNow();
        var key = email.Trim().ToLowerInvariant();

        // Both counters are checked before either is incremented, so a request refused by one limit
        // does not use up room under the other.
        lock (_byEmail)
        {
            if (Count(_byEmail, key, now) >= PerEmailAnHour) return false;
            if (Count(_byIp, ipAddress, now) >= PerAddressAnHour) return false;

            _byEmail.GetOrAdd(key, _ => []).Add(now);
            _byIp.GetOrAdd(ipAddress, _ => []).Add(now);
            return true;
        }
    }

    private static int Count(ConcurrentDictionary<string, List<DateTimeOffset>> counts, string key, DateTimeOffset now)
    {
        if (!counts.TryGetValue(key, out var times)) return 0;

        times.RemoveAll(at => now - at >= Window);
        if (times.Count == 0) counts.TryRemove(key, out _);
        return times.Count;
    }
}
```

- [ ] **Step 5: Register it**

In `ServiceExtensions.cs`, beside the other registrations:

```csharp
        // Singleton: the counts are the point, and they are per process.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IResetRequestThrottle, ResetRequestThrottle>();
```

If `TimeProvider.System` is already registered elsewhere in the file, do not register it twice.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~ResetRequestThrottleTests"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.API/Accounts/IResetRequestThrottle.cs src/PeopleCore.API/Accounts/ResetRequestThrottle.cs src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Application.Tests/Api/ResetRequestThrottleTests.cs
git commit -m "feat(auth): limit how often a password reset may be asked for"
```

---

### Task 6: The forgot and reset endpoints

**Files:**
- Modify: `src/PeopleCore.API/Controllers/Auth/AuthController.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/PasswordResetTests.cs`

**Interfaces:**
- Consumes: `IEmailSender`, `IEmailSettingsStore`, `PasswordResetMail` (Tasks 3 and 4); `IResetRequestThrottle` (Task 5).
- Produces:
  - `POST api/auth/forgot-password`, body `ForgotPasswordRequest(string Email)` → always 200 `{ message }`
  - `POST api/auth/reset-password`, body `ResetPasswordRequest(string Email, string Token, string NewPassword)` → 200 `{ message }` or 400 `ProblemDetails`
  - `GET api/auth/password-reset-available` → 200 `{ available: bool }`
  - `public record ForgotPasswordRequest(string Email);`
  - `public record ResetPasswordRequest(string Email, string Token, string NewPassword);`

`AuthController`'s constructor grows four parameters: `IEmailSender email`, `IEmailSettingsStore
mailSettings`, `IResetRequestThrottle throttle`, `ILogger<AuthController> log`. Every existing test
that constructs it must be updated — `AuthTokenTests` and `AccountTokenValidatorTests` are the ones
that do; pass `Mock.Of<IEmailSender>()`, `Mock.Of<IEmailSettingsStore>()`,
`Mock.Of<IResetRequestThrottle>()` and `NullLogger<AuthController>.Instance`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/PasswordResetTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Infrastructure.Email;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Asking for a reset link and using one. The answer to a request never says whether the address
/// has an account: not for an unknown address, a deactivated one, a rate-limited caller, or a mail
/// server that refused the message.
/// </summary>
public class PasswordResetTests
{
    private const string Email = "ana@company.test";
    private const string Answer = "If that address has an account, we've sent a link to reset the password.";

    private static readonly MailAccount Account = new(
        "smtp.example.com", 587, true, "mailer@example.com", "s3cret",
        "hr@example.com", "PeopleCore", "https://people.example.com");

    private readonly ApplicationUser _user = new()
    {
        Id = "u1", Email = Email, FirstName = "Ana", SecurityStamp = "stamp-1", IsActive = true
    };

    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IEmailSettingsStore> _mailSettings = new();
    private readonly Mock<IResetRequestThrottle> _throttle = new();
    private readonly AuthController _sut;

    public PasswordResetTests()
    {
        _users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        _users.Setup(u => u.FindByEmailAsync(Email)).ReturnsAsync(_user);
        _users.Setup(u => u.GeneratePasswordResetTokenAsync(_user)).ReturnsAsync("reset-token");
        _users.Setup(u => u.ResetPasswordAsync(_user, "reset-token", It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.SetLockoutEndDateAsync(_user, null)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.ResetAccessFailedCountAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _mailSettings.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Account);
        _throttle.Setup(t => t.TryRequest(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        _sut = new AuthController(_users.Object, signIn.Object, TestJwtConfiguration.Create(),
            Mock.Of<IRolePermissionReader>(), _email.Object, _mailSettings.Object, _throttle.Object,
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    // The endpoints answer with an anonymous object, so the message is read by reflection.
    private static string MessageOf(IActionResult result)
    {
        var value = result.Should().BeOfType<OkObjectResult>().Which.Value!;
        return value.GetType().GetProperty("message")!.GetValue(value)!.ToString()!;
    }

    private static string DetailOf(IActionResult result) =>
        result.Should().BeOfType<BadRequestObjectResult>().Which.Value
            .Should().BeOfType<ProblemDetails>().Subject.Detail!;

    private EmailMessage? Sent() =>
        _email.Invocations.Where(i => i.Method.Name == nameof(IEmailSender.SendAsync))
            .Select(i => (EmailMessage)i.Arguments[0]).SingleOrDefault();

    [Fact]
    public async Task AKnownAddress_IsSentALinkCarryingTheToken()
    {
        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent()!.ToAddress.Should().Be(Email);
        Sent()!.Text.Should()
            .Contain("https://people.example.com/reset-password?")
            .And.Contain("token=reset-token")
            .And.Contain("email=ana%40company.test");
    }

    [Fact]
    public async Task AnUnknownAddress_GetsTheSameAnswer_AndNoMail()
    {
        _users.Setup(u => u.FindByEmailAsync("nobody@company.test")).ReturnsAsync((ApplicationUser?)null);

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest("nobody@company.test"), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
    }

    [Fact]
    public async Task ADeactivatedAccount_GetsTheSameAnswer_AndNoMail()
    {
        _user.IsActive = false;

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
    }

    [Fact]
    public async Task ARateLimitedCaller_GetsTheSameAnswer_AndNoMail()
    {
        _throttle.Setup(t => t.TryRequest(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
        _users.Verify(u => u.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task WithNoMailConfigured_TheAnswerIsTheSame_AndNothingIsSent()
    {
        _mailSettings.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MailAccount?)null);

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
    }

    [Fact]
    public async Task AMailServerThatRefuses_DoesNotChangeTheAnswer()
    {
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("relay refused"));

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
    }

    [Fact]
    public async Task AGoodLink_SetsTheNewPassword()
    {
        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "N3wPassword"));

        MessageOf(result).Should().Be("Your password has been changed. Sign in with your new password.");
        _users.Verify(u => u.ResetPasswordAsync(_user, "reset-token", "N3wPassword"), Times.Once);
    }

    [Fact]
    public async Task AGoodLink_ClearsALockout_AndTheMustChangeFlag()
    {
        _user.MustChangePassword = true;

        await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "N3wPassword"));

        _users.Verify(u => u.SetLockoutEndDateAsync(_user, null), Times.Once);
        _users.Verify(u => u.ResetAccessFailedCountAsync(_user), Times.Once);
        _user.MustChangePassword.Should().BeFalse();
        _users.Verify(u => u.UpdateAsync(_user), Times.Once);
    }

    [Fact]
    public async Task AnExpiredOrUsedLink_SaysSo()
    {
        _users.Setup(u => u.ResetPasswordAsync(_user, "stale", It.IsAny<string>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "stale", "N3wPassword"));

        DetailOf(result).Should().Be("This link has expired or has already been used. Ask for a new one.");
    }

    [Fact]
    public async Task ALinkForAnAddressWithNoAccount_SaysTheSameThing()
    {
        _users.Setup(u => u.FindByEmailAsync("nobody@company.test")).ReturnsAsync((ApplicationUser?)null);

        var result = await _sut.ResetPassword(new ResetPasswordRequest("nobody@company.test", "reset-token", "N3wPassword"));

        DetailOf(result).Should().Be("This link has expired or has already been used. Ask for a new one.");
    }

    [Fact]
    public async Task APasswordThePolicyRefuses_ComesBackWithThePolicysReason()
    {
        _users.Setup(u => u.ResetPasswordAsync(_user, "reset-token", "short"))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." }));

        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "short"));

        DetailOf(result).Should().Be("Passwords must be at least 8 characters.");
    }

    [Fact]
    public async Task WhetherResetsAreAvailable_FollowsWhetherMailIsConfigured()
    {
        var configured = await _sut.PasswordResetAvailable(CancellationToken.None);
        configured.Should().BeOfType<OkObjectResult>().Which.Value!.ToString().Should().Contain("True");

        _mailSettings.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MailAccount?)null);

        var not = await _sut.PasswordResetAvailable(CancellationToken.None);
        not.Should().BeOfType<OkObjectResult>().Which.Value!.ToString().Should().Contain("False");
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~PasswordResetTests"`
Expected: FAIL to compile — the actions and the constructor parameters do not exist.

- [ ] **Step 3: Widen the constructor**

In `src/PeopleCore.API/Controllers/Auth/AuthController.cs`, add the fields and parameters:

```csharp
    private readonly IEmailSender _email;
    private readonly IEmailSettingsStore _mailSettings;
    private readonly IResetRequestThrottle _throttle;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        IRolePermissionReader permissions,
        IEmailSender email,
        IEmailSettingsStore mailSettings,
        IResetRequestThrottle throttle,
        ILogger<AuthController> log)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _permissions = permissions;
        _email = email;
        _mailSettings = mailSettings;
        _throttle = throttle;
        _log = log;
    }
```

with `using PeopleCore.Infrastructure.Email;` added to the file's usings.

- [ ] **Step 4: Add the three actions**

After `ChangePassword`, in the same file:

```csharp
    // Every outcome answers the same way. An answer that varied - "no such account", "too many
    // tries" - would turn this endpoint into a way to find out which addresses are real.
    private const string ResetRequested = "If that address has an account, we've sent a link to reset the password.";

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var caller = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (email.Length == 0 || !_throttle.TryRequest(email, caller))
        {
            _log.LogInformation("Password reset not sent: empty address or rate limit reached.");
            return Ok(new { message = ResetRequested });
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            _log.LogInformation("Password reset not sent: no active account for the address given.");
            return Ok(new { message = ResetRequested });
        }

        var account = await _mailSettings.GetAccountAsync(ct);
        if (account is null)
        {
            _log.LogWarning("Password reset not sent for {UserId}: no email account is configured.", user.Id);
            return Ok(new { message = ResetRequested });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{account.AppBaseUrl.TrimEnd('/')}/reset-password" +
                   $"?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

        try
        {
            await _email.SendAsync(PasswordResetMail.For(user.Email!, user.FirstName ?? string.Empty, link), ct);
            _log.LogInformation("Password reset link sent to {UserId}.", user.Id);
        }
        catch (Exception ex)
        {
            // The user is told nothing either way; an administrator finds out here.
            _log.LogError(ex, "Sending the password reset link to {UserId} failed.", user.Id);
        }

        return Ok(new { message = ResetRequested });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync((request.Email ?? string.Empty).Trim());
        // An address with no account is answered like a stale link, so a link is not a way to ask
        // whether an account exists either.
        if (user is null || !user.IsActive || string.IsNullOrEmpty(request.Token)) return StaleLink();

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword ?? string.Empty);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken")) return StaleLink();
            return PasswordProblem(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // ResetPasswordAsync has replaced the security stamp, so every token issued before now is
        // dead. What is left is the state an administrator's reset also clears.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await _userManager.UpdateAsync(user);
        }

        _log.LogInformation("Password reset completed for {UserId}.", user.Id);
        return Ok(new { message = "Your password has been changed. Sign in with your new password." });
    }

    /// <summary>Lets the login page hide the link rather than send people to a page that cannot deliver.</summary>
    [AllowAnonymous]
    [HttpGet("password-reset-available")]
    public async Task<IActionResult> PasswordResetAvailable(CancellationToken ct) =>
        Ok(new { available = await _mailSettings.GetAccountAsync(ct) is not null });

    private BadRequestObjectResult StaleLink() =>
        BadRequest(new ProblemDetails
        {
            Title = "Link no longer valid",
            Detail = "This link has expired or has already been used. Ask for a new one.",
            Status = StatusCodes.Status400BadRequest
        });
```

and the request records beside the existing ones at the bottom of the file:

```csharp
public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
```

- [ ] **Step 5: Fix the tests that build an AuthController**

`tests/PeopleCore.Application.Tests/Api/AuthTokenTests.cs` and any other file that calls `new
AuthController(...)` must pass the four new arguments:

```csharp
        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create(), _permissions.Object,
            Mock.Of<IEmailSender>(), Mock.Of<IEmailSettingsStore>(), Mock.Of<IResetRequestThrottle>(),
            NullLogger<AuthController>.Instance);
```

Find them with: `grep -rn "new AuthController(" tests/`

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo`
Expected: PASS, including the 12 new ones.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.API/Controllers/Auth/AuthController.cs tests/PeopleCore.Application.Tests/Api
git commit -m "feat(auth): ask for a password reset link, and use one"
```

---

### Task 7: The email settings endpoints

**Files:**
- Create: `src/PeopleCore.API/Controllers/Account/EmailSettingsController.cs`
- Modify: `tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/EmailSettingsControllerTests.cs`

**Interfaces:**
- Consumes: `IEmailSettingsStore`, `IEmailSender`, `MailAccount`, `MailAccountView` (Tasks 3 and 4); `Permissions.SettingsManage` (Task 1).
- Produces, route `api/email-settings`, every action `[RequirePermission(Permissions.SettingsManage)]`:
  - `GET` → `EmailSettingsDto`, or 204 when nothing is configured
  - `PUT` → `EmailSettingsDto`
  - `POST test` → 200 `{ sent = true }` or 400 `ProblemDetails` carrying the server's own error
  - `public record EmailSettingsDto(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt);`
  - `public record SaveEmailSettingsRequest(string Host, int Port, bool UseStartTls, string? Username, string? Password, string FromAddress, string FromName, string AppBaseUrl);`

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/EmailSettingsControllerTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Infrastructure.Email;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Configuring the account the application sends from. The stored password never comes back out,
/// and the test button reports what the server actually said - this is the person fixing it.
/// </summary>
public class EmailSettingsControllerTests
{
    private readonly Mock<IEmailSettingsStore> _store = new();
    private readonly Mock<IEmailSender> _sender = new();
    private readonly EmailSettingsController _sut;

    private static readonly SaveEmailSettingsRequest Valid = new(
        "smtp.example.com", 587, true, "mailer@example.com", "s3cret",
        "hr@example.com", "PeopleCore", "https://people.example.com");

    public EmailSettingsControllerTests()
    {
        _sut = new EmailSettingsController(_store.Object, _sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "admin@company.test")], "test"))
                }
            }
        };
    }

    private static string DetailOf(IActionResult result) =>
        result.Should().BeOfType<BadRequestObjectResult>().Which.Value
            .Should().BeOfType<ProblemDetails>().Subject.Detail!;

    [Fact]
    public async Task WithNothingConfigured_ThereIsNoContent()
    {
        _store.Setup(s => s.GetViewAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MailAccountView?)null);

        (await _sut.Get(CancellationToken.None)).Result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task TheSettingsComeBackWithoutThePassword()
    {
        _store.Setup(s => s.GetViewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new MailAccountView("smtp.example.com", 587, true, "mailer@example.com", true,
                "hr@example.com", "PeopleCore", "https://people.example.com", DateTime.UtcNow));

        var dto = (await _sut.Get(CancellationToken.None)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<EmailSettingsDto>().Subject;

        dto.HasPassword.Should().BeTrue();
        dto.Host.Should().Be("smtp.example.com");
        dto.GetType().GetProperties().Select(p => p.Name).Should().NotContain("Password");
    }

    [Fact]
    public async Task SavingStoresTheAccount()
    {
        _store.Setup(s => s.GetViewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new MailAccountView("smtp.example.com", 587, true, "mailer@example.com", true,
                "hr@example.com", "PeopleCore", "https://people.example.com", DateTime.UtcNow));

        await _sut.Save(Valid, CancellationToken.None);

        _store.Verify(s => s.SaveAsync(It.Is<MailAccount>(a =>
            a.Host == "smtp.example.com" && a.Port == 587 && a.UseStartTls
            && a.Password == "s3cret" && a.FromAddress == "hr@example.com"
            && a.AppBaseUrl == "https://people.example.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", 587, "hr@example.com", "https://people.example.com", "Enter the mail server's address.")]
    [InlineData("smtp.example.com", 0, "hr@example.com", "https://people.example.com", "Enter a port between 1 and 65535.")]
    [InlineData("smtp.example.com", 587, "not-an-address", "https://people.example.com", "Enter the address mail is sent from.")]
    [InlineData("smtp.example.com", 587, "hr@example.com", "people.example.com", "Enter the app's web address, starting with https://.")]
    public async Task WhatIsNotUsable_IsRefusedWithTheReason(string host, int port, string from, string baseUrl, string expected)
    {
        var result = await _sut.Save(Valid with { Host = host, Port = port, FromAddress = from, AppBaseUrl = baseUrl },
            CancellationToken.None);

        DetailOf(result.Result!).Should().Be(expected);
        _store.Verify(s => s.SaveAsync(It.IsAny<MailAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ATestMessage_GoesToTheSignedInAdministrator()
    {
        var result = await _sut.SendTest(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _sender.Verify(s => s.SendAsync(It.Is<EmailMessage>(m =>
            m.ToAddress == "admin@company.test" && m.Subject == "PeopleCore test message"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailedTest_ReportsWhatTheServerSaid()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("535 authentication failed"));

        var result = await _sut.SendTest(CancellationToken.None);

        DetailOf(result).Should().Contain("535 authentication failed");
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo --filter "FullyQualifiedName~EmailSettingsControllerTests"`
Expected: FAIL to compile — the controller does not exist.

- [ ] **Step 3: Write the controller**

Create `src/PeopleCore.API/Controllers/Account/EmailSettingsController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Email;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// The account the application sends mail from. Behind settings.manage, so mail can be configured
/// by someone who manages neither users nor roles. The stored password is never read back out.
/// </summary>
[ApiController]
[Route("api/email-settings")]
[RequirePermission(Permissions.SettingsManage)]
public class EmailSettingsController : ControllerBase
{
    private readonly IEmailSettingsStore _store;
    private readonly IEmailSender _email;

    public EmailSettingsController(IEmailSettingsStore store, IEmailSender email)
    {
        _store = store;
        _email = email;
    }

    [HttpGet]
    public async Task<ActionResult<EmailSettingsDto>> Get(CancellationToken ct) =>
        await _store.GetViewAsync(ct) is { } view ? Ok(ToDto(view)) : NoContent();

    [HttpPut]
    public async Task<ActionResult<EmailSettingsDto>> Save([FromBody] SaveEmailSettingsRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } problem) return SettingsProblem(problem);

        // An empty password means "keep the one already stored": the page never receives it, so it
        // cannot send it back.
        var password = string.IsNullOrEmpty(request.Password) ? null : request.Password;

        await _store.SaveAsync(new MailAccount(
            request.Host.Trim(), request.Port, request.UseStartTls, request.Username?.Trim(), password,
            request.FromAddress.Trim(), request.FromName.Trim(), request.AppBaseUrl.Trim()), ct);

        var saved = await _store.GetViewAsync(ct);
        return Ok(ToDto(saved!));
    }

    /// <summary>Sends to the administrator asking, which is the one address we know is theirs.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken ct)
    {
        var to = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(to)) return SettingsProblem("Your account has no email address to send a test to.");

        try
        {
            await _email.SendAsync(new EmailMessage(to, string.Empty, "PeopleCore test message",
                "<p>PeopleCore can send mail. Nothing else to do.</p>",
                "PeopleCore can send mail. Nothing else to do."), ct);
        }
        catch (Exception ex)
        {
            // The real reason, on purpose: this is the person who can fix it.
            return SettingsProblem($"The mail server refused the message: {ex.Message}");
        }

        return Ok(new { sent = true, to });
    }

    private static string? Validate(SaveEmailSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host)) return "Enter the mail server's address.";
        if (request.Port is < 1 or > 65535) return "Enter a port between 1 and 65535.";
        if (string.IsNullOrWhiteSpace(request.FromAddress) || !request.FromAddress.Contains('@'))
            return "Enter the address mail is sent from.";
        if (string.IsNullOrWhiteSpace(request.FromName)) return "Enter the name mail is sent from.";
        if (!Uri.TryCreate(request.AppBaseUrl?.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            return "Enter the app's web address, starting with https://.";
        return null;
    }

    private static EmailSettingsDto ToDto(MailAccountView view) =>
        new(view.Host, view.Port, view.UseStartTls, view.Username, view.HasPassword,
            view.FromAddress, view.FromName, view.AppBaseUrl, view.UpdatedAt);

    private BadRequestObjectResult SettingsProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Email settings not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });
}

public record EmailSettingsDto(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword,
    string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt);

public record SaveEmailSettingsRequest(string Host, int Port, bool UseStartTls, string? Username, string? Password,
    string FromAddress, string FromName, string AppBaseUrl);
```

- [ ] **Step 4: Tell the equivalence test about the new endpoints**

In `tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs`, add to `AddedEndpoints`:

```csharp
        ["EmailSettingsController.Get"] = [Permissions.SettingsManage],
        ["EmailSettingsController.Save"] = [Permissions.SettingsManage],
        ["EmailSettingsController.SendTest"] = [Permissions.SettingsManage],
```

The three new `AuthController` actions are `[AllowAnonymous]` and need no entry — and the test that
proves endpoints open before permissions are still open covers them.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --nologo`
Expected: PASS, including the 9 new ones and every equivalence test.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.API/Controllers/Account/EmailSettingsController.cs tests/PeopleCore.Application.Tests/Api
git commit -m "feat(api): configure and test the email account"
```

---
### Task 8: The web client's calls

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs`
- Test: `tests/PeopleCore.Web.Tests/Services/ApiClientEmailTests.cs`

**Interfaces:**
- Consumes: the endpoints from Tasks 6 and 7.
- Produces, in namespace `PeopleCore.Web.Services`:
  - `Task<bool> IsPasswordResetAvailableAsync()` — false when the call fails, so a page never breaks over it
  - `Task ForgotPasswordAsync(string email)`
  - `Task ResetPasswordAsync(string email, string token, string newPassword)` — throws with the API's sentence
  - `Task<EmailSettingsDto?> GetEmailSettingsAsync()` — null when nothing is configured (204)
  - `Task<EmailSettingsDto?> SaveEmailSettingsAsync(SaveEmailSettingsRequest request)`
  - `Task<string> SendTestEmailAsync()` — the address it went to; throws with the server's reason
  - `record EmailSettingsDto(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt)`
  - `record SaveEmailSettingsRequest(string Host, int Port, bool UseStartTls, string? Username, string? Password, string FromAddress, string FromName, string AppBaseUrl)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Web.Tests/Services/ApiClientEmailTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>Password resets and the mail settings, over the wire.</summary>
public class ApiClientEmailTests
{
    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    [Fact]
    public async Task WhetherResetsAreAvailable_ComesFromTheApi()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.OK, """{"available":true}""");

        (await CreateClient().IsPasswordResetAvailableAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task WhenThatCallFails_ResetsCountAsUnavailable()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.InternalServerError);

        (await CreateClient().IsPasswordResetAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AskingForALink_PostsTheAddress()
    {
        _api.On(HttpMethod.Post, "/api/auth/forgot-password", HttpStatusCode.OK, """{"message":"If that address has an account, we've sent a link to reset the password."}""");

        await CreateClient().ForgotPasswordAsync("ana@company.test");

        _api.RequestBodies.Single().Should().Contain("ana@company.test");
    }

    [Fact]
    public async Task UsingALink_PostsTheTokenAndTheNewPassword()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.OK, """{"message":"Your password has been changed. Sign in with your new password."}""");

        await CreateClient().ResetPasswordAsync("ana@company.test", "tok", "N3wPassword");

        _api.RequestBodies.Single().Should().Contain("tok").And.Contain("N3wPassword");
    }

    [Fact]
    public async Task AStaleLink_ThrowsWithTheApisSentence()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.BadRequest,
            """{"title":"Link no longer valid","detail":"This link has expired or has already been used. Ask for a new one.","status":400}""");

        var act = () => CreateClient().ResetPasswordAsync("ana@company.test", "stale", "N3wPassword");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*This link has expired or has already been used*");
    }

    [Fact]
    public async Task WithNoMailConfigured_TheSettingsAreNull()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent);

        (await CreateClient().GetEmailSettingsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TheSettings_AreReadAndSaved()
    {
        const string json = """{"host":"smtp.example.com","port":587,"useStartTls":true,"username":"mailer@example.com","hasPassword":true,"fromAddress":"hr@example.com","fromName":"PeopleCore","appBaseUrl":"https://people.example.com","updatedAt":"2026-09-16T09:00:00Z"}""";
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, json)
            .On(HttpMethod.Put, "/api/email-settings", HttpStatusCode.OK, json);
        var client = CreateClient();

        (await client.GetEmailSettingsAsync())!.HasPassword.Should().BeTrue();
        var saved = await client.SaveEmailSettingsAsync(new SaveEmailSettingsRequest(
            "smtp.example.com", 587, true, "mailer@example.com", "s3cret", "hr@example.com", "PeopleCore", "https://people.example.com"));

        saved!.Host.Should().Be("smtp.example.com");
    }

    [Fact]
    public async Task ATestMessage_ReturnsWhereItWent()
    {
        _api.On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.OK, """{"sent":true,"to":"admin@company.test"}""");

        (await CreateClient().SendTestEmailAsync()).Should().Be("admin@company.test");
    }

    [Fact]
    public async Task AFailedTestMessage_ThrowsWithTheServersReason()
    {
        _api.On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.BadRequest,
            """{"title":"Email settings not saved","detail":"The mail server refused the message: 535 authentication failed","status":400}""");

        var act = () => CreateClient().SendTestEmailAsync();

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*535 authentication failed*");
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Web.Tests --nologo --filter "FullyQualifiedName~ApiClientEmailTests"`
Expected: FAIL to compile — the methods do not exist.

- [ ] **Step 3: Add the methods**

In `src/PeopleCore.Web/Services/ApiClient.cs`, after `ChangePasswordAsync`:

```csharp
    /// <summary>
    /// Whether the login page should offer a password reset. A failure counts as "no": better a
    /// missing link than one that leads nowhere.
    /// </summary>
    public async Task<bool> IsPasswordResetAvailableAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/password-reset-available");
            if (!response.IsSuccessStatusCode) return false;
            return (await response.Content.ReadFromJsonAsync<ResetAvailability>(JsonOptions))?.Available ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Asks for a reset link. The answer is the same whether or not the address has an account.</summary>
    public async Task ForgotPasswordAsync(string email)
        => await EnsureSuccessAsync(await _http.PostAsJsonAsync("api/auth/forgot-password", new { email }));

    /// <summary>Sets a new password from a link. A stale link throws with the API's own sentence.</summary>
    public async Task ResetPasswordAsync(string email, string token, string newPassword)
        => await EnsureSuccessAsync(await _http.PostAsJsonAsync("api/auth/reset-password", new { email, token, newPassword }));

    // Email settings
    public async Task<EmailSettingsDto?> GetEmailSettingsAsync()
    {
        var response = await _http.GetAsync("api/email-settings");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<EmailSettingsDto>(JsonOptions);
    }

    public Task<EmailSettingsDto?> SaveEmailSettingsAsync(SaveEmailSettingsRequest request)
        => SendJsonAsync<EmailSettingsDto>(HttpMethod.Put, "api/email-settings", request);

    /// <summary>Sends a test message to the signed-in administrator, returning where it went.</summary>
    public async Task<string> SendTestEmailAsync()
    {
        var response = await _http.PostAsync("api/email-settings/test", content: null);
        await EnsureSuccessAsync(response);
        var sent = await response.Content.ReadFromJsonAsync<TestEmailResult>(JsonOptions);
        return sent?.To ?? string.Empty;
    }
```

and the records beside the other DTO records in that file:

```csharp
public record EmailSettingsDto(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword,
    string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt);

public record SaveEmailSettingsRequest(string Host, int Port, bool UseStartTls, string? Username, string? Password,
    string FromAddress, string FromName, string AppBaseUrl);

internal record ResetAvailability(bool Available);

internal record TestEmailResult(bool Sent, string? To);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --nologo --filter "FullyQualifiedName~ApiClientEmailTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Web/Services/ApiClient.cs tests/PeopleCore.Web.Tests/Services/ApiClientEmailTests.cs
git commit -m "feat(web): call the password reset and email settings endpoints"
```

---

### Task 9: The forgot and reset pages

**Files:**
- Modify: `src/PeopleCore.Web/Components/Accounts/ChangePasswordForm.razor`
- Create: `src/PeopleCore.Web/Pages/Auth/ForgotPassword.razor`
- Create: `src/PeopleCore.Web/Pages/Auth/ResetPassword.razor`
- Modify: `src/PeopleCore.Web/Pages/Auth/Login.razor`
- Test: `tests/PeopleCore.Web.Tests/Pages/ForgotPasswordTests.cs`
- Test: `tests/PeopleCore.Web.Tests/Pages/ResetPasswordTests.cs`
- Test: `tests/PeopleCore.Web.Tests/Pages/LoginTests.cs`

**Interfaces:**
- Consumes: `ForgotPasswordAsync`, `ResetPasswordAsync`, `IsPasswordResetAvailableAsync` (Task 8).
- Produces:
  - `ChangePasswordForm` gains `[Parameter] public string? ResetEmail { get; set; }` and
    `[Parameter] public string? ResetToken { get; set; }`. When `ResetToken` is set the current
    password field is not rendered and the form calls `ResetPasswordAsync`; otherwise nothing about
    it changes.
  - Routes `/forgot-password` and `/reset-password`, both on `EmptyLayout` like `/login`.
  - `/login?reset=1` shows "Your password has been changed. Sign in with your new password."

- [ ] **Step 1: Write the failing page tests**

Create `tests/PeopleCore.Web.Tests/Pages/ForgotPasswordTests.cs`:

```csharp
using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Auth;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages;

/// <summary>
/// Asking for a link. Whatever the address, the page says the same thing - the page must not become
/// the tell the API refuses to be.
/// </summary>
public class ForgotPasswordTests : BunitContext
{
    private const string Answer = "If that address has an account, we've sent a link to reset the password.";

    private readonly StubHttpHandler _api = new();

    public ForgotPasswordTests() => Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));

    private IRenderedComponent<ForgotPassword> Ask(string email)
    {
        var cut = Render<ForgotPassword>();
        cut.Find("#email").Input(email);
        cut.Find("form").Submit();
        return cut;
    }

    [Fact]
    public void AnAddress_IsSentToTheApi_AndTheAnswerIsShown()
    {
        _api.On(HttpMethod.Post, "/api/auth/forgot-password", HttpStatusCode.OK, $$"""{"message":"{{Answer}}"}""");

        var cut = Ask("ana@company.test");

        cut.Find("[data-forgot-result]").TextContent.Should().Contain(Answer);
        _api.RequestBodies.Single().Should().Contain("ana@company.test");
    }

    [Fact]
    public void AnEmptyAddress_IsNotSent()
    {
        var cut = Ask("   ");

        cut.Find("[data-forgot-error]").TextContent.Should().Contain("Enter your email address.");
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public void WhenTheApiFails_ThePageStillSaysTheSameThing()
    {
        _api.On(HttpMethod.Post, "/api/auth/forgot-password", HttpStatusCode.InternalServerError);

        var cut = Ask("ana@company.test");

        cut.Find("[data-forgot-result]").TextContent.Should().Contain(Answer);
    }

    [Fact]
    public void ThereIsAWayBackToSignIn()
    {
        Render<ForgotPassword>().FindAll("a[href='/login']").Should().NotBeEmpty();
    }
}
```

Create `tests/PeopleCore.Web.Tests/Pages/ResetPasswordTests.cs`:

```csharp
using System.Net;
using Blazored.LocalStorage;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.Auth;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages;

/// <summary>Setting a new password from a link.</summary>
public class ResetPasswordTests : BunitContext
{
    private readonly StubHttpHandler _api = new();

    public ResetPasswordTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        Services.AddSingleton(new JwtAuthStateProvider(Mock.Of<ILocalStorageService>()));
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ResetPassword> OpenLink(string email = "ana@company.test", string token = "tok")
    {
        Nav.NavigateTo($"/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
        return Render<ResetPassword>();
    }

    private static void Fill(IRenderedComponent<ResetPassword> cut, string password)
    {
        cut.Find("#new-password").Input(password);
        cut.Find("#confirm-password").Input(password);
        cut.Find("form").Submit();
    }

    [Fact]
    public void TheCurrentPasswordIsNotAskedFor()
    {
        OpenLink().FindAll("#current-password").Should().BeEmpty();
    }

    [Fact]
    public void ANewPassword_IsSentWithTheTokenFromTheLink()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.OK, """{"message":"Your password has been changed. Sign in with your new password."}""");

        Fill(OpenLink(), "N3wPassword");

        _api.RequestBodies.Single().Should().Contain("tok").And.Contain("ana@company.test");
    }

    [Fact]
    public void AfterwardsTheUserIsSentToSignIn()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.OK, """{"message":"Your password has been changed. Sign in with your new password."}""");

        Fill(OpenLink(), "N3wPassword");

        Nav.Uri.Should().EndWith("/login?reset=1");
    }

    [Fact]
    public void AStaleLink_SaysSo_AndOffersAnotherOne()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.BadRequest,
            """{"title":"Link no longer valid","detail":"This link has expired or has already been used. Ask for a new one.","status":400}""");

        var cut = OpenLink();
        Fill(cut, "N3wPassword");

        cut.Find("[data-password-result]").TextContent.Should().Contain("This link has expired or has already been used");
        cut.FindAll("a[href='/forgot-password']").Should().NotBeEmpty();
    }

    [Fact]
    public void ALinkWithNoToken_SaysItIsNotUsable_AndAsksForNothing()
    {
        Nav.NavigateTo("/reset-password");

        var cut = Render<ResetPassword>();

        cut.Find("[data-link-error]").TextContent.Should().Contain("This link has expired or has already been used");
        cut.FindAll("#new-password").Should().BeEmpty();
    }
}
```

Add to `tests/PeopleCore.Web.Tests/Pages/LoginTests.cs`:

```csharp
    [Fact]
    public void TheForgotPasswordLink_ShowsOnlyWhenTheApiCanSendMail()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.OK, """{"available":true}""");

        Render<Login>().FindAll("a[href='/forgot-password']").Should().NotBeEmpty();
    }

    [Fact]
    public void WithNoMailConfigured_ThereIsNoForgotPasswordLink()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.OK, """{"available":false}""");

        Render<Login>().FindAll("a[href='/forgot-password']").Should().BeEmpty();
    }

    [Fact]
    public void AfterAReset_TheLoginPageSaysSo()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?reset=1");

        Render<Login>().Find("[data-reset-done]").TextContent
            .Should().Contain("Your password has been changed. Sign in with your new password.");
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Web.Tests --nologo --filter "FullyQualifiedName~ForgotPasswordTests|FullyQualifiedName~ResetPasswordTests|FullyQualifiedName~LoginTests"`
Expected: FAIL to compile — the pages do not exist.

- [ ] **Step 3: Teach the form its reset mode**

In `src/PeopleCore.Web/Components/Accounts/ChangePasswordForm.razor`, wrap the current password
field so it only renders when there is no token, and add the parameters. Replace the
`FormField` for the current password with:

```razor
    @if (ResetToken is null)
    {
        <FormField LabelText="@CurrentPasswordLabel" Id="current-password" Error="@_currentError">
            <PasswordInput Id="current-password" @bind-Value="_currentPassword"
                           AutoComplete="current-password" HasError="@(_currentError is not null)"
                           Disabled="@_saving" />
        </FormField>
    }
```

Add the parameters beside the existing ones:

```csharp
    /// <summary>Set together with <see cref="ResetEmail"/> to use a link's token instead of the current password.</summary>
    [Parameter] public string? ResetToken { get; set; }

    [Parameter] public string? ResetEmail { get; set; }
```

In `ChangePassword()`, skip the current-password check and call the other endpoint. The validation
block becomes:

```csharp
        _currentError = ResetToken is null && string.IsNullOrEmpty(_currentPassword)
            ? "Enter your current password."
            : null;
        _newError = _newPassword switch
        {
            "" => "Enter a new password.",
            { Length: < MinimumLength } => $"Use at least {MinimumLength} characters.",
            _ when !_newPassword.Any(char.IsDigit) => "Include at least one number.",
            _ when ResetToken is null && _newPassword == _currentPassword => "Choose a password different from your current one.",
            _ => null
        };
```

and the call itself:

```csharp
            if (ResetToken is not null)
            {
                // A reset link stands in for the current password, and there is no session to keep
                // alive: whoever follows the link signs in afterwards.
                await Api.ResetPasswordAsync(ResetEmail ?? string.Empty, ResetToken, _newPassword);
            }
            else
            {
                var session = await Api.ChangePasswordAsync(_currentPassword, _newPassword);
                // Changing the password revoked the token this request was sent with. Keep the new
                // one, or the next call would come back 401 and sign the user out.
                if (session is not null)
                    await AuthProvider.LoginAsync(session.Token);
            }
```

- [ ] **Step 4: The forgot page**

Create `src/PeopleCore.Web/Pages/Auth/ForgotPassword.razor`:

```razor
@page "/forgot-password"
@layout PeopleCore.Web.Components.Layout.EmptyLayout
@inject PeopleCore.Web.Services.ApiClient Api

@*
    Asking for a reset link. The page says the same thing whatever happens, including when the API
    itself fails: anything else would tell a stranger which addresses have accounts.
*@

<div class="min-h-screen flex items-center justify-center bg-muted/30">
    <Card Class="w-full max-w-sm">
        <CardHeader>
            <CardTitle Class="text-2xl text-center">Forgot your password?</CardTitle>
            <p class="text-sm text-muted-foreground text-center mt-1">
                Give us the address you sign in with and we'll send a link to set a new password.
            </p>
        </CardHeader>
        <CardContent>
            @if (_sent)
            {
                <Alert Variant="success" data-forgot-result>
                    If that address has an account, we've sent a link to reset the password.
                </Alert>
                <p class="text-sm text-muted-foreground mt-4">
                    The link lasts one hour. Check the junk folder if it isn't there.
                </p>
            }
            else
            {
                <form class="space-y-4" @onsubmit="Send" @onsubmit:preventDefault>
                    @if (_error is not null)
                    {
                        <Alert Variant="destructive" data-forgot-error>@_error</Alert>
                    }
                    <FormField LabelText="Email" Id="email">
                        <Input TValue="string" Id="email" Type="email" @bind-Value="_email"
                               AutoComplete="username" Placeholder="you@company.com" Disabled="@_sending" />
                    </FormField>
                    <Button Type="submit" Variant="primary" Class="w-full" Loading="@_sending">Send the link</Button>
                </form>
            }
            <p class="text-sm text-center mt-6">
                <a class="text-primary hover:underline" href="/login">Back to sign in</a>
            </p>
        </CardContent>
    </Card>
</div>

@code {
    private string _email = string.Empty;
    private string? _error;
    private bool _sending;
    private bool _sent;

    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(_email))
        {
            _error = "Enter your email address.";
            return;
        }

        _error = null;
        _sending = true;
        try
        {
            await Api.ForgotPasswordAsync(_email.Trim());
        }
        catch (Exception)
        {
            // Even a failure here is answered the same way. An administrator sees the real reason
            // in the API's log.
        }
        finally
        {
            _sending = false;
            _sent = true;
        }
    }
}
```

- [ ] **Step 5: The reset page**

Create `src/PeopleCore.Web/Pages/Auth/ResetPassword.razor`:

```razor
@page "/reset-password"
@layout PeopleCore.Web.Components.Layout.EmptyLayout
@inject NavigationManager Nav

@*
    Setting a new password from an emailed link. The token comes from the query string and is
    dropped from the address bar once read, so it does not sit in browser history.
*@

<div class="min-h-screen flex items-center justify-center bg-muted/30">
    <Card Class="w-full max-w-sm">
        <CardHeader>
            <CardTitle Class="text-2xl text-center">Set a new password</CardTitle>
        </CardHeader>
        <CardContent>
            @if (_token is null)
            {
                <Alert Variant="destructive" data-link-error>
                    This link has expired or has already been used. Ask for a new one.
                </Alert>
                <p class="text-sm text-center mt-6">
                    <a class="text-primary hover:underline" href="/forgot-password">Ask for a new link</a>
                </p>
            }
            else
            {
                <ChangePasswordForm ResetEmail="@_email" ResetToken="@_token"
                                    SubmitText="Set Password" OnChanged="Done" />
                <p class="text-sm text-center mt-6">
                    <a class="text-primary hover:underline" href="/forgot-password">Ask for a new link</a>
                </p>
            }
        </CardContent>
    </Card>
</div>

@code {
    private string? _email;
    private string? _token;

    protected override void OnInitialized()
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query);
        _email = query["email"];
        _token = string.IsNullOrWhiteSpace(query["token"]) ? null : query["token"];

        // Held in fields from here on, so the address bar does not need to carry it.
        if (_token is not null) Nav.NavigateTo("/reset-password", replace: true);
    }

    private void Done() => Nav.NavigateTo("/login?reset=1");
}
```

If `System.Web.HttpUtility` is not available in the WASM project, use
`Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Nav.Uri).Query)` instead, which
that project already references through the components package. Whichever you use, the behaviour
the tests pin is the same.

- [ ] **Step 6: The login page's link and its success note**

In `src/PeopleCore.Web/Pages/Auth/Login.razor`, inside `CardContent` above the alert block:

```razor
            @if (_resetDone)
            {
                <Alert Variant="success" Class="mb-4" data-reset-done>
                    Your password has been changed. Sign in with your new password.
                </Alert>
            }
```

after the password field, before the Sign In button:

```razor
                @if (_resetAvailable)
                {
                    <p class="text-sm text-right -mt-2">
                        <a class="text-primary hover:underline" href="/forgot-password">Forgot password?</a>
                    </p>
                }
```

and in `@code`:

```csharp
    private bool _resetAvailable;
    private bool _resetDone;

    protected override async Task OnInitializedAsync()
    {
        _resetDone = new Uri(Nav.Uri).Query.Contains("reset=1", StringComparison.Ordinal);
        // No link at all is better than one that leads to a page which cannot send anything.
        _resetAvailable = await Api.IsPasswordResetAvailableAsync();
    }
```

- [ ] **Step 7: Run the web tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --nologo`
Expected: PASS, including the 12 new ones. `MyProfile`'s existing change-password tests must still
pass untouched — that is the check that reset mode did not disturb the normal one.

- [ ] **Step 8: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests/Pages
git commit -m "feat(web): ask for a reset link, and set a new password from one"
```

---

### Task 10: The email settings page and its menu entries

**Files:**
- Create: `src/PeopleCore.Web/Pages/Admin/EmailSettings.razor`
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`
- Modify: `src/PeopleCore.Web/Layout/MobileFooterNav.razor`
- Test: `tests/PeopleCore.Web.Tests/Pages/Admin/EmailSettingsTests.cs`
- Test: `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`
- Test: `tests/PeopleCore.Web.Tests/Layout/MobileFooterNavTests.cs`

**Interfaces:**
- Consumes: `GetEmailSettingsAsync`, `SaveEmailSettingsAsync`, `SendTestEmailAsync` (Task 8);
  `Permissions.SettingsManage` (Task 1).
- Produces: the page at `/admin/email`, with hooks `form[data-email-settings]`, `#smtp-host`,
  `#smtp-port`, `#smtp-starttls`, `#smtp-username`, `#smtp-password`, `#from-address`,
  `#from-name`, `#app-base-url`, `[data-email-error]`, `[data-email-result]`,
  `button[data-send-test]`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Web.Tests/Pages/Admin/EmailSettingsTests.cs`:

```csharp
using System.Net;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

/// <summary>
/// Configuring the mail account. The stored password is never shown - the field is left blank and
/// blank means "keep it" - and the test button reports what the server said.
/// </summary>
public class EmailSettingsTests : BunitContext
{
    private const string Configured = """{"host":"smtp.example.com","port":587,"useStartTls":true,"username":"mailer@example.com","hasPassword":true,"fromAddress":"hr@example.com","fromName":"PeopleCore","appBaseUrl":"https://people.example.com","updatedAt":"2026-09-16T09:00:00Z"}""";

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public EmailSettingsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("admin@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("Admin"));
    }

    [Fact]
    public void TheStoredSettings_FillTheForm_ButNotThePassword()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured);

        var cut = Render<EmailSettings>();

        cut.Find("#smtp-host").GetAttribute("value").Should().Be("smtp.example.com");
        cut.Find("#from-address").GetAttribute("value").Should().Be("hr@example.com");
        cut.Find("#smtp-password").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Markup.Should().Contain("A password is stored");
    }

    [Fact]
    public void WithNothingConfigured_TheFormStartsEmpty_WithTheUsualPort()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent);

        var cut = Render<EmailSettings>();

        cut.Find("#smtp-host").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find("#smtp-port").GetAttribute("value").Should().Be("587");
    }

    [Fact]
    public void Saving_SendsWhatWasTyped()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent)
            .On(HttpMethod.Put, "/api/email-settings", HttpStatusCode.OK, Configured);

        var cut = Render<EmailSettings>();
        cut.Find("#smtp-host").Input("smtp.example.com");
        cut.Find("#from-address").Input("hr@example.com");
        cut.Find("#app-base-url").Input("https://people.example.com");
        cut.Find("form[data-email-settings]").Submit();

        _api.RequestBodies.Last().Should().Contain("smtp.example.com").And.Contain("https://people.example.com");
        cut.Find("[data-email-result]").TextContent.Should().Contain("Saved");
    }

    [Fact]
    public void WhatTheApiRefuses_IsShown()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent)
            .On(HttpMethod.Put, "/api/email-settings", HttpStatusCode.BadRequest,
                """{"title":"Email settings not saved","detail":"Enter the mail server's address.","status":400}""");

        var cut = Render<EmailSettings>();
        cut.Find("form[data-email-settings]").Submit();

        cut.Find("[data-email-error]").TextContent.Should().Contain("Enter the mail server's address.");
    }

    [Fact]
    public void TheTestButton_ReportsWhereTheMessageWent()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured)
            .On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.OK, """{"sent":true,"to":"admin@company.test"}""");

        var cut = Render<EmailSettings>();
        cut.Find("button[data-send-test]").Click();

        cut.Find("[data-email-result]").TextContent.Should().Contain("admin@company.test");
    }

    [Fact]
    public void AFailedTest_ShowsTheServersReason()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured)
            .On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.BadRequest,
                """{"title":"Email settings not saved","detail":"The mail server refused the message: 535 authentication failed","status":400}""");

        var cut = Render<EmailSettings>();
        cut.Find("button[data-send-test]").Click();

        cut.Find("[data-email-error]").TextContent.Should().Contain("535 authentication failed");
    }
}
```

In `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`, the Admin link count goes from 18 to 19:

```csharp
        cut.FindAll("a[href]").Should().HaveCount(19);
```

and add:

```csharp
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HRManager", false)]
    [InlineData("Manager", false)]
    public void TheEmailSettingsPage_IsListedOnlyForThoseWhoManageSettings(string role, bool listed)
    {
        Links(RenderAs(role)).Contains("/admin/email").Should().Be(listed);
    }
```

In `tests/PeopleCore.Web.Tests/Layout/MobileFooterNavTests.cs`, add `"admin/email"` to the links an
Admin sees in `AnAdmin_SeesEveryGroup_UnderMore`.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Web.Tests --nologo --filter "FullyQualifiedName~EmailSettingsTests|FullyQualifiedName~NavMenuTests|FullyQualifiedName~MobileFooterNavTests"`
Expected: FAIL — the page and the menu entries do not exist.

- [ ] **Step 3: The page**

Create `src/PeopleCore.Web/Pages/Admin/EmailSettings.razor`:

```razor
@page "/admin/email"
@attribute [RequirePermission(Permissions.SettingsManage)]
@inject ApiClient Api

@*
    The account the application sends from. The stored password is never sent back to the browser:
    the field starts blank, and blank means "keep the one stored".
*@

<PageHeader Title="Email" Section="Administration"
            Description="The account PeopleCore sends mail from, including password reset links." />

@if (_loading)
{
    <p class="text-sm text-muted-foreground">Loading…</p>
}
else
{
    <Card Class="max-w-2xl">
        <CardContent Class="pt-6">
            <form class="space-y-4" data-email-settings @onsubmit="Save" @onsubmit:preventDefault>
                @if (_error is not null)
                {
                    <Alert Variant="destructive" data-email-error>@_error</Alert>
                }
                else if (_result is not null)
                {
                    <Alert Variant="success" data-email-result>@_result</Alert>
                }
                else if (!_configured)
                {
                    <Alert Variant="warning">
                        Mail is not set up yet, so nobody can reset a forgotten password by email.
                    </Alert>
                }

                <div class="grid gap-4 sm:grid-cols-3">
                    <FormField LabelText="Server" Id="smtp-host" Class="min-w-0 sm:col-span-2">
                        <Input TValue="string" Id="smtp-host" @bind-Value="_host" Placeholder="smtp.example.com" Disabled="@_saving" />
                    </FormField>
                    <FormField LabelText="Port" Id="smtp-port" Class="min-w-0">
                        <Input TValue="int" Id="smtp-port" @bind-Value="_port" Type="number" Disabled="@_saving" />
                    </FormField>
                </div>

                <Checkbox Value="@_useStartTls" ValueChanged="@(on => _useStartTls = on)"
                          Disabled="@_saving" id="smtp-starttls">
                    <span>Use STARTTLS</span>
                    <span class="block text-xs font-normal text-muted-foreground">
                        What nearly every provider wants on port 587.
                    </span>
                </Checkbox>

                <div class="grid gap-4 sm:grid-cols-2">
                    <FormField LabelText="Username" Id="smtp-username" Class="min-w-0">
                        <Input TValue="string" Id="smtp-username" @bind-Value="_username"
                               AutoComplete="off" Disabled="@_saving" />
                    </FormField>
                    <FormField LabelText="Password" Id="smtp-password" Class="min-w-0"
                               Description="@(_hasPassword ? "A password is stored. Leave this blank to keep it." : "")">
                        <PasswordInput Id="smtp-password" @bind-Value="_password"
                                       AutoComplete="new-password" Disabled="@_saving" />
                    </FormField>
                </div>

                <div class="grid gap-4 sm:grid-cols-2">
                    <FormField LabelText="Sender address" Id="from-address" Class="min-w-0">
                        <Input TValue="string" Id="from-address" @bind-Value="_fromAddress"
                               Placeholder="hr@example.com" Disabled="@_saving" />
                    </FormField>
                    <FormField LabelText="Sender name" Id="from-name" Class="min-w-0">
                        <Input TValue="string" Id="from-name" @bind-Value="_fromName" Disabled="@_saving" />
                    </FormField>
                </div>

                <FormField LabelText="App address" Id="app-base-url"
                           Description="Where reset links point. The address people open PeopleCore at.">
                    <Input TValue="string" Id="app-base-url" @bind-Value="_appBaseUrl"
                           Placeholder="https://people.example.com" Disabled="@_saving" />
                </FormField>

                <div class="flex justify-end gap-2 pt-2">
                    <Button Type="button" Variant="outline" data-send-test OnClick="SendTest"
                            Disabled="@(!_configured || _saving)" Loading="@_testing">
                        Send test email
                    </Button>
                    <Button Type="submit" Variant="primary" Loading="@_saving">Save</Button>
                </div>
            </form>
        </CardContent>
    </Card>
}

@code {
    private string _host = string.Empty;
    private int _port = 587;
    private bool _useStartTls = true;
    private string? _username;
    private string _password = string.Empty;
    private string _fromAddress = string.Empty;
    private string _fromName = "PeopleCore";
    private string _appBaseUrl = string.Empty;
    private bool _hasPassword;
    private bool _configured;
    private bool _loading = true;
    private bool _saving;
    private bool _testing;
    private string? _error;
    private string? _result;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (await Api.GetEmailSettingsAsync() is { } settings) Apply(settings);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Apply(EmailSettingsDto settings)
    {
        _host = settings.Host;
        _port = settings.Port;
        _useStartTls = settings.UseStartTls;
        _username = settings.Username;
        _fromAddress = settings.FromAddress;
        _fromName = settings.FromName;
        _appBaseUrl = settings.AppBaseUrl;
        _hasPassword = settings.HasPassword;
        _configured = true;
        _password = string.Empty;
    }

    private async Task Save()
    {
        _error = _result = null;
        _saving = true;
        try
        {
            var saved = await Api.SaveEmailSettingsAsync(new SaveEmailSettingsRequest(
                _host, _port, _useStartTls, _username,
                string.IsNullOrEmpty(_password) ? null : _password,
                _fromAddress, _fromName, _appBaseUrl));

            if (saved is not null) Apply(saved);
            _result = "Saved. Send a test message to make sure it works.";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task SendTest()
    {
        _error = _result = null;
        _testing = true;
        try
        {
            var to = await Api.SendTestEmailAsync();
            _result = $"A test message is on its way to {to}.";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _testing = false;
        }
    }
}
```

Check `PageHeader` and `Alert`'s `warning` variant exist by looking at a neighbouring page such as
`src/PeopleCore.Web/Pages/Admin/Roles.razor`; use whatever those pages use. Do not invent a
component.

- [ ] **Step 4: The menu entries**

In `src/PeopleCore.Web/Layout/NavMenu.razor`, the Administration section becomes:

```csharp
        new("Administration", [Permissions.UsersManage, Permissions.RolesManage, Permissions.SettingsManage],
        [
            new("/admin/users", "Users", "bi-person-gear", AnyOf: [Permissions.UsersManage]),
            new("/admin/roles", "Roles", "bi-shield-lock", AnyOf: [Permissions.RolesManage]),
            new("/admin/email", "Email", "bi-envelope", AnyOf: [Permissions.SettingsManage])
        ])
```

In `src/PeopleCore.Web/Layout/MobileFooterNav.razor`, the Administration group gains:

```csharp
            new("admin/email", "Email", [Permissions.SettingsManage])
```

- [ ] **Step 5: Run the web tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --nologo`
Expected: PASS, including the 6 new page tests and the updated menu counts.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): configure the email account from Administration"
```

---

### Task 11: Whole-branch verification

**Files:**
- None, unless a check fails.

- [ ] **Step 1: Build and run every suite**

```bash
dotnet build PeopleCore.slnx
dotnet test tests/PeopleCore.Application.Tests
dotnet test tests/PeopleCore.Infrastructure.Tests
dotnet test tests/PeopleCore.Web.Tests
```

Expected: 0 warnings, 0 errors, everything green. Record the counts.

- [ ] **Step 2: Confirm no secret is logged or returned**

```bash
grep -rnE "LogInformation.*token|LogDebug.*token|Log.*Password" src --include=*.cs
```

Expected: no line that puts a token or a password into a log. `"Password reset link sent to
{UserId}"` and similar are fine; anything interpolating `token`, `_password` or `PasswordProtected`
is not.

- [ ] **Step 3: Smoke-test against a throwaway database and a local mail sink**

Start a mail sink that accepts anything and prints what it received. Save this as
`smtp-sink.py` in the scratchpad — it is not part of the repository:

```python
"""A single-threaded SMTP sink. Prints each message it accepts to stdout."""
import socketserver, sys

class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        self.wfile.write(b"220 sink\r\n")
        body, reading = [], False
        while True:
            line = self.rfile.readline()
            if not line:
                return
            text = line.decode("utf-8", "replace").rstrip("\r\n")
            if reading:
                if text == ".":
                    reading = False
                    print("\n".join(body), flush=True)
                    self.wfile.write(b"250 ok\r\n")
                    body = []
                else:
                    body.append(text)
                continue
            upper = text.upper()
            if upper.startswith("EHLO") or upper.startswith("HELO"):
                self.wfile.write(b"250-sink\r\n250 AUTH LOGIN PLAIN\r\n")
            elif upper.startswith("AUTH"):
                self.wfile.write(b"235 ok\r\n")
            elif upper.startswith("DATA"):
                reading = True
                self.wfile.write(b"354 go\r\n")
            elif upper.startswith("QUIT"):
                self.wfile.write(b"221 bye\r\n")
                return
            else:
                self.wfile.write(b"250 ok\r\n")

socketserver.TCPServer.allow_reuse_address = True
socketserver.TCPServer(("127.0.0.1", 2525), Handler).serve_forever()
```

Run it in the background, then start the API against a fresh database named
`peoplecore_mail_smoke` with `--Seed:AdminPassword=SmokeAdmin2026`, never the developer's
`peoplecore` database, and never commit the launch entry.

Then, over HTTP:

1. **Resets are unavailable before mail is set up.** `GET api/auth/password-reset-available` →
   `available: false`.
2. **Configure mail.** As the admin, `PUT api/email-settings` with host `127.0.0.1`, port `2525`,
   `useStartTls: false`, no username, sender `hr@smoke.test`, name `PeopleCore`, app address
   `http://localhost:5199`. Then `GET api/email-settings` → the values, `hasPassword: false`, and
   **no password field of any kind** in the JSON.
3. **Resets are available now.** `GET api/auth/password-reset-available` → `available: true`.
4. **A test message arrives.** `POST api/email-settings/test` → 200; the sink prints a message
   addressed to the admin.
5. **An unknown address is answered the same way, silently.** `POST api/auth/forgot-password` with
   `nobody@smoke.test` → the standard sentence, and the sink prints nothing.
6. **A real request sends a link.** Create an account `ana@smoke.test`, sign in once to clear the
   temporary password, then `POST api/auth/forgot-password` with that address. The sink prints a
   message; take the link out of it.
7. **The link works once.** `POST api/auth/reset-password` with the email, the token from that link
   and `AnasNewPass1` → 200. Sign in with the new password → 200. Post the same token again → 400
   with "This link has expired or has already been used. Ask for a new one."
8. **The old session is dead.** A token captured before the reset now gets 401.
9. **The rate limit holds.** Ask four times in a row for `ana@smoke.test`; the sink prints three
   messages, not four, and all four answers are identical.
10. **Restarting does not void a link.** Ask for a link, restart the API, then use that link → 200.
    This is the check that the key ring in the database is doing its job.

Stop the API and the sink afterwards.

- [ ] **Step 4: Confirm the tree is clean**

Run: `git status --short`
Expected: empty.

---

## Spec coverage

| Spec requirement | Task |
|---|---|
| `settings.manage` permission, Admin holds it by rule | 1 |
| Data Protection keys in the database, one-hour token lifespan | 2 |
| `email_settings` table, password encrypted, blank keeps the stored one | 3 |
| MailKit sender, reset message wording, link in both bodies | 4 |
| Three per address and ten per IP an hour | 5 |
| `forgot-password` answers identically in every case | 6 |
| Reset clears lockout and the must-change flag, signs out everywhere | 6 |
| "This link has expired or has already been used." | 6 |
| `password-reset-available` so the login page can hide the link | 6, 9 |
| Settings endpoints, test button showing the real SMTP error | 7 |
| Web client calls | 8 |
| `/forgot-password`, `/reset-password`, reused change-password form | 9 |
| Token dropped from the address bar | 9 |
| Administration → Email page and menu entries | 10 |
| Deactivated accounts get nothing | 6 |
| Port 587 with STARTTLS by default | 3, 10 |
