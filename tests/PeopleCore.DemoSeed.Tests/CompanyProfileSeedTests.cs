using System.Net;
using System.Text;
using FluentAssertions;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;
using PeopleCore.DemoSeed.Seeding;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// The demo fills in Bayanihan Trading's employer details so its 2316s print an employer, but only
/// on a site that has none: a TIN already set means real details the demo must not overwrite.
/// </summary>
public class CompanyProfileSeedTests
{
    private sealed class Site(string storedTin, string roles = """["Employee","Admin"]""") : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, path, body));

            var response = path switch
            {
                "api/auth/login" => $$"""{"token":"t","roles":{{roles}},"mustChangePassword":false}""",
                "api/company-profile" => $$"""{"name":"My Company","tin":"{{storedTin}}"}""",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private static Seeder SeederFor(Site site) =>
        new(new ApiClient(new HttpClient(site) { BaseAddress = new Uri("https://api.test/") }),
            DemoPlan.Build(20260918, new DateOnly(2026, 9, 18)), TextWriter.Null);

    [Fact]
    public async Task A_site_without_a_TIN_gets_Bayanihan_Trading()
    {
        var site = new Site(storedTin: "");

        await SeederFor(site).FillCompanyProfileOnlyAsync("admin@example.test", "pw");

        site.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Put && r.Path == "api/company-profile")
            .Which.Body.Should().Contain("Bayanihan Trading Corporation").And.Contain("123-456-789-00000");
    }

    [Fact]
    public async Task A_site_whose_company_already_has_a_TIN_is_left_alone()
    {
        var site = new Site(storedTin: "987-654-321-00000");

        await SeederFor(site).FillCompanyProfileOnlyAsync("admin@example.test", "pw");

        site.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task A_non_admin_is_refused_before_anything_is_read_or_written()
    {
        var site = new Site(storedTin: "", roles: """["Employee","HRManager"]""");

        var act = () => SeederFor(site).FillCompanyProfileOnlyAsync("hr@example.test", "pw");

        await act.Should().ThrowAsync<PreflightRefusedException>();
        site.Requests.Should().OnlyContain(r => r.Path == "api/auth/login");
    }
}
