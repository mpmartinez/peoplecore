using FluentAssertions;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The catalogue is the vocabulary every access check, role and token speaks. Its keys are stored
/// in the database and in tokens, so they are pinned here exactly.
/// </summary>
public class PermissionsCatalogueTests
{
    [Fact]
    public void TheCatalogue_HasExactlyTheseKeys_InThisOrder()
    {
        Permissions.AllKeys.Should().Equal(
            "employees.view-all", "employees.manage", "organization.manage", "organization.delete",
            "attendance.manage", "attendance.device-sync", "leave.manage", "leave.run-accruals",
            "approvals.team", "approvals.all", "performance.manage", "payroll.manage",
            "recruitment.manage", "scheduling.manage", "analytics.hr", "analytics.executive",
            "users.manage", "roles.manage", "settings.manage");
    }

    [Fact]
    public void EveryPermission_HasAGroupLabelAndDescription()
    {
        Permissions.All.Should().OnlyContain(p =>
            !string.IsNullOrWhiteSpace(p.Group) && !string.IsNullOrWhiteSpace(p.Label) && !string.IsNullOrWhiteSpace(p.Description));
    }

    [Fact]
    public void APolicyName_ListsItsPermissions_AndParsesBack()
    {
        var name = PermissionPolicy.NameFor(Permissions.ApprovalsTeam, Permissions.ApprovalsAll);

        name.Should().Be("permission:approvals.team|approvals.all");
        PermissionPolicy.TryParse(name, out var anyOf).Should().BeTrue();
        anyOf.Should().Equal("approvals.team", "approvals.all");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("permission:")]
    [InlineData("permission:not.a-permission")]
    public void ANameThatIsNotAPermissionPolicy_DoesNotParse(string name)
    {
        PermissionPolicy.TryParse(name, out _).Should().BeFalse();
    }

    [Fact]
    public void APolicyForNoPermissions_CannotBeNamed()
    {
        var act = () => PermissionPolicy.NameFor();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void APolicyForAKeyNotInTheCatalogue_CannotBeNamed()
    {
        // A typo here would otherwise reach the policy provider, which returns null for an
        // unrecognised policy name and turns every request against the endpoint into a 500.
        var act = () => PermissionPolicy.NameFor("employees.veiw-all");

        act.Should().Throw<ArgumentException>().WithMessage("*employees.veiw-all*");
    }

    [Fact]
    public void ApprovingForEveryone_ImpliesApprovingForTheTeam()
    {
        Permissions.WithImplied([Permissions.ApprovalsAll]).Should().Equal(Permissions.ApprovalsTeam, Permissions.ApprovalsAll);
    }

    [Fact]
    public void NothingElse_IsImplied()
    {
        Permissions.WithImplied([Permissions.PayrollManage, Permissions.ApprovalsTeam]).Should().Equal(Permissions.ApprovalsTeam, Permissions.PayrollManage);
    }
}
