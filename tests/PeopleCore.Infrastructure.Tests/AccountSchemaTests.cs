using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// The account columns user management depends on, proven against the schema the migrations
/// actually produce rather than the model EF believes in.
/// </summary>
public class AccountSchemaTests : DatabaseTestBase
{
    public AccountSchemaTests(PostgresFixture fixture) : base(fixture) { }

    private static ApplicationUser AnAccount(Guid? employeeId = null)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        return new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmployeeId = employeeId
        };
    }

    [Fact]
    public async Task ASecondLoginForTheSameEmployee_IsRejectedByTheDatabase()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        Context.Users.Add(AnAccount(employee.Id));
        await Context.SaveChangesAsync();

        await using var other = NewContext();
        other.Users.Add(AnAccount(employee.Id));
        var act = () => other.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AnyNumberOfAccounts_MayBelongToNoEmployee()
    {
        Context.Users.AddRange(AnAccount(), AnAccount());

        var act = () => Context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ANewAccount_IsActive_AndNeedsNoPasswordChange()
    {
        var account = AnAccount();
        Context.Users.Add(account);
        await Context.SaveChangesAsync();

        await using var read = NewContext();
        var stored = await read.Users.SingleAsync(u => u.Id == account.Id);

        stored.IsActive.Should().BeTrue();
        stored.MustChangePassword.Should().BeFalse();
    }
}
