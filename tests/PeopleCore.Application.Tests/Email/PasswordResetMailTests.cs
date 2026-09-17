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
