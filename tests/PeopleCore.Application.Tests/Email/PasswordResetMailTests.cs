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

    private const string EncodedLink = "https://people.example.com/reset-password?email=ana%40company.test&amp;token=abc";

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
        message.Html.Should().Contain(EncodedLink);
    }

    [Fact]
    public void BothBodies_SayTheLinkLastsAnHour_AndThatItCanBeIgnored()
    {
        var message = Message();

        message.Text.Should().Contain("one hour").And.Contain("ignore");
        message.Html.Should().Contain("one hour").And.Contain("ignore");
    }

    [Fact]
    public void TheHtmlBody_EncodesTheLinkInItsHref()
    {
        Message().Html.Should().Contain($"href=\"{EncodedLink}\"");
    }

    // FirstName is editable by HR, so it must not be able to put markup - a link of its own - into a
    // genuine PeopleCore email.
    [Fact]
    public void AMarkupFirstName_IsEscapedInTheHtmlBody()
    {
        const string name = "<a href=https://evil>x</a>";

        var message = PasswordResetMail.For("ana@company.test", name, Link);

        message.Html.Should().NotContain(name)
            .And.Contain("Hello &lt;a href=https://evil&gt;x&lt;/a&gt;,");
    }

    [Fact]
    public void ItIsAddressedToTheAccount()
    {
        var message = Message();

        message.ToAddress.Should().Be("ana@company.test");
        message.ToName.Should().Be("Ana");
    }
}
