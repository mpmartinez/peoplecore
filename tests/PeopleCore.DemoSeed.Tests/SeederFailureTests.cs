using System.Net;
using System.Text;
using FluentAssertions;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;
using PeopleCore.DemoSeed.Seeding;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// A run that fails part-way keeps what it made, says how far it got, and switches off the demo
/// logins it created, without letting that cleanup hide the error that stopped it.
/// </summary>
public class SeederFailureTests
{
    /// <summary>A stand-in site that accepts everything except the requests <c>fail</c> picks out.</summary>
    private sealed class FakeSite(Func<HttpMethod, string, bool> fail) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var method = request.Method;
            Requests.Add((method, path));

            if (fail(method, path)) return Respond(HttpStatusCode.InternalServerError, """{"detail":"boom"}""");

            var body = (method.Method, path) switch
            {
                ("POST", "api/auth/login") => """{"token":"t","roles":["Employee","Admin"],"mustChangePassword":false}""",
                ("POST", "api/auth/change-password") => """{"token":"t2"}""",
                ("POST", "api/users") => $$"""{"account":{"id":"{{Guid.NewGuid()}}"},"temporaryPassword":"Temp1234"}""",
                ("POST", _) when path.EndsWith("/deactivate") => "{}",
                ("POST", _) => $$"""{"id":"{{Guid.NewGuid()}}"}""",
                ("GET", "api/company-profile") => """{"name":"My Company","tin":""}""",
                ("GET", "api/companies") => $$"""[{"id":"{{Guid.NewGuid()}}","name":"My Company"}]""",
                ("GET", "api/employees" or "api/users") => """{"items":[],"totalCount":0}""",
                ("GET", _) => "[]",
                _ => "{}",
            };
            return Respond(HttpStatusCode.OK, body);
        }
    }

    /// <summary>Signs in as an HR Manager, and fails the test on any other request.</summary>
    private sealed class NonAdminSite : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/auth/login", "the role check needs no request after sign-in");
            return Respond(HttpStatusCode.OK, """{"token":"t","roles":["Employee","HRManager"],"mustChangePassword":false}""");
        }
    }

    /// <summary>Answers the login create with <c>createResponse</c>, and every sign-in as an Admin.</summary>
    private sealed class ScriptedUsersSite(string createResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Respond(HttpStatusCode.OK, request.RequestUri!.AbsolutePath == "/api/users"
                ? createResponse
                : """{"token":"t","roles":["Employee","Admin"],"mustChangePassword":false}""");
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static ApiClient Api(HttpMessageHandler site) => new(new HttpClient(site) { BaseAddress = new Uri("https://api.test/") });

    private static Seeder SeederFor(HttpMessageHandler site) =>
        new(Api(site), DemoPlan.Build(20260918, new DateOnly(2026, 9, 18)), TextWriter.Null);

    private static bool CreatingALeaveType(HttpMethod method, string path) => method == HttpMethod.Post && path == "api/leave-types";

    [Fact]
    public async Task AFailureAfterTheLoginsExist_SwitchesEveryOneOff_AndRethrowsTheOriginalError()
    {
        var site = new FakeSite(CreatingALeaveType);
        var seeder = SeederFor(site);

        var act = () => seeder.RunAsync("admin@example.test", "pw");

        (await act.Should().ThrowAsync<SeedException>()).Which.Path.Should().Be("api/leave-types");
        site.Requests.Count(r => r.Path.EndsWith("/deactivate")).Should().Be(20);
        seeder.LoginCleanup.Should().BeEquivalentTo(new LoginCleanup(Created: 20, SwitchedOff: 20, Errors: []));
        seeder.WroteAnything.Should().BeTrue();
        seeder.CountsSoFar["logins"].Should().Be(20);
        seeder.CountsSoFar["employees"].Should().Be(20);
    }

    [Fact]
    public async Task ACleanupThatFails_IsReported_AndNeverMasksTheOriginalError()
    {
        var seeder = SeederFor(new FakeSite((method, path) => CreatingALeaveType(method, path) || path.EndsWith("/deactivate")));

        var act = () => seeder.RunAsync("admin@example.test", "pw");

        (await act.Should().ThrowAsync<SeedException>()).Which.Path.Should().Be("api/leave-types");
        seeder.LoginCleanup!.Created.Should().Be(20);
        seeder.LoginCleanup.SwitchedOff.Should().Be(0);
        seeder.LoginCleanup.Errors.Should().HaveCount(20).And.OnlyContain(e => e.Contains("boom"));
    }

    [Fact]
    public async Task AFailureBeforeAnyLoginExists_HasNothingToSwitchOff()
    {
        var site = new FakeSite((method, path) => method == HttpMethod.Post && path == "api/departments");
        var seeder = SeederFor(site);

        var act = () => seeder.RunAsync("admin@example.test", "pw");

        await act.Should().ThrowAsync<SeedException>();
        site.Requests.Should().NotContain(r => r.Path.EndsWith("/deactivate"));
        seeder.LoginCleanup.Should().BeEquivalentTo(new LoginCleanup(0, 0, []));
        seeder.WroteAnything.Should().BeTrue();
    }

    [Fact]
    public async Task ARefusalInPreflight_WritesNothing_AndHasNothingToClean()
    {
        var seeder = SeederFor(new NonAdminSite());

        var act = () => seeder.RunAsync("hr@example.test", "pw");

        await act.Should().ThrowAsync<PreflightRefusedException>();
        seeder.WroteAnything.Should().BeFalse();
        seeder.LoginCleanup.Should().BeNull();
        seeder.CountsSoFar.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"temporaryPassword":"x"}""", "account.id")]
    [InlineData("""{"account":{"id":"abc"}}""", "temporaryPassword")]
    [InlineData("""{"account":{"id":7},"temporaryPassword":"x"}""", "account.id")]
    public async Task ALoginCreateResponseWithoutItsFields_StopsTheRunNamingTheStep(string createResponse, string missing)
    {
        var logins = new Logins(Api(new ScriptedUsersSite(createResponse)));
        await logins.AddAdminAsync("admin@example.test", "pw");

        var act = () => logins.CreateAsync(4, Guid.NewGuid(), "x@y.test", "X", "Y", "Employee");

        var error = (await act.Should().ThrowAsync<SeedException>()).Which;
        error.Step.Should().Be("Create a login for DEMO-0004");
        error.Message.Should().Contain(missing);
    }

    [Fact]
    public async Task AChangePasswordResponseWithoutAToken_StopsTheRunNamingTheStep()
    {
        // The create succeeds; the change-password answer has no token.
        var changeWithoutToken = new Logins(Api(new NoTokenOnChangeSite()));
        await changeWithoutToken.AddAdminAsync("admin@example.test", "pw");

        var act = () => changeWithoutToken.CreateAsync(4, Guid.NewGuid(), "x@y.test", "X", "Y", "Employee");

        var error = (await act.Should().ThrowAsync<SeedException>()).Which;
        error.Step.Should().Be("First password change for DEMO-0004");
        error.Message.Should().Contain("token");
        changeWithoutToken.CreatedUserIds.Should().ContainKey(4).WhoseValue.Should().Be("abc");
    }

    private sealed class NoTokenOnChangeSite : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Respond(HttpStatusCode.OK, request.RequestUri!.AbsolutePath switch
            {
                "/api/users" => """{"account":{"id":"abc"},"temporaryPassword":"x"}""",
                "/api/auth/change-password" => "{}",
                _ => """{"token":"t","roles":["Employee","Admin"],"mustChangePassword":false}""",
            });
    }
}
