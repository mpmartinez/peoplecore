using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A new account is active, holds Employee plus what it was given, and signs in with a password
/// the server generated - shown to its creator once - that the user must replace.
/// </summary>
public class UsersControllerCreateTests : UsersControllerTestBase
{
    private ApplicationUser? _created;
    private string? _password;
    private List<string>? _granted;

    public UsersControllerCreateTests()
    {
        Users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
             .Callback<ApplicationUser, string>((user, password) => { _created = user; _password = password; })
             .ReturnsAsync(IdentityResult.Success);
        Users.Setup(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
             .Callback<ApplicationUser, IEnumerable<string>>((_, roles) => _granted = roles.ToList())
             .ReturnsAsync(IdentityResult.Success);
        Directory.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string id, CancellationToken _) => Row(id, "Employee"));
    }

    private static CreateUserAccountRequest Request(string[] roles, Guid? employeeId = null, string email = "new.hire@company.test") =>
        new(email, "  Ana ", " Reyes ", employeeId, roles);

    private Task<ActionResult<CreatedUserAccountDto>> Create(CreateUserAccountRequest request) =>
        Sut.Create(request, CancellationToken.None);

    private void NothingWasCreated() =>
        Users.Verify(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);

    [Fact]
    public async Task ANewAccount_IsActive_AndMustChangeTheGeneratedPasswordItsCreatorIsShown()
    {
        var result = await Create(Request(["Manager"]));

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>()
            .Which.Value.Should().BeOfType<CreatedUserAccountDto>().Subject;
        created.TemporaryPassword.Should().Be(_password).And.HaveLength(16);
        _created!.Email.Should().Be("new.hire@company.test");
        _created.UserName.Should().Be("new.hire@company.test");
        _created.EmailConfirmed.Should().BeTrue();
        _created.FirstName.Should().Be("Ana");
        _created.LastName.Should().Be("Reyes");
        _created.IsActive.Should().BeTrue();
        _created.MustChangePassword.Should().BeTrue();
        _granted.Should().BeEquivalentTo("Manager", "Employee");
    }

    [Fact]
    public async Task EveryAccount_HoldsEmployee_EvenWhenItWasNotAskedFor()
    {
        await Create(Request([]));

        _granted.Should().Equal("Employee");
    }

    [Fact]
    public async Task ARoleNobodyCanAssign_IsRejected()
    {
        var result = await Create(Request(["Service"]));

        BadRequestDetail(result).Should().Be("Service is not a role that can be assigned.");
        NothingWasCreated();
    }

    [Fact]
    public async Task HR_CannotCreateAnAdmin()
    {
        SignInAs("HRManager", "Employee");

        var result = await Create(Request(["Admin"]));

        ForbiddenDetail(result).Should().Be("Only an administrator can grant or remove the Admin role.");
        NothingWasCreated();
    }

    [Fact]
    public async Task HR_CanCreateAManager()
    {
        SignInAs("HRManager", "Employee");

        (await Create(Request(["Manager"]))).Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task AnEmailAlreadyInUse_IsRejected()
    {
        Users.Setup(u => u.FindByEmailAsync("new.hire@company.test")).ReturnsAsync(new ApplicationUser());

        BadRequestDetail(await Create(Request(["Manager"])))
            .Should().Be("An account with the email new.hire@company.test already exists.");
        NothingWasCreated();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Ana <ana@company.test>")]
    public async Task AMalformedEmail_IsRejected(string email)
    {
        BadRequestDetail(await Create(Request([], email: email))).Should().Be("Enter a valid email address.");
        NothingWasCreated();
    }

    [Fact]
    public async Task AMissingName_IsRejected()
    {
        var result = await Create(new CreateUserAccountRequest("new.hire@company.test", " ", "Reyes", null, []));

        BadRequestDetail(result).Should().Be("Enter a first name.");
    }

    [Fact]
    public async Task AnEmployeeThatDoesNotExist_IsRejected()
    {
        BadRequestDetail(await Create(Request([], EmployeeId))).Should().Be("That employee record does not exist.");
        NothingWasCreated();
    }

    [Fact]
    public async Task AnEmployeeWhoAlreadyHasALogin_IsRejected()
    {
        EmployeeExists(EmployeeId);
        Directory.Setup(d => d.FindUserLinkedToEmployeeAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync("someone-else");

        BadRequestDetail(await Create(Request([], EmployeeId))).Should().Be("That employee already has a login.");
        NothingWasCreated();
    }

    [Fact]
    public async Task AnAccountForAnEmployee_IsLinkedToThem()
    {
        EmployeeExists(EmployeeId);

        await Create(Request([], EmployeeId));

        _created!.EmployeeId.Should().Be(EmployeeId);
    }

    [Fact]
    public async Task AnAccountTheIdentityRulesReject_ReportsTheirReasons()
    {
        Users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Username is invalid." }));

        BadRequestDetail(await Create(Request([]))).Should().Be("Username is invalid.");
        Users.Verify(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task WhenRolesCannotBeGranted_TheHalfMadeAccountIsDeleted()
    {
        Users.Setup(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role Service does not exist." }));
        Users.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var result = await Create(Request(["Manager"]));

        BadRequestDetail(result).Should().Be("Role Service does not exist.");
        Users.Verify(u => u.DeleteAsync(It.Is<ApplicationUser>(a => a == _created)), Times.Once);
    }

    [Fact]
    public async Task WhenRolesCannotBeGranted_AndDeletingItAlsoFails_TheProblemNamesBothFailures()
    {
        Users.Setup(u => u.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role Service does not exist." }));
        Users.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Concurrency failure." }));

        var result = await Create(Request(["Manager"]));

        BadRequestDetail(result).Should().Be(
            "The account new.hire@company.test was created but its roles could not be assigned (Role Service does not exist.), " +
            "and removing it failed (Concurrency failure.). Delete or fix it before trying again.");
    }
}
