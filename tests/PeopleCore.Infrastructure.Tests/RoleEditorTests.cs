using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// Creating, changing and deleting roles against Postgres. A permission change must revoke the
/// holders' tokens in the same save - a role that grants less must not keep working for eight hours.
/// </summary>
public class RoleEditorTests : DatabaseTestBase
{
    public RoleEditorTests(PostgresFixture fixture) : base(fixture) { }

    private RoleEditor Editor() => new(NewContext());

    private async Task<List<string>> PermissionsOfAsync(string roleId)
    {
        await using var read = NewContext();
        return await read.RoleClaims.Where(c => c.RoleId == roleId && c.ClaimType == Permissions.ClaimType)
            .Select(c => c.ClaimValue!).OrderBy(v => v).ToListAsync();
    }

    private async Task<ApplicationUser> HolderOfAsync(string roleId)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var user = new ApplicationUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = "stamp-before"
        };
        Context.Users.Add(user);
        Context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
        await Context.SaveChangesAsync();
        return user;
    }

    private async Task<string?> StampOfAsync(string userId)
    {
        await using var read = NewContext();
        return (await read.Users.SingleAsync(u => u.Id == userId)).SecurityStamp;
    }

    [Fact]
    public async Task Create_StoresTheRole_ItsDescription_AndItsPermissions()
    {
        var id = await Editor().CreateAsync("Recruiter", "Hires people.", [Permissions.RecruitmentManage, Permissions.AnalyticsHr]);

        await using var read = NewContext();
        var role = await read.Roles.SingleAsync(r => r.Id == id);
        role.Name.Should().Be("Recruiter");
        role.NormalizedName.Should().Be("RECRUITER");
        role.Description.Should().Be("Hires people.");
        role.IsSystem.Should().BeFalse();
        (await PermissionsOfAsync(id)).Should().Equal("analytics.hr", "recruitment.manage");
    }

    [Fact]
    public async Task ChangingPermissions_ReplacesThem_AndRevokesEveryHoldersTokens()
    {
        var id = await Editor().CreateAsync("Recruiter", null, [Permissions.RecruitmentManage]);
        var holder = await HolderOfAsync(id);

        var signedOut = await Editor().UpdateAsync(id, "Recruiter", null, [Permissions.AnalyticsHr]);

        signedOut.Should().Be(1);
        (await PermissionsOfAsync(id)).Should().Equal("analytics.hr");
        (await StampOfAsync(holder.Id)).Should().NotBe("stamp-before");
    }

    [Fact]
    public async Task RenamingOrRedescribing_WithoutChangingPermissions_LeavesTokensAlone()
    {
        var id = await Editor().CreateAsync("Recruiter", null, [Permissions.RecruitmentManage]);
        var holder = await HolderOfAsync(id);

        var signedOut = await Editor().UpdateAsync(id, "Talent", "Finds people.", [Permissions.RecruitmentManage]);

        signedOut.Should().Be(0);
        await using var read = NewContext();
        var role = await read.Roles.SingleAsync(r => r.Id == id);
        role.Name.Should().Be("Talent");
        role.NormalizedName.Should().Be("TALENT");
        role.Description.Should().Be("Finds people.");
        (await StampOfAsync(holder.Id)).Should().Be("stamp-before");
    }

    [Fact]
    public async Task Delete_RemovesTheRoleAndItsPermissions()
    {
        var id = await Editor().CreateAsync("Recruiter", null, [Permissions.RecruitmentManage]);

        await Editor().DeleteAsync(id);

        await using var read = NewContext();
        (await read.Roles.AnyAsync(r => r.Id == id)).Should().BeFalse();
        (await read.RoleClaims.AnyAsync(c => c.RoleId == id)).Should().BeFalse();
    }

    [Fact]
    public async Task NameTaken_IgnoresCase_AndTheRoleBeingRenamed()
    {
        var id = await Editor().CreateAsync("Recruiter", null, []);

        (await Editor().NameTakenAsync("recruiter", exceptRoleId: null)).Should().BeTrue();
        (await Editor().NameTakenAsync("RECRUITER", exceptRoleId: id)).Should().BeFalse();
        (await Editor().NameTakenAsync("Auditor", exceptRoleId: null)).Should().BeFalse();
    }
}
