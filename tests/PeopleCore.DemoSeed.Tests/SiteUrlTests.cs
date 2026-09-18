using FluentAssertions;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>The admin password and every demo password cross the wire, so only HTTPS leaves this machine.</summary>
public class SiteUrlTests
{
    [Theory]
    [InlineData("https://peoplecore.m2netsolutions.com")]
    [InlineData("https://peoplecore.m2netsolutions.com/")]
    [InlineData("HTTPS://example.test:8443/")]
    [InlineData("http://localhost:5180")]
    [InlineData("http://LOCALHOST:5180/")]
    [InlineData("http://127.0.0.1:5180")]
    public void HttpsAnywhere_OrPlainHttpOnThisMachine_IsAccepted(string url)
    {
        SiteUrl.Problem(url).Should().BeNull();
    }

    [Theory]
    [InlineData("http://peoplecore.m2netsolutions.com")]
    [InlineData("http://192.168.1.20:5180")]
    [InlineData("http://localhost.example.com")]
    [InlineData("http://127.0.0.2")]
    public void PlainHttpToAnotherMachine_IsRefused(string url)
    {
        SiteUrl.Problem(url).Should().Contain("https://");
    }

    [Theory]
    [InlineData("peoplecore.m2netsolutions.com")]
    [InlineData("ftp://peoplecore.m2netsolutions.com")]
    [InlineData("not a url")]
    public void AnythingThatIsNotAnHttpUrl_IsRefused(string url)
    {
        SiteUrl.Problem(url).Should().NotBeNull();
    }
}
