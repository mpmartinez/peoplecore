using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Infrastructure.Persistence;
using System.Security.Cryptography;

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
    private readonly ILogger<EmailSettingsStore> _logger;

    public EmailSettingsStore(AppDbContext db, IDataProtectionProvider protection, ILogger<EmailSettingsStore> logger)
    {
        _db = db;
        _protector = protection.CreateProtector(Purpose);
        _logger = logger;
    }

    public async Task<MailAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        var row = await _db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Host)) return null;

        return new MailAccount(row.Host, row.Port, row.UseStartTls, row.Username, Reveal(row.PasswordProtected, row.Host),
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
    private string? Reveal(string? protectedPassword, string? host)
    {
        if (string.IsNullOrEmpty(protectedPassword)) return null;
        try
        {
            return _protector.Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            _logger.LogWarning(
                "The stored SMTP password for {Host} could not be decrypted with the current key ring. " +
                "It should be re-entered on the Email settings page.", host);
            return null;
        }
    }
}
