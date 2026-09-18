using System.Net;
using System.Text;
using FluentAssertions;
using PeopleCore.DemoSeed.Api;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// The client stops on the first refused request, and says which step, which call, and what the
/// API said. It never repeats a password.
/// </summary>
public class ApiClientTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (ApiClient Client, Handler Handler) Client(HttpStatusCode status, string body)
    {
        var handler = new Handler(status, body);
        return (new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }), handler);
    }

    [Fact]
    public async Task ASuccessfulCall_ReturnsTheJson_AndSendsTheBearerToken()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """{"id":"abc"}""");

        var node = await client.PostAsync("Create thing", "api/things", new { name = "x" }, "tok");

        node!["id"]!.GetValue<string>().Should().Be("abc");
        handler.Last!.Headers.Authorization!.ToString().Should().Be("Bearer tok");
        handler.LastBody.Should().Be("""{"name":"x"}""");
    }

    [Fact]
    public async Task DatesAndTimes_TravelInTheApisFormat()
    {
        var (client, handler) = Client(HttpStatusCode.OK, "{}");

        await client.PostAsync("s", "api/x", new { day = new DateOnly(2026, 3, 9), at = new TimeOnly(8, 0) }, "t");

        handler.LastBody.Should().Be("""{"day":"2026-03-09","at":"08:00:00"}""");
    }

    [Fact]
    public async Task ARefusal_Throws_NamingTheStepCallStatusAndTheApisDetail()
    {
        var (client, _) = Client(HttpStatusCode.BadRequest, """{"title":"Nope","detail":"Insufficient leave balance.","status":400}""");

        var act = () => client.PostAsync("File leave for DEMO-0004", "api/leave-requests", new { }, "t");

        (await act.Should().ThrowAsync<SeedException>()).Which.Message
            .Should().Be("File leave for DEMO-0004: POST api/leave-requests returned 400: Insufficient leave balance.");
    }

    [Theory]
    [InlineData("""{"message":"Invalid credentials."}""", "Invalid credentials.")]
    [InlineData("""{"errors":{"Email":["The Email field is required."]}}""", "Email: The Email field is required.")]
    [InlineData("plain text failure", "plain text failure")]
    [InlineData("", "(no body)")]
    [InlineData("""{"detail":{"nested":true}}""", """{"detail":{"nested":true}}""")]
    [InlineData("""{"errors":"just a string"}""", """{"errors":"just a string"}""")]
    [InlineData("""{"errors":{"Email":[null, 5, "Required."]}}""", "Email: Required.")]
    [InlineData("""{"errors":{}}""", """{"errors":{}}""")]
    [InlineData("""{"message":42}""", """{"message":42}""")]
    public void TheApisMessage_IsFoundWhereverItIs(string body, string expected)
    {
        ApiClient.ReadMessage(body).Should().Be(expected);
    }

    [Fact]
    public async Task AFailedSignIn_DoesNotRepeatThePassword()
    {
        var (client, _) = Client(HttpStatusCode.Unauthorized, """{"message":"Invalid credentials."}""");

        var act = () => client.SignInAsync("admin@example.test", "Sup3rSecret!");

        (await act.Should().ThrowAsync<SeedException>()).Which.Message.Should().NotContain("Sup3rSecret!");
    }

    [Fact]
    public void GeneratedPasswords_MeetThePolicy_AndDiffer()
    {
        var a = Logins.NewPassword();
        var b = Logins.NewPassword();

        a.Length.Should().BeGreaterThanOrEqualTo(12);
        a.Should().MatchRegex(@"\d");
        a.Should().NotBe(b);
    }
}
