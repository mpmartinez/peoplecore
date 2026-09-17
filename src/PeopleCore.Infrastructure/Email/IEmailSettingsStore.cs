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
