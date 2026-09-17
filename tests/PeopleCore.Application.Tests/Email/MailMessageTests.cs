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
