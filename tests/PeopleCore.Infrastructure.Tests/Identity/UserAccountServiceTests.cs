using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Infrastructure.Identity;
using PeopleCore.Infrastructure.Identity.UserAccounts;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Tests.Identity;

/// <summary>
/// Account administration against real Identity stores on Postgres, so the role joins, the ILIKE
/// search and the transaction that keeps a half-saved account from surviving are all proven to work
/// as deployed rather than against a mock of UserManager.
/// </summary>
public class UserAccountServiceTests : DatabaseTestBase, IAsyncLifetime
{
    private const string Password = "Passw0rd!";

    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private UserManager<ApplicationUser> _users = null!;
    private UserAccountService _sut = null!;

    public UserAccountServiceTests(PostgresFixture fixture) : base(fixture) { }

    private async Task StartAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddScoped(_ => Context);
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // The API's policy, so a password the tests call weak is one the API would refuse.
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateAsyncScope();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "HRManager", "Manager", "Employee" })
            await roles.CreateAsync(new IdentityRole(role));

        _sut = new UserAccountService(Context, _users, roles);
    }

    public new async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<ApplicationUser> SeedAccountAsync(string email, params string[] roles)
    {
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Seed", LastName = "Account" };
        (await _users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        if (roles.Length > 0) (await _users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task<Employee> SeedEmployeeAsync(string firstName = "Ana", string lastName = "Reyes")
    {
        var employee = AnEmployee(lastName, firstName);
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        return employee;
    }

    private static CreateUserAccountRequest ACreate(
        string email = "ana@company.test", string password = Password, string[]? roles = null, Guid? employeeId = null)
        => new(email, password, "Ana", "Reyes", roles ?? ["Employee"], employeeId);

    private static UpdateUserAccountRequest AnUpdate(
        string email, string[] roles, Guid? employeeId = null, string? newPassword = null, string firstName = "Ana", string lastName = "Reyes")
        => new(email, firstName, lastName, roles, employeeId, newPassword);

    /// <summary>Reads the account back through a fresh context, so nothing is answered from the change tracker.</summary>
    private async Task<ApplicationUser?> StoredAccountAsync(string email)
    {
        await using var context = NewContext();
        return await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
    }

    private async Task<int> StoredAccountCountAsync()
    {
        await using var context = NewContext();
        return await context.Users.CountAsync();
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_SavesTheAccount_WithItsNamesRolesAndEmployee_AndItCanSignIn()
    {
        await StartAsync();
        var employee = await SeedEmployeeAsync();

        var result = await _sut.CreateAsync(
            new CreateUserAccountRequest(" ana@company.test ", Password, " Ana ", " Reyes ", ["manager", "Employee", "Manager"], employee.Id));

        result.Succeeded.Should().BeTrue(result.Message);
        result.User.Should().BeEquivalentTo(new
        {
            Email = "ana@company.test",
            FirstName = "Ana",
            LastName = "Reyes",
            Roles = new[] { "Employee", "Manager" },
            EmployeeId = (Guid?)employee.Id,
            employee.EmployeeNumber,
            EmployeeName = "Ana Reyes"
        });

        var stored = await StoredAccountAsync("ana@company.test");
        stored.Should().NotBeNull();
        stored!.UserName.Should().Be("ana@company.test");
        (await _users.CheckPasswordAsync((await _users.FindByIdAsync(stored.Id))!, Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Creating_WithAnEmailAlreadyInUse_InAnyCase_IsAConflict()
    {
        await StartAsync();
        await SeedAccountAsync("ana@company.test");

        var result = await _sut.CreateAsync(ACreate(email: "ANA@company.test"));

        result.Failure.Should().Be(UserAccountFailure.Conflict);
        result.Message.Should().Be("An account with the email ANA@company.test already exists.");
        (await StoredAccountCountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("", "Enter an email address.")]
    [InlineData("not-an-email", "\"not-an-email\" is not a valid email address.")]
    public async Task Creating_WithoutAUsableEmail_IsRefused(string email, string message)
    {
        await StartAsync();

        var result = await _sut.CreateAsync(ACreate(email: email));

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        result.Message.Should().Be(message);
        (await StoredAccountCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Creating_WithAPasswordThePolicyRefuses_SavesNothing_AndSaysWhy()
    {
        await StartAsync();

        var result = await _sut.CreateAsync(ACreate(password: "short"));

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        result.Message.Should().Contain("at least 8 characters").And.Contain("digit");
        (await StoredAccountCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Creating_WithARoleThatDoesNotExist_SavesNothing()
    {
        await StartAsync();

        var result = await _sut.CreateAsync(ACreate(roles: ["Employee", "SuperUser"]));

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        result.Message.Should().Be("There is no role named \"SuperUser\".");
        (await StoredAccountCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Creating_LinkedToAnEmployeeAnotherAccountAlreadyHas_IsAConflict()
    {
        await StartAsync();
        var employee = await SeedEmployeeAsync();
        var existing = await SeedAccountAsync("first@company.test");
        existing.EmployeeId = employee.Id;
        await _users.UpdateAsync(existing);

        var result = await _sut.CreateAsync(ACreate(employeeId: employee.Id));

        result.Failure.Should().Be(UserAccountFailure.Conflict);
        result.Message.Should().Be("The selected employee is already linked to another account.");
    }

    [Fact]
    public async Task Creating_LinkedToAnEmployeeThatDoesNotExist_IsRefused()
    {
        await StartAsync();

        var result = await _sut.CreateAsync(ACreate(employeeId: Guid.NewGuid()));

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        result.Message.Should().Be("The selected employee could not be found.");
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listing_FindsAccountsByEmailOrName_InEmailOrder_WithTheirRolesAndEmployee()
    {
        await StartAsync();
        var employee = await SeedEmployeeAsync("Carla", "Santos");
        var carla = await SeedAccountAsync("carla@company.test", "Manager", "Employee");
        carla.FirstName = "Carla";
        carla.EmployeeId = employee.Id;
        await _users.UpdateAsync(carla);
        await SeedAccountAsync("bea@company.test", "Admin");
        await SeedAccountAsync("dan@elsewhere.test");

        var all = await _sut.ListAsync(search: null, page: 1, pageSize: 20);
        all.Items.Select(u => u.Email).Should().Equal("bea@company.test", "carla@company.test", "dan@elsewhere.test");
        all.Items[1].Roles.Should().Equal("Employee", "Manager");
        all.Items[1].EmployeeName.Should().Be("Carla Santos");
        all.Items[0].EmployeeId.Should().BeNull();

        (await _sut.ListAsync("COMPANY", 1, 20)).Items.Select(u => u.Email).Should().Equal("bea@company.test", "carla@company.test");
        (await _sut.ListAsync("carl", 1, 20)).Items.Select(u => u.Email).Should().Equal("carla@company.test");
        (await _sut.ListAsync("_", 1, 20)).Items.Should().BeEmpty("an underscore is text to find, not a wildcard");

        var secondPage = await _sut.ListAsync(null, page: 2, pageSize: 2);
        secondPage.TotalCount.Should().Be(3);
        secondPage.Items.Select(u => u.Email).Should().Equal("dan@elsewhere.test");
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Updating_ReplacesEmailNamesRolesAndEmployee_AndTheNewEmailSignsIn()
    {
        await StartAsync();
        var employee = await SeedEmployeeAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        var ana = await SeedAccountAsync("ana@company.test", "Employee", "Manager");

        var result = await _sut.UpdateAsync(ana.Id,
            AnUpdate("ana.reyes@company.test", ["HRManager", "Employee"], employee.Id, firstName: "Annie"), admin.Id);

        result.Succeeded.Should().BeTrue(result.Message);
        result.User!.Roles.Should().Equal("Employee", "HRManager");

        var stored = await StoredAccountAsync("ana.reyes@company.test");
        stored.Should().NotBeNull();
        stored!.UserName.Should().Be("ana.reyes@company.test");
        stored.FirstName.Should().Be("Annie");
        stored.EmployeeId.Should().Be(employee.Id);
        (await StoredAccountAsync("ana@company.test")).Should().BeNull();
        (await _sut.GetAsync(ana.Id))!.Roles.Should().Equal("Employee", "HRManager");
    }

    [Fact]
    public async Task Updating_WithANewPassword_ResetsIt_AndWithoutOne_LeavesItAlone()
    {
        await StartAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        var ana = await SeedAccountAsync("ana@company.test", "Employee");

        (await _sut.UpdateAsync(ana.Id, AnUpdate("ana@company.test", ["Employee"]), admin.Id)).Succeeded.Should().BeTrue();
        (await _users.CheckPasswordAsync(ana, Password)).Should().BeTrue();

        (await _sut.UpdateAsync(ana.Id, AnUpdate("ana@company.test", ["Employee"], newPassword: "Brand-new-1"), admin.Id)).Succeeded.Should().BeTrue();
        (await _users.CheckPasswordAsync(ana, "Brand-new-1")).Should().BeTrue();
        (await _users.CheckPasswordAsync(ana, Password)).Should().BeFalse();
    }

    [Fact]
    public async Task AnUpdateThatFailsPartWay_LeavesTheAccountAsItWas()
    {
        await StartAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        var ana = await SeedAccountAsync("ana@company.test", "Employee");

        // Names, email and roles are written before the password is reset; the refused password must undo them.
        var result = await _sut.UpdateAsync(ana.Id,
            AnUpdate("changed@company.test", ["Manager"], newPassword: "weak", firstName: "Changed"), admin.Id);

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        (await StoredAccountAsync("changed@company.test")).Should().BeNull();
        var stored = await StoredAccountAsync("ana@company.test");
        stored!.FirstName.Should().Be("Seed");
        await using var context = NewContext();
        var roleNames = await (from ur in context.UserRoles join r in context.Roles on ur.RoleId equals r.Id
                               where ur.UserId == ana.Id select r.Name).ToListAsync();
        roleNames.Should().Equal("Employee");
    }

    [Fact]
    public async Task Updating_ToAnEmailAnotherAccountHas_IsAConflict()
    {
        await StartAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        var ana = await SeedAccountAsync("ana@company.test");

        var result = await _sut.UpdateAsync(ana.Id, AnUpdate("Admin@Company.test", []), admin.Id);

        result.Failure.Should().Be(UserAccountFailure.Conflict);
    }

    [Fact]
    public async Task AnAdmin_CannotRemoveTheirOwnAdminRole()
    {
        await StartAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        await SeedAccountAsync("second@company.test", "Admin");

        var result = await _sut.UpdateAsync(admin.Id, AnUpdate("admin@company.test", ["Employee"]), admin.Id);

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        result.Message.Should().Be("You cannot remove the Admin role from the account you are signed in with.");
        (await _users.IsInRoleAsync(admin, "Admin")).Should().BeTrue();
    }

    [Fact]
    public async Task TheLastAdmin_CannotBeDemoted()
    {
        await StartAsync();
        var onlyAdmin = await SeedAccountAsync("admin@company.test", "Admin");
        var caller = await SeedAccountAsync("stale-token@company.test");

        var result = await _sut.UpdateAsync(onlyAdmin.Id, AnUpdate("admin@company.test", ["Employee"]), caller.Id);

        result.Message.Should().Be("This is the only Admin account. Give another account the Admin role first.");
        (await _users.IsInRoleAsync(onlyAdmin, "Admin")).Should().BeTrue();
    }

    [Fact]
    public async Task Updating_AnAccountThatDoesNotExist_IsNotFound()
    {
        await StartAsync();

        var result = await _sut.UpdateAsync("no-such-account", AnUpdate("ana@company.test", []), "caller");

        result.Failure.Should().Be(UserAccountFailure.NotFound);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_RemovesTheAccount_ButNotItsEmployeeRecord()
    {
        await StartAsync();
        var employee = await SeedEmployeeAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        var ana = await SeedAccountAsync("ana@company.test", "Employee");
        ana.EmployeeId = employee.Id;
        await _users.UpdateAsync(ana);

        var result = await _sut.DeleteAsync(ana.Id, admin.Id);

        result.Succeeded.Should().BeTrue(result.Message);
        (await StoredAccountAsync("ana@company.test")).Should().BeNull();
        await using var context = NewContext();
        (await context.Employees.AnyAsync(e => e.Id == employee.Id)).Should().BeTrue();
        (await context.UserRoles.AnyAsync(ur => ur.UserId == ana.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task NobodyCanDeleteTheAccountTheyAreSignedInWith()
    {
        await StartAsync();
        var admin = await SeedAccountAsync("admin@company.test", "Admin");
        await SeedAccountAsync("second@company.test", "Admin");

        var result = await _sut.DeleteAsync(admin.Id, admin.Id);

        result.Message.Should().Be("You cannot delete the account you are signed in with.");
        (await StoredAccountAsync("admin@company.test")).Should().NotBeNull();
    }

    [Fact]
    public async Task TheLastAdmin_CannotBeDeleted()
    {
        await StartAsync();
        var onlyAdmin = await SeedAccountAsync("admin@company.test", "Admin");
        var caller = await SeedAccountAsync("stale-token@company.test");

        var result = await _sut.DeleteAsync(onlyAdmin.Id, caller.Id);

        result.Failure.Should().Be(UserAccountFailure.Invalid);
        (await StoredAccountAsync("admin@company.test")).Should().NotBeNull();
    }

    [Fact]
    public async Task Deleting_AnAccountThatDoesNotExist_IsNotFound()
    {
        await StartAsync();

        (await _sut.DeleteAsync("no-such-account", "caller")).Failure.Should().Be(UserAccountFailure.NotFound);
    }
}
