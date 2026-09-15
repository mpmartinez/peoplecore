using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// The account list and the counts user management decides with. Run against Postgres so the
/// case-insensitive search, the role joins and the employee-name lookup are proven to translate.
/// </summary>
public class UserAccountDirectoryTests : DatabaseTestBase
{
    public UserAccountDirectoryTests(PostgresFixture fixture) : base(fixture) { }

    private UserAccountDirectory Directory() => new(NewContext());

    private async Task<ApplicationRole> RoleAsync(string name)
    {
        var role = new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant() };
        Context.Roles.Add(role);
        await Context.SaveChangesAsync();
        return role;
    }

    private async Task<ApplicationUser> AccountAsync(
        string email, string? firstName = null, string? lastName = null, Guid? employeeId = null,
        bool active = true, ApplicationRole[]? roles = null)
    {
        var user = new ApplicationUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            FirstName = firstName, LastName = lastName, EmployeeId = employeeId, IsActive = active
        };
        Context.Users.Add(user);
        foreach (var role in roles ?? [])
            Context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        await Context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Search_MatchesEmailOrNames_IgnoringCase_OrderedByEmail()
    {
        await AccountAsync("zed@company.test", "Ana", "Reyes");
        await AccountAsync("ana.cruz@company.test", "Maria", "Cruz");
        await AccountAsync("bob@company.test", "Bob", "Santos");

        var (items, total) = await Directory().SearchAsync("ANA", page: 1, pageSize: 20);

        total.Should().Be(2);
        items.Select(i => i.Email).Should().Equal("ana.cruz@company.test", "zed@company.test");
    }

    [Fact]
    public async Task Search_PagesTheResults_ButCountsEveryMatch()
    {
        for (var i = 0; i < 3; i++)
            await AccountAsync($"user{i}@company.test");

        var (items, total) = await Directory().SearchAsync(null, page: 2, pageSize: 2);

        total.Should().Be(3);
        items.Select(i => i.Email).Should().Equal("user2@company.test");
    }

    [Fact]
    public async Task Get_ReturnsTheAccountsRoles_AndTheNameOfItsEmployee()
    {
        var employee = AnEmployee("Reyes", "Ana");
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        var manager = await RoleAsync("Manager");
        var staff = await RoleAsync("Employee");
        var account = await AccountAsync("ana@company.test", employeeId: employee.Id, roles: [manager, staff]);

        var row = await Directory().GetAsync(account.Id);

        row.Should().NotBeNull();
        row!.Roles.Should().Equal("Employee", "Manager");
        row.EmployeeId.Should().Be(employee.Id);
        row.EmployeeName.Should().Be("Ana Reyes");
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Get_ForAnUnknownId_IsNull()
    {
        (await Directory().GetAsync("no-such-account")).Should().BeNull();
    }

    [Fact]
    public async Task EmployeeLinks_ListOnlyLinkedAccounts_WithWhetherTheyAreActive()
    {
        var first = AnEmployee("One");
        var second = AnEmployee("Two");
        Context.Employees.AddRange(first, second);
        await Context.SaveChangesAsync();
        var active = await AccountAsync("one@company.test", employeeId: first.Id);
        var inactive = await AccountAsync("two@company.test", employeeId: second.Id, active: false);
        await AccountAsync("admin@company.test");

        var links = await Directory().GetEmployeeLinksAsync();

        links.Should().BeEquivalentTo([
            new EmployeeLinkRow(first.Id, active.Id, true),
            new EmployeeLinkRow(second.Id, inactive.Id, false)
        ]);
    }

    [Fact]
    public async Task CountActiveInRole_IgnoresDeactivatedAccounts_AndOtherRoles()
    {
        var admin = await RoleAsync("Admin");
        var hr = await RoleAsync("HRManager");
        await AccountAsync("a1@company.test", roles: [admin]);
        await AccountAsync("a2@company.test", roles: [admin, hr]);
        await AccountAsync("gone@company.test", active: false, roles: [admin]);
        await AccountAsync("hr@company.test", roles: [hr]);

        (await Directory().CountActiveInRoleAsync("Admin")).Should().Be(2);
    }

    [Fact]
    public async Task FindUserLinkedToEmployee_NamesTheAccount_OrNull()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        var account = await AccountAsync("ana@company.test", employeeId: employee.Id);

        (await Directory().FindUserLinkedToEmployeeAsync(employee.Id)).Should().Be(account.Id);
        (await Directory().FindUserLinkedToEmployeeAsync(Guid.NewGuid())).Should().BeNull();
    }
}
