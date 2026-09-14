using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Changing an existing account. Each change that succeeds replaces the security stamp, so the
/// account's existing tokens stop working and the next sign-in reflects the change.
/// </summary>
public class UsersControllerChangeTests : UsersControllerTestBase
{
    public UsersControllerChangeTests()
    {
        Users.Setup(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
        Users.Setup(u => u.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
    }

    private void StampWasReplaced(ApplicationUser user) =>
        Users.Verify(u => u.UpdateSecurityStampAsync(user), Times.Once);

    private void StampWasNotReplaced() =>
        Users.Verify(u => u.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()), Times.Never);

    private static IEnumerable<string> Exactly(params string[] roles) =>
        It.Is<IEnumerable<string>>(r => r.OrderBy(x => x).SequenceEqual(roles.OrderBy(x => x)));

    // --- Roles ----------------------------------------------------------------------------------

    [Fact]
    public async Task SetRoles_GrantsTheNewRoles_RemovesTheDroppedOnes_AndRevokesTokens()
    {
        var user = Account("Employee", "PayrollService");

        var result = await Sut.SetRoles(TargetId, new SetRolesRequest(["Manager"]), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        Users.Verify(u => u.AddToRolesAsync(user, Exactly("Manager")), Times.Once);
        Users.Verify(u => u.RemoveFromRolesAsync(user, Exactly("PayrollService")), Times.Once);
        StampWasReplaced(user);
    }

    [Fact]
    public async Task SetRoles_NeverRemovesEmployee()
    {
        var user = Account("Employee", "Manager");

        await Sut.SetRoles(TargetId, new SetRolesRequest([]), CancellationToken.None);

        Users.Verify(u => u.RemoveFromRolesAsync(user, Exactly("Manager")), Times.Once);
        Users.Verify(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task SetRoles_LeavesARoleNobodyCanAssignInPlace()
    {
        var user = Account("Employee", "Service");

        await Sut.SetRoles(TargetId, new SetRolesRequest(["Employee"]), CancellationToken.None);

        Users.Verify(u => u.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        StampWasReplaced(user);
    }

    [Fact]
    public async Task SetRoles_WithARoleNobodyCanAssign_IsRejected()
    {
        Account("Employee");

        var result = await Sut.SetRoles(TargetId, new SetRolesRequest(["Service"]), CancellationToken.None);

        BadRequestDetail(result).Should().Be("Service is not a role that can be assigned.");
        StampWasNotReplaced();
    }

    [Fact]
    public async Task SetRoles_ByHrOnAnHrManager_IsRefused()
    {
        SignInAs("HRManager", "Employee");
        Account("HRManager", "Employee");

        var result = await Sut.SetRoles(TargetId, new SetRolesRequest(["HRManager", "Manager"]), CancellationToken.None);

        ForbiddenDetail(result).Should().Be("Only an administrator can change an Admin or HR Manager account.");
        StampWasNotReplaced();
    }

    [Fact]
    public async Task SetRoles_ThatWouldDemoteTheLastActiveAdmin_IsRefused()
    {
        Directory.Setup(d => d.CountActiveInRoleAsync("Admin", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Account("Admin", "Employee");

        var result = await Sut.SetRoles(TargetId, new SetRolesRequest(["Employee"]), CancellationToken.None);

        ForbiddenDetail(result).Should().StartWith("This is the last active administrator.");
    }

    [Fact]
    public async Task SetRoles_OnAnUnknownAccount_IsNotFound()
    {
        (await Sut.SetRoles("nobody", new SetRolesRequest([]), CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
    }

    // --- Employee link --------------------------------------------------------------------------

    [Fact]
    public async Task LinkEmployee_LinksTheAccount_AndRevokesTokens()
    {
        var user = Account("Employee");
        EmployeeExists(EmployeeId);

        var result = await Sut.LinkEmployee(TargetId, new LinkEmployeeRequest(EmployeeId), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        user.EmployeeId.Should().Be(EmployeeId);
        StampWasReplaced(user);
    }

    [Fact]
    public async Task LinkEmployee_WithNoEmployee_Unlinks()
    {
        var user = Account("Employee");
        user.EmployeeId = EmployeeId;

        await Sut.LinkEmployee(TargetId, new LinkEmployeeRequest(null), CancellationToken.None);

        user.EmployeeId.Should().BeNull();
        StampWasReplaced(user);
    }

    [Fact]
    public async Task LinkEmployee_ToSomeoneElsesEmployee_IsRejected()
    {
        Account("Employee");
        EmployeeExists(EmployeeId);
        Directory.Setup(d => d.FindUserLinkedToEmployeeAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync("someone-else");

        var result = await Sut.LinkEmployee(TargetId, new LinkEmployeeRequest(EmployeeId), CancellationToken.None);

        BadRequestDetail(result).Should().Be("That employee already has a login.");
        StampWasNotReplaced();
    }

    [Fact]
    public async Task LinkEmployee_ToTheEmployeeItIsAlreadyLinkedTo_IsFine()
    {
        Account("Employee");
        EmployeeExists(EmployeeId);
        Directory.Setup(d => d.FindUserLinkedToEmployeeAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(TargetId);

        (await Sut.LinkEmployee(TargetId, new LinkEmployeeRequest(EmployeeId), CancellationToken.None))
            .Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task LinkEmployee_ByHrOnAnAdmin_IsRefused()
    {
        SignInAs("HRManager", "Employee");
        Account("Admin", "Employee");

        ForbiddenDetail(await Sut.LinkEmployee(TargetId, new LinkEmployeeRequest(null), CancellationToken.None))
            .Should().NotBeNullOrEmpty();
        StampWasNotReplaced();
    }

    // --- Deactivate / reactivate ----------------------------------------------------------------

    [Fact]
    public async Task Deactivate_StopsTheAccount_AndRevokesTokens()
    {
        var user = Account("Employee");

        var result = await Sut.Deactivate(TargetId, CancellationToken.None);

        OkValue(result).IsActive.Should().BeFalse();
        user.IsActive.Should().BeFalse();
        StampWasReplaced(user);
    }

    [Fact]
    public async Task Deactivate_YourOwnAccount_IsRefused()
    {
        var self = CallersOwnAccount("Admin", "Employee");

        ForbiddenDetail(await Sut.Deactivate(CallerId, CancellationToken.None)).Should().Be("You can't deactivate your own account.");
        self.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_TheLastActiveAdmin_IsRefused()
    {
        Directory.Setup(d => d.CountActiveInRoleAsync("Admin", It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var admin = Account("Admin", "Employee");

        ForbiddenDetail(await Sut.Deactivate(TargetId, CancellationToken.None)).Should().StartWith("This is the last active administrator.");
        admin.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_LetsTheAccountSignInAgain()
    {
        var user = InactiveAccount("Employee");

        OkValue(await Sut.Reactivate(TargetId, CancellationToken.None)).IsActive.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        StampWasReplaced(user);
    }

    // --- Reset password -------------------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_SetsAGeneratedPassword_ThatMustBeChanged_AndClearsAnyLockout()
    {
        var user = Account("Employee");
        string? newPassword = null;
        Users.Setup(u => u.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        Users.Setup(u => u.ResetPasswordAsync(user, "reset-token", It.IsAny<string>()))
             .Callback<ApplicationUser, string, string>((_, _, password) => newPassword = password)
             .ReturnsAsync(IdentityResult.Success);
        Users.Setup(u => u.SetLockoutEndDateAsync(user, null)).ReturnsAsync(IdentityResult.Success);
        Users.Setup(u => u.ResetAccessFailedCountAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await Sut.ResetPassword(TargetId, CancellationToken.None);

        OkValue(result).TemporaryPassword.Should().Be(newPassword).And.HaveLength(16);
        user.MustChangePassword.Should().BeTrue();
        Users.Verify(u => u.SetLockoutEndDateAsync(user, null), Times.Once);
        Users.Verify(u => u.ResetAccessFailedCountAsync(user), Times.Once);
        StampWasReplaced(user);
    }

    [Fact]
    public async Task ResetPassword_OnYourOwnAccount_IsRefused()
    {
        CallersOwnAccount("Admin", "Employee");

        ForbiddenDetail(await Sut.ResetPassword(CallerId, CancellationToken.None))
            .Should().Be("Use Change Password on My Profile to change your own password.");
    }

    [Fact]
    public async Task ResetPassword_ThatIdentityRejects_LeavesTheAccountAsItWas()
    {
        var user = Account("Employee");
        Users.Setup(u => u.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        Users.Setup(u => u.ResetPasswordAsync(user, "reset-token", It.IsAny<string>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token." }));

        BadRequestDetail(await Sut.ResetPassword(TargetId, CancellationToken.None)).Should().Be("Invalid token.");
        user.MustChangePassword.Should().BeFalse();
        StampWasNotReplaced();
    }
}
