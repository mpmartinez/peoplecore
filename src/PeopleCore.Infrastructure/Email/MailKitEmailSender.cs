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
    // The forgot-password request waits on this send, so a slow or wrong SMTP host must not hold it
    // open for MailKit's default (multi-minute) timeout.
    private const int TimeoutMilliseconds = 15_000;

    private readonly IEmailSettingsStore _settings;

    public MailKitEmailSender(IEmailSettingsStore settings) => _settings = settings;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var account = await _settings.GetAccountAsync(ct)
            ?? throw new InvalidOperationException("No email account is configured.");

        if (!string.IsNullOrWhiteSpace(account.Username) && string.IsNullOrEmpty(account.Password))
            throw new InvalidOperationException(
                "No SMTP password is available. Re-enter it on the Email settings page.");

        using var client = new SmtpClient();
        client.Timeout = TimeoutMilliseconds;
        // StartTls on 587 is what nearly every provider wants, and port 25 is blocked by many hosts.
        // Auto (used when the box is unticked) gives implicit TLS on port 465 and opportunistic
        // STARTTLS everywhere else, so unticking it never forces plain text when the server offers encryption.
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
