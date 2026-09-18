using System.Net;

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
        // The name is editable by HR users, so in HTML it is text, never markup.
        var htmlGreeting = WebUtility.HtmlEncode(greeting);
        var htmlLink = WebUtility.HtmlEncode(link);

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
            <p>{htmlGreeting}</p>
            <p>Someone asked to reset the password for your PeopleCore account.
               Choose a new one here. The link lasts one hour and works once:</p>
            <p><a href="{htmlLink}">Set a new password</a></p>
            <p>If you did not ask for this, you can ignore this message. Your password stays as it is.</p>
            """;

        return new EmailMessage(toAddress, toName, "Reset your PeopleCore password", html, text);
    }
}
