# User & Role Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Admin and HR create sign-in accounts, link them to employees, manage their roles, deactivate them and reset their passwords, with every change taking effect on the next request.

**Architecture:** A pure `AccountManagementPolicy` decides who may do what. `UsersController` asks it, then changes the account through `UserManager<ApplicationUser>`. Reads that need joins go through a new `IUserAccountDirectory` in Infrastructure. Tokens carry the account's security stamp, and a `JwtBearerEvents.OnTokenValidated` hook rejects stale ones. A global MVC filter blocks everything but change-password while a temporary password is in use. On the web side there is a new `/admin/users` page, a Create login action on Employees, and a gate in `MainLayout` for temporary passwords.

**Tech Stack:** .NET 10, ASP.NET Core Identity + JWT bearer, EF Core 10 / Npgsql (snake_case naming), Blazor WebAssembly, xUnit + Moq + FluentAssertions, bUnit 2.10.

**Spec:** `docs/superpowers/specs/2026-09-13-user-role-management-design.md`

## Global Constraints

- Assignable roles, exactly: `Admin`, `HRManager`, `Manager`, `Employee`, `PayrollService`. Privileged: `Admin`, `HRManager`. `Service` is never assignable.
- `Employee` is on every account created or edited through this feature and can never be removed.
- HRManager may only grant or remove `Employee`, `Manager` and `PayrollService`, and may not change any account that holds `Admin` or `HRManager`.
- Nobody may remove their own `Admin`/`HRManager` role, deactivate themselves, or reset their own password. The last active `Admin` can't lose `Admin` or be deactivated.
- Temporary passwords: 16 characters, no `0 O 1 l I`, at least one upper, one lower and one digit. Returned only by create and reset, and never logged.
- Claim names: `sec_stamp` (the security stamp) and `must_change_password` (value `"true"`, present only when set).
- Every account mutation ends with `UpdateSecurityStampAsync` on the target.
- The UI copy after create or reset reads exactly: "This password is shown once. Share it with the user securely; they will be asked to change it when they sign in."
- Follow the house style: comments explain *why*, test names read as sentences (`Thing_DoesWhat_WhenWhat`), and API errors are `ProblemDetails` whose `Detail` a page can show as it is.
- Run the API tests with `dotnet test tests/PeopleCore.Application.Tests`, the infrastructure tests with `dotnet test tests/PeopleCore.Infrastructure.Tests` (they need the local `m2net-postgres` container) and the web tests with `dotnet test tests/PeopleCore.Web.Tests`.

## Deviations from the spec, decided while planning

1. **`SecurityStampValidator` is named `AccountTokenValidator`.** `Microsoft.AspNetCore.Identity.SecurityStampValidator` already exists, and both namespaces are imported in the same files.
2. **List, search and join queries live in `IUserAccountDirectory` (Infrastructure), not in `UserManager.Users`.** `UserManager.Users` can't be mocked for EF async operators, so the controller stays unit-testable and the SQL is proven against Postgres.
3. **The filtered index names the snake_case column:** `employee_id IS NOT NULL`.
4. **The web app registers `JwtAuthStateProvider` and `AuthenticationStateProvider` as two separate instances** (`src/PeopleCore.Web/Program.cs:29-30`). Storing the fresh token therefore can't notify the cascading auth state, so the password-change gate force-reloads the page after success, as `Login.razor` already does. The flag is read from the token's `must_change_password` claim rather than from the login response.
5. **Policy refusals and failed actions on `/admin/users` show in an inline `Alert`**, the pattern every other page uses, rather than a toast.

## File Map

**API** (`src/PeopleCore.API`)
- Create `Accounts/AccountRoles.cs`: role name constants and the assignable and privileged lists.
- Create `Accounts/AccountManagementPolicy.cs`: the permission matrix, which has no Identity dependency.
- Create `Accounts/TemporaryPasswordGenerator.cs`
- Create `Accounts/AccountClaims.cs`: the `sec_stamp` and `must_change_password` claim names.
- Create `Accounts/AccountTokenValidator.cs`: per-request revocation check.
- Create `Accounts/PasswordChangeRequiredFilter.cs`: the global filter plus `AllowDuringPasswordChangeAttribute`.
- Create `Controllers/Account/UsersController.cs`: the `api/users` endpoints and their DTOs.
- Modify `Controllers/Auth/AuthController.cs`: token claims, refuse inactive accounts, return a token from change-password.
- Modify `Controllers/Account/ProfileController.cs`: exempt `Get` from the filter.
- Modify `Extensions/ServiceExtensions.cs`: identity options method, bearer events, DI registrations.
- Modify `Program.cs`: register the filter, and seed `Employee` on the admin.

**Infrastructure** (`src/PeopleCore.Infrastructure`)
- Modify `Identity/ApplicationUser.cs`: `IsActive`, `MustChangePassword`.
- Modify `Persistence/Configurations/Identity/ApplicationUserConfiguration.cs`: the unique filtered index.
- Create `Identity/IUserAccountDirectory.cs` and `Identity/UserAccountDirectory.cs`
- Create `Persistence/Migrations/<timestamp>_AddAccountStatus.cs` (generated, then edited).

**Web** (`src/PeopleCore.Web`)
- Modify `Services/ApiClient.cs`: account endpoints and DTOs; change-password returns a token.
- Create `Components/Accounts/ChangePasswordForm.razor`, extracted from My Profile.
- Create `Components/Accounts/RoleCheckboxes.razor`, `EmployeePicker.razor`, `CreateAccountDialog.razor`, `TemporaryPasswordDialog.razor`.
- Create `Layout/PasswordChangeGate.razor`
- Create `Pages/Admin/Users.razor`
- Modify `_Imports.razor`, `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, `Layout/MobileFooterNav.razor`, `Pages/ESS/MyProfile.razor`, `Pages/HR/Employees.razor`.

**Tests**
- `tests/PeopleCore.Infrastructure.Tests/AccountSchemaTests.cs`, `UserAccountDirectoryTests.cs`
- `tests/PeopleCore.Application.Tests/Api/`: `AccountManagementPolicyTests.cs`, `TemporaryPasswordGeneratorTests.cs`, `UsersControllerTestBase.cs`, `UsersControllerReadTests.cs`, `UsersControllerCreateTests.cs`, `UsersControllerChangeTests.cs`, `TestJwtConfiguration.cs`, `AuthTokenTests.cs`, `AccountTokenValidatorTests.cs`, `PasswordChangeRequiredFilterTests.cs`; modify `ChangePasswordTests.cs`.
- `tests/PeopleCore.Web.Tests/`: `Services/ApiClientAccountsTests.cs`, `Layout/PasswordChangeGateTests.cs`, `Pages/Admin/UsersTests.cs`; modify `Pages/ESS/MyProfileTests.cs`, `Layout/NavMenuTests.cs`, `Pages/HR/EmployeesTests.cs`.

---

### Task 1: Account status columns and one login per employee

**Files:**
- Modify: `src/PeopleCore.Infrastructure/Identity/ApplicationUser.cs`
- Modify: `src/PeopleCore.Infrastructure/Persistence/Configurations/Identity/ApplicationUserConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Migrations/<timestamp>_AddAccountStatus.cs` (generated)
- Test: `tests/PeopleCore.Infrastructure.Tests/AccountSchemaTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ApplicationUser.IsActive` (`bool`, default `true`), `ApplicationUser.MustChangePassword` (`bool`, default `false`), and a unique index on `AspNetUsers.employee_id` where not null.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Infrastructure.Tests/AccountSchemaTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~AccountSchemaTests"`
Expected: build error, `'ApplicationUser' does not contain a definition for 'IsActive'`.

- [ ] **Step 3: Add the fields**

In `src/PeopleCore.Infrastructure/Identity/ApplicationUser.cs`, directly after `public Guid? EmployeeId { get; set; }`, add:

```csharp

    // A deactivated account cannot sign in, and the tokens it already holds stop working on their
    // next request. Deliberately not Identity's lockout: failed sign-ins write LockoutEnd, and
    // sharing that field would let a lockout expiring quietly reactivate the account.
    public bool IsActive { get; set; } = true;

    // Set when Admin or HR issues a temporary password; cleared once the user chooses their own.
    public bool MustChangePassword { get; set; }
```

- [ ] **Step 4: Add the index**

Replace the body of `Configure` in `ApplicationUserConfiguration.cs` with:

```csharp
        builder.Property(u => u.FirstName).HasMaxLength(ApplicationUser.NameMaxLength);
        builder.Property(u => u.LastName).HasMaxLength(ApplicationUser.NameMaxLength);

        // One login per employee. Two accounts on one employee record would give two people the
        // same attendance, leave and payslips.
        builder.HasIndex(u => u.EmployeeId)
               .IsUnique()
               .HasFilter("employee_id IS NOT NULL");
```

No `HasDefaultValue` on `IsActive`. EF won't send a CLR default (`false`) when a database default exists, so a model default of `true` would make deactivation impossible to save. The migration sets the default for existing rows instead (Step 6).

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddAccountStatus --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API --output-dir Persistence/Migrations`
Expected: `Done.` There should be a new `Persistence/Migrations/<timestamp>_AddAccountStatus.cs` and designer file, and an updated `AppDbContextModelSnapshot.cs`.

- [ ] **Step 6: Make existing accounts active**

In the generated `Up`, the `is_active` column comes out with `defaultValue: false`, which would deactivate the seeded admin on deploy. Edit it so `Up` reads exactly:

```csharp
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_employee_id",
                table: "AspNetUsers",
                column: "employee_id",
                unique: true,
                filter: "employee_id IS NOT NULL");
```

Leave `Down` as generated.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~AccountSchemaTests"`
Expected: 3 passed.

- [ ] **Step 8: Commit**

```bash
git add src/PeopleCore.Infrastructure tests/PeopleCore.Infrastructure.Tests/AccountSchemaTests.cs
git commit -m "feat(identity): give accounts an active flag, a password-change flag, and one login per employee"
```

---

### Task 2: The permission matrix

**Files:**
- Create: `src/PeopleCore.API/Accounts/AccountRoles.cs`
- Create: `src/PeopleCore.API/Accounts/AccountManagementPolicy.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/AccountManagementPolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `PeopleCore.API.Accounts`):
  - `static class AccountRoles`: `const string Admin, HRManager, Manager, Employee, PayrollService`; `static IReadOnlyList<string> Assignable`, in the order Admin, HRManager, Manager, Employee, PayrollService; `static IReadOnlyList<string> Privileged`; `static bool IsPrivileged(IEnumerable<string> roles)`.
  - `sealed record AccountActor(string UserId, IReadOnlyCollection<string> Roles)` with `bool IsAdmin`.
  - `sealed record AccountTarget(string UserId, IReadOnlyCollection<string> Roles, bool IsActive)`.
  - `readonly record struct PolicyDecision(bool Allowed, string? Reason)` with `static PolicyDecision Allow` and `static PolicyDecision Deny(string reason)`.
  - `static class AccountManagementPolicy` with `AssignableRolesFor(AccountActor)`, `CanManage(actor, target)`, `CanCreate(actor, IReadOnlyCollection<string> roles)`, `CanSetRoles(actor, target, IReadOnlyCollection<string> roles, int activeAdmins)`, `CanLinkEmployee(actor, target)`, `CanDeactivate(actor, target, int activeAdmins)`, `CanReactivate(actor, target)`, `CanResetPassword(actor, target)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/AccountManagementPolicyTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.API.Accounts;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Who may change which account. Admins may change anything short of locking the organisation out
/// of its own administration. HR runs everyday accounts but cannot touch, or mint, Admin or HR
/// Manager accounts - otherwise any HR Manager could make themselves an administrator.
/// </summary>
public class AccountManagementPolicyTests
{
    private const string AdminId = "admin-1";
    private const string HrId = "hr-1";
    private const string OtherId = "other-1";

    private static readonly AccountActor Admin = new(AdminId, ["Admin", "Employee"]);
    private static readonly AccountActor Hr = new(HrId, ["HRManager", "Employee"]);

    private static AccountTarget Staff(params string[] roles) => new(OtherId, ["Employee", .. roles], IsActive: true);

    private static AccountTarget AdminAccount(string id = OtherId, bool active = true) => new(id, ["Admin", "Employee"], active);

    private static AccountTarget HrAccount() => new(OtherId, ["HRManager", "Employee"], IsActive: true);

    // --- Assignable roles -----------------------------------------------------------------------

    [Fact]
    public void AnAdmin_MayGrantEveryAssignableRole()
    {
        AccountManagementPolicy.AssignableRolesFor(Admin)
            .Should().Equal("Admin", "HRManager", "Manager", "Employee", "PayrollService");
    }

    [Fact]
    public void HR_MayGrantOnlyTheUnprivilegedRoles()
    {
        AccountManagementPolicy.AssignableRolesFor(Hr).Should().Equal("Manager", "Employee", "PayrollService");
    }

    // --- Create ---------------------------------------------------------------------------------

    [Fact]
    public void HR_MayCreateAManager()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Manager"]).Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HRManager")]
    public void HR_MayNotCreateAPrivilegedAccount(string role)
    {
        var decision = AccountManagementPolicy.CanCreate(Hr, ["Employee", role]);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be($"Only an administrator can grant or remove the {role} role.");
    }

    [Fact]
    public void AnAdmin_MayCreateAnotherAdmin()
    {
        AccountManagementPolicy.CanCreate(Admin, ["Employee", "Admin"]).Allowed.Should().BeTrue();
    }

    // --- Manage ---------------------------------------------------------------------------------

    [Fact]
    public void HR_MayManageAnEverydayAccount()
    {
        AccountManagementPolicy.CanManage(Hr, Staff("Manager")).Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotManageAnAdminOrAnotherHrManager()
    {
        AccountManagementPolicy.CanManage(Hr, AdminAccount()).Reason
            .Should().Be("Only an administrator can change an Admin or HR Manager account.");
        AccountManagementPolicy.CanManage(Hr, HrAccount()).Allowed.Should().BeFalse();
    }

    [Fact]
    public void AnAdmin_MayManageAPrivilegedAccount()
    {
        AccountManagementPolicy.CanManage(Admin, HrAccount()).Allowed.Should().BeTrue();
    }

    // --- Set roles ------------------------------------------------------------------------------

    [Fact]
    public void HR_MayGrantAndRemoveEverydayRoles()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Staff("PayrollService"), ["Employee", "Manager"], activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotPromoteAnyoneToHrManager()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Staff(), ["Employee", "HRManager"], activeAdmins: 1).Reason
            .Should().Be("Only an administrator can grant or remove the HRManager role.");
    }

    [Fact]
    public void HR_MayNotChangeTheRolesOfAPrivilegedAccount()
    {
        AccountManagementPolicy.CanSetRoles(Hr, HrAccount(), ["HRManager", "Employee", "Manager"], activeAdmins: 1)
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void ARoleNobodyCanAssign_ThatTheAccountAlreadyHolds_DoesNotBlockAnEdit()
    {
        // Service is seeded but unassignable. An account holding it is still HR's to edit; the
        // controller leaves the role in place.
        var target = new AccountTarget(OtherId, ["Employee", "Service"], IsActive: true);

        AccountManagementPolicy.CanSetRoles(Hr, target, ["Employee", "Manager"], activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void AnAdmin_MayNotRemoveTheirOwnAdminRole()
    {
        var self = AdminAccount(AdminId);

        AccountManagementPolicy.CanSetRoles(Admin, self, ["Employee"], activeAdmins: 3).Reason
            .Should().Be("You can't remove your own Admin or HR Manager role.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotLoseTheAdminRole()
    {
        AccountManagementPolicy.CanSetRoles(Admin, AdminAccount(), ["Employee"], activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void AnAdmin_MayLoseTheAdminRole_WhileAnotherActiveAdminRemains()
    {
        AccountManagementPolicy.CanSetRoles(Admin, AdminAccount(), ["Employee"], activeAdmins: 2)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void ADeactivatedAdmin_IsNotTheLastActiveAdmin()
    {
        AccountManagementPolicy.CanSetRoles(Admin, AdminAccount(active: false), ["Employee"], activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    // --- Deactivate / reactivate ----------------------------------------------------------------

    [Fact]
    public void HR_MayDeactivateAnEverydayAccount_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanDeactivate(Hr, Staff(), activeAdmins: 1).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanDeactivate(Hr, HrAccount(), activeAdmins: 1).Allowed.Should().BeFalse();
    }

    [Fact]
    public void NobodyMayDeactivateTheirOwnAccount()
    {
        AccountManagementPolicy.CanDeactivate(Admin, AdminAccount(AdminId), activeAdmins: 3).Reason
            .Should().Be("You can't deactivate your own account.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotBeDeactivated()
    {
        AccountManagementPolicy.CanDeactivate(Admin, AdminAccount(), activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void HR_MayReactivateAnEverydayAccount_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanReactivate(Hr, Staff() with { IsActive = false }).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanReactivate(Hr, AdminAccount(active: false)).Allowed.Should().BeFalse();
    }

    // --- Reset password / link employee ---------------------------------------------------------

    [Fact]
    public void NobodyMayResetTheirOwnPassword_ThatIsWhatChangePasswordIsFor()
    {
        AccountManagementPolicy.CanResetPassword(Admin, AdminAccount(AdminId)).Reason
            .Should().Be("Use Change Password on My Profile to change your own password.");
    }

    [Fact]
    public void HR_MayResetAnEverydayPassword_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanResetPassword(Hr, Staff()).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanResetPassword(Hr, AdminAccount()).Allowed.Should().BeFalse();
    }

    [Fact]
    public void HR_MayLinkAnEverydayAccountToAnEmployee_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanLinkEmployee(Hr, Staff()).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanLinkEmployee(Hr, HrAccount()).Allowed.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~AccountManagementPolicyTests"`
Expected: build error, `The type or namespace name 'Accounts' does not exist in the namespace 'PeopleCore.API'`.

- [ ] **Step 3: Write `AccountRoles`**

Create `src/PeopleCore.API/Accounts/AccountRoles.cs`:

```csharp
namespace PeopleCore.API.Accounts;

/// <summary>
/// The roles user management can hand out. Program.cs also seeds "Service", which no endpoint
/// uses, so it is deliberately absent here and cannot be granted.
/// </summary>
public static class AccountRoles
{
    public const string Admin = "Admin";
    public const string HRManager = "HRManager";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string PayrollService = "PayrollService";

    /// <summary>In display order. <see cref="Employee"/> is on every account and cannot be removed.</summary>
    public static readonly IReadOnlyList<string> Assignable = [Admin, HRManager, Manager, Employee, PayrollService];

    /// <summary>Roles that confer control over other accounts, and so only an Admin may grant.</summary>
    public static readonly IReadOnlyList<string> Privileged = [Admin, HRManager];

    public static bool IsPrivileged(IEnumerable<string> roles) => roles.Any(role => Privileged.Contains(role));
}
```

- [ ] **Step 4: Write the policy**

Create `src/PeopleCore.API/Accounts/AccountManagementPolicy.cs`:

```csharp
namespace PeopleCore.API.Accounts;

/// <summary>The signed-in caller acting on an account.</summary>
public sealed record AccountActor(string UserId, IReadOnlyCollection<string> Roles)
{
    public bool IsAdmin => Roles.Contains(AccountRoles.Admin);
}

/// <summary>The account being acted on, as it stands before the change.</summary>
public sealed record AccountTarget(string UserId, IReadOnlyCollection<string> Roles, bool IsActive);

public readonly record struct PolicyDecision(bool Allowed, string? Reason)
{
    public static PolicyDecision Allow => new(true, null);

    public static PolicyDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Who may change which account. Plain values in, a decision and a sentence out - no Identity, no
/// database - so every combination is unit tested. The sentence is shown to the caller as it is.
/// </summary>
public static class AccountManagementPolicy
{
    private const string PrivilegedTarget = "Only an administrator can change an Admin or HR Manager account.";
    private const string SelfDemotion = "You can't remove your own Admin or HR Manager role.";
    private const string SelfDeactivation = "You can't deactivate your own account.";
    private const string SelfReset = "Use Change Password on My Profile to change your own password.";
    private const string LastAdmin = "This is the last active administrator. Make another account an Admin first.";

    public static IReadOnlyList<string> AssignableRolesFor(AccountActor actor) =>
        actor.IsAdmin
            ? AccountRoles.Assignable
            : AccountRoles.Assignable.Where(role => !AccountRoles.Privileged.Contains(role)).ToList();

    /// <summary>Whether the actor may change the target at all. Every other check starts here.</summary>
    public static PolicyDecision CanManage(AccountActor actor, AccountTarget target) =>
        actor.IsAdmin || !AccountRoles.IsPrivileged(target.Roles)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny(PrivilegedTarget);

    public static PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> roles) =>
        FirstUngrantable(actor, roles) is { } role ? Ungrantable(role) : PolicyDecision.Allow;

    public static PolicyDecision CanSetRoles(
        AccountActor actor, AccountTarget target, IReadOnlyCollection<string> roles, int activeAdmins)
    {
        var manage = CanManage(actor, target);
        if (!manage.Allowed) return manage;

        // Only assignable roles are compared: one nobody can assign (Service) stays where it is.
        var held = target.Roles.Where(role => AccountRoles.Assignable.Contains(role)).ToHashSet();
        var changed = held.Except(roles).Concat(roles.Except(held)).ToList();
        if (FirstUngrantable(actor, changed) is { } forbidden) return Ungrantable(forbidden);

        var losesPrivilege = AccountRoles.Privileged.Any(role => held.Contains(role) && !roles.Contains(role));
        if (losesPrivilege && actor.UserId == target.UserId) return PolicyDecision.Deny(SelfDemotion);

        if (IsLastActiveAdmin(target, activeAdmins) && !roles.Contains(AccountRoles.Admin))
            return PolicyDecision.Deny(LastAdmin);

        return PolicyDecision.Allow;
    }

    public static PolicyDecision CanLinkEmployee(AccountActor actor, AccountTarget target) => CanManage(actor, target);

    public static PolicyDecision CanDeactivate(AccountActor actor, AccountTarget target, int activeAdmins)
    {
        var manage = CanManage(actor, target);
        if (!manage.Allowed) return manage;
        if (actor.UserId == target.UserId) return PolicyDecision.Deny(SelfDeactivation);
        return IsLastActiveAdmin(target, activeAdmins) ? PolicyDecision.Deny(LastAdmin) : PolicyDecision.Allow;
    }

    public static PolicyDecision CanReactivate(AccountActor actor, AccountTarget target) => CanManage(actor, target);

    public static PolicyDecision CanResetPassword(AccountActor actor, AccountTarget target) =>
        actor.UserId == target.UserId ? PolicyDecision.Deny(SelfReset) : CanManage(actor, target);

    private static string? FirstUngrantable(AccountActor actor, IEnumerable<string> roles)
    {
        var grantable = AssignableRolesFor(actor);
        return roles.FirstOrDefault(role => !grantable.Contains(role));
    }

    private static PolicyDecision Ungrantable(string role) =>
        PolicyDecision.Deny($"Only an administrator can grant or remove the {role} role.");

    private static bool IsLastActiveAdmin(AccountTarget target, int activeAdmins) =>
        target.IsActive && target.Roles.Contains(AccountRoles.Admin) && activeAdmins <= 1;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~AccountManagementPolicyTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.API/Accounts tests/PeopleCore.Application.Tests/Api/AccountManagementPolicyTests.cs
git commit -m "feat(api): decide who may change which account"
```

---

### Task 3: Temporary passwords

**Files:**
- Create: `src/PeopleCore.API/Accounts/TemporaryPasswordGenerator.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs:52-60`
- Test: `tests/PeopleCore.Application.Tests/Api/TemporaryPasswordGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static string TemporaryPasswordGenerator.Generate()`, `const int TemporaryPasswordGenerator.Length = 16`, and `public static void ServiceExtensions.ConfigureIdentityOptions(IdentityOptions options)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/TemporaryPasswordGeneratorTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Extensions;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A temporary password is read aloud or copied by hand from one person to another, so it avoids
/// characters that look alike - and it must always satisfy the password policy, or creating an
/// account would fail at random.
/// </summary>
public class TemporaryPasswordGeneratorTests
{
    private const int Samples = 500;

    [Fact]
    public void APassword_IsSixteenCharacters()
    {
        TemporaryPasswordGenerator.Generate().Should().HaveLength(16);
    }

    [Fact]
    public void EveryPassword_MixesCaseAndDigits_WithoutLookAlikes()
    {
        for (var i = 0; i < Samples; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();

            password.Should().Match(p => p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit), password);
            password.Should().NotContainAny(["0", "O", "1", "l", "I"]);
            password.All(char.IsLetterOrDigit).Should().BeTrue(password);
        }
    }

    [Fact]
    public void Passwords_AreNotRepeated()
    {
        Enumerable.Range(0, Samples).Select(_ => TemporaryPasswordGenerator.Generate())
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task EveryPassword_SatisfiesTheAppsOwnPasswordPolicy()
    {
        var options = new IdentityOptions();
        ServiceExtensions.ConfigureIdentityOptions(options);
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(options), null!, null!, null!, null!, null!, null!, null!);
        var validator = new PasswordValidator<ApplicationUser>();

        for (var i = 0; i < Samples; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();
            var result = await validator.ValidateAsync(users.Object, new ApplicationUser(), password);
            result.Succeeded.Should().BeTrue(password);
        }
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~TemporaryPasswordGeneratorTests"`
Expected: build error, `The name 'TemporaryPasswordGenerator' does not exist`.

- [ ] **Step 3: Pull the identity options out where a test can reach them**

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, replace:

```csharp
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
```

with:

```csharp
        services.AddIdentity<ApplicationUser, IdentityRole>(ConfigureIdentityOptions)
```

Then add this method to the class, directly above `ResolveJwtSigningKey`:

```csharp
    /// <summary>
    /// The password and lockout rules every account is held to. A method rather than a lambda so
    /// the test that proves a generated temporary password always passes them uses these rules,
    /// not a copy that could drift.
    /// </summary>
    public static void ConfigureIdentityOptions(IdentityOptions options)
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    }
```

- [ ] **Step 4: Write the generator**

Create `src/PeopleCore.API/Accounts/TemporaryPasswordGenerator.cs`:

```csharp
using System.Security.Cryptography;

namespace PeopleCore.API.Accounts;

/// <summary>
/// The one-time password Admin or HR hands to a user when creating their account or resetting it.
/// The user must replace it at sign-in, so its job is to be unguessable and easy to pass on: letters
/// and digits only, none of the pairs people misread (0/O, 1/l/I).
/// </summary>
public static class TemporaryPasswordGenerator
{
    public const int Length = 16;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Any = Upper + Lower + Digits;

    public static string Generate()
    {
        var chars = new char[Length];

        // One of each class guarantees the policy is met; the shuffle stops them always leading.
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        for (var i = 3; i < Length; i++)
            chars[i] = Pick(Any);

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~TemporaryPasswordGeneratorTests"`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.API tests/PeopleCore.Application.Tests/Api/TemporaryPasswordGeneratorTests.cs
git commit -m "feat(api): generate temporary passwords that always meet the password policy"
```

---

### Task 4: Reading accounts: the user account directory

**Files:**
- Create: `src/PeopleCore.Infrastructure/Identity/IUserAccountDirectory.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/UserAccountDirectory.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (DI)
- Test: `tests/PeopleCore.Infrastructure.Tests/UserAccountDirectoryTests.cs`

**Interfaces:**
- Consumes: `ApplicationUser.IsActive` and `MustChangePassword` (Task 1).
- Produces (namespace `PeopleCore.Infrastructure.Identity`):
  - `sealed record UserAccountRow(string Id, string Email, string? FirstName, string? LastName, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword, Guid? EmployeeId, string? EmployeeName)`
  - `sealed record EmployeeLinkRow(Guid EmployeeId, string UserId, bool IsActive)`
  - `interface IUserAccountDirectory` with:
    - `Task<(IReadOnlyList<UserAccountRow> Items, int TotalCount)> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default)`
    - `Task<UserAccountRow?> GetAsync(string userId, CancellationToken ct = default)`
    - `Task<IReadOnlyList<EmployeeLinkRow>> GetEmployeeLinksAsync(CancellationToken ct = default)`
    - `Task<int> CountActiveInRoleAsync(string role, CancellationToken ct = default)`
    - `Task<string?> FindUserLinkedToEmployeeAsync(Guid employeeId, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Infrastructure.Tests/UserAccountDirectoryTests.cs`:

```csharp
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

    private async Task<IdentityRole> RoleAsync(string name)
    {
        var role = new IdentityRole(name) { NormalizedName = name.ToUpperInvariant() };
        Context.Roles.Add(role);
        await Context.SaveChangesAsync();
        return role;
    }

    private async Task<ApplicationUser> AccountAsync(
        string email, string? firstName = null, string? lastName = null, Guid? employeeId = null,
        bool active = true, IdentityRole[]? roles = null)
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
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~UserAccountDirectoryTests"`
Expected: build error, `The type or namespace name 'UserAccountDirectory' could not be found`.

- [ ] **Step 3: Write the interface**

Create `src/PeopleCore.Infrastructure/Identity/IUserAccountDirectory.cs`:

```csharp
namespace PeopleCore.Infrastructure.Identity;

/// <summary>An account as the user management screens list it.</summary>
public sealed record UserAccountRow(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool MustChangePassword,
    Guid? EmployeeId,
    string? EmployeeName);

/// <summary>Which employee a login belongs to.</summary>
public sealed record EmployeeLinkRow(Guid EmployeeId, string UserId, bool IsActive);

/// <summary>
/// Reads across accounts, their roles and their employees. Changes to an account go through
/// UserManager; these reads need joins UserManager does not offer and, unlike UserManager.Users,
/// can be stubbed in a controller test.
/// </summary>
public interface IUserAccountDirectory
{
    /// <summary>Accounts whose email, first or last name contains <paramref name="search"/>, ignoring case, ordered by email.</summary>
    Task<(IReadOnlyList<UserAccountRow> Items, int TotalCount)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);

    Task<UserAccountRow?> GetAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeLinkRow>> GetEmployeeLinksAsync(CancellationToken ct = default);

    Task<int> CountActiveInRoleAsync(string role, CancellationToken ct = default);

    Task<string?> FindUserLinkedToEmployeeAsync(Guid employeeId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the implementation**

Create `src/PeopleCore.Infrastructure/Identity/UserAccountDirectory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IUserAccountDirectory"/>
public class UserAccountDirectory : IUserAccountDirectory
{
    private readonly AppDbContext _db;

    public UserAccountDirectory(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<UserAccountRow> Items, int TotalCount)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var users = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            users = users.Where(u =>
                EF.Functions.ILike(u.Email!, pattern) ||
                EF.Functions.ILike(u.FirstName!, pattern) ||
                EF.Functions.ILike(u.LastName!, pattern));
        }

        var total = await users.CountAsync(ct);
        var pageOfUsers = await users
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (await ToRowsAsync(pageOfUsers, ct), total);
    }

    public async Task<UserAccountRow?> GetAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : (await ToRowsAsync([user], ct))[0];
    }

    public async Task<IReadOnlyList<EmployeeLinkRow>> GetEmployeeLinksAsync(CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.EmployeeId != null)
            .Select(u => new EmployeeLinkRow(u.EmployeeId!.Value, u.Id, u.IsActive))
            .ToListAsync(ct);

    public Task<int> CountActiveInRoleAsync(string role, CancellationToken ct = default) =>
        (from user in _db.Users
         join userRole in _db.UserRoles on user.Id equals userRole.UserId
         join identityRole in _db.Roles on userRole.RoleId equals identityRole.Id
         where user.IsActive && identityRole.Name == role
         select user.Id)
        .Distinct()
        .CountAsync(ct);

    public Task<string?> FindUserLinkedToEmployeeAsync(Guid employeeId, CancellationToken ct = default) =>
        _db.Users
            .Where(u => u.EmployeeId == employeeId)
            .Select(u => (string?)u.Id)
            .SingleOrDefaultAsync(ct);

    // Two batched queries for a whole page - roles and employee names - rather than two per account.
    private async Task<IReadOnlyList<UserAccountRow>> ToRowsAsync(IReadOnlyList<ApplicationUser> users, CancellationToken ct)
    {
        var userIds = users.Select(u => u.Id).ToList();
        var roles = await (from userRole in _db.UserRoles
                           join identityRole in _db.Roles on userRole.RoleId equals identityRole.Id
                           where userIds.Contains(userRole.UserId)
                           select new { userRole.UserId, identityRole.Name })
                          .ToListAsync(ct);

        var employeeIds = users.Where(u => u.EmployeeId.HasValue).Select(u => u.EmployeeId!.Value).ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, ct);

        return users.Select(u => new UserAccountRow(
                u.Id,
                u.Email ?? string.Empty,
                u.FirstName,
                u.LastName,
                roles.Where(r => r.UserId == u.Id && r.Name != null).Select(r => r.Name!).Order().ToList(),
                u.IsActive,
                u.MustChangePassword,
                u.EmployeeId,
                u.EmployeeId is { } id && employees.TryGetValue(id, out var employee)
                    ? $"{employee.FirstName} {employee.LastName}"
                    : null))
            .ToList();
    }
}
```

- [ ] **Step 5: Register it**

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, directly after `services.AddScoped<ICurrentUserService, CurrentUserService>();`, add:

```csharp
        services.AddScoped<IUserAccountDirectory, UserAccountDirectory>();
```

(`PeopleCore.Infrastructure.Identity` is already imported.)

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~UserAccountDirectoryTests"`
Expected: 7 passed.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Infrastructure/Identity src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Infrastructure.Tests/UserAccountDirectoryTests.cs
git commit -m "feat(identity): list and count accounts with their roles and employees"
```

---

### Task 5: `api/users`: list, read and create

**Files:**
- Create: `src/PeopleCore.API/Controllers/Account/UsersController.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/UsersControllerTestBase.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/UsersControllerReadTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/UsersControllerCreateTests.cs`

**Interfaces:**
- Consumes: `AccountRoles`, `AccountActor`, `AccountTarget`, `PolicyDecision`, `AccountManagementPolicy` (Task 2); `TemporaryPasswordGenerator.Generate()` (Task 3); `IUserAccountDirectory` and `UserAccountRow` (Task 4); `IEmployeeRepository.GetByIdAsync(Guid, CancellationToken)` (existing); `PagedResult<T>.Create(items, totalCount, page, pageSize)` from `PeopleCore.Application.Common.DTOs` (existing).
- Produces (namespace `PeopleCore.API.Controllers.Account`):
  - `UsersController(UserManager<ApplicationUser> users, IUserAccountDirectory directory, IEmployeeRepository employees)` with the actions `List(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)`, `AssignableRoles()`, `EmployeeLinks(CancellationToken)`, `Get(string id, CancellationToken)` and `Create(CreateUserAccountRequest, CancellationToken)`.
  - DTOs: `UserAccountDto(string Id, string Email, string? FirstName, string? LastName, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword, Guid? EmployeeId, string? EmployeeName, bool CanManage)`, `CreateUserAccountRequest(string? Email, string? FirstName, string? LastName, Guid? EmployeeId, IReadOnlyList<string>? Roles)`, `CreatedUserAccountDto(UserAccountDto Account, string TemporaryPassword)`, `EmployeeLinkDto(Guid EmployeeId, string UserId, bool IsActive)`.
  - Private helpers Task 6 reuses: `Caller`, `Target(ApplicationUser, IEnumerable<string>)`, `ToDto(UserAccountRow)`, `NormalizeRoles`, `ValidateEmployeeLinkAsync`, `AccountProblem(string)`, `Refused(PolicyDecision)`, `Describe(IdentityResult)`, `AccountAsync(string id, CancellationToken)`.

- [ ] **Step 1: Write the shared test fixture**

Create `tests/PeopleCore.Application.Tests/Api/UsersControllerTestBase.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
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
    protected readonly UsersController Sut;

    protected UsersControllerTestBase()
    {
        Users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        Users.Setup(u => u.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        Directory.Setup(d => d.CountActiveInRoleAsync("Admin", It.IsAny<CancellationToken>())).ReturnsAsync(2);

        Sut = new UsersController(Users.Object, Directory.Object, Employees.Object);
        SignInAs("Admin", "Employee");
    }

    protected void SignInAs(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, CallerId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
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
```

- [ ] **Step 2: Write the failing read tests**

Create `tests/PeopleCore.Application.Tests/Api/UsersControllerReadTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

public class UsersControllerReadTests : UsersControllerTestBase
{
    [Fact]
    public void TheWholeController_IsForAdminAndHrOnly()
    {
        typeof(UsersController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Admin,HRManager");
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
```

- [ ] **Step 3: Write the failing create tests**

Create `tests/PeopleCore.Application.Tests/Api/UsersControllerCreateTests.cs`:

```csharp
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
}
```

- [ ] **Step 4: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~UsersController"`
Expected: build error, `The type or namespace name 'UsersController' could not be found`.

- [ ] **Step 5: Write the controller**

Create `src/PeopleCore.API/Controllers/Account/UsersController.cs`:

```csharp
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Accounts;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// Sign-in accounts: creating them, the roles they hold, the employee each belongs to, and whether
/// they may sign in at all. Whether the caller may make a given change is
/// <see cref="AccountManagementPolicy"/>'s decision; this controller asks, then acts. Every change
/// replaces the account's security stamp, which revokes the tokens that account already holds.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,HRManager")]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly UserManager<ApplicationUser> _users;
    private readonly IUserAccountDirectory _directory;
    private readonly IEmployeeRepository _employees;

    public UsersController(UserManager<ApplicationUser> users, IUserAccountDirectory directory, IEmployeeRepository employees)
    {
        _users = users;
        _directory = directory;
        _employees = employees;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserAccountDto>>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (rows, total) = await _directory.SearchAsync(search, page, pageSize, ct);
        return Ok(PagedResult<UserAccountDto>.Create(rows.Select(ToDto).ToList(), total, page, pageSize));
    }

    [HttpGet("assignable-roles")]
    public ActionResult<IReadOnlyList<string>> AssignableRoles() => Ok(AccountManagementPolicy.AssignableRolesFor(Caller));

    [HttpGet("employee-links")]
    public async Task<ActionResult<IReadOnlyList<EmployeeLinkDto>>> EmployeeLinks(CancellationToken ct) =>
        Ok((await _directory.GetEmployeeLinksAsync(ct))
            .Select(link => new EmployeeLinkDto(link.EmployeeId, link.UserId, link.IsActive))
            .ToList());

    [HttpGet("{id}")]
    public Task<ActionResult<UserAccountDto>> Get(string id, CancellationToken ct) => AccountAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CreatedUserAccountDto>> Create([FromBody] CreateUserAccountRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        var problem = ValidateEmail(email) ?? ValidateName(firstName, "first name") ?? ValidateName(lastName, "last name");
        if (problem is not null) return AccountProblem(problem);
        if (NormalizeRoles(request.Roles, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var decision = AccountManagementPolicy.CanCreate(Caller, roles);
        if (!decision.Allowed) return Refused(decision);

        if (await _users.FindByEmailAsync(email!) is not null)
            return AccountProblem($"An account with the email {email} already exists.");
        if (request.EmployeeId is { } employeeId && await ValidateEmployeeLinkAsync(employeeId, exceptUserId: null, ct) is { } linkProblem)
            return AccountProblem(linkProblem);

        var password = TemporaryPasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            EmployeeId = request.EmployeeId,
            IsActive = true,
            MustChangePassword = true
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded) return AccountProblem(Describe(created));

        var granted = await _users.AddToRolesAsync(user, roles);
        if (!granted.Succeeded)
        {
            // An account with no roles is not what was asked for; do not leave one behind.
            await _users.DeleteAsync(user);
            return AccountProblem(Describe(granted));
        }

        var row = await _directory.GetAsync(user.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, new CreatedUserAccountDto(ToDto(row!), password));
    }

    // --- Shared by every action ---------------------------------------------------------------

    // Roles come from the token. That is safe to trust because AccountTokenValidator rejects any
    // token issued before the account's roles last changed.
    private AccountActor Caller => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList());

    private static AccountTarget Target(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, roles.ToList(), user.IsActive);

    private UserAccountDto ToDto(UserAccountRow row) => new(
        row.Id, row.Email, row.FirstName, row.LastName, row.Roles, row.IsActive, row.MustChangePassword,
        row.EmployeeId, row.EmployeeName,
        CanManage: AccountManagementPolicy.CanManage(Caller, new AccountTarget(row.Id, row.Roles, row.IsActive)).Allowed);

    private async Task<ActionResult<UserAccountDto>> AccountAsync(string id, CancellationToken ct)
    {
        var row = await _directory.GetAsync(id, ct);
        return row is null ? NotFound() : Ok(ToDto(row));
    }

    /// <summary>
    /// The requested roles in <see cref="AccountRoles.Assignable"/> order, always with Employee.
    /// Returns a problem naming the first role that cannot be assigned, if any.
    /// </summary>
    private static string? NormalizeRoles(IReadOnlyList<string>? requested, out IReadOnlyList<string> roles)
    {
        requested ??= [];
        roles = AccountRoles.Assignable
            .Where(role => role == AccountRoles.Employee || requested.Contains(role))
            .ToList();

        var unknown = requested.FirstOrDefault(role => !AccountRoles.Assignable.Contains(role));
        return unknown is null ? null : $"{unknown} is not a role that can be assigned.";
    }

    private async Task<string?> ValidateEmployeeLinkAsync(Guid employeeId, string? exceptUserId, CancellationToken ct)
    {
        if (await _employees.GetByIdAsync(employeeId, ct) is null)
            return "That employee record does not exist.";

        var linkedTo = await _directory.FindUserLinkedToEmployeeAsync(employeeId, ct);
        return linkedTo is not null && linkedTo != exceptUserId ? "That employee already has a login." : null;
    }

    // The exact-match check rejects display-name forms such as "Ana <ana@company.test>", which
    // MailAddress accepts but which cannot be a sign-in username.
    private static string? ValidateEmail(string? email) =>
        !string.IsNullOrEmpty(email) && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email
            ? null
            : "Enter a valid email address.";

    private static string? ValidateName(string? name, string label) => name switch
    {
        null or "" => $"Enter a {label}.",
        { Length: > ApplicationUser.NameMaxLength } =>
            $"{char.ToUpperInvariant(label[0])}{label[1..]} must be {ApplicationUser.NameMaxLength} characters or fewer.",
        _ => null
    };

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));

    private BadRequestObjectResult AccountProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Account not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });

    private ObjectResult Refused(PolicyDecision decision) =>
        StatusCode(StatusCodes.Status403Forbidden,
            new ProblemDetails { Title = "Not allowed", Detail = decision.Reason, Status = StatusCodes.Status403Forbidden });
}

public record UserAccountDto(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool MustChangePassword,
    Guid? EmployeeId,
    string? EmployeeName,
    bool CanManage);

public record CreateUserAccountRequest(string? Email, string? FirstName, string? LastName, Guid? EmployeeId, IReadOnlyList<string>? Roles);

public record CreatedUserAccountDto(UserAccountDto Account, string TemporaryPassword);

public record EmployeeLinkDto(Guid EmployeeId, string UserId, bool IsActive);
```

`Target(...)` is unused until Task 6. If the compiler warns about it, leave the warning.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~UsersController"`
Expected: all read and create tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.API/Controllers/Account/UsersController.cs tests/PeopleCore.Application.Tests/Api/UsersController*.cs
git commit -m "feat(api): list accounts and create them with a temporary password"
```

---

### Task 6: `api/users`: roles, employee link, deactivate, reactivate, reset password

**Files:**
- Modify: `src/PeopleCore.API/Controllers/Account/UsersController.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/UsersControllerChangeTests.cs`

**Interfaces:**
- Consumes: everything from Task 5, plus `AccountManagementPolicy.CanSetRoles`, `CanLinkEmployee`, `CanDeactivate`, `CanReactivate` and `CanResetPassword` (Task 2), and `IUserAccountDirectory.CountActiveInRoleAsync` (Task 4).
- Produces: new actions `SetRoles(string id, SetRolesRequest, CancellationToken)`, `LinkEmployee(string id, LinkEmployeeRequest, CancellationToken)`, `Deactivate(string id, CancellationToken)`, `Reactivate(string id, CancellationToken)` and `ResetPassword(string id, CancellationToken)`. New DTOs `SetRolesRequest(IReadOnlyList<string>? Roles)`, `LinkEmployeeRequest(Guid? EmployeeId)` and `TemporaryPasswordDto(string TemporaryPassword)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/UsersControllerChangeTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~UsersControllerChangeTests"`
Expected: build error, `'UsersController' does not contain a definition for 'SetRoles'`.

- [ ] **Step 3: Add the actions**

In `UsersController.cs`, insert these actions directly after `Create` and before the `// --- Shared by every action` comment:

```csharp
    [HttpPut("{id}/roles")]
    public async Task<ActionResult<UserAccountDto>> SetRoles(string id, [FromBody] SetRolesRequest request, CancellationToken ct)
    {
        if (NormalizeRoles(request.Roles, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var held = await _users.GetRolesAsync(user);

        var decision = AccountManagementPolicy.CanSetRoles(Caller, Target(user, held), roles, await ActiveAdminsAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        // Only assignable roles are compared, so a role nobody can assign (Service) is left alone.
        var toAdd = roles.Except(held).ToList();
        var toRemove = held.Where(role => AccountRoles.Assignable.Contains(role)).Except(roles).ToList();

        if (toAdd.Count > 0 && await _users.AddToRolesAsync(user, toAdd) is { Succeeded: false } added)
            return AccountProblem(Describe(added));
        if (toRemove.Count > 0 && await _users.RemoveFromRolesAsync(user, toRemove) is { Succeeded: false } removed)
            return AccountProblem(Describe(removed));

        return await SaveAndRevokeAsync(user, ct);
    }

    [HttpPut("{id}/employee")]
    public async Task<ActionResult<UserAccountDto>> LinkEmployee(string id, [FromBody] LinkEmployeeRequest request, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var decision = AccountManagementPolicy.CanLinkEmployee(Caller, Target(user, await _users.GetRolesAsync(user)));
        if (!decision.Allowed) return Refused(decision);

        if (request.EmployeeId is { } employeeId && await ValidateEmployeeLinkAsync(employeeId, exceptUserId: id, ct) is { } linkProblem)
            return AccountProblem(linkProblem);

        // The employee_id claim in the account's tokens is now wrong, so they are revoked too.
        user.EmployeeId = request.EmployeeId;
        return await SaveAndRevokeAsync(user, ct);
    }

    [HttpPost("{id}/deactivate")]
    public Task<ActionResult<UserAccountDto>> Deactivate(string id, CancellationToken ct) => SetActiveAsync(id, active: false, ct);

    [HttpPost("{id}/reactivate")]
    public Task<ActionResult<UserAccountDto>> Reactivate(string id, CancellationToken ct) => SetActiveAsync(id, active: true, ct);

    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<TemporaryPasswordDto>> ResetPassword(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var decision = AccountManagementPolicy.CanResetPassword(Caller, Target(user, await _users.GetRolesAsync(user)));
        if (!decision.Allowed) return Refused(decision);

        var password = TemporaryPasswordGenerator.Generate();
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded) return AccountProblem(Describe(reset));

        // A user locked out by failed guesses is usually why the reset was asked for.
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);

        user.MustChangePassword = true;
        var saved = await _users.UpdateSecurityStampAsync(user);
        if (!saved.Succeeded) return AccountProblem(Describe(saved));

        return Ok(new TemporaryPasswordDto(password));
    }
```

Then add these private helpers in the `// --- Shared by every action` section, directly after `AccountAsync`:

```csharp
    private async Task<ActionResult<UserAccountDto>> SetActiveAsync(string id, bool active, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var target = Target(user, await _users.GetRolesAsync(user));
        var decision = active
            ? AccountManagementPolicy.CanReactivate(Caller, target)
            : AccountManagementPolicy.CanDeactivate(Caller, target, await ActiveAdminsAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        user.IsActive = active;
        return await SaveAndRevokeAsync(user, ct);
    }

    /// <summary>
    /// Replaces the security stamp, which revokes every token the account holds. UpdateSecurityStampAsync
    /// saves the whole user, so any field set just before it is written in the same update.
    /// </summary>
    private async Task<ActionResult<UserAccountDto>> SaveAndRevokeAsync(ApplicationUser user, CancellationToken ct)
    {
        var saved = await _users.UpdateSecurityStampAsync(user);
        if (!saved.Succeeded) return AccountProblem(Describe(saved));
        return await AccountAsync(user.Id, ct);
    }

    private Task<int> ActiveAdminsAsync(CancellationToken ct) => _directory.CountActiveInRoleAsync(AccountRoles.Admin, ct);
```

Finally, append these DTOs at the bottom of the file:

```csharp
public record SetRolesRequest(IReadOnlyList<string>? Roles);

public record LinkEmployeeRequest(Guid? EmployeeId);

public record TemporaryPasswordDto(string TemporaryPassword);
```

- [ ] **Step 4: Run all the controller tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~UsersController"`
Expected: all pass, including the Task 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.API/Controllers/Account/UsersController.cs tests/PeopleCore.Application.Tests/Api/UsersControllerChangeTests.cs
git commit -m "feat(api): change an account's roles, employee, status and password"
```

---

### Task 7: Revocable tokens: security stamp, inactive accounts, fresh token on password change

**Files:**
- Create: `src/PeopleCore.API/Accounts/AccountClaims.cs`
- Create: `src/PeopleCore.API/Accounts/AccountTokenValidator.cs`
- Modify: `src/PeopleCore.API/Controllers/Auth/AuthController.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (`AddJwtBearer` block, DI)
- Modify: `src/PeopleCore.API/Program.cs` (admin seeding)
- Test: `tests/PeopleCore.Application.Tests/Api/TestJwtConfiguration.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/AccountTokenValidatorTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/AuthTokenTests.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Api/ChangePasswordTests.cs`

**Interfaces:**
- Consumes: `ApplicationUser.IsActive` and `MustChangePassword` (Task 1).
- Produces:
  - `static class AccountClaims { const string SecurityStamp = "sec_stamp"; const string MustChangePassword = "must_change_password"; }` (namespace `PeopleCore.API.Accounts`)
  - `class AccountTokenValidator(UserManager<ApplicationUser>)` with `Task<string?> FindRejectionAsync(ClaimsPrincipal principal)`: null means the token may be used.
  - `static JwtBearerEvents ServiceExtensions.CreateJwtBearerEvents()`
  - `record AuthTokenResponse(string Token, string? Email, IReadOnlyList<string> Roles, bool MustChangePassword)` (namespace `PeopleCore.API.Controllers.Auth`), returned by both `Login` and `ChangePassword`.
  - JSON for login and change-password: `{ "token", "email", "roles", "mustChangePassword" }`.

- [ ] **Step 1: Write a shared JWT configuration for tests**

Create `tests/PeopleCore.Application.Tests/Api/TestJwtConfiguration.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace PeopleCore.Application.Tests.Api;

/// <summary>Enough configuration for AuthController to sign a real token.</summary>
public static class TestJwtConfiguration
{
    public static IConfiguration Create() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "a-test-signing-key-that-is-comfortably-over-32-bytes",
            ["Jwt:Issuer"] = "peoplecore-tests",
            ["Jwt:Audience"] = "peoplecore-tests",
            ["Jwt:ExpiryMinutes"] = "60"
        })
        .Build();
}
```

- [ ] **Step 2: Write the failing validator tests**

Create `tests/PeopleCore.Application.Tests/Api/AccountTokenValidatorTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Extensions;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A token is only as good as the account behind it right now. Deactivating the account, or any
/// change that replaces its security stamp, stops the token working on its very next request.
/// </summary>
public class AccountTokenValidatorTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = new(
        Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly ApplicationUser _user = new() { Id = "u1", SecurityStamp = "stamp-1", IsActive = true };

    public AccountTokenValidatorTests() => _users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(_user);

    private static ClaimsPrincipal Token(string? userId = "u1", string? stamp = "stamp-1")
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (stamp is not null) claims.Add(new Claim(AccountClaims.SecurityStamp, stamp));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private Task<string?> Check(ClaimsPrincipal token) => new AccountTokenValidator(_users.Object).FindRejectionAsync(token);

    [Fact]
    public async Task ATokenForAnUnchangedActiveAccount_IsAccepted()
    {
        (await Check(Token())).Should().BeNull();
    }

    [Fact]
    public async Task ATokenIssuedBeforeTheAccountChanged_IsRejected()
    {
        _user.SecurityStamp = "stamp-2";

        (await Check(Token())).Should().Be("The account has changed since the token was issued.");
    }

    [Fact]
    public async Task ATokenForADeactivatedAccount_IsRejected()
    {
        _user.IsActive = false;

        (await Check(Token())).Should().Be("The account has been deactivated.");
    }

    [Fact]
    public async Task ATokenForADeletedAccount_IsRejected()
    {
        (await Check(Token(userId: "gone"))).Should().Be("The account no longer exists.");
    }

    [Fact]
    public async Task ATokenWithoutAStamp_IsRejected_SoTokensFromBeforeThisExistedStopWorking()
    {
        (await Check(Token(stamp: null))).Should().Be("The token was issued before revocation checks existed.");
    }

    [Fact]
    public async Task TheBearerPipeline_FailsARevokedToken_AndPassesAGoodOne()
    {
        var services = new ServiceCollection().AddSingleton(new AccountTokenValidator(_users.Object)).BuildServiceProvider();
        TokenValidatedContext ContextFor(ClaimsPrincipal principal) => new(
            new DefaultHttpContext { RequestServices = services },
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions()) { Principal = principal };
        var events = ServiceExtensions.CreateJwtBearerEvents();

        var good = ContextFor(Token());
        await events.OnTokenValidated(good);
        good.Result.Should().BeNull();

        _user.IsActive = false;
        var revoked = ContextFor(Token());
        await events.OnTokenValidated(revoked);
        revoked.Result!.Failure!.Message.Should().Be("The account has been deactivated.");
    }
}
```

- [ ] **Step 3: Write the failing auth tests**

Create `tests/PeopleCore.Application.Tests/Api/AuthTokenTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Infrastructure.Identity;
using Xunit;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// What a token carries now that accounts can be revoked: the security stamp it was issued
/// against, and whether the account is still on a temporary password.
/// </summary>
public class AuthTokenTests
{
    private const string Email = "ana@company.test";
    private const string Password = "Passw0rd1";

    private readonly ApplicationUser _user = new() { Id = "u1", Email = Email, SecurityStamp = "stamp-1", IsActive = true };
    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<SignInManager<ApplicationUser>> _signIn;
    private readonly AuthController _sut;

    public AuthTokenTests()
    {
        _users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        _users.Setup(u => u.FindByEmailAsync(Email)).ReturnsAsync(_user);
        _users.Setup(u => u.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "Employee" });
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, Password, true)).ReturnsAsync(SignInResult.Success);

        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create());
    }

    private static AuthTokenResponse Session(IActionResult result) =>
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<AuthTokenResponse>().Subject;

    private static Dictionary<string, string> ClaimsOf(AuthTokenResponse session) =>
        new JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);

    [Fact]
    public async Task SigningInToADeactivatedAccount_IsRefused_ExactlyLikeAWrongPassword()
    {
        _user.IsActive = false;

        var result = await _sut.Login(new LoginRequest(Email, Password));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _signIn.Verify(s => s.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task AToken_CarriesTheSecurityStampItWasIssuedAgainst()
    {
        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        ClaimsOf(session).Should().Contain("sec_stamp", "stamp-1").And.NotContainKey("must_change_password");
        session.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task AnAccountOnATemporaryPassword_SaysSo_InTheTokenAndTheResponse()
    {
        _user.MustChangePassword = true;

        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        ClaimsOf(session).Should().Contain("must_change_password", "true");
        session.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task ChangingATemporaryPassword_ClearsTheFlag_AndReturnsATokenForTheNewStamp()
    {
        _user.MustChangePassword = true;
        _users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(_user);
        _users.Setup(u => u.ChangePasswordAsync(_user, Password, "NewPassw0rd"))
              .Callback(() => _user.SecurityStamp = "stamp-2")
              .ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "u1")], "Bearer"))
            }
        };

        var session = Session(await _sut.ChangePassword(new ChangePasswordRequest(Password, "NewPassw0rd")));

        _user.MustChangePassword.Should().BeFalse();
        _users.Verify(u => u.UpdateAsync(_user), Times.Once);
        ClaimsOf(session).Should().Contain("sec_stamp", "stamp-2").And.NotContainKey("must_change_password");
    }
}
```

- [ ] **Step 4: Update the existing change-password test for the new response**

In `tests/PeopleCore.Application.Tests/Api/ChangePasswordTests.cs`:

1. In the constructor, replace `_sut = new AuthController(_users.Object, _signIn.Object, Mock.Of<IConfiguration>());` with:

```csharp
        _users.Setup(u => u.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "Employee" });

        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create());
```

2. Remove the now-unused `using Microsoft.Extensions.Configuration;`.

3. Replace the test `TheRightCurrentPassword_ChangesThePasswordOfTheTokensOwnAccount` with:

```csharp
    [Fact]
    public async Task TheRightCurrentPassword_ChangesThePasswordOfTheTokensOwnAccount_AndReturnsAFreshToken()
    {
        // Changing a password replaces the security stamp, which revokes the token the request came
        // with - so the response has to carry a new one or the user would be signed out.
        var result = await _sut.ChangePassword(new ChangePasswordRequest(Current, Next));

        result.Should().BeOfType<OkObjectResult>()
              .Which.Value.Should().BeOfType<AuthTokenResponse>()
              .Which.Token.Should().NotBeNullOrEmpty();
        _users.Verify(u => u.ChangePasswordAsync(_user, Current, Next), Times.Once);
    }
```

- [ ] **Step 5: Run the tests and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~AccountTokenValidatorTests|FullyQualifiedName~AuthTokenTests|FullyQualifiedName~ChangePasswordTests"`
Expected: build error, `The type or namespace name 'AccountTokenValidator' could not be found`.

- [ ] **Step 6: Write the claim names and the validator**

Create `src/PeopleCore.API/Accounts/AccountClaims.cs`:

```csharp
namespace PeopleCore.API.Accounts;

/// <summary>Claims PeopleCore adds to its own tokens. The web client reads them by these names too.</summary>
public static class AccountClaims
{
    /// <summary>The account's security stamp when the token was issued.</summary>
    public const string SecurityStamp = "sec_stamp";

    /// <summary>Present, with the value "true", while the account is on a temporary password.</summary>
    public const string MustChangePassword = "must_change_password";
}
```

Create `src/PeopleCore.API/Accounts/AccountTokenValidator.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Accounts;

/// <summary>
/// Checks, on every request, that the account a token names still exists, is active, and has not
/// changed since the token was issued. Without it a deactivated account, or one whose roles were
/// just removed, would keep working until its token expired - up to eight hours.
/// </summary>
public class AccountTokenValidator
{
    private readonly UserManager<ApplicationUser> _users;

    public AccountTokenValidator(UserManager<ApplicationUser> users) => _users = users;

    /// <returns>Why the token must be refused, or null when it may be used.</returns>
    public async Task<string?> FindRejectionAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue(AccountClaims.SecurityStamp);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
            return "The token was issued before revocation checks existed.";

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return "The account no longer exists.";
        if (!user.IsActive) return "The account has been deactivated.";

        return user.SecurityStamp == stamp ? null : "The account has changed since the token was issued.";
    }
}
```

- [ ] **Step 7: Hook the validator into the bearer pipeline**

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`:

1. Add `using PeopleCore.API.Accounts;` to the usings.
2. In `.AddJwtBearer(options => { ... })`, after the `options.TokenValidationParameters = new TokenValidationParameters { ... };` statement, add:

```csharp
            options.Events = CreateJwtBearerEvents();
```

3. Directly after `services.AddScoped<IUserAccountDirectory, UserAccountDirectory>();` (Task 4), add:

```csharp
        services.AddScoped<AccountTokenValidator>();
```

4. Add this method directly above `ConfigureIdentityOptions`:

```csharp
    /// <summary>
    /// Re-checks the account behind every token, so deactivating an account or changing its roles
    /// takes effect on its next request rather than when the token expires.
    /// </summary>
    public static JwtBearerEvents CreateJwtBearerEvents() => new()
    {
        OnTokenValidated = async context =>
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<AccountTokenValidator>();
            if (await validator.FindRejectionAsync(context.Principal!) is { } reason)
                context.Fail(reason);
        }
    };
```

- [ ] **Step 8: Issue tokens that carry the stamp and refuse inactive accounts**

In `src/PeopleCore.API/Controllers/Auth/AuthController.cs`:

1. Add `using PeopleCore.API.Accounts;`.
2. Replace the whole `Login` action with:

```csharp
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        // A deactivated account gets the same answer as a wrong password, so the response never
        // confirms that an email belongs to an account. Checked before the password so a
        // deactivated account's lockout counter is left alone.
        if (user is null || !user.IsActive) return Unauthorized(new { message = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new { message = "Invalid credentials." });

        return Ok(await IssueTokenAsync(user));
    }
```

3. In `ChangePassword`, replace the final `return NoContent();` with:

```csharp
        // ChangePasswordAsync has just replaced the security stamp, revoking the token this request
        // came with. Hand back a fresh one so the user stays signed in.
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            var cleared = await _userManager.UpdateAsync(user);
            if (!cleared.Succeeded)
                return PasswordProblem(string.Join(" ", cleared.Errors.Select(e => e.Description)));
        }

        return Ok(await IssueTokenAsync(user));
```

4. Add this method directly above `GenerateJwtToken`:

```csharp
    private async Task<AuthTokenResponse> IssueTokenAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return new AuthTokenResponse(GenerateJwtToken(user, roles), user.Email, roles.ToList(), user.MustChangePassword);
    }
```

5. In `GenerateJwtToken`, replace the claims setup:

```csharp
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (user.EmployeeId.HasValue)
            claims.Add(new Claim("employee_id", user.EmployeeId.Value.ToString()));
```

with:

```csharp
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AccountClaims.SecurityStamp, user.SecurityStamp ?? string.Empty)
        };
        if (user.EmployeeId.HasValue)
            claims.Add(new Claim("employee_id", user.EmployeeId.Value.ToString()));
        if (user.MustChangePassword)
            claims.Add(new Claim(AccountClaims.MustChangePassword, "true"));
```

6. At the bottom of the file, next to `LoginRequest`, add:

```csharp
public record AuthTokenResponse(string Token, string? Email, IReadOnlyList<string> Roles, bool MustChangePassword);
```

- [ ] **Step 9: Give the seeded admin the Employee role**

In `src/PeopleCore.API/Program.cs`, replace:

```csharp
        if (created.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
```

with:

```csharp
        if (created.Succeeded)
        {
            await userManager.AddToRolesAsync(admin, ["Admin", "Employee"]);
        }
```

Then directly after the closing brace of the `if (existingAdmin is null && adminPassword is not null) { ... }` block, add:

```csharp
    // Every account holds Employee (see AccountRoles). An admin seeded before that rule gets it here.
    if (existingAdmin is not null && !await userManager.IsInRoleAsync(existingAdmin, "Employee"))
        await userManager.AddToRoleAsync(existingAdmin, "Employee");
```

- [ ] **Step 10: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~AccountTokenValidatorTests|FullyQualifiedName~AuthTokenTests|FullyQualifiedName~ChangePasswordTests"`
Expected: all pass.

- [ ] **Step 11: Run the whole API test project**

Run: `dotnet test tests/PeopleCore.Application.Tests`
Expected: all pass. Nothing else constructs `AuthController`, so no other test should change.

- [ ] **Step 12: Commit**

```bash
git add src/PeopleCore.API tests/PeopleCore.Application.Tests/Api
git commit -m "feat(auth): revoke tokens when an account changes or is deactivated"
```

---

### Task 8: Nothing but a password change while on a temporary password

**Files:**
- Create: `src/PeopleCore.API/Accounts/PasswordChangeRequiredFilter.cs`
- Modify: `src/PeopleCore.API/Program.cs` (the `AddControllers` call)
- Modify: `src/PeopleCore.API/Controllers/Auth/AuthController.cs` (attribute on `ChangePassword`)
- Modify: `src/PeopleCore.API/Controllers/Account/ProfileController.cs` (attribute on `Get`)
- Test: `tests/PeopleCore.Application.Tests/Api/PasswordChangeRequiredFilterTests.cs`

**Interfaces:**
- Consumes: `AccountClaims.MustChangePassword` (Task 7).
- Produces: `sealed class PasswordChangeRequiredFilter : IActionFilter` and `sealed class AllowDuringPasswordChangeAttribute : Attribute` (method-only), both in `PeopleCore.API.Accounts`. The 403 body is a `ProblemDetails` titled `"Password change required"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/PasswordChangeRequiredFilterTests.cs`:

```csharp
using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using PeopleCore.API.Accounts;
using PeopleCore.API.Controllers.Account;
using PeopleCore.API.Controllers.Auth;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A temporary password was chosen by somebody else and passed on by hand. Until the user replaces
/// it, the only thing it opens is the door to replacing it.
/// </summary>
public class PasswordChangeRequiredFilterTests
{
    private static ClaimsPrincipal SignedIn(bool mustChangePassword)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u1") };
        if (mustChangePassword) claims.Add(new Claim(AccountClaims.MustChangePassword, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static ActionExecutingContext Request(ClaimsPrincipal user, params object[] endpointMetadata)
    {
        var action = new ActionContext(
            new DefaultHttpContext { User = user },
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() });
        return new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());
    }

    [Fact]
    public void OnATemporaryPassword_AnOrdinaryAction_IsRefused()
    {
        var context = Request(SignedIn(mustChangePassword: true));

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        var refused = context.Result.Should().BeOfType<ObjectResult>().Subject;
        refused.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        refused.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("Password change required");
    }

    [Fact]
    public void OnATemporaryPassword_AnExemptAction_Runs()
    {
        var context = Request(SignedIn(mustChangePassword: true), new AllowDuringPasswordChangeAttribute());

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void WithAPasswordOfTheirOwn_EveryActionRuns()
    {
        var context = Request(SignedIn(mustChangePassword: false));

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.ChangePassword))]
    [InlineData(typeof(ProfileController), nameof(ProfileController.Get))]
    public void ChangingThePassword_AndReadingYourOwnProfile_AreExempt(Type controller, string action)
    {
        controller.GetMethod(action)!.GetCustomAttribute<AllowDuringPasswordChangeAttribute>().Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PasswordChangeRequiredFilterTests"`
Expected: build error, `The type or namespace name 'PasswordChangeRequiredFilter' could not be found`.

- [ ] **Step 3: Write the filter**

Create `src/PeopleCore.API/Accounts/PasswordChangeRequiredFilter.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PeopleCore.API.Accounts;

/// <summary>
/// Refuses every action, except those marked <see cref="AllowDuringPasswordChangeAttribute"/>, to a
/// token that says its account is on a temporary password. Registered globally in Program.cs.
/// Enforced here rather than only in the web client, or the API would honour a temporary password
/// for as long as nobody used the browser.
/// </summary>
public sealed class PasswordChangeRequiredFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.User.HasClaim(c => c.Type == AccountClaims.MustChangePassword)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowDuringPasswordChangeAttribute>().Any()) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = "Password change required",
            Detail = "You signed in with a temporary password. Choose a new password before doing anything else.",
            Status = StatusCodes.Status403Forbidden
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

/// <summary>An action a user on a temporary password may still call.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowDuringPasswordChangeAttribute : Attribute { }
```

- [ ] **Step 4: Mark the two exempt actions**

In `AuthController.cs`, add `[AllowDuringPasswordChange]` directly above `[HttpPost("change-password")]`. `PeopleCore.API.Accounts` is already imported from Task 7.

In `ProfileController.cs`, add `using PeopleCore.API.Accounts;` and put `[AllowDuringPasswordChange]` directly above the `[HttpGet]` on `Get`.

- [ ] **Step 5: Register the filter globally**

In `src/PeopleCore.API/Program.cs`, add `using PeopleCore.API.Accounts;` and replace `builder.Services.AddControllers()` (the start of the `AddControllers().AddJsonOptions(...)` chain) with:

```csharp
builder.Services.AddControllers(options => options.Filters.Add<PasswordChangeRequiredFilter>())
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PasswordChangeRequiredFilterTests"`
Expected: 5 passed.

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build PeopleCore.slnx`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/PeopleCore.API tests/PeopleCore.Application.Tests/Api/PasswordChangeRequiredFilterTests.cs
git commit -m "feat(api): allow nothing but a password change on a temporary password"
```

---

### Task 9: Web client for accounts, and a change-password form that keeps the fresh token

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs`
- Create: `src/PeopleCore.Web/Components/Accounts/ChangePasswordForm.razor`
- Modify: `src/PeopleCore.Web/_Imports.razor`
- Modify: `src/PeopleCore.Web/Pages/ESS/MyProfile.razor`
- Test: `tests/PeopleCore.Web.Tests/Services/ApiClientAccountsTests.cs`
- Modify test: `tests/PeopleCore.Web.Tests/Pages/ESS/MyProfileTests.cs`

**Interfaces:**
- Consumes: the JSON shapes from Tasks 5–7.
- Produces (namespace `PeopleCore.Web.Services`):
  - `LoginResponse(string Token, string Email, IReadOnlyList<string> Roles, bool MustChangePassword = false)`, which gains its last parameter.
  - `Task<LoginResponse?> ChangePasswordAsync(string currentPassword, string newPassword)`, which used to return `Task`.
  - `Task<PagedResult<UserAccountDto>?> GetUserAccountsAsync(int page = 1, int pageSize = 20, string? search = null)`
  - `Task<IReadOnlyList<string>?> GetAssignableRolesAsync()`
  - `Task<IReadOnlyList<EmployeeLinkDto>?> GetEmployeeLinksAsync()`
  - `Task<CreatedUserAccountDto?> CreateUserAccountAsync(CreateUserAccountRequest request)`
  - `Task<UserAccountDto?> SetUserRolesAsync(string userId, IReadOnlyList<string> roles)`
  - `Task<UserAccountDto?> LinkUserEmployeeAsync(string userId, Guid? employeeId)`
  - `Task<UserAccountDto?> DeactivateUserAsync(string userId)` and `Task<UserAccountDto?> ReactivateUserAsync(string userId)`
  - `Task<string?> ResetUserPasswordAsync(string userId)`
  - Records `UserAccountDto(string Id, string Email, string? FirstName, string? LastName, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword, Guid? EmployeeId, string? EmployeeName, bool CanManage)`, `CreateUserAccountRequest(string Email, string FirstName, string LastName, Guid? EmployeeId, IReadOnlyList<string> Roles)`, `CreatedUserAccountDto(UserAccountDto Account, string TemporaryPassword)`, `EmployeeLinkDto(Guid EmployeeId, string UserId, bool IsActive)`, `TemporaryPasswordDto(string TemporaryPassword)`.
  - Component `ChangePasswordForm` (namespace `PeopleCore.Web.Components.Accounts`) with parameters `string CurrentPasswordLabel = "Current password"`, `string SubmitText = "Change Password"` and `EventCallback OnChanged`. It keeps the ids `form#change-password`, `#current-password`, `#new-password`, `#confirm-password` and `[data-password-result]`.

- [ ] **Step 1: Write the failing client tests**

Create `tests/PeopleCore.Web.Tests/Services/ApiClientAccountsTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

public class ApiClientAccountsTests
{
    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    private const string AccountJson =
        """{"id":"u-1","email":"ana@company.test","firstName":"Ana","lastName":"Reyes","roles":["Employee","Manager"],"isActive":true,"mustChangePassword":true,"employeeId":null,"employeeName":null,"canManage":true}""";

    private JsonElement OnlyBody() => JsonDocument.Parse(_api.RequestBodies.Single()!).RootElement;

    [Fact]
    public async Task ChangePassword_ReturnsTheFreshTokenTheApiIssues()
    {
        _api.On(HttpMethod.Post, "/api/auth/change-password", HttpStatusCode.OK,
            """{"token":"fresh-token","email":"ana@company.test","roles":["Employee"],"mustChangePassword":false}""");

        var session = await CreateClient().ChangePasswordAsync("Temp0rary1", "NewPassw0rd");

        session!.Token.Should().Be("fresh-token");
        session.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task ListingAccounts_SendsTheSearch()
    {
        _api.On(HttpMethod.Get, "/api/users?page=2&pageSize=20&search=ana%40company", HttpStatusCode.OK,
            $$"""{"items":[{{AccountJson}}],"totalCount":21,"page":2,"pageSize":20,"totalPages":2}""");

        var page = await CreateClient().GetUserAccountsAsync(2, 20, "ana@company");

        page!.Items.Single().Roles.Should().Equal("Employee", "Manager");
    }

    [Fact]
    public async Task CreatingAnAccount_PostsItsDetails_AndReturnsTheTemporaryPassword()
    {
        var employeeId = Guid.Parse("9c4e1a7b-3d2f-4b8a-8e6c-0f5d2a9b7c44");
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
            $$"""{"account":{{AccountJson}},"temporaryPassword":"Kx7mPq2RtW9zNb4s"}""");

        var created = await CreateClient().CreateUserAccountAsync(
            new CreateUserAccountRequest("ana@company.test", "Ana", "Reyes", employeeId, ["Employee", "Manager"]));

        created!.TemporaryPassword.Should().Be("Kx7mPq2RtW9zNb4s");
        var body = OnlyBody();
        body.GetProperty("email").GetString().Should().Be("ana@company.test");
        body.GetProperty("employeeId").GetGuid().Should().Be(employeeId);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Employee", "Manager");
    }

    [Fact]
    public async Task SettingRoles_PutsTheWholeSet()
    {
        _api.On(HttpMethod.Put, "/api/users/u-1/roles", HttpStatusCode.OK, AccountJson);

        await CreateClient().SetUserRolesAsync("u-1", ["Employee", "Manager"]);

        OnlyBody().GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Employee", "Manager");
    }

    [Fact]
    public async Task ResettingAPassword_ReturnsTheNewTemporaryPassword()
    {
        _api.On(HttpMethod.Post, "/api/users/u-1/reset-password", HttpStatusCode.OK, """{"temporaryPassword":"Zp8nQw3LmT6yHc2v"}""");

        (await CreateClient().ResetUserPasswordAsync("u-1")).Should().Be("Zp8nQw3LmT6yHc2v");
    }

    [Fact]
    public async Task ARefusedChange_ThrowsWithTheApisReason()
    {
        _api.On(HttpMethod.Post, "/api/users/u-1/deactivate", HttpStatusCode.Forbidden,
            """{"title":"Not allowed","status":403,"detail":"You can't deactivate your own account."}""");

        var act = () => CreateClient().DeactivateUserAsync("u-1");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Be("You can't deactivate your own account.");
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~ApiClientAccountsTests"`
Expected: build error, `'ApiClient' does not contain a definition for 'GetUserAccountsAsync'`.

- [ ] **Step 3: Add the client methods and DTOs**

In `src/PeopleCore.Web/Services/ApiClient.cs`:

1. The doc comment about changing a password currently sits above `GetMyProfileAsync`. Replace this block:

```csharp
    /// <summary>
    /// Changes the signed-in account's own password. A wrong current password or one the password
    /// policy rejects comes back as a 400 whose message says which, ready to show as it is.
    /// </summary>
    // The signed-in account's own profile. Not under api/auth: a 401 here is an expired session.
    public async Task<UserProfileDto?> GetMyProfileAsync()
```

with:

```csharp
    // The signed-in account's own profile. Not under api/auth: a 401 here is an expired session.
    public async Task<UserProfileDto?> GetMyProfileAsync()
```

2. Replace:

```csharp
    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
        => await EnsureSuccessAsync(await _http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword }));
```

with:

```csharp
    /// <summary>
    /// Changes the signed-in account's own password. A wrong current password or one the password
    /// policy rejects comes back as a 400 whose message says which, ready to show as it is. On
    /// success the API returns a fresh token: changing a password revokes the one the request used.
    /// </summary>
    public async Task<LoginResponse?> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var response = await _http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    // Accounts (Admin and HR)
    public async Task<PagedResult<UserAccountDto>?> GetUserAccountsAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        var url = $"api/users?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await GetJsonAsync<PagedResult<UserAccountDto>>(url);
    }

    public async Task<IReadOnlyList<string>?> GetAssignableRolesAsync()
        => await GetJsonAsync<List<string>>("api/users/assignable-roles");

    public async Task<IReadOnlyList<EmployeeLinkDto>?> GetEmployeeLinksAsync()
        => await GetJsonAsync<List<EmployeeLinkDto>>("api/users/employee-links");

    public Task<CreatedUserAccountDto?> CreateUserAccountAsync(CreateUserAccountRequest request)
        => SendJsonAsync<CreatedUserAccountDto>(HttpMethod.Post, "api/users", request);

    public Task<UserAccountDto?> SetUserRolesAsync(string userId, IReadOnlyList<string> roles)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Put, $"{UserPath(userId)}/roles", new { roles });

    public Task<UserAccountDto?> LinkUserEmployeeAsync(string userId, Guid? employeeId)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Put, $"{UserPath(userId)}/employee", new { employeeId });

    public Task<UserAccountDto?> DeactivateUserAsync(string userId)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Post, $"{UserPath(userId)}/deactivate");

    public Task<UserAccountDto?> ReactivateUserAsync(string userId)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Post, $"{UserPath(userId)}/reactivate");

    public async Task<string?> ResetUserPasswordAsync(string userId)
        => (await SendJsonAsync<TemporaryPasswordDto>(HttpMethod.Post, $"{UserPath(userId)}/reset-password"))?.TemporaryPassword;

    private static string UserPath(string userId) => $"api/users/{Uri.EscapeDataString(userId)}";
```

3. Directly after the private `GetJsonAsync<T>` method, add:

```csharp
    private async Task<T?> SendJsonAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }
```

4. Replace `public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles);` with:

```csharp
public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles, bool MustChangePassword = false);
public record UserAccountDto(string Id, string Email, string? FirstName, string? LastName, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword, Guid? EmployeeId, string? EmployeeName, bool CanManage);
public record CreateUserAccountRequest(string Email, string FirstName, string LastName, Guid? EmployeeId, IReadOnlyList<string> Roles);
public record CreatedUserAccountDto(UserAccountDto Account, string TemporaryPassword);
public record EmployeeLinkDto(Guid EmployeeId, string UserId, bool IsActive);
public record TemporaryPasswordDto(string TemporaryPassword);
```

- [ ] **Step 4: Run the client tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~ApiClientAccountsTests"`
Expected: 6 passed. `MyProfile.razor` still compiles unchanged, because awaiting `Task<LoginResponse?>` is valid wherever awaiting `Task` was.

- [ ] **Step 5: Update My Profile's tests for the token hand-back (failing)**

In `tests/PeopleCore.Web.Tests/Pages/ESS/MyProfileTests.cs`:

1. Add the usings `using Blazored.LocalStorage;`, `using Moq;` and `using PeopleCore.Web.Auth;`.
2. Add the field `private readonly Mock<ILocalStorageService> _storage = new();` next to `_api`.
3. In the constructor, after `Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));`, add:

```csharp
        Services.AddSingleton(new JwtAuthStateProvider(_storage.Object));
```

4. Replace the test `AValidChange_IsSentToTheApi_ConfirmedAndTheFieldsCleared` with:

```csharp
    [Fact]
    public void AValidChange_IsSentToTheApi_ConfirmedAndTheFieldsCleared_AndTheFreshTokenKept()
    {
        _api.On(HttpMethod.Post, ChangePasswordRoute, HttpStatusCode.OK,
            """{"token":"fresh-token","email":"ana@company.test","roles":["Employee"],"mustChangePassword":false}""");
        var cut = RenderUnlinked();

        FillAndSubmit(cut, "OldPassw0rd", "NewPassw0rd", "NewPassw0rd");

        cut.WaitForAssertion(() =>
            cut.Find("[data-password-result]").TextContent.Should().Contain("Your password has been changed."));
        ChangePasswordBodies().Should().ContainSingle()
            .Which.Should().Be("""{"currentPassword":"OldPassw0rd","newPassword":"NewPassw0rd"}""");
        foreach (var id in new[] { "current-password", "new-password", "confirm-password" })
            cut.Find($"#{id}").GetAttribute("value").Should().BeEmpty();
        // Changing the password revoked the old token; without the new one the next request signs the user out.
        _storage.Verify(s => s.SetItemAsync("auth_token", "fresh-token"), Times.Once);
    }
```

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~MyProfileTests"`
Expected: FAIL on `AValidChange_...AndTheFreshTokenKept`, because the storage verify never happened.

- [ ] **Step 6: Extract the form, keeping the fresh token**

Create `src/PeopleCore.Web/Components/Accounts/ChangePasswordForm.razor`:

```razor
@inject ApiClient Api
@inject JwtAuthStateProvider AuthProvider

@*
    Changing the signed-in account's own password. Used on My Profile, and by the screen that makes
    a user replace a temporary password before they can use the app.
*@

<form id="change-password" class="space-y-4" @onsubmit="ChangePassword" @onsubmit:preventDefault>
    @if (_passwordError is not null)
    {
        <Alert Variant="destructive" data-password-result>@_passwordError</Alert>
    }
    else if (_passwordChanged)
    {
        <Alert Variant="success" data-password-result>Your password has been changed.</Alert>
    }

    <FormField LabelText="@CurrentPasswordLabel" Id="current-password" Error="@_currentError">
        <PasswordInput Id="current-password" @bind-Value="_currentPassword"
                       AutoComplete="current-password" HasError="@(_currentError is not null)"
                       Disabled="@_saving" />
    </FormField>
    <FormField LabelText="New password" Id="new-password" Error="@_newError"
               Description="@($"At least {MinimumLength} characters, including a number.")">
        <PasswordInput Id="new-password" @bind-Value="_newPassword"
                       AutoComplete="new-password" HasError="@(_newError is not null)"
                       Disabled="@_saving" />
    </FormField>
    <FormField LabelText="Confirm new password" Id="confirm-password" Error="@_confirmError">
        <PasswordInput Id="confirm-password" @bind-Value="_confirmPassword"
                       AutoComplete="new-password" HasError="@(_confirmError is not null)"
                       Disabled="@_saving" />
    </FormField>
    <Button Type="submit" Variant="primary" Loading="@_saving">@SubmitText</Button>
</form>

@code {
    // Mirrors the API's Identity password policy (ServiceExtensions.ConfigureIdentityOptions) so the
    // common mistakes are caught before a round trip. The API stays the authority: anything it
    // rejects for another reason comes back in the error message.
    private const int MinimumLength = 8;

    [Parameter] public string CurrentPasswordLabel { get; set; } = "Current password";
    [Parameter] public string SubmitText { get; set; } = "Change Password";

    /// <summary>Raised after the password has changed and the fresh token is stored.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string? _currentError;
    private string? _newError;
    private string? _confirmError;
    private string? _passwordError;
    private bool _passwordChanged;
    private bool _saving;

    private async Task ChangePassword()
    {
        _passwordError = null;
        _passwordChanged = false;

        _currentError = string.IsNullOrEmpty(_currentPassword) ? "Enter your current password." : null;
        _newError = _newPassword switch
        {
            "" => "Enter a new password.",
            { Length: < MinimumLength } => $"Use at least {MinimumLength} characters.",
            _ when !_newPassword.Any(char.IsDigit) => "Include at least one number.",
            _ when _newPassword == _currentPassword => "Choose a password different from your current one.",
            _ => null
        };
        _confirmError = _newError is null && _confirmPassword != _newPassword ? "The passwords don't match." : null;

        if (_currentError is not null || _newError is not null || _confirmError is not null) return;

        _saving = true;
        try
        {
            var session = await Api.ChangePasswordAsync(_currentPassword, _newPassword);
            // Changing the password revoked the token this request was sent with. Keep the new one,
            // or the next call would come back 401 and sign the user out.
            if (session is not null)
                await AuthProvider.LoginAsync(session.Token);

            _passwordChanged = true;
            _currentPassword = _newPassword = _confirmPassword = string.Empty;
        }
        catch (Exception ex)
        {
            _passwordError = ex.Message;
            return;
        }
        finally
        {
            _saving = false;
        }

        await OnChanged.InvokeAsync();
    }
}
```

In `src/PeopleCore.Web/_Imports.razor`, add the line:

```razor
@using PeopleCore.Web.Components.Accounts
```

- [ ] **Step 7: Use the form on My Profile**

In `src/PeopleCore.Web/Pages/ESS/MyProfile.razor`:

1. Inside the `<section aria-label="Change password">` card, replace everything from `<form id="change-password"` through its closing `</form>` with:

```razor
                <ChangePasswordForm />
```

2. In `@code`, delete the comment and constant `private const int MinimumLength = 8;` (the comment starts "Mirrors the API's Identity password policy").
3. Delete the nine fields `_currentPassword`, `_newPassword`, `_confirmPassword`, `_currentError`, `_newError`, `_confirmError`, `_passwordError`, `_passwordChanged`, `_saving`.
4. Delete the whole `private async Task ChangePassword()` method.

Leave `NameMaxLength`, the account-details fields and methods, and the employee-record code untouched.

- [ ] **Step 8: Run the web tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~MyProfileTests|FullyQualifiedName~ApiClient"`
Expected: all pass. That includes the unchanged show-password-toggle, validation and rejected-change tests, which prove the extraction kept the markup.

- [ ] **Step 9: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): call the account endpoints, and keep the token a password change returns"
```

---

### Task 10: The temporary-password gate

**Files:**
- Create: `src/PeopleCore.Web/Layout/PasswordChangeGate.razor`
- Modify: `src/PeopleCore.Web/Layout/MainLayout.razor`
- Test: `tests/PeopleCore.Web.Tests/Layout/PasswordChangeGateTests.cs`

**Interfaces:**
- Consumes: `ChangePasswordForm` (Task 9); the `must_change_password` claim (Task 7).
- Produces: component `PasswordChangeGate` with `[Parameter] RenderFragment? ChildContent`. It renders `[data-password-change-gate]` in place of the child content while the claim is present.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Web.Tests/Layout/PasswordChangeGateTests.cs`:

```csharp
using System.Net;
using System.Security.Claims;
using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Layout;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Layout;

/// <summary>
/// Someone signed in with a temporary password sees one thing - a form to replace it - whichever
/// page they asked for. The API refuses everything else anyway; this makes that make sense.
/// </summary>
public class PasswordChangeGateTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private readonly Mock<ILocalStorageService> _storage = new();
    private readonly BunitAuthorizationContext _auth;

    public PasswordChangeGateTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        Services.AddSingleton(new JwtAuthStateProvider(_storage.Object));
        _auth = AddAuthorization();
        _auth.SetAuthorized("new.hire@company.test");
    }

    private IRenderedComponent<PasswordChangeGate> RenderGate() =>
        Render<PasswordChangeGate>(p => p.AddChildContent("""<p id="routed-page">Dashboard</p>"""));

    [Fact]
    public void WithAPasswordOfTheirOwn_TheRequestedPageShows()
    {
        var cut = RenderGate();

        cut.FindAll("#routed-page").Should().ContainSingle();
        cut.FindAll("[data-password-change-gate]").Should().BeEmpty();
    }

    [Fact]
    public void OnATemporaryPassword_TheFormReplacesThePage()
    {
        _auth.SetClaims(new Claim("must_change_password", "true"));

        var cut = RenderGate();

        cut.FindAll("#routed-page").Should().BeEmpty();
        cut.Find("[data-password-change-gate]").TextContent.Should().Contain("Set a new password");
        cut.Find("label[for=current-password]").TextContent.Should().Contain("Temporary password");
    }

    [Fact]
    public void SettingANewPassword_KeepsTheFreshToken_AndReloadsSignedInWithIt()
    {
        _auth.SetClaims(new Claim("must_change_password", "true"));
        _api.On(HttpMethod.Post, "/api/auth/change-password", HttpStatusCode.OK,
            """{"token":"fresh-token","email":"new.hire@company.test","roles":["Employee"],"mustChangePassword":false}""");
        var cut = RenderGate();

        cut.Find("#current-password").Input("Kx7mPq2RtW9zNb4s");
        cut.Find("#new-password").Input("MyOwnPassw0rd");
        cut.Find("#confirm-password").Input("MyOwnPassw0rd");
        cut.Find("form#change-password").Submit();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        cut.WaitForAssertion(() => nav.History.Should().NotBeEmpty());
        _storage.Verify(s => s.SetItemAsync("auth_token", "fresh-token"), Times.Once);
        nav.History.First().Options.ForceLoad.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~PasswordChangeGateTests"`
Expected: build error, `The type or namespace name 'PasswordChangeGate' could not be found`.

- [ ] **Step 3: Write the gate**

Create `src/PeopleCore.Web/Layout/PasswordChangeGate.razor`:

```razor
@inject NavigationManager Nav

@*
    Stands between the layout and the routed page. While the signed-in token says its account is on
    a temporary password, the page is replaced by a form to choose a new one - the API refuses
    everything else until then (PasswordChangeRequiredFilter).
*@

@if (_mustChangePassword)
{
    <div class="mx-auto max-w-md py-6" data-password-change-gate>
        <Card>
            <CardHeader>
                <CardTitle>Set a new password</CardTitle>
                <CardDescription>You signed in with a temporary password. Choose your own to continue.</CardDescription>
            </CardHeader>
            <CardContent>
                <ChangePasswordForm CurrentPasswordLabel="Temporary password" SubmitText="Set Password" OnChanged="ReloadSignedIn" />
            </CardContent>
        </Card>
    </div>
}
else
{
    @ChildContent
}

@code {
    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    private bool _mustChangePassword;

    protected override async Task OnParametersSetAsync()
    {
        var state = AuthenticationState is null ? null : await AuthenticationState;
        _mustChangePassword = state?.User.HasClaim(c => c.Type == "must_change_password") ?? false;
    }

    // Program.cs registers the app's AuthenticationStateProvider and JwtAuthStateProvider as separate
    // instances, so storing the fresh token does not reach the cascading state. Reloading rebuilds
    // it from the stored token - the same thing Login does after signing in.
    private void ReloadSignedIn() => Nav.NavigateTo(Nav.Uri, forceLoad: true);
}
```

- [ ] **Step 4: Put it round the routed page**

In `src/PeopleCore.Web/Layout/MainLayout.razor`, replace:

```razor
        <main class="app-scroll-region flex-1 p-4 md:p-6">
            @Body
        </main>
```

with:

```razor
        <main class="app-scroll-region flex-1 p-4 md:p-6">
            <PasswordChangeGate>@Body</PasswordChangeGate>
        </main>
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~PasswordChangeGateTests"`
Expected: 3 passed. If `nav.History`'s element type in bUnit 2.10 exposes the flag under another name, check `BunitNavigationManager.History` in the bUnit XML docs: the entry type is `NavigationHistory`, with `Uri` and `Options` (`NavigationOptions.ForceLoad`).

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Web/Layout tests/PeopleCore.Web.Tests/Layout/PasswordChangeGateTests.cs
git commit -m "feat(web): make a user replace a temporary password before using the app"
```

---

### Task 11: The Users page

**Files:**
- Create: `src/PeopleCore.Web/Components/Accounts/RoleCheckboxes.razor`
- Create: `src/PeopleCore.Web/Components/Accounts/EmployeePicker.razor`
- Create: `src/PeopleCore.Web/Components/Accounts/CreateAccountDialog.razor`
- Create: `src/PeopleCore.Web/Components/Accounts/TemporaryPasswordDialog.razor`
- Create: `src/PeopleCore.Web/Pages/Admin/Users.razor`
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`, `src/PeopleCore.Web/Layout/MobileFooterNav.razor`
- Test: `tests/PeopleCore.Web.Tests/Pages/Admin/UsersTests.cs`
- Modify test: `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`

**Interfaces:**
- Consumes: the `ApiClient` account methods and DTOs (Task 9); `ApiClient.GetEmployeesAsync(int page, int pageSize, string? search, bool? isActive)` and `EmployeeListDto` (existing).
- Produces (namespace `PeopleCore.Web.Components.Accounts`, used again by Task 12):
  - `RoleCheckboxes`: `[Parameter] IReadOnlyList<string> Roles`, `[Parameter] HashSet<string> Selected` (changed in place), `[Parameter] bool Disabled`, and `public static string Label(string role)`. Each checkbox button carries `data-role="<role>"`.
  - `EmployeePicker`: `[Parameter] string? Id`, `[Parameter] EmployeeListDto? Selected`, `[Parameter] EventCallback<EmployeeListDto?> SelectedChanged`. Options carry `data-employee-option`; the chosen employee shows in `[data-selected-employee]`.
  - `CreateAccountDialog`: `[Parameter] bool IsOpen`, `EventCallback<bool> IsOpenChanged`, `EmployeeListDto? ForEmployee`, `EventCallback<CreatedUserAccountDto> OnCreated`. Its form is `form[data-create-account]`, with inputs `#account-email`, `#account-first-name`, `#account-last-name`, `#account-employee`, and errors in `[data-create-account-error]`.
  - `TemporaryPasswordDialog`: `[Parameter] string? Password` (open while non-null), `string? Email`, `EventCallback OnClosed`. It renders `[data-temporary-password-dialog]` and `[data-temporary-password]`.
  - Page `/admin/users` (`PeopleCore.Web.Pages.Admin.Users`) with `[SupplyParameterFromQuery] string? Search`.

- [ ] **Step 1: Write the failing page tests**

Create `tests/PeopleCore.Web.Tests/Pages/Admin/UsersTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

public class UsersTests : BunitContext
{
    private const string FirstPage = "/api/users?page=1&pageSize=20";
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    public UsersTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("hr@company.test");
        auth.SetRoles("HRManager");
        _api.On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK, """["Manager","Employee","PayrollService"]""");
    }

    private static string Json(bool value) => value ? "true" : "false";

    private static string Account(string id, string[] roles, bool canManage = true, bool active = true, bool mustChange = false, string? employeeName = null) =>
        $$"""
        {"id":"{{id}}","email":"{{id}}@company.test","firstName":"Ana","lastName":"Reyes",
         "roles":[{{string.Join(",", roles.Select(r => $"\"{r}\""))}}],"isActive":{{Json(active)}},"mustChangePassword":{{Json(mustChange)}},
         "employeeId":null,"employeeName":{{(employeeName is null ? "null" : $"\"{employeeName}\"")}},"canManage":{{Json(canManage)}}}
        """;

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private IRenderedComponent<Users> RenderPage(string firstPage)
    {
        _api.On(HttpMethod.Get, FirstPage, HttpStatusCode.OK, firstPage);
        var cut = Render<Users>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement RowFor(IRenderedComponent<Users> cut, string email) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(email));

    private static IElement ButtonIn(IElement scope, string text) =>
        scope.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"{method} {path} should have been sent");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    private static IEnumerable<string?> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString());

    [Fact]
    public void EveryAccount_IsListedWithItsRolesEmployeeAndStatus()
    {
        var cut = RenderPage(Paged(
            Account("u1", ["Employee", "PayrollService"], employeeName: "Maria Santos"),
            Account("u2", ["Employee"], active: false),
            Account("u3", ["Employee"], mustChange: true)));

        RowFor(cut, "u1@company.test").TextContent.Should().Contain("Payroll").And.Contain("Maria Santos").And.Contain("Active");
        RowFor(cut, "u2@company.test").TextContent.Should().Contain("Deactivated");
        RowFor(cut, "u3@company.test").TextContent.Should().Contain("Must change password");
    }

    [Fact]
    public void AnAccountTheCallerMayNotChange_HasEveryActionDisabled_AndSaysWhy()
    {
        var cut = RenderPage(Paged(Account("u1", ["Admin", "Employee"], canManage: false)));

        var row = RowFor(cut, "u1@company.test");
        row.QuerySelectorAll("button").Should().OnlyContain(b => b.HasAttribute("disabled"));
        ButtonIn(row, "Roles").GetAttribute("title").Should().Be("Only an administrator can change an Admin or HR Manager account.");
    }

    [Fact]
    public void ASearchInTheAddress_IsAppliedOnArrival()
    {
        _api.On(HttpMethod.Get, "/api/users?page=1&pageSize=20&search=maria%40company.test", HttpStatusCode.OK, Paged(Account("maria", ["Employee"])));

        var cut = Render<Users>(p => p.Add(x => x.Search, "maria@company.test"));

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());
        cut.Find("#account-search").GetAttribute("value").Should().Be("maria@company.test");
    }

    [Fact]
    public void EditingRoles_SendsTheTickedRoles_AlwaysWithEmployee()
    {
        _api.On(HttpMethod.Put, "/api/users/u1/roles", HttpStatusCode.OK, Account("u1", ["Employee", "Manager"]));
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Roles").Click();
        cut.Find("[data-roles-dialog] [data-role=Employee]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-roles-dialog] [data-role=Manager]").Click();
        ButtonIn(cut.Find("[data-roles-dialog]"), "Save Roles").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-roles-dialog]").Should().BeEmpty());
        Strings(BodyOf(HttpMethod.Put, "/api/users/u1/roles").GetProperty("roles")).Should().BeEquivalentTo("Employee", "Manager");
    }

    [Fact]
    public void LinkingAnEmployee_FindsThemBySearch_AndSendsTheirId()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=10&search=maria", HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{MariaId}}","employeeNumber":"EMP-0007","firstName":"Maria","lastName":"Santos","fullName":"Maria Santos",
              "workEmail":"maria@company.test","departmentName":null,"positionTitle":null,"employmentStatus":"Regular","isActive":true}],
             "totalCount":1,"page":1,"pageSize":10,"totalPages":1}
            """);
        _api.On(HttpMethod.Put, "/api/users/u1/employee", HttpStatusCode.OK, Account("u1", ["Employee"], employeeName: "Maria Santos"));
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Employee").Click();
        cut.Find("#link-employee").Input("maria");
        cut.WaitForAssertion(() => cut.FindAll("[data-employee-option]").Should().ContainSingle());
        cut.Find("[data-employee-option]").Click();
        ButtonIn(cut.Find("[data-link-dialog]"), "Link Employee").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-link-dialog]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, "/api/users/u1/employee").GetProperty("employeeId").GetGuid().Should().Be(MariaId);
    }

    [Fact]
    public void ResettingAPassword_AfterConfirming_ShowsTheNewPasswordOnce()
    {
        _api.On(HttpMethod.Post, "/api/users/u1/reset-password", HttpStatusCode.OK, """{"temporaryPassword":"Zp8nQw3LmT6yHc2v"}""");
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Reset Password").Click();
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Reset Password").Click();

        cut.WaitForAssertion(() => cut.Find("[data-temporary-password]").TextContent.Trim().Should().Be("Zp8nQw3LmT6yHc2v"));
        cut.Find("[data-temporary-password-dialog]").TextContent.Should().Contain(
            "This password is shown once. Share it with the user securely; they will be asked to change it when they sign in.");

        ButtonIn(cut.Find("[data-temporary-password-dialog]"), "Done").Click();
        cut.FindAll("[data-temporary-password]").Should().BeEmpty();
    }

    [Fact]
    public void Deactivating_AfterConfirming_IsSent_AndTheListRefreshes()
    {
        _api.On(HttpMethod.Post, "/api/users/u1/deactivate", HttpStatusCode.OK, Account("u1", ["Employee"], active: false));
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Deactivate").Click();
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Deactivate").Click();

        cut.WaitForAssertion(() => _api.Requests.Count(r => r.RequestUri!.PathAndQuery == FirstPage).Should().Be(2));
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/users/u1/deactivate");
    }

    [Fact]
    public void ARefusedAction_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Post, "/api/users/u1/reactivate", HttpStatusCode.Forbidden,
            """{"title":"Not allowed","status":403,"detail":"Only an administrator can change an Admin or HR Manager account."}""");
        var cut = RenderPage(Paged(Account("u1", ["Employee"], active: false)));

        ButtonIn(RowFor(cut, "u1@company.test"), "Reactivate").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent
            .Should().Contain("Only an administrator can change an Admin or HR Manager account."));
    }

    [Fact]
    public void CreatingAnAccount_SendsItsDetails_AndShowsTheTemporaryPassword()
    {
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
            $$"""{"account":{{Account("new.hire", ["Employee", "Manager"], mustChange: true)}},"temporaryPassword":"Kx7mPq2RtW9zNb4s"}""");
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Account").Click();
        cut.Find("#account-email").Input("new.hire@company.test");
        cut.Find("#account-first-name").Input("Ana");
        cut.Find("#account-last-name").Input("Reyes");
        cut.Find("form[data-create-account] [data-role=Manager]").Click();
        cut.Find("form[data-create-account]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-temporary-password]").TextContent.Trim().Should().Be("Kx7mPq2RtW9zNb4s"));
        var body = BodyOf(HttpMethod.Post, "/api/users");
        body.GetProperty("email").GetString().Should().Be("new.hire@company.test");
        body.GetProperty("firstName").GetString().Should().Be("Ana");
        body.GetProperty("employeeId").ValueKind.Should().Be(JsonValueKind.Null);
        Strings(body.GetProperty("roles")).Should().BeEquivalentTo("Employee", "Manager");
    }

    [Fact]
    public void CreatingAnAccountWithoutAnEmail_SaysSo_WithoutCallingTheApi()
    {
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Account").Click();
        cut.Find("form[data-create-account]").Submit();

        cut.Find("[data-create-account-error]").TextContent.Should().Contain("Enter an email address.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }
}
```

- [ ] **Step 2: Add the nav test (failing)**

In `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`:

1. In `AnAdmin_SeesEverySection`, change `HaveCount(16)` to `HaveCount(17)`.
2. Add:

```csharp
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HRManager", true)]
    [InlineData("Manager", false)]
    [InlineData("PayrollService", false)]
    public void TheUsersPage_IsListedOnlyForAdminAndHr(string role, bool listed)
    {
        Links(RenderAs(role)).Contains("/admin/users").Should().Be(listed);
    }
```

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~UsersTests|FullyQualifiedName~NavMenuTests"`
Expected: build error, `The type or namespace name 'Admin' does not exist in the namespace 'PeopleCore.Web.Pages'`.

- [ ] **Step 3: Write the role checkboxes**

Create `src/PeopleCore.Web/Components/Accounts/RoleCheckboxes.razor`:

```razor
@*
    Tick-boxes for the roles the signed-in user may grant. Employee is on every account, so it is
    shown ticked and cannot be unticked - the API adds it back anyway.
*@

<div class="space-y-2" data-role-checkboxes>
    @foreach (var role in Roles)
    {
        var locked = role == EmployeeRole;
        <Checkbox Value="@(locked || Selected.Contains(role))"
                  Disabled="@(locked || Disabled)"
                  ValueChanged="@(on => Toggle(role, on))"
                  data-role="@role">
            @Label(role)
            @if (locked)
            {
                <span class="ml-1 text-xs font-normal text-muted-foreground">(every account)</span>
            }
        </Checkbox>
    }
</div>

@code {
    private const string EmployeeRole = "Employee";

    [Parameter] public IReadOnlyList<string> Roles { get; set; } = [];

    /// <summary>The ticked roles. Changed in place, so the owner reads it when saving.</summary>
    [Parameter] public HashSet<string> Selected { get; set; } = [];

    [Parameter] public bool Disabled { get; set; }

    public static string Label(string role) => role switch
    {
        "HRManager" => "HR Manager",
        "PayrollService" => "Payroll",
        _ => role
    };

    private void Toggle(string role, bool on)
    {
        if (on) Selected.Add(role);
        else Selected.Remove(role);
    }
}
```

- [ ] **Step 4: Write the employee picker**

Create `src/PeopleCore.Web/Components/Accounts/EmployeePicker.razor`:

```razor
@inject ApiClient Api

@*
    Choose one employee by typing part of their name or number. Only HR can list employees, which
    is fine: only HR and Admins reach the screens that use this.
*@

<div class="space-y-2" data-employee-picker>
    @if (Selected is not null)
    {
        <div class="flex items-center justify-between gap-2 rounded-md border border-border px-3 py-2 text-sm">
            <span data-selected-employee>
                @Selected.FullName <span class="text-muted-foreground">(@Selected.EmployeeNumber)</span>
            </span>
            <Button Variant="ghost" Size="sm" OnClick="Clear">Change</Button>
        </div>
    }
    else
    {
        <Input TValue="string" Id="@Id" Value="@_search"
               ValueChanged="@(async v => { _search = v; await SearchAsync(); })"
               Placeholder="Search by name or employee number..." AutoComplete="off" />
        @if (_error is not null)
        {
            <p class="text-sm text-destructive">@_error</p>
        }
        @foreach (var employee in _results)
        {
            <button type="button" data-employee-option="@employee.Id"
                    class="block w-full rounded-md px-3 py-2 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                    @onclick="() => Select(employee)">
                @employee.FullName <span class="text-muted-foreground">(@employee.EmployeeNumber)</span>
            </button>
        }
    }
</div>

@code {
    private const int MaxResults = 10;

    [Parameter] public string? Id { get; set; }
    [Parameter] public EmployeeListDto? Selected { get; set; }
    [Parameter] public EventCallback<EmployeeListDto?> SelectedChanged { get; set; }

    private string _search = string.Empty;
    private List<EmployeeListDto> _results = [];
    private string? _error;
    private int _searchVersion;

    private async Task SearchAsync()
    {
        // Answers to earlier keystrokes can arrive after later ones; only the latest may show.
        var version = ++_searchVersion;
        if (string.IsNullOrWhiteSpace(_search))
        {
            _results = [];
            return;
        }

        try
        {
            var page = await Api.GetEmployeesAsync(1, MaxResults, _search.Trim());
            if (version != _searchVersion) return;
            _results = page?.Items.ToList() ?? [];
            _error = null;
        }
        catch (Exception ex)
        {
            if (version != _searchVersion) return;
            _results = [];
            _error = ex.Message;
        }
    }

    private async Task Select(EmployeeListDto employee)
    {
        _search = string.Empty;
        _results = [];
        await SelectedChanged.InvokeAsync(employee);
    }

    private Task Clear() => SelectedChanged.InvokeAsync(null);
}
```

- [ ] **Step 5: Write the create dialog**

Create `src/PeopleCore.Web/Components/Accounts/CreateAccountDialog.razor`:

```razor
@inject ApiClient Api

<Dialog IsOpen="@IsOpen" OnClose="Close" Title="New account"
        Description="The account gets a temporary password, shown to you once, which the user replaces when they first sign in.">
    <ChildContent>
        <form class="space-y-4" data-create-account @onsubmit="Submit" @onsubmit:preventDefault>
            @if (_error is not null)
            {
                <Alert Variant="destructive" data-create-account-error>@_error</Alert>
            }

            <FormField LabelText="Email" Id="account-email" Description="What they sign in with.">
                <Input TValue="string" Id="account-email" Type="email" @bind-Value="_email" AutoComplete="off" Disabled="@_saving" />
            </FormField>
            <div class="grid gap-4 sm:grid-cols-2">
                <FormField LabelText="First name" Id="account-first-name">
                    <Input TValue="string" Id="account-first-name" @bind-Value="_firstName" MaxLength="@NameMaxLength" Disabled="@_saving" />
                </FormField>
                <FormField LabelText="Last name" Id="account-last-name">
                    <Input TValue="string" Id="account-last-name" @bind-Value="_lastName" MaxLength="@NameMaxLength" Disabled="@_saving" />
                </FormField>
            </div>
            <FormField LabelText="Employee record" Id="account-employee"
                       Description="Optional. Links the login to an employee, so they see their own attendance, leave and payslips.">
                <EmployeePicker Id="account-employee" @bind-Selected="_employee" />
            </FormField>
            <div class="space-y-2">
                <span class="block text-sm font-medium leading-none">Roles</span>
                @if (_rolesError is not null)
                {
                    <p class="text-sm text-destructive">Roles could not be loaded: @_rolesError</p>
                }
                else
                {
                    <RoleCheckboxes Roles="@_assignable" Selected="@_roles" Disabled="@_saving" />
                }
            </div>
            <div class="flex justify-end gap-2 pt-2">
                <Button Variant="outline" Size="sm" OnClick="Close">Cancel</Button>
                <Button Type="submit" Variant="primary" Size="sm" Loading="@_saving">Create Account</Button>
            </div>
        </form>
    </ChildContent>
</Dialog>

@code {
    // The API's column width (ApplicationUser.NameMaxLength).
    private const int NameMaxLength = 100;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>The employee the login is for, when opened from the Employees page.</summary>
    [Parameter] public EmployeeListDto? ForEmployee { get; set; }

    [Parameter] public EventCallback<CreatedUserAccountDto> OnCreated { get; set; }

    private string _email = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private EmployeeListDto? _employee;
    private HashSet<string> _roles = [];
    private IReadOnlyList<string> _assignable = [];
    private string? _rolesError;
    private string? _error;
    private bool _saving;
    private bool _wasOpen;

    protected override async Task OnParametersSetAsync()
    {
        var opening = IsOpen && !_wasOpen;
        _wasOpen = IsOpen;
        if (!opening) return;

        // Start from the employee, or blank, each time it opens - never from the last account.
        _email = ForEmployee?.WorkEmail ?? string.Empty;
        _firstName = ForEmployee?.FirstName ?? string.Empty;
        _lastName = ForEmployee?.LastName ?? string.Empty;
        _employee = ForEmployee;
        _roles = [];
        _error = null;

        if (_assignable.Count == 0 && _rolesError is null)
            await LoadRolesAsync();
    }

    private async Task LoadRolesAsync()
    {
        try
        {
            _assignable = await Api.GetAssignableRolesAsync() ?? [];
        }
        catch (Exception ex)
        {
            _rolesError = ex.Message;
        }
    }

    private async Task Submit()
    {
        _error = string.IsNullOrWhiteSpace(_email) ? "Enter an email address."
            : string.IsNullOrWhiteSpace(_firstName) ? "Enter a first name."
            : string.IsNullOrWhiteSpace(_lastName) ? "Enter a last name."
            : null;
        if (_error is not null) return;

        _saving = true;
        try
        {
            var created = await Api.CreateUserAccountAsync(new CreateUserAccountRequest(
                _email.Trim(), _firstName.Trim(), _lastName.Trim(), _employee?.Id, [.. _roles, "Employee"]));

            await IsOpenChanged.InvokeAsync(false);
            if (created is not null)
                await OnCreated.InvokeAsync(created);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private Task Close() => IsOpenChanged.InvokeAsync(false);
}
```

- [ ] **Step 6: Write the temporary password dialog**

Create `src/PeopleCore.Web/Components/Accounts/TemporaryPasswordDialog.razor`:

```razor
@inject IJSRuntime JS

@*
    Shows a temporary password exactly once. The owner holds the only copy and discards it on
    OnClosed, so closing this dialog is the last chance to read it.
*@

<Dialog IsOpen="@(Password is not null)" OnClose="Close" CloseOnOverlayClick="false"
        Title="Temporary password" Description="@(Email is null ? null : $"For {Email}")">
    <ChildContent>
        <div class="space-y-4" data-temporary-password-dialog>
            <div class="flex items-center gap-2">
                <code class="flex-1 rounded-md border border-border bg-muted px-3 py-2 font-mono text-base tracking-wider" data-temporary-password>@Password</code>
                <Button Variant="outline" Size="sm" OnClick="Copy" Title="Copy to clipboard">
                    <i class="bi @(_copied ? "bi-check2" : "bi-clipboard")"></i> @(_copied ? "Copied" : "Copy")
                </Button>
            </div>
            <p class="text-sm text-muted-foreground">This password is shown once. Share it with the user securely; they will be asked to change it when they sign in.</p>
            <div class="flex justify-end">
                <Button Variant="primary" Size="sm" OnClick="Close">Done</Button>
            </div>
        </div>
    </ChildContent>
</Dialog>

@code {
    [Parameter] public string? Password { get; set; }
    [Parameter] public string? Email { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private bool _copied;

    protected override void OnParametersSet()
    {
        if (Password is null) _copied = false;
    }

    private async Task Copy()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", Password);
            _copied = true;
        }
        catch (JSException)
        {
            // Clipboard access denied (e.g. an insecure origin). The password is on screen to copy by hand.
        }
    }

    private Task Close() => OnClosed.InvokeAsync();
}
```

- [ ] **Step 7: Write the page**

Create `src/PeopleCore.Web/Pages/Admin/Users.razor`:

```razor
@page "/admin/users"
@attribute [Authorize(Roles = "Admin,HRManager")]
@inject ApiClient Api

<PageTitle>Users</PageTitle>

<PageHeader Title="Users" Section="Administration" Description="Sign-in accounts, the roles they hold, and the employee each belongs to.">
    <Actions>
        <Button Variant="primary" OnClick="() => _showCreate = true">
            <i class="bi bi-plus-lg mr-1"></i> New Account
        </Button>
    </Actions>
</PageHeader>

<Card Class="mb-4">
    <CardContent Class="pt-4">
        <Input TValue="string" Id="account-search" Value="@_search"
               ValueChanged="@(async v => { _search = v; _page = 1; await LoadAccounts(); })"
               Placeholder="Search by email or name..." Type="text" Class="max-w-xs" />
    </CardContent>
</Card>

@if (_actionError is not null)
{
    <Alert Variant="destructive" Class="mb-4" data-action-error>@_actionError</Alert>
}

<Card>
    <CardContent Class="p-0">
        @if (_loadError is not null)
        {
            <div class="p-4"><Alert Variant="destructive">@_loadError</Alert></div>
        }
        else if (_accounts is null)
        {
            <div class="p-6 flex justify-center"><Spinner /></div>
        }
        else if (!_accounts.Items.Any())
        {
            <div class="p-6 text-center text-muted-foreground text-sm">No accounts found.</div>
        }
        else
        {
            <Table>
                <TableHeader>
                    <TableRow>
                        <TableHead Class="px-4">Account</TableHead>
                        <TableHead Class="px-4">Roles</TableHead>
                        <TableHead Class="px-4">Employee</TableHead>
                        <TableHead Class="px-4">Status</TableHead>
                        <TableHead Class="px-4">Actions</TableHead>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    @foreach (var account in _accounts.Items)
                    {
                        <TableRow>
                            <TableCell Class="px-4 py-3">
                                <div class="font-medium">@account.Email</div>
                                <div class="text-xs text-muted-foreground">@FullName(account)</div>
                            </TableCell>
                            <TableCell Class="px-4 py-3">
                                <div class="flex flex-wrap gap-1">
                                    @foreach (var role in account.Roles)
                                    {
                                        <Badge Variant="outline">@RoleCheckboxes.Label(role)</Badge>
                                    }
                                </div>
                            </TableCell>
                            <TableCell Class="px-4 py-3 text-muted-foreground">@(account.EmployeeName ?? "--")</TableCell>
                            <TableCell Class="px-4 py-3">
                                @if (!account.IsActive)
                                {
                                    <Badge Variant="secondary">Deactivated</Badge>
                                }
                                else if (account.MustChangePassword)
                                {
                                    <Badge Variant="warning">Must change password</Badge>
                                }
                                else
                                {
                                    <Badge Variant="success">Active</Badge>
                                }
                            </TableCell>
                            <TableCell Class="px-4 py-3">
                                <div class="flex flex-wrap gap-1">
                                    <Button Variant="ghost" Size="sm" Disabled="@(!account.CanManage)" Title="@LockedReason(account)"
                                            OnClick="() => OpenRoles(account)">Roles</Button>
                                    <Button Variant="ghost" Size="sm" Disabled="@(!account.CanManage)" Title="@LockedReason(account)"
                                            OnClick="() => OpenLink(account)">Employee</Button>
                                    <Button Variant="ghost" Size="sm" Disabled="@(!account.CanManage)" Title="@LockedReason(account)"
                                            OnClick="() => _confirm = new PendingConfirm(account, ConfirmAction.ResetPassword)">Reset Password</Button>
                                    @if (account.IsActive)
                                    {
                                        <Button Variant="ghost" Size="sm" Disabled="@(!account.CanManage)" Title="@LockedReason(account)"
                                                OnClick="() => _confirm = new PendingConfirm(account, ConfirmAction.Deactivate)">Deactivate</Button>
                                    }
                                    else
                                    {
                                        <Button Variant="ghost" Size="sm" Disabled="@(!account.CanManage)" Title="@LockedReason(account)"
                                                OnClick="() => Reactivate(account)">Reactivate</Button>
                                    }
                                </div>
                            </TableCell>
                        </TableRow>
                    }
                </TableBody>
            </Table>

            @if (_accounts.TotalPages > 1)
            {
                <div class="border-t px-4 py-3">
                    <Pagination CurrentPage="@_page" TotalPages="@_accounts.TotalPages" OnPageChange="@HandlePageChange" />
                </div>
            }
        }
    </CardContent>
</Card>

<CreateAccountDialog IsOpen="@_showCreate" IsOpenChanged="@(open => _showCreate = open)" OnCreated="OnAccountCreated" />

<Dialog IsOpen="@(_rolesFor is not null)" OnClose="CloseRoles" Title="Edit roles" Description="@_rolesFor?.Email">
    <ChildContent>
        <div class="space-y-4" data-roles-dialog>
            @if (_dialogError is not null)
            {
                <Alert Variant="destructive">@_dialogError</Alert>
            }
            @if (_assignableError is not null)
            {
                <Alert Variant="destructive">Roles could not be loaded: @_assignableError</Alert>
            }
            else
            {
                <RoleCheckboxes Roles="@_assignable" Selected="@_rolesSelected" Disabled="@_busy" />
            }
            <p class="text-xs text-muted-foreground">Changes apply straight away. The user will need to sign in again.</p>
            <div class="flex justify-end gap-2">
                <Button Variant="outline" Size="sm" OnClick="CloseRoles">Cancel</Button>
                <Button Variant="primary" Size="sm" Loading="@_busy" OnClick="SaveRoles">Save Roles</Button>
            </div>
        </div>
    </ChildContent>
</Dialog>

<Dialog IsOpen="@(_linkFor is not null)" OnClose="CloseLink" Title="Employee record" Description="@_linkFor?.Email">
    <ChildContent>
        <div class="space-y-4" data-link-dialog>
            @if (_dialogError is not null)
            {
                <Alert Variant="destructive">@_dialogError</Alert>
            }
            @if (_linkFor?.EmployeeName is { } current)
            {
                <p class="text-sm">Currently linked to <span class="font-medium">@current</span>.</p>
            }
            <EmployeePicker Id="link-employee" @bind-Selected="_linkEmployee" />
            <div class="flex justify-end gap-2">
                @if (_linkFor?.EmployeeId is not null)
                {
                    <Button Variant="outline" Size="sm" Loading="@_busy" OnClick="() => SaveLink(null)">Unlink</Button>
                }
                <Button Variant="primary" Size="sm" Disabled="@(_linkEmployee is null)" Loading="@_busy"
                        OnClick="() => SaveLink(_linkEmployee?.Id)">Link Employee</Button>
            </div>
        </div>
    </ChildContent>
</Dialog>

<AlertDialog IsOpen="@(_confirm is not null)"
             Title="@ConfirmTitle"
             Description="@ConfirmDescription"
             ConfirmText="@ConfirmButtonText"
             ConfirmVariant="destructive"
             Loading="@_busy"
             OnConfirm="RunConfirmed"
             OnCancel="() => _confirm = null" />

<TemporaryPasswordDialog Password="@_temporaryPassword" Email="@_temporaryPasswordFor" OnClosed="ClearTemporaryPassword" />

@code {
    private enum ConfirmAction { Deactivate, ResetPassword }

    private sealed record PendingConfirm(UserAccountDto Account, ConfirmAction Action);

    private const int PageSize = 20;

    /// <summary>A search to start with, e.g. from an employee's Account button.</summary>
    [SupplyParameterFromQuery] public string? Search { get; set; }

    private PagedResult<UserAccountDto>? _accounts;
    private string? _loadError;
    private string _search = string.Empty;
    private int _page = 1;
    private int _loadVersion;

    private IReadOnlyList<string> _assignable = [];
    private string? _assignableError;

    private string? _actionError;
    private string? _dialogError;
    private bool _busy;

    private bool _showCreate;
    private UserAccountDto? _rolesFor;
    private HashSet<string> _rolesSelected = [];
    private UserAccountDto? _linkFor;
    private EmployeeListDto? _linkEmployee;
    private PendingConfirm? _confirm;
    private string? _temporaryPassword;
    private string? _temporaryPasswordFor;

    protected override async Task OnInitializedAsync()
    {
        _search = Search ?? string.Empty;
        await Task.WhenAll(LoadAccounts(), LoadAssignableRoles());
    }

    private async Task LoadAssignableRoles()
    {
        try
        {
            _assignable = await Api.GetAssignableRolesAsync() ?? [];
        }
        catch (Exception ex)
        {
            _assignableError = ex.Message;
        }
    }

    private async Task LoadAccounts()
    {
        // Every keystroke in the search box starts a load; only the latest may touch the list.
        var version = ++_loadVersion;
        try
        {
            var result = await Api.GetUserAccountsAsync(_page, PageSize, _search);
            if (version != _loadVersion) return;
            _accounts = result;
            _loadError = null;
        }
        catch (Exception ex)
        {
            if (version != _loadVersion) return;
            _accounts = null;
            _loadError = ex.Message;
        }
    }

    private async Task HandlePageChange(int page)
    {
        _page = page;
        await LoadAccounts();
    }

    private static string FullName(UserAccountDto account) =>
        string.Join(" ", new[] { account.FirstName, account.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    private static string? LockedReason(UserAccountDto account) =>
        account.CanManage ? null : "Only an administrator can change an Admin or HR Manager account.";

    /// <summary>Runs a change, then refreshes the list. A failure is shown in the open dialog, or above the list.</summary>
    private async Task RunAsync(Func<Task> change, bool inDialog)
    {
        _busy = true;
        _dialogError = null;
        _actionError = null;
        try
        {
            await change();
            await LoadAccounts();
        }
        catch (Exception ex)
        {
            if (inDialog) _dialogError = ex.Message;
            else _actionError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OnAccountCreated(CreatedUserAccountDto created)
    {
        ShowTemporaryPassword(created.Account.Email, created.TemporaryPassword);
        await LoadAccounts();
    }

    private void OpenRoles(UserAccountDto account)
    {
        _dialogError = null;
        _rolesSelected = account.Roles.ToHashSet();
        _rolesFor = account;
    }

    private void CloseRoles() => _rolesFor = null;

    private Task SaveRoles()
    {
        if (_rolesFor is not { } account) return Task.CompletedTask;

        // Only roles this user may grant are sent; any others the account holds are the API's to keep.
        var roles = _rolesSelected.Where(r => _assignable.Contains(r)).Append("Employee").Distinct().ToList();
        return RunAsync(async () =>
        {
            await Api.SetUserRolesAsync(account.Id, roles);
            _rolesFor = null;
        }, inDialog: true);
    }

    private void OpenLink(UserAccountDto account)
    {
        _dialogError = null;
        _linkEmployee = null;
        _linkFor = account;
    }

    private void CloseLink() => _linkFor = null;

    private Task SaveLink(Guid? employeeId)
    {
        if (_linkFor is not { } account) return Task.CompletedTask;

        return RunAsync(async () =>
        {
            await Api.LinkUserEmployeeAsync(account.Id, employeeId);
            _linkFor = null;
        }, inDialog: true);
    }

    private Task Reactivate(UserAccountDto account) => RunAsync(() => Api.ReactivateUserAsync(account.Id), inDialog: false);

    private string? ConfirmTitle => _confirm switch
    {
        { Action: ConfirmAction.Deactivate } c => $"Deactivate {c.Account.Email}?",
        { Action: ConfirmAction.ResetPassword } c => $"Reset the password for {c.Account.Email}?",
        _ => null
    };

    private string? ConfirmDescription => _confirm?.Action switch
    {
        ConfirmAction.Deactivate => "They are signed out straight away and can't sign in again until the account is reactivated.",
        ConfirmAction.ResetPassword => "Their current password stops working and they are signed out. You'll get a temporary password to give them.",
        _ => null
    };

    private string ConfirmButtonText => _confirm?.Action == ConfirmAction.ResetPassword ? "Reset Password" : "Deactivate";

    private async Task RunConfirmed()
    {
        if (_confirm is not { } pending) return;

        await RunAsync(async () =>
        {
            if (pending.Action == ConfirmAction.Deactivate)
                await Api.DeactivateUserAsync(pending.Account.Id);
            else
                ShowTemporaryPassword(pending.Account.Email, await Api.ResetUserPasswordAsync(pending.Account.Id));
        }, inDialog: false);

        _confirm = null;
    }

    private void ShowTemporaryPassword(string email, string? password)
    {
        _temporaryPasswordFor = email;
        _temporaryPassword = password;
    }

    private void ClearTemporaryPassword()
    {
        _temporaryPassword = null;
        _temporaryPasswordFor = null;
    }
}
```

- [ ] **Step 8: Add the navigation entries**

In `src/PeopleCore.Web/Layout/NavMenu.razor`, add a section after the `Analytics` section. Mind the comma after the `Analytics` entry's closing `])`:

```csharp
        new("Administration", "Admin,HRManager",
        [
            new("/admin/users", "Users", "bi-person-gear")
        ])
```

In `src/PeopleCore.Web/Layout/MobileFooterNav.razor`, add a group after the `Recruitment` group, again minding the comma:

```csharp
        new("Administration", "Admin,HRManager",
        [
            new("admin/users", "Users")
        ])
```

- [ ] **Step 9: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~UsersTests|FullyQualifiedName~NavMenuTests"`
Expected: all pass. If `ButtonIn(row, "Roles").GetAttribute("title")` is null, check how `Button.razor` renders its `Title` parameter and assert on that attribute instead. Don't change the component.

- [ ] **Step 10: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): manage accounts, roles and temporary passwords from an Administration page"
```

---

### Task 12: Create login and Account on the Employees page

**Files:**
- Modify: `src/PeopleCore.Web/Pages/HR/Employees.razor`
- Modify test: `tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs`

**Interfaces:**
- Consumes: `ApiClient.GetEmployeeLinksAsync()`, `EmployeeLinkDto`, `CreatedUserAccountDto` (Task 9); `CreateAccountDialog` and `TemporaryPasswordDialog` (Task 11).
- Produces: in each employee row, a **Create login** button when the employee has no login and an **Account** button when they do. **Account** navigates to `/admin/users?search=<work email>`.

- [ ] **Step 1: Stub the new calls in the existing test fixture**

In `tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs`:

1. Add the field, with the other fields:

```csharp
    private static readonly Guid JuanId = Guid.Parse("c3d4e5f6-a7b8-4c9d-8e0f-2a3b4c5d6e7f");

    // Read when the request arrives, so a test can say which employees have logins before rendering.
    private string _linksJson = "[]";
```

2. At the end of the constructor, add:

```csharp
        _api.On(HttpMethod.Get, "/api/users/employee-links", () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_linksJson, Encoding.UTF8, "application/json")
            })
            .On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK, """["Manager","Employee","PayrollService"]""");
```

- [ ] **Step 2: Write the failing tests**

Add these tests to `EmployeesTests`:

```csharp
    private static IElement RowNamed(IRenderedComponent<Employees> cut, string name) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(name));

    private static List<string> ButtonsIn(IElement row) =>
        row.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

    [Fact]
    public void EachEmployee_OffersToCreateALogin_OrOpensTheOneTheyHave()
    {
        _linksJson = $$"""[{"employeeId":"{{MariaId}}","userId":"u-1","isActive":true}]""";

        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId), Employee("EMP-2", "Juan Cruz", id: JuanId)));

        ButtonsIn(RowNamed(cut, "Maria Santos")).Should().Contain("Account").And.NotContain("Create login");
        ButtonsIn(RowNamed(cut, "Juan Cruz")).Should().Contain("Create login").And.NotContain("Account");
    }

    [Fact]
    public void Account_OpensTheUsersPage_SearchingForTheirEmail()
    {
        _linksJson = $$"""[{"employeeId":"{{MariaId}}","userId":"u-1","isActive":true}]""";
        var cut = RenderPage(Paged(1, Employee("EMP-1", "Maria Santos", id: MariaId)));

        RowNamed(cut, "Maria Santos").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Account").Click();

        CurrentUri.Should().EndWith("/admin/users?search=maria%40company.test");
    }

    [Fact]
    public void CreateLogin_StartsFromTheEmployee_AndShowsTheTemporaryPasswordOnce()
    {
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
            $$"""
            {"account":{"id":"u-9","email":"juan@company.test","firstName":"Juan","lastName":"Cruz","roles":["Employee"],
              "isActive":true,"mustChangePassword":true,"employeeId":"{{JuanId}}","employeeName":"Juan Cruz","canManage":true},
             "temporaryPassword":"Kx7mPq2RtW9zNb4s"}
            """);
        var cut = RenderPage(Paged(1, Employee("EMP-2", "Juan Cruz", id: JuanId)));

        RowNamed(cut, "Juan Cruz").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Create login").Click();
        cut.WaitForAssertion(() => cut.Find("#account-email").GetAttribute("value").Should().Be("juan@company.test"));
        cut.Find("#account-first-name").GetAttribute("value").Should().Be("Juan");
        cut.Find("#account-last-name").GetAttribute("value").Should().Be("Cruz");
        cut.Find("[data-selected-employee]").TextContent.Should().Contain("Juan Cruz");

        cut.Find("form[data-create-account]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-temporary-password]").TextContent.Trim().Should().Be("Kx7mPq2RtW9zNb4s"));
        var postIndex = _api.Requests.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/users");
        System.Text.Json.JsonDocument.Parse(_api.RequestBodies[postIndex]!).RootElement
            .GetProperty("employeeId").GetGuid().Should().Be(JuanId);
        _api.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/users/employee-links").Should().Be(2,
            "the list learns that Juan now has a login");
    }
```

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~EmployeesTests"`
Expected: the three new tests FAIL, because no "Create login" or "Account" buttons exist yet. The existing tests still pass.

- [ ] **Step 3: Load the links and offer the actions**

In `src/PeopleCore.Web/Pages/HR/Employees.razor`:

1. Replace the Actions cell:

```razor
                            <TableCell Class="px-4 py-3">
                                <AuthorizeView Roles="Admin,HRManager,PayrollService">
                                    <Button Variant="ghost" Size="sm" OnClick="() => GoToCompensation(emp.Id)">Compensation</Button>
                                </AuthorizeView>
                            </TableCell>
```

with:

```razor
                            <TableCell Class="px-4 py-3">
                                <AuthorizeView Roles="Admin,HRManager,PayrollService">
                                    <Button Variant="ghost" Size="sm" OnClick="() => GoToCompensation(emp.Id)">Compensation</Button>
                                </AuthorizeView>
                                @if (_links is not null)
                                {
                                    if (_links.TryGetValue(emp.Id, out var link))
                                    {
                                        <Button Variant="ghost" Size="sm" Class="@(link.IsActive ? null : "text-muted-foreground")"
                                                Title="@(link.IsActive ? "Open their sign-in account" : "Their sign-in account is deactivated")"
                                                OnClick="() => GoToAccount(emp)">Account</Button>
                                    }
                                    else
                                    {
                                        <Button Variant="ghost" Size="sm" OnClick="() => _createLoginFor = emp">Create login</Button>
                                    }
                                }
                            </TableCell>
```

2. Directly above `<Card>` (the one wrapping the table, below the filters card), add:

```razor
@if (_linksError is not null)
{
    <p class="mb-2 text-sm text-muted-foreground">Who has a login could not be loaded: @_linksError</p>
}
```

3. After the closing `</Sheet>`, add:

```razor
<CreateAccountDialog IsOpen="@(_createLoginFor is not null)"
                     IsOpenChanged="@(open => { if (!open) _createLoginFor = null; })"
                     ForEmployee="@_createLoginFor"
                     OnCreated="OnLoginCreated" />

<TemporaryPasswordDialog Password="@_temporaryPassword" Email="@_temporaryPasswordFor" OnClosed="ClearTemporaryPassword" />
```

4. In `@code`, add these fields after `private EmployeeFormModel _form = new();`:

```csharp
    private Dictionary<Guid, EmployeeLinkDto>? _links;
    private string? _linksError;
    private EmployeeListDto? _createLoginFor;
    private string? _temporaryPassword;
    private string? _temporaryPasswordFor;
```

5. Replace `await Task.WhenAll(LoadEmployees(), LoadDepartments());` with:

```csharp
        await Task.WhenAll(LoadEmployees(), LoadDepartments(), LoadLinks());
```

6. Add these methods after `LoadDepartments`:

```csharp
    private async Task LoadLinks()
    {
        // Only the login buttons need these, so a failure hides them rather than the employee list.
        try
        {
            _links = (await Api.GetEmployeeLinksAsync() ?? []).ToDictionary(link => link.EmployeeId);
            _linksError = null;
        }
        catch (Exception ex)
        {
            _links = null;
            _linksError = ex.Message;
        }
    }

    private void GoToAccount(EmployeeListDto employee) =>
        Nav.NavigateTo($"/admin/users?search={Uri.EscapeDataString(employee.WorkEmail)}");

    private async Task OnLoginCreated(CreatedUserAccountDto created)
    {
        _temporaryPasswordFor = created.Account.Email;
        _temporaryPassword = created.TemporaryPassword;
        await LoadLinks();
    }

    private void ClearTemporaryPassword()
    {
        _temporaryPassword = null;
        _temporaryPasswordFor = null;
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~EmployeesTests"`
Expected: all pass, old and new.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Web/Pages/HR/Employees.razor tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs
git commit -m "feat(web): create an employee's login, or open it, from the Employees page"
```

---

### Task 13: Whole-branch verification

**Files:**
- Modify: `.claude/launch.json`, only if the API has no entry yet.

**Interfaces:**
- Consumes: everything above.
- Produces: evidence. Every suite green, plus one walk-through of the real flow.

- [ ] **Step 1: Build and run every suite**

Run each in turn:

```bash
dotnet build PeopleCore.slnx
dotnet test tests/PeopleCore.Application.Tests
dotnet test tests/PeopleCore.Infrastructure.Tests
dotnet test tests/PeopleCore.Web.Tests
```

Expected: 0 build errors, and every test passes in all three projects. Record the pass counts. The branch started at 452 tests.

- [ ] **Step 2: Start the API and the web app**

The API applies pending migrations at startup, so this runs `AddAccountStatus` against the local development database. Existing accounts stay active (Task 1, Step 6).

If `.claude/launch.json` has no API entry, add one next to `peoplecore-web`:

```json
    {
      "name": "peoplecore-api",
      "runtimeExecutable": "dotnet",
      "runtimeArgs": ["run", "--project", "src/PeopleCore.API/PeopleCore.API.csproj", "--launch-profile", "http"],
      "port": 5180
    }
```

Start both with the Browser pane's `preview_start` (`peoplecore-api`, then `peoplecore-web`).

- [ ] **Step 3: Walk the flow in the browser**

1. Any token issued before this change is rejected, so you land on the login page. Sign in as the seeded admin.
2. Open **Administration → Users**. The admin is listed as Active, with Admin and Employee.
3. On the admin's own row, **Deactivate** is refused with "You can't deactivate your own account." (shown above the list).
4. From **Employees**, choose **Create login** for an employee and tick **Manager**. The temporary password dialog appears; copy the password.
5. The employee's row now shows **Account**, which opens Users filtered to them, marked "Must change password".
6. In a private window, sign in as that employee with the temporary password. The "Set a new password" screen replaces the dashboard. Set one; the app reloads into the dashboard, with the Management section in the nav.
7. Back as the admin, remove **Manager** from that account. In the employee's window, the next navigation that calls the API sends them to the login page.
8. As the admin, **Deactivate** the account. Signing in as the employee fails with "Invalid email or password."
9. **Reactivate**, then **Reset Password**. A new temporary password is shown, and signing in with it shows the set-a-new-password screen again.

Take a screenshot of the Users page and of the set-a-new-password screen as proof.

- [ ] **Step 4: Stop the servers and confirm the tree is clean**

Run: `git status --short`
Expected: empty, or only the `launch.json` change from Step 2. If that's there, commit it:

```bash
git add .claude/launch.json
git commit -m "chore: add a launch entry for the API"
```

---

## Spec coverage

| Spec section | Task |
|---|---|
| Data model: `IsActive`, `MustChangePassword`, unique `EmployeeId` | 1 |
| Roles: assignable/privileged, Employee always present | 2, 5, 6 |
| Permission matrix and the three guards | 2 |
| `GET /`, `/{id}`, `/assignable-roles`, `/employee-links`, `POST /` | 5 |
| `PUT roles`, `PUT employee`, deactivate, reactivate, reset-password | 6 |
| Validation (400) and refusals (403) | 5, 6 |
| Temporary passwords | 3, 5, 6 |
| Security stamp: claim, per-request check, stamp change on every mutation | 7, 6 |
| Login refuses inactive; change-password returns a token and clears the flag | 7 |
| `PasswordChangeRequiredFilter` | 8 |
| Seeding: admin holds Employee | 7 |
| Web client and DTOs | 9 |
| Forced change screen | 10 |
| `/admin/users` page, dialogs, nav (desktop and mobile) | 11 |
| Employees page: Create login and Account | 12 |
| Testing section | 1–12, verified end to end in 13 |
