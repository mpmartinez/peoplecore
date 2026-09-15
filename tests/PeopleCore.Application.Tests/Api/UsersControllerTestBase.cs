using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A UsersController over a mocked UserManager and directory, signed in as an Admin unless a test
/// says otherwise. Two Admins are active by default, so no test trips the last-admin guard by
/// accident.
/// </summary>
public abstract class UsersControllerTestBase
{
    protected const string CallerId = "caller-0001";
    protected const string TargetId = "target-0001";
    protected static readonly Guid EmployeeId = Guid.Parse("9c4e1a7b-3d2f-4b8a-8e6c-0f5d2a9b7c44");

    protected readonly Mock<UserManager<ApplicationUser>> Users;
    protected readonly Mock<IUserAccountDirectory> Directory = new();
    protected readonly Mock<IEmployeeRepository> Employees = new();
    protected readonly Mock<IRoleCatalog> Roles = new();
    protected readonly UsersController Sut;

    protected UsersControllerTestBase()
    {
        Users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        Users.Setup(u => u.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        Directory.Setup(d => d.CountActiveInRoleAsync("Admin", It.IsAny<CancellationToken>())).ReturnsAsync(2);

        // The seeded roles, as the catalogue returns them: system roles first, then by name.
        Roles.Setup(r => r.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new[] { "Admin", "Employee", "Service", "HRManager", "Manager", "PayrollService" }
                .Select(name => new RoleRecord(name.ToLowerInvariant(), name, null, SeededRoles.System.Contains(name),
                    SeededRoles.PermissionsOf([name]), 0))
                .ToList());

        Sut = new UsersController(Users.Object, Directory.Object, Employees.Object, Roles.Object);
        SignInAs("Admin", "Employee");
    }

    protected void SignInAs(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, CallerId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(SeededRoles.PermissionsOf(roles).Select(p => new Claim(Permissions.ClaimType, p)));
        Sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
    }

    protected static UserAccountRow Row(string id, params string[] roles) =>
        new(id, $"{id}@company.test", "Ana", "Reyes", roles, IsActive: true, MustChangePassword: false, EmployeeId: null, EmployeeName: null);

    /// <summary>An active account someone else holds, known to both UserManager and the directory.</summary>
    protected ApplicationUser Account(params string[] roles) => Existing(TargetId, active: true, roles);

    protected ApplicationUser InactiveAccount(params string[] roles) => Existing(TargetId, active: false, roles);

    /// <summary>The signed-in caller's own account.</summary>
    protected ApplicationUser CallersOwnAccount(params string[] roles) => Existing(CallerId, active: true, roles);

    protected void EmployeeExists(Guid id) =>
        Employees.Setup(e => e.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PeopleCore.Domain.Entities.Employees.Employee { FirstName = "Ana", LastName = "Reyes" });

    private ApplicationUser Existing(string id, bool active, string[] roles)
    {
        var user = new ApplicationUser { Id = id, Email = $"{id}@company.test", UserName = $"{id}@company.test", IsActive = active };
        Users.Setup(u => u.FindByIdAsync(id)).ReturnsAsync(user);
        Users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync((IList<string>)roles.ToList());
        Directory.Setup(d => d.GetAsync(id, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(() => new UserAccountRow(id, user.Email!, user.FirstName, user.LastName, roles,
                     user.IsActive, user.MustChangePassword, user.EmployeeId, null));
        return user;
    }

    protected static T OkValue<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<T>().Subject;

    protected static string? BadRequestDetail<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<ProblemDetails>().Which.Detail;

    protected static string? ForbiddenDetail<T>(ActionResult<T> result)
    {
        var refused = result.Result.Should().BeOfType<ObjectResult>().Subject;
        refused.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        return refused.Value.Should().BeOfType<ProblemDetails>().Which.Detail;
    }
}
