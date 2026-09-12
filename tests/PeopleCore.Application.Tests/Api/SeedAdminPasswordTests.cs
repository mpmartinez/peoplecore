using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using PeopleCore.API.Extensions;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

public class SeedAdminPasswordTests
{
    private static IConfiguration Configuration(string? seedAdminPassword)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Seed:AdminPassword"]).Returns(seedAdminPassword);
        return config.Object;
    }

    private static IHostEnvironment Environment(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }

    [Fact]
    public void Throws_when_production_has_no_password_and_no_admin_exists()
    {
        // The case this guard exists for: the deploy would come up healthy, serve the client,
        // answer /health, and have no account anyone could log in with.
        var act = () => ServiceExtensions.ResolveSeedAdminPassword(
            Configuration(null), Environment("Production"), adminExists: false);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Seed__AdminPassword*");
    }

    [Fact]
    public void Returns_null_when_production_has_no_password_but_an_admin_already_exists()
    {
        // Nothing to seed, so no credential is needed. Throwing here would take a running
        // deployment down the moment the variable was pruned from its environment.
        var password = ServiceExtensions.ResolveSeedAdminPassword(
            Configuration(null), Environment("Production"), adminExists: true);

        password.Should().BeNull();
    }

    [Fact]
    public void Returns_the_configured_password_in_production()
    {
        var password = ServiceExtensions.ResolveSeedAdminPassword(
            Configuration("S3cret-From-Dokploy"), Environment("Production"), adminExists: false);

        password.Should().Be("S3cret-From-Dokploy");
    }

    [Fact]
    public void Falls_back_to_the_well_known_password_in_development()
    {
        // `dotnet run` against a local Postgres has to keep working with no configuration.
        var password = ServiceExtensions.ResolveSeedAdminPassword(
            Configuration(null), Environment("Development"), adminExists: false);

        password.Should().Be(ServiceExtensions.DevelopmentAdminPassword);
    }
}
