using FluentAssertions;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Seeding;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>The checks the seeder makes before it writes anything.</summary>
public class PreflightTests
{
    [Fact]
    public void AnAdminWithTheirOwnPassword_IsAccepted()
    {
        Preflight.AdminProblem(new SignIn("t", ["Employee", "Admin"], MustChangePassword: false)).Should().BeNull();
    }

    [Theory]
    [InlineData("Employee,HRManager")]
    [InlineData("Employee")]
    [InlineData("")]
    [InlineData("Employee,admin")]
    public void AnAccountWithoutTheAdminRole_IsRefused(string roles)
    {
        var signIn = new SignIn("t", roles.Split(',', StringSplitOptions.RemoveEmptyEntries), MustChangePassword: false);

        Preflight.AdminProblem(signIn).Should().StartWith("PEOPLECORE_ADMIN_EMAIL must be an Admin account with its own password");
    }

    [Fact]
    public void AnAdminStillOnATemporaryPassword_IsRefused()
    {
        var signIn = new SignIn("t", ["Employee", "Admin"], MustChangePassword: true);

        Preflight.AdminProblem(signIn).Should().StartWith("PEOPLECORE_ADMIN_EMAIL must be an Admin account with its own password")
            .And.Contain("temporary password");
    }
}
