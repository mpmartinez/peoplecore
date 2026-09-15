using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

public class UsersControllerReadTests : UsersControllerTestBase
{
    [Fact]
    public void TheWholeController_RequiresUsersManage()
    {
        typeof(UsersController).GetCustomAttribute<RequirePermissionAttribute>()!.AnyOf
            .Should().BeEquivalentTo([Permissions.UsersManage]);
    }

    [Fact]
    public async Task List_PassesTheSearchThrough_AndCapsThePageSize()
    {
        Directory.Setup(d => d.SearchAsync("ana", 1, 100, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(((IReadOnlyList<UserAccountRow>)[Row("u1", "Employee")], 1));

        var page = OkValue(await Sut.List("ana", page: 0, pageSize: 5000));

        page.Items.Should().ContainSingle().Which.Email.Should().Be("u1@company.test");
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task List_TellsHrWhichAccountsItMayChange()
    {
        SignInAs("HRManager", "Employee");
        Directory.Setup(d => d.SearchAsync(null, 1, 20, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(((IReadOnlyList<UserAccountRow>)[Row("admin", "Admin", "Employee"), Row("staff", "Employee")], 2));

        var page = OkValue(await Sut.List(null));

        page.Items.Select(i => (i.Id, i.CanManage)).Should().Equal(("admin", false), ("staff", true));
    }

    [Fact]
    public void AssignableRoles_AreTheOnesTheCallerMayGrant()
    {
        SignInAs("HRManager", "Employee");

        OkValue(Sut.AssignableRoles()).Should().Equal("Manager", "Employee", "PayrollService");
    }

    [Fact]
    public async Task EmployeeLinks_ListWhichEmployeesHaveALogin()
    {
        Directory.Setup(d => d.GetEmployeeLinksAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new EmployeeLinkRow(EmployeeId, "u1", false)]);

        OkValue(await Sut.EmployeeLinks(CancellationToken.None))
            .Should().Equal(new EmployeeLinkDto(EmployeeId, "u1", false));
    }

    [Fact]
    public async Task Get_AnUnknownAccount_IsNotFound()
    {
        (await Sut.Get("nobody", CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
    }
}
