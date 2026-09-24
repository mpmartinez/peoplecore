using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;
using PeopleCore.DemoSeed.Seeding;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// The demo's leave types say how final pay treats them: unused Vacation Leave is paid out and
/// counts toward the de minimis ceiling on converted vacation days; Sick Leave is neither.
/// </summary>
public class LeaveTypeSeedTests
{
    /// <summary>
    /// Accepts everything, records each leave type the seeder creates, and stops the run once both
    /// exist - at the SL accrual policy, which is created right after SL itself.
    /// </summary>
    private sealed class Site : HttpMessageHandler
    {
        public List<JsonNode> LeaveTypesCreated { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var method = request.Method.Method;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

            if (method == "POST" && path == "api/leave-types")
                LeaveTypesCreated.Add(JsonNode.Parse(body)!);
            if (method == "POST" && path == "api/leave-accrual-policies" && LeaveTypesCreated.Count == 2)
                return Respond(HttpStatusCode.InternalServerError, """{"detail":"stop here"}""");

            return Respond(HttpStatusCode.OK, (method, path) switch
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
            });
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task VacationLeaveConvertsToCash_AndSickLeaveDoesNot()
    {
        var site = new Site();
        var seeder = new Seeder(
            new ApiClient(new HttpClient(site) { BaseAddress = new Uri("https://api.test/") }),
            DemoPlan.Build(20260918, new DateOnly(2026, 9, 18)), TextWriter.Null);

        var act = () => seeder.RunAsync("admin@example.test", "pw");

        await act.Should().ThrowAsync<SeedException>();
        var byCode = site.LeaveTypesCreated.ToDictionary(t => t["code"]!.GetValue<string>());
        byCode.Keys.Should().BeEquivalentTo(["VL", "SL"]);
        byCode["VL"]["isConvertibleToCash"]!.GetValue<bool>().Should().BeTrue();
        byCode["VL"]["countsAsVacationForDeMinimis"]!.GetValue<bool>().Should().BeTrue();
        byCode["SL"]["isConvertibleToCash"]!.GetValue<bool>().Should().BeFalse();
        byCode["SL"]["countsAsVacationForDeMinimis"]!.GetValue<bool>().Should().BeFalse();
    }

    /// <summary>
    /// Accepts everything, records every call in order with the token it carried, and stops the run
    /// at the first month's accruals - after the leave setup is done. Each sign-in's token names the
    /// email it was issued to, so a call made as the administrator can be told apart.
    /// </summary>
    private sealed class RecordingSite : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Token, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var method = request.Method.Method;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((method, path, request.Headers.Authorization?.Parameter, body));

            if (path == "api/leave-accruals/run-manual")
                return Respond(HttpStatusCode.InternalServerError, """{"detail":"stop here"}""");

            return Respond(HttpStatusCode.OK, (method, path) switch
            {
                ("POST", "api/auth/login") =>
                    $$"""{"token":"token-of-{{JsonNode.Parse(body)!["email"]!.GetValue<string>()}}","roles":["Employee","Admin"],"mustChangePassword":false}""",
                ("POST", "api/auth/change-password") => """{"token":"t2"}""",
                ("POST", "api/users") => $$"""{"account":{"id":"{{Guid.NewGuid()}}"},"temporaryPassword":"Temp1234"}""",
                ("POST", "api/leave-types/statutory") =>
                    """{"added":["SIL","ML","AML","PL","SPL","VAWC","SLW"],"skipped":[]}""",
                ("POST", _) when path.EndsWith("/deactivate") => "{}",
                ("POST", _) => $$"""{"id":"{{Guid.NewGuid()}}"}""",
                ("GET", "api/company-profile") => """{"name":"My Company","tin":""}""",
                ("GET", "api/companies") => $$"""[{"id":"{{Guid.NewGuid()}}","name":"My Company"}]""",
                ("GET", "api/employees" or "api/users") => """{"items":[],"totalCount":0}""",
                ("GET", _) => "[]",
                _ => "{}",
            });
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task AfterVacationAndSickLeave_TheAdminAddsTheStatutorySet_BeforeAnyAccrualRuns()
    {
        var site = new RecordingSite();
        var seeder = new Seeder(
            new ApiClient(new HttpClient(site) { BaseAddress = new Uri("https://api.test/") }),
            DemoPlan.Build(20260918, new DateOnly(2026, 9, 18)), TextWriter.Null);

        var act = () => seeder.RunAsync("admin@example.test", "pw");

        await act.Should().ThrowAsync<SeedException>();
        var calls = site.Calls;
        var statutory = calls.FindIndex(c => c.Method == "POST" && c.Path == "api/leave-types/statutory");
        statutory.Should().BeGreaterThan(-1, "the seeder adds the statutory set");
        calls.Count(c => c.Path == "api/leave-types/statutory").Should().Be(1);
        calls[statutory].Token.Should().Be("token-of-admin@example.test");
        calls[statutory].Body.Should().BeEmpty();

        var leaveTypesCreated = calls.Select((c, i) => (c, i))
            .Where(x => x.c.Method == "POST" && x.c.Path == "api/leave-types").Select(x => x.i).ToList();
        leaveTypesCreated.Should().HaveCount(2);
        leaveTypesCreated.Should().OnlyContain(i => i < statutory, "VL and SL are created first");
        calls.FindIndex(c => c.Path == "api/leave-accruals/run-manual").Should().BeGreaterThan(statutory);
    }
}
