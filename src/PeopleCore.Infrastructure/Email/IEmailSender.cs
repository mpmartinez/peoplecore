namespace PeopleCore.Infrastructure.Email;

/// <summary>One message, ready to send: both bodies, because mail clients differ.</summary>
public record EmailMessage(string ToAddress, string ToName, string Subject, string Html, string Text);

public interface IEmailSender
{
    /// <summary>Sends through the configured account. Throws when mail is not configured or the server refuses.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
