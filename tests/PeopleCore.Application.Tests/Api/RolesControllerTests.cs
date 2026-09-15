using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

public class RolesControllerTests
{
    private const string CallerId = "caller-1";

    private readonly Mock<IRoleCatalog> _catalog = new();
    private readonly Mock<IRoleEditor> _editor = new();
    private readonly Mock<IUserAccountDirectory> _directory = new();
    private readonly RolesController _sut;

    private readonly List<RoleRecord> _roles =
    [
        new("admin", "Admin", "Everything.", IsSystem: true, Permissions.AllKeys, AccountCount: 1),
        new("employee", "Employee", "Self-service.", IsSystem: true, [], AccountCount: 9),
        new("recruiter", "Recruiter", "Hires.", IsSystem: false, [Permissions.RecruitmentManage], AccountCount: 0),
        new("payroll", "Payroll", "Pays.", IsSystem: false, [Permissions.PayrollManage], AccountCount: 12),
    ];

    public RolesControllerTests()
    {
        _catalog.Setup(c => c.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _roles);
        _catalog.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => _roles.SingleOrDefault(r => r.Id == id));
        _editor.Setup(e => e.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("recruiter");
        _sut = new RolesController(_catalog.Object, _editor.Object, _directory.Object);
        SignInAs(["Admin", "Employee"], Permissions.AllKeys.ToArray());
    }

    private void SignInAs(string[] roles, string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, CallerId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
        _directory.Setup(d => d.GetAsync(CallerId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new UserAccountRow(CallerId, "me@company.test", null, null, roles, true, false, null, null));
    }

    private static string? Detail(IActionResult result, int status)
    {
        var problem = result.Should().BeAssignableTo<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(status);
        return problem.Value.Should().BeOfType<ProblemDetails>().Which.Detail;
    }

    private static string? Detail<T>(ActionResult<T> result, int status) => Detail(result.Result!, status);

    [Fact]
    public void TheWholeController_RequiresRolesManage()
    {
        typeof(RolesController).GetCustomAttribute<RequirePermissionAttribute>()!.AnyOf.Should().Equal(Permissions.RolesManage);
    }

    [Fact]
    public async Task List_MarksSystemRolesAndRolesBeyondTheCaller_AsNotEditable()
    {
        SignInAs(["Delegates", "Employee"], [Permissions.RolesManage, Permissions.RecruitmentManage]);

        var roles = (await _sut.List(CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IReadOnlyList<RoleDto>>().Subject;

        roles.Select(r => (r.Name, r.CanEdit)).Should().Equal(("Admin", false), ("Employee", false), ("Recruiter", true), ("Payroll", false));
        roles.Single(r => r.Name == "Payroll").AccountCount.Should().Be(12);
    }

    [Fact]
    public void ThePermissionCatalogue_IsServedInOrder_WithItsWords()
    {
        var catalogue = _sut.PermissionCatalogue().Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IReadOnlyList<PermissionDto>>().Subject;

        catalogue.Select(p => p.Key).Should().Equal(Permissions.AllKeys);
        catalogue.First().Label.Should().Be("View all employees");
    }

    [Fact]
    public async Task Create_TrimsAndStoresTheRole()
    {
        var result = await _sut.Create(new SaveRoleRequest("  Recruiter ", "  ", [Permissions.RecruitmentManage, Permissions.RecruitmentManage]), CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _editor.Verify(e => e.CreateAsync("Recruiter", null,
            It.Is<IReadOnlyCollection<string>>(p => p.SequenceEqual(new[] { Permissions.RecruitmentManage })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "Enter a role name.")]
    [InlineData("A role name well over fifty characters long, which is too long", "A role name must be 50 characters or fewer.")]
    public async Task Create_WithABadName_IsRejected(string name, string message)
    {
        Detail(await _sut.Create(new SaveRoleRequest(name, null, []), CancellationToken.None), 400).Should().Be(message);
    }

    [Theory]
    [InlineData("Admin\u200B")]      // zero-width space: looks exactly like Admin
    [InlineData("Re\u200Dcruiter")]  // zero-width joiner
    [InlineData("Payroll\u0007")]    // a control character
    public async Task Create_WithAnInvisibleCharacterInTheName_IsRejected(string name)
    {
        Detail(await _sut.Create(new SaveRoleRequest(name, null, []), CancellationToken.None), 400)
            .Should().Be("A role name can't contain invisible characters.");
        _editor.Verify(e => e.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithAnInvisibleCharacterInTheName_IsRejected()
    {
        Detail(await _sut.Update("recruiter", new SaveRoleRequest("Recruiter\u2060", null, [Permissions.RecruitmentManage]), CancellationToken.None), 400)
            .Should().Be("A role name can't contain invisible characters.");
    }

    [Fact]
    public async Task Create_WithAnUnknownPermission_IsRejected()
    {
        Detail(await _sut.Create(new SaveRoleRequest("Recruiter", null, ["reports.everything"]), CancellationToken.None), 400)
            .Should().Be("reports.everything is not a permission.");
    }

    [Fact]
    public async Task Create_WithANullPermission_IsRejected()
    {
        Detail(await _sut.Create(new SaveRoleRequest("Recruiter", null, [null!]), CancellationToken.None), 400)
            .Should().Be("A permission can't be blank.");
    }

    [Fact]
    public async Task Create_WithADescriptionOver200Characters_IsRejected()
    {
        Detail(await _sut.Create(new SaveRoleRequest("Recruiter", new string('x', 201), []), CancellationToken.None), 400)
            .Should().Be("A description must be 200 characters or fewer.");
    }

    [Fact]
    public async Task Create_WithATakenName_IsRejected()
    {
        _editor.Setup(e => e.NameTakenAsync("Payroll", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Detail(await _sut.Create(new SaveRoleRequest("Payroll", null, []), CancellationToken.None), 400)
            .Should().Be("A role called Payroll already exists.");
    }

    [Fact]
    public async Task Create_WithAPermissionTheCallerLacks_IsRefused()
    {
        SignInAs(["Delegates", "Employee"], [Permissions.RolesManage, Permissions.RecruitmentManage]);

        Detail(await _sut.Create(new SaveRoleRequest("Payroll2", null, [Permissions.PayrollManage]), CancellationToken.None), 403)
            .Should().Be("You can't give a role permissions you don't have yourself.");
        _editor.Verify(e => e.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_OfAnUnknownRole_IsNotFound()
    {
        (await _sut.Update("nope", new SaveRoleRequest("X", null, []), CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_OfASystemRole_IsRefused()
    {
        Detail(await _sut.Update("employee", new SaveRoleRequest("Staff", null, []), CancellationToken.None), 403)
            .Should().Be("System roles can't be changed.");
    }

    [Fact]
    public async Task Update_WithANullPermission_IsRejected()
    {
        Detail(await _sut.Update("recruiter", new SaveRoleRequest("Recruiter", null, [null!]), CancellationToken.None), 400)
            .Should().Be("A permission can't be blank.");
    }

    [Fact]
    public async Task Update_StoresTheChange_AndReturnsTheRole()
    {
        var result = await _sut.Update("recruiter", new SaveRoleRequest("Talent", "Finds people.", [Permissions.RecruitmentManage, Permissions.AnalyticsHr]), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _editor.Verify(e => e.UpdateAsync("recruiter", "Talent", "Finds people.",
            It.Is<IReadOnlyCollection<string>>(p => p.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ThatWouldTakeAwayTheCallersOwnPermissionToManageRoles_IsRefused()
    {
        _roles.Add(new RoleRecord("delegates", "Delegates", null, IsSystem: false, [Permissions.RolesManage, Permissions.UsersManage], AccountCount: 1));
        SignInAs(["Delegates", "Employee"], [Permissions.RolesManage, Permissions.UsersManage]);

        Detail(await _sut.Update("delegates", new SaveRoleRequest("Delegates", null, [Permissions.UsersManage]), CancellationToken.None), 403)
            .Should().Be("You can't remove your own permission to manage roles.");
    }

    [Fact]
    public async Task Delete_OfARoleAccountsStillHold_IsRejected_SayingHowMany()
    {
        Detail(await _sut.Delete("payroll", CancellationToken.None), 400)
            .Should().Be("12 accounts still have Payroll — remove it from them first.");
    }

    [Fact]
    public async Task Delete_OfARoleOneAccountHolds_SaysAccountInTheSingular()
    {
        _roles[2] = _roles[2] with { AccountCount = 1 };

        Detail(await _sut.Delete("recruiter", CancellationToken.None), 400)
            .Should().Be("1 account still has Recruiter — remove it from them first.");
    }

    [Fact]
    public async Task Delete_OfAnUnusedRole_RemovesIt()
    {
        (await _sut.Delete("recruiter", CancellationToken.None)).Should().BeOfType<NoContentResult>();
        _editor.Verify(e => e.DeleteAsync("recruiter", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_OfASystemRole_IsRefused()
    {
        Detail(await _sut.Delete("admin", CancellationToken.None), 403).Should().Be("System roles can't be changed.");
    }
}
