using FluentAssertions;
using Moq;
using PeopleCore.Infrastructure.Email;
using Xunit;

namespace PeopleCore.Application.Tests.Email;

/// <summary>
/// SendAsync should fail with a message that points at the real cause, and it should fail before
/// touching the network when the problem is already known from the stored settings.
/// </summary>
public class MailKitEmailSenderTests
{
    private static EmailMessage Message() =>
        new("ana@company.test", "Ana", "Subject", "<p>Html</p>", "Text");

    [Fact]
    public async Task AUsernameWithNoPassword_FailsClearly_BeforeTouchingTheNetwork()
    {
        var store = new Mock<IEmailSettingsStore>();
        store.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailAccount(
                "unreachable.invalid", 587, true, "mailer@example.com", null,
                "noreply@company.test", "PeopleCore", "https://people.example.com"));

        var sender = new MailKitEmailSender(store.Object);

        var act = () => sender.SendAsync(Message());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Re-enter it on the Email settings page*");
    }

    [Fact]
    public async Task NoAccountConfigured_FailsClearly()
    {
        var store = new Mock<IEmailSettingsStore>();
        store.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((MailAccount?)null);

        var sender = new MailKitEmailSender(store.Object);

        var act = () => sender.SendAsync(Message());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("No email account is configured.");
    }
}
