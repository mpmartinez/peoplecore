# Permissions Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every role-name access check in PeopleCore with a permission check, backed by permissions stored on roles, with no change to what any existing account can do.

**Architecture:**
- A fixed permission catalogue lives in `PeopleCore.Application`.
- Roles become `ApplicationRole` records whose permissions are rows in Identity's `AspNetRoleClaims`. A migration seeds existing databases; `RoleSeeder` seeds fresh ones.
- At sign-in the token receives one `permission` claim per effective permission plus `perm_v`. Admin gets the whole catalogue.
- `[RequirePermission(...)]` (an `AuthorizeAttribute` with a policy name) plus a `PermissionPolicyProvider` enforce access on the API and in the Blazor client.
- In-code checks move to `ICurrentUserService.HasPermission`.
- An equivalence test proves that every seeded role reaches exactly the endpoints it reached before.

**Tech Stack:** .NET 10, ASP.NET Core Identity + JWT bearer, ASP.NET Core authorization policies, EF Core 10 / Npgsql (snake_case), Blazor WebAssembly, xUnit + Moq + FluentAssertions, bUnit 2.10.

**Spec:** `docs/superpowers/specs/2026-09-14-custom-roles-permissions-design.md` (phase 1 only)

## Global Constraints

- Permission keys, exactly: `employees.view-all`, `employees.manage`, `organization.manage`, `organization.delete`, `attendance.manage`, `attendance.device-sync`, `leave.manage`, `leave.run-accruals`, `approvals.team`, `approvals.all`, `performance.manage`, `payroll.manage`, `recruitment.manage`, `scheduling.manage`, `analytics.hr`, `analytics.executive`, `users.manage`, `roles.manage`.
- Seeded permissions:
  - HRManager: `employees.view-all`, `employees.manage`, `organization.manage`, `attendance.manage`, `attendance.device-sync`, `leave.manage`, `approvals.all`, `performance.manage`, `payroll.manage`, `recruitment.manage`, `scheduling.manage`, `analytics.hr`, `users.manage`.
  - Manager: `approvals.team`.
  - PayrollService: `payroll.manage`.
  - Service: `attendance.device-sync`.
  - Employee: none.
  - Admin: none stored; it holds every permission by rule.
- System roles, locked: `Admin`, `Employee`, `Service`. Role names are unchanged by this phase.
- Claim types: `permission` (one claim per key) and `perm_v` with value `"1"`. A token without `perm_v` = `"1"` is rejected with 401.
- Policy names: `"permission:" + string.Join("|", anyOf)`. A policy passes when the user is authenticated and holds any listed key.
- **No visible change:** every account reaches exactly the endpoints, pages and menu items it reaches today. The only user-facing effect is that everyone signs in once after deploy.
- `AccountManagementPolicy` and `AccountRoles` (role-name granting rules) are **not** changed in this phase.
- House style: comments explain *why*; test names read as sentences.
- Test commands:
  - `dotnet test tests/PeopleCore.Application.Tests`
  - `dotnet test tests/PeopleCore.Infrastructure.Tests` (needs the local `m2net-postgres` container)
  - `dotnet test tests/PeopleCore.Web.Tests`

## Deviations from the spec, decided while planning

1. **Seeding runs exactly once instead of "only when a role has no permission claims yet".**
   - **Why:** the spec's rule would re-seed a role an admin deliberately emptied in phase 2.
   - **Existing databases:** the migration's own SQL seeds their roles.
   - **Fresh databases:** `RoleSeeder` seeds a role only at the moment it creates it.
   - **Test:** a Postgres test checks that the migration SQL and `SeededRoles` agree.
2. **The web client shows or hides things with a small `PermissionView` component instead of `AuthorizeView Policy=`.**
   - **Why:** bUnit's fake authorization checks policies by name and ignores claims, so an `AuthorizeView Policy` could not be tested against real permission claims.
   - **Pages** still use `[RequirePermission]` with the policy provider.
3. **`EmployeeAccessService.IsHrStaff` is renamed `CanReachEveryone`**, as the spec says, and nothing outside the service and its tests used it.

## File Map

**Application** (`src/PeopleCore.Application`)
- Create `Common/Authorization/Permissions.cs`: keys, catalogue (`PermissionDefinition`), claim types, `PermissionPolicy` name helpers.
- Modify `Common/Interfaces/ICurrentUserService.cs`: replace `IsInRole` with `HasPermission`.
- Modify `Employees/Services/EmployeeAccessService.cs`, `Employees/Interfaces/IEmployeeAccessService.cs`.

**Infrastructure** (`src/PeopleCore.Infrastructure`)
- Create `Identity/ApplicationRole.cs`, `Identity/SeededRoles.cs`, `Identity/RoleSeeder.cs`, `Identity/IRolePermissionReader.cs`, `Identity/RolePermissionReader.cs`.
- Create `Persistence/Configurations/Identity/ApplicationRoleConfiguration.cs`.
- Create `Persistence/Migrations/<timestamp>_AddRolePermissions.cs` (generated, then edited).
- Modify `Persistence/AppDbContext.cs`, `Identity/CurrentUserService.cs`.

**API** (`src/PeopleCore.API`)
- Create `Authorization/RequirePermissionAttribute.cs`, `Authorization/PermissionPolicyProvider.cs`.
- Modify `Extensions/ServiceExtensions.cs`, `Program.cs`, `Accounts/AccountTokenValidator.cs`, `Controllers/Auth/AuthController.cs`, `Controllers/Employees/EmployeesController.cs`, and the 20 controllers listed in Task 7.

**Web** (`src/PeopleCore.Web`)
- Create `Auth/Permissions.cs`, `Auth/RequirePermissionAttribute.cs`, `Auth/PermissionPolicyProvider.cs`, `Auth/PermissionClaimsExtensions.cs`, `Auth/PermissionView.razor`.
- Modify `Program.cs`, `Layout/NavMenu.razor`, `Layout/MobileFooterNav.razor`, `Pages/Dashboard.razor`, `Pages/Analytics/AnalyticsDashboard.razor`, `Pages/HR/Employees.razor`, and the page guards listed in Task 9.

**Tests**
- Application: `Api/PermissionsCatalogueTests.cs`, `Api/PermissionPolicyProviderTests.cs`, `Api/PermissionEquivalenceTests.cs`, `Api/WebPermissionsMirrorTests.cs`; modify `Api/AccountTokenValidatorTests.cs`, `Api/AuthTokenTests.cs`, `Api/ChangePasswordTests.cs`, `Common/SignedInCaller.cs`, `Employees/EmployeeAccessServiceTests.cs`, `Employees/EmployeesControllerAuthorizationTests.cs`.
- Infrastructure: `RoleSeedingTests.cs`, `RolePermissionReaderTests.cs`; modify `UserAccountDirectoryTests.cs`.
- Web: `TestSupport/SeededPermissions.cs`, `Auth/PermissionViewTests.cs`, `Auth/PermissionPolicyProviderTests.cs`; modify `Layout/NavMenuTests.cs`, `Pages/DashboardTests.cs`, `Pages/Analytics/AnalyticsDashboardTests.cs`, `Pages/HR/EmployeesTests.cs`.

---

### Task 1: The permission catalogue

**Files:**
- Create: `src/PeopleCore.Application/Common/Authorization/Permissions.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/PermissionsCatalogueTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `PeopleCore.Application.Common.Authorization`):
  - `static class Permissions`, with:
    - the key constants `EmployeesViewAll`, `EmployeesManage`, `OrganizationManage`, `OrganizationDelete`, `AttendanceManage`, `AttendanceDeviceSync`, `LeaveManage`, `LeaveRunAccruals`, `ApprovalsTeam`, `ApprovalsAll`, `PerformanceManage`, `PayrollManage`, `RecruitmentManage`, `SchedulingManage`, `AnalyticsHr`, `AnalyticsExecutive`, `UsersManage`, `RolesManage`;
    - `const string ClaimType = "permission"`, `const string VersionClaimType = "perm_v"`, `const string CurrentVersion = "1"`;
    - `IReadOnlyList<PermissionDefinition> All` and `IReadOnlyList<string> AllKeys`, both in catalogue order.
  - `sealed record PermissionDefinition(string Key, string Group, string Label, string Description)`.
  - `static class PermissionPolicy`, with `const string Prefix = "permission:"`, `string NameFor(params string[] anyOf)` and `bool TryParse(string policyName, out IReadOnlyList<string> anyOf)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/PermissionsCatalogueTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The catalogue is the vocabulary every access check, role and token speaks. Its keys are stored
/// in the database and in tokens, so they are pinned here exactly.
/// </summary>
public class PermissionsCatalogueTests
{
    [Fact]
    public void TheCatalogue_HasExactlyTheseKeys_InThisOrder()
    {
        Permissions.AllKeys.Should().Equal(
            "employees.view-all", "employees.manage", "organization.manage", "organization.delete",
            "attendance.manage", "attendance.device-sync", "leave.manage", "leave.run-accruals",
            "approvals.team", "approvals.all", "performance.manage", "payroll.manage",
            "recruitment.manage", "scheduling.manage", "analytics.hr", "analytics.executive",
            "users.manage", "roles.manage");
    }

    [Fact]
    public void EveryPermission_HasAGroupLabelAndDescription()
    {
        Permissions.All.Should().OnlyContain(p =>
            !string.IsNullOrWhiteSpace(p.Group) && !string.IsNullOrWhiteSpace(p.Label) && !string.IsNullOrWhiteSpace(p.Description));
    }

    [Fact]
    public void APolicyName_ListsItsPermissions_AndParsesBack()
    {
        var name = PermissionPolicy.NameFor(Permissions.ApprovalsTeam, Permissions.ApprovalsAll);

        name.Should().Be("permission:approvals.team|approvals.all");
        PermissionPolicy.TryParse(name, out var anyOf).Should().BeTrue();
        anyOf.Should().Equal("approvals.team", "approvals.all");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("permission:")]
    [InlineData("permission:not.a-permission")]
    public void ANameThatIsNotAPermissionPolicy_DoesNotParse(string name)
    {
        PermissionPolicy.TryParse(name, out _).Should().BeFalse();
    }

    [Fact]
    public void APolicyForNoPermissions_CannotBeNamed()
    {
        var act = () => PermissionPolicy.NameFor();

        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PermissionsCatalogueTests"`
Expected: build error, `The type or namespace name 'Authorization' does not exist in the namespace 'PeopleCore.Application.Common'`.

- [ ] **Step 3: Write the catalogue**

Create `src/PeopleCore.Application/Common/Authorization/Permissions.cs`:

```csharp
namespace PeopleCore.Application.Common.Authorization;

/// <summary>One thing a role can allow, with the words the Roles page shows for it.</summary>
public sealed record PermissionDefinition(string Key, string Group, string Label, string Description);

/// <summary>
/// Everything a role can allow. Access checks name these keys, never a role, so a role an admin
/// creates at runtime means something without a code change. Keys are persisted - in
/// AspNetRoleClaims and in tokens - so a key is renamed only with a migration. The web client keeps
/// a copy (PeopleCore.Web/Auth/Permissions.cs); WebPermissionsMirrorTests keeps the two equal.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    /// <summary>Tokens without this claim at <see cref="CurrentVersion"/> predate permissions and are refused.</summary>
    public const string VersionClaimType = "perm_v";
    public const string CurrentVersion = "1";

    public const string EmployeesViewAll = "employees.view-all";
    public const string EmployeesManage = "employees.manage";
    public const string OrganizationManage = "organization.manage";
    public const string OrganizationDelete = "organization.delete";
    public const string AttendanceManage = "attendance.manage";
    public const string AttendanceDeviceSync = "attendance.device-sync";
    public const string LeaveManage = "leave.manage";
    public const string LeaveRunAccruals = "leave.run-accruals";
    public const string ApprovalsTeam = "approvals.team";
    public const string ApprovalsAll = "approvals.all";
    public const string PerformanceManage = "performance.manage";
    public const string PayrollManage = "payroll.manage";
    public const string RecruitmentManage = "recruitment.manage";
    public const string SchedulingManage = "scheduling.manage";
    public const string AnalyticsHr = "analytics.hr";
    public const string AnalyticsExecutive = "analytics.executive";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(EmployeesViewAll, "People", "View all employees",
            "Any employee's full profile, government IDs, emergency contacts and documents, and everyone's attendance, leave, overtime and schedules."),
        new(EmployeesManage, "People", "Manage employees",
            "Create, edit and deactivate employees, and edit any employee's government IDs, emergency contacts and documents."),
        new(OrganizationManage, "Organisation", "Manage departments, positions and teams",
            "Create and edit departments, positions and teams."),
        new(OrganizationDelete, "Organisation", "Delete departments, positions and teams",
            "Delete departments, positions and teams."),
        new(AttendanceManage, "Time", "Manage attendance and holidays",
            "Import attendance, and create and delete holidays."),
        new(AttendanceDeviceSync, "Time", "Sync attendance devices",
            "Send clock-ins from attendance devices."),
        new(LeaveManage, "Time", "Manage leave types and accrual policies",
            "Create, edit and delete leave types and accrual policies."),
        new(LeaveRunAccruals, "Time", "Run leave accruals",
            "Run leave accruals by hand."),
        new(ApprovalsTeam, "Approvals", "Approve for my team",
            "Decide direct reports' leave and overtime, write their performance reviews, and see their records."),
        new(ApprovalsAll, "Approvals", "Approve for everyone",
            "Decide anyone's leave and overtime and write anyone's performance review."),
        new(PerformanceManage, "Performance", "Manage review cycles",
            "Create and close performance review cycles."),
        new(PayrollManage, "Payroll", "Run payroll",
            "Payroll runs, settings, compensation, BIR 2316, payroll export and any employee's payslips."),
        new(RecruitmentManage, "Recruitment", "Manage recruitment",
            "Job postings, applicants and interviews."),
        new(SchedulingManage, "Scheduling", "Manage schedules",
            "Shift templates, rotating patterns and shift assignments."),
        new(AnalyticsHr, "Analytics", "HR analytics",
            "Headcount, turnover, attendance, leave, overtime, recruitment and performance analytics."),
        new(AnalyticsExecutive, "Analytics", "Executive analytics",
            "Workforce summary, hiring trend, attrition and executive leave and performance views."),
        new(UsersManage, "Administration", "Manage users",
            "Create sign-in accounts, change their roles and employee links, deactivate them and reset passwords."),
        new(RolesManage, "Administration", "Manage roles",
            "Create, rename, edit and delete roles."),
    ];

    public static readonly IReadOnlyList<string> AllKeys = All.Select(p => p.Key).ToList();
}

/// <summary>
/// Names for authorization policies that require a permission. One policy name can carry several
/// keys, satisfied by any of them, so "a team approver or an everyone approver" is one attribute.
/// </summary>
public static class PermissionPolicy
{
    public const string Prefix = "permission:";

    public static string NameFor(params string[] anyOf)
    {
        if (anyOf.Length == 0)
            throw new ArgumentException("A permission policy needs at least one permission.", nameof(anyOf));
        return Prefix + string.Join('|', anyOf);
    }

    public static bool TryParse(string policyName, out IReadOnlyList<string> anyOf)
    {
        anyOf = [];
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var keys = policyName[Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0 || keys.Any(k => !Permissions.AllKeys.Contains(k))) return false;

        anyOf = keys;
        return true;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PermissionsCatalogueTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Application/Common/Authorization tests/PeopleCore.Application.Tests/Api/PermissionsCatalogueTests.cs
git commit -m "feat(auth): define the permission catalogue"
```

---

### Task 2: Roles that hold permissions, seeded exactly once

**Files:**
- Create: `src/PeopleCore.Infrastructure/Identity/ApplicationRole.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/SeededRoles.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/RoleSeeder.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Identity/ApplicationRoleConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Migrations/<timestamp>_AddRolePermissions.cs` (generated, then edited)
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs:17`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs:53`
- Modify: `src/PeopleCore.API/Program.cs:156-162`
- Modify test: `tests/PeopleCore.Infrastructure.Tests/UserAccountDirectoryTests.cs`
- Test: `tests/PeopleCore.Infrastructure.Tests/RoleSeedingTests.cs`

**Interfaces:**
- Consumes: `Permissions` (Task 1).
- Produces (namespace `PeopleCore.Infrastructure.Identity`):
  - `class ApplicationRole : IdentityRole`, with `string? Description`, `bool IsSystem`, and `const int DescriptionMaxLength = 200`.
  - `static class SeededRoles`, with:
    - role-name constants `Admin`, `HRManager`, `Manager`, `Employee`, `PayrollService`, `Service`;
    - `IReadOnlyList<string> All` and `IReadOnlyList<string> System`;
    - `IReadOnlyDictionary<string, string> Descriptions`;
    - `IReadOnlyList<string> StoredPermissions(string role)` and `IReadOnlyList<string> PermissionsOf(IEnumerable<string> roles)`.
  - `class RoleSeeder(AppDbContext db)`, with `Task SeedAsync(CancellationToken ct = default)`.
  - The migration class `AddRolePermissions`, with `public const string SeedExistingRolesSql`.
  - `AppDbContext` is now `IdentityDbContext<ApplicationUser, ApplicationRole, string>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Infrastructure.Tests/RoleSeedingTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;
using PeopleCore.Infrastructure.Persistence.Migrations;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// Existing roles get their permissions from the migration; roles created on a fresh database get
/// them from RoleSeeder at the moment of creation. Both must hand out exactly what SeededRoles says,
/// and neither may ever touch a role that already exists.
/// </summary>
public class RoleSeedingTests : DatabaseTestBase
{
    public RoleSeedingTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Dictionary<string, List<string>>> StoredPermissionsByRoleAsync()
    {
        await using var read = NewContext();
        var rows = await (from claim in read.RoleClaims
                          join role in read.Roles on claim.RoleId equals role.Id
                          where claim.ClaimType == Permissions.ClaimType
                          select new { role.Name, claim.ClaimValue }).ToListAsync();
        return rows.GroupBy(r => r.Name!).ToDictionary(g => g.Key, g => g.Select(r => r.ClaimValue!).Order().ToList());
    }

    private static Dictionary<string, List<string>> Expected() =>
        SeededRoles.All
            .Where(r => SeededRoles.StoredPermissions(r).Count > 0)
            .ToDictionary(r => r, r => SeededRoles.StoredPermissions(r).Order().ToList());

    [Fact]
    public async Task OnAFreshDatabase_TheSeederCreatesEveryRole_WithItsPermissions_AndMarksTheSystemRoles()
    {
        await new RoleSeeder(Context).SeedAsync();

        await using var read = NewContext();
        var roles = await read.Roles.ToListAsync();
        roles.Select(r => r.Name).Should().BeEquivalentTo(SeededRoles.All);
        roles.Where(r => r.IsSystem).Select(r => r.Name).Should().BeEquivalentTo("Admin", "Employee", "Service");
        roles.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Description));
        (await StoredPermissionsByRoleAsync()).Should().BeEquivalentTo(Expected());
    }

    [Fact]
    public async Task TheSeeder_NeverTouchesARoleThatAlreadyExists()
    {
        // An admin who has emptied Manager's permissions must find it still empty after a restart.
        Context.Roles.Add(new ApplicationRole { Name = "Manager", NormalizedName = "MANAGER" });
        await Context.SaveChangesAsync();

        await new RoleSeeder(Context).SeedAsync();
        await new RoleSeeder(NewContext()).SeedAsync();

        var stored = await StoredPermissionsByRoleAsync();
        stored.Should().NotContainKey("Manager");
        stored.Should().ContainKey("HRManager");
    }

    [Fact]
    public async Task OnAnExistingDatabase_TheMigrationSql_GivesTheExistingRolesTheirPermissions()
    {
        foreach (var name in SeededRoles.All)
            Context.Roles.Add(new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant() });
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(AddRolePermissions.SeedExistingRolesSql);
        await Context.Database.ExecuteSqlRawAsync(AddRolePermissions.SeedExistingRolesSql);

        (await StoredPermissionsByRoleAsync()).Should().BeEquivalentTo(Expected(), "the migration and SeededRoles must agree, and running it twice adds nothing");
        await using var read = NewContext();
        (await read.Roles.Where(r => r.IsSystem).Select(r => r.Name).ToListAsync())
            .Should().BeEquivalentTo("Admin", "Employee", "Service");
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RoleSeedingTests"`
Expected: build error, `The type or namespace name 'ApplicationRole' could not be found`.

- [ ] **Step 3: Write the role type and its configuration**

Create `src/PeopleCore.Infrastructure/Identity/ApplicationRole.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// A role: a name, a description, and the permissions it grants (rows in AspNetRoleClaims with claim
/// type "permission"). System roles are the ones the app relies on by name - Admin holds every
/// permission, Employee is the self-service baseline, Service is the attendance device - so they
/// can be neither edited nor deleted.
/// </summary>
public class ApplicationRole : IdentityRole
{
    public const int DescriptionMaxLength = 200;

    public string? Description { get; set; }

    public bool IsSystem { get; set; }
}
```

Create `src/PeopleCore.Infrastructure/Persistence/Configurations/Identity/ApplicationRoleConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Identity;

public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(r => r.Description).HasMaxLength(ApplicationRole.DescriptionMaxLength);
    }
}
```

In `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`, replace `public class AppDbContext : IdentityDbContext<ApplicationUser>` with:

```csharp
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
```

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, replace `services.AddIdentity<ApplicationUser, IdentityRole>(ConfigureIdentityOptions)` with:

```csharp
        services.AddIdentity<ApplicationUser, ApplicationRole>(ConfigureIdentityOptions)
```

- [ ] **Step 4: Write the seeded roles and the seeder**

Create `src/PeopleCore.Infrastructure/Identity/SeededRoles.cs`:

```csharp
using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// The roles every PeopleCore database starts with, and the permissions each is seeded with. These
/// reproduce, permission for permission, what the hard-coded role checks allowed before permissions
/// existed (PermissionEquivalenceTests proves it). After seeding the database is the authority: an
/// admin may change the ordinary roles, and nothing here overwrites that.
/// </summary>
public static class SeededRoles
{
    public const string Admin = "Admin";
    public const string HRManager = "HRManager";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string PayrollService = "PayrollService";
    public const string Service = "Service";

    public static readonly IReadOnlyList<string> All = [Admin, HRManager, Manager, Employee, PayrollService, Service];

    /// <summary>Roles the app relies on by name, which cannot be edited or deleted.</summary>
    public static readonly IReadOnlyList<string> System = [Admin, Employee, Service];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Admin] = "Every permission, always. Cannot be edited or deleted.",
        [HRManager] = "Runs HR: employees, organisation, time, approvals, performance, payroll, recruitment, schedules, analytics and user accounts.",
        [Manager] = "Approves leave, overtime and performance reviews for their direct reports.",
        [Employee] = "Self-service: their own profile, attendance, leave and payslips. Every account holds it.",
        [PayrollService] = "Runs payroll.",
        [Service] = "Attendance devices sending clock-ins. Cannot be edited or deleted.",
    };

    /// <summary>
    /// The permissions written to the database for <paramref name="role"/> when it is seeded. Admin
    /// stores none: it holds every permission by rule, so a permission added in a later release
    /// reaches it without a data change.
    /// </summary>
    public static IReadOnlyList<string> StoredPermissions(string role) => role switch
    {
        HRManager =>
        [
            Permissions.EmployeesViewAll, Permissions.EmployeesManage, Permissions.OrganizationManage,
            Permissions.AttendanceManage, Permissions.AttendanceDeviceSync, Permissions.LeaveManage,
            Permissions.ApprovalsAll, Permissions.PerformanceManage, Permissions.PayrollManage,
            Permissions.RecruitmentManage, Permissions.SchedulingManage, Permissions.AnalyticsHr,
            Permissions.UsersManage,
        ],
        Manager => [Permissions.ApprovalsTeam],
        PayrollService => [Permissions.PayrollManage],
        Service => [Permissions.AttendanceDeviceSync],
        _ => [],
    };

    /// <summary>What an account holding <paramref name="roles"/> may do under the seeded roles, in catalogue order.</summary>
    public static IReadOnlyList<string> PermissionsOf(IEnumerable<string> roles)
    {
        var held = roles.ToList();
        return held.Contains(Admin)
            ? Permissions.AllKeys
            : Permissions.AllKeys.Where(key => held.Any(role => StoredPermissions(role).Contains(key))).ToList();
    }
}
```

Create `src/PeopleCore.Infrastructure/Identity/RoleSeeder.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// Creates any seeded role that does not exist yet, with its description, system flag and
/// permissions. A role that already exists is never touched - not even to add a permission it lacks
/// - because once roles are editable its contents are an admin's decision. Existing databases got
/// their permissions once, from the AddRolePermissions migration.
/// </summary>
public class RoleSeeder
{
    private readonly AppDbContext _db;

    public RoleSeeder(AppDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var name in SeededRoles.All)
        {
            var normalized = name.ToUpperInvariant();
            if (await _db.Roles.AnyAsync(r => r.NormalizedName == normalized, ct)) continue;

            var role = new ApplicationRole
            {
                Name = name,
                NormalizedName = normalized,
                Description = SeededRoles.Descriptions[name],
                IsSystem = SeededRoles.System.Contains(name),
            };
            _db.Roles.Add(role);

            foreach (var key in SeededRoles.StoredPermissions(name))
                _db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = key });
        }

        await _db.SaveChangesAsync(ct);
    }
}
```

In `src/PeopleCore.API/Program.cs`, replace:

```csharp
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = ["Admin", "HRManager", "Manager", "Employee", "PayrollService", "Service"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
```

with:

```csharp
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    await new RoleSeeder(dbContext).SeedAsync();
```

(`dbContext` is the `AppDbContext` already resolved earlier in the same scope. `PeopleCore.Infrastructure.Identity` is already imported.)

In `tests/PeopleCore.Infrastructure.Tests/UserAccountDirectoryTests.cs`, replace every `IdentityRole` with `ApplicationRole`. There are three: the `RoleAsync` return type, `new IdentityRole(name)`, and the `IdentityRole[]? roles` parameter. Keep `IdentityUserRole<string>` as it is.

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddRolePermissions --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API --output-dir Persistence/Migrations`
Expected: `Done.`

Open the generated `<timestamp>_AddRolePermissions.cs`.
- **Expected contents of `Up`:** exactly two `AddColumn` calls on `AspNetRoles`: `description` (`character varying(200)`, nullable) and `is_system` (`boolean`, not null, default `false`).
- **If it drops or recreates `AspNetRoles` or any other table,** stop and report BLOCKED with the generated code. Swapping `IdentityRole` for `ApplicationRole` must not rebuild the table.

- [ ] **Step 6: Add the data seeding to the migration**

Edit the generated class so it reads as follows. Keep the generated `AddColumn` and `DropColumn` calls as they came out, and add the constant and the `Sql` call.

```csharp
    /// <inheritdoc />
    public partial class AddRolePermissions : Migration
    {
        /// <summary>
        /// Gives roles that already exist the permissions matching what the hard-coded checks allowed,
        /// and marks the system roles. Runs once, as part of this migration; a fresh database has no
        /// roles yet at this point and gets them from RoleSeeder instead. Kept in step with
        /// SeededRoles by RoleSeedingTests. Idempotent, so running it twice changes nothing.
        /// </summary>
        public const string SeedExistingRolesSql = """
            INSERT INTO "AspNetRoleClaims" (role_id, claim_type, claim_value)
            SELECT r.id, 'permission', p.key
            FROM "AspNetRoles" r
            JOIN (VALUES
                ('HRMANAGER', 'employees.view-all'),
                ('HRMANAGER', 'employees.manage'),
                ('HRMANAGER', 'organization.manage'),
                ('HRMANAGER', 'attendance.manage'),
                ('HRMANAGER', 'attendance.device-sync'),
                ('HRMANAGER', 'leave.manage'),
                ('HRMANAGER', 'approvals.all'),
                ('HRMANAGER', 'performance.manage'),
                ('HRMANAGER', 'payroll.manage'),
                ('HRMANAGER', 'recruitment.manage'),
                ('HRMANAGER', 'scheduling.manage'),
                ('HRMANAGER', 'analytics.hr'),
                ('HRMANAGER', 'users.manage'),
                ('MANAGER', 'approvals.team'),
                ('PAYROLLSERVICE', 'payroll.manage'),
                ('SERVICE', 'attendance.device-sync')
            ) AS p(role, key) ON r.normalized_name = p.role
            WHERE NOT EXISTS (
                SELECT 1 FROM "AspNetRoleClaims" c
                WHERE c.role_id = r.id AND c.claim_type = 'permission' AND c.claim_value = p.key);

            UPDATE "AspNetRoles" SET is_system = TRUE WHERE normalized_name IN ('ADMIN', 'EMPLOYEE', 'SERVICE');

            UPDATE "AspNetRoles" SET description = CASE normalized_name
                WHEN 'ADMIN' THEN 'Every permission, always. Cannot be edited or deleted.'
                WHEN 'HRMANAGER' THEN 'Runs HR: employees, organisation, time, approvals, performance, payroll, recruitment, schedules, analytics and user accounts.'
                WHEN 'MANAGER' THEN 'Approves leave, overtime and performance reviews for their direct reports.'
                WHEN 'EMPLOYEE' THEN 'Self-service: their own profile, attendance, leave and payslips. Every account holds it.'
                WHEN 'PAYROLLSERVICE' THEN 'Runs payroll.'
                WHEN 'SERVICE' THEN 'Attendance devices sending clock-ins. Cannot be edited or deleted.'
            END
            WHERE description IS NULL
              AND normalized_name IN ('ADMIN', 'HRMANAGER', 'MANAGER', 'EMPLOYEE', 'PAYROLLSERVICE', 'SERVICE');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (the two generated AddColumn calls stay here, unchanged)

            migrationBuilder.Sql(SeedExistingRolesSql);
        }

        // (the generated Down stays unchanged)
    }
```

- [ ] **Step 7: Run the infrastructure tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests`
Expected: all pass, including the three `RoleSeedingTests` and the updated `UserAccountDirectoryTests`.

- [ ] **Step 8: Build the solution**

Run: `dotnet build PeopleCore.slnx`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/PeopleCore.Infrastructure src/PeopleCore.API/Extensions/ServiceExtensions.cs src/PeopleCore.API/Program.cs tests/PeopleCore.Infrastructure.Tests
git commit -m "feat(identity): give roles permissions, seeded once per database"
```

---

### Task 3: Reading an account's permissions

**Files:**
- Create: `src/PeopleCore.Infrastructure/Identity/IRolePermissionReader.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/RolePermissionReader.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (DI)
- Test: `tests/PeopleCore.Infrastructure.Tests/RolePermissionReaderTests.cs`

**Interfaces:**
- Consumes: `Permissions` (Task 1); `ApplicationRole`, `SeededRoles` and `RoleSeeder` (Task 2).
- Produces: `interface IRolePermissionReader`, with `Task<IReadOnlyList<string>> GetPermissionsAsync(IEnumerable<string> roleNames, CancellationToken ct = default)`. It returns the effective permissions in catalogue order, or all of them when the roles include `Admin`. Its implementation is `RolePermissionReader(AppDbContext db)`, registered scoped.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Infrastructure.Tests/RolePermissionReaderTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>What a token will say an account may do: the union of its roles' stored permissions.</summary>
public class RolePermissionReaderTests : DatabaseTestBase
{
    public RolePermissionReaderTests(PostgresFixture fixture) : base(fixture) { }

    private RolePermissionReader Reader() => new(NewContext());

    [Fact]
    public async Task TheSeededRoles_ReadBackAsSeeded()
    {
        await new RoleSeeder(Context).SeedAsync();

        foreach (var role in SeededRoles.All.Where(r => r != SeededRoles.Admin))
            (await Reader().GetPermissionsAsync([role])).Should().Equal(SeededRoles.PermissionsOf([role]), role);
    }

    [Fact]
    public async Task SeveralRoles_GiveTheUnion_InCatalogueOrder_WithoutRepeats()
    {
        await new RoleSeeder(Context).SeedAsync();

        (await Reader().GetPermissionsAsync(["PayrollService", "Manager", "Employee"]))
            .Should().Equal(Permissions.ApprovalsTeam, Permissions.PayrollManage);
    }

    [Fact]
    public async Task Admin_HoldsTheWholeCatalogue_ThoughNothingIsStoredForIt()
    {
        await new RoleSeeder(Context).SeedAsync();

        (await Reader().GetPermissionsAsync(["Employee", "Admin"])).Should().Equal(Permissions.AllKeys);
    }

    [Fact]
    public async Task AStoredKeyTheCatalogueNoLongerHas_IsIgnored()
    {
        var role = new ApplicationRole { Name = "Legacy", NormalizedName = "LEGACY" };
        Context.Roles.Add(role);
        Context.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = "reports.retired" });
        Context.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = Permissions.AnalyticsHr });
        await Context.SaveChangesAsync();

        (await Reader().GetPermissionsAsync(["legacy"])).Should().Equal(Permissions.AnalyticsHr);
    }

    [Fact]
    public async Task NoRoles_MeansNoPermissions()
    {
        (await Reader().GetPermissionsAsync([])).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RolePermissionReaderTests"`
Expected: build error, `The type or namespace name 'RolePermissionReader' could not be found`.

- [ ] **Step 3: Write the reader**

Create `src/PeopleCore.Infrastructure/Identity/IRolePermissionReader.cs`:

```csharp
namespace PeopleCore.Infrastructure.Identity;

/// <summary>What an account holding a set of roles may do, as the token issued at sign-in will say.</summary>
public interface IRolePermissionReader
{
    /// <summary>
    /// The union of the roles' permissions, in catalogue order. Every permission when the roles
    /// include Admin. Role names match case-insensitively, as Identity's do.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(IEnumerable<string> roleNames, CancellationToken ct = default);
}
```

Create `src/PeopleCore.Infrastructure/Identity/RolePermissionReader.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IRolePermissionReader"/>
public class RolePermissionReader : IRolePermissionReader
{
    private readonly AppDbContext _db;

    public RolePermissionReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(IEnumerable<string> roleNames, CancellationToken ct = default)
    {
        var normalized = roleNames.Select(name => name.ToUpperInvariant()).ToList();
        if (normalized.Count == 0) return [];
        if (normalized.Contains(SeededRoles.Admin.ToUpperInvariant())) return Permissions.AllKeys;

        var stored = await (from claim in _db.RoleClaims
                            join role in _db.Roles on claim.RoleId equals role.Id
                            where normalized.Contains(role.NormalizedName!) && claim.ClaimType == Permissions.ClaimType
                            select claim.ClaimValue!)
                           .Distinct()
                           .ToListAsync(ct);

        // Filtering through the catalogue fixes the order and drops any key a later release retired.
        return Permissions.AllKeys.Where(stored.Contains).ToList();
    }
}
```

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, directly after `services.AddScoped<IUserAccountDirectory, UserAccountDirectory>();`, add:

```csharp
        services.AddScoped<IRolePermissionReader, RolePermissionReader>();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RolePermissionReaderTests"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Infrastructure/Identity src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Infrastructure.Tests/RolePermissionReaderTests.cs
git commit -m "feat(identity): read what an account's roles permit"
```

---

### Task 4: Tokens carry permissions

**Files:**
- Modify: `src/PeopleCore.API/Controllers/Auth/AuthController.cs`
- Modify: `src/PeopleCore.API/Accounts/AccountTokenValidator.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Api/AuthTokenTests.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Api/ChangePasswordTests.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Api/AccountTokenValidatorTests.cs`

**Interfaces:**
- Consumes: `Permissions.ClaimType`, `Permissions.VersionClaimType` and `Permissions.CurrentVersion` (Task 1); `IRolePermissionReader` (Task 3).
- Produces:
  - `AuthController(UserManager<ApplicationUser>, SignInManager<ApplicationUser>, IConfiguration, IRolePermissionReader)`.
  - Every issued token has one `permission` claim per effective permission, plus `perm_v` = `"1"`.
  - `AccountTokenValidator` rejects a principal without `perm_v` = `"1"` with the reason `"The token was issued before permissions existed."`.

- [ ] **Step 1: Write the failing token tests**

In `tests/PeopleCore.Application.Tests/Api/AuthTokenTests.cs`:

1. Add `using PeopleCore.Application.Common.Authorization;`.
2. Add a field next to `_signIn`:

```csharp
    private readonly Mock<IRolePermissionReader> _permissions = new();
```

3. In the constructor, replace `_sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create());` with:

```csharp
        _permissions.Setup(p => p.GetPermissionsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create(), _permissions.Object);
```

4. `ClaimsOf` groups claims by type and keeps the first value, so it can't see repeated claims. Add this helper beside it:

```csharp
    private static List<string> PermissionsIn(AuthTokenResponse session) =>
        new JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims
            .Where(c => c.Type == "permission").Select(c => c.Value).ToList();
```

5. Add these tests:

```csharp
    [Fact]
    public async Task AToken_CarriesEveryPermissionTheAccountsRolesGrant_AndThePermissionsVersion()
    {
        _users.Setup(u => u.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "Employee", "Manager" });
        _permissions.Setup(p => p.GetPermissionsAsync(
                It.Is<IEnumerable<string>>(r => r.OrderBy(x => x).SequenceEqual(new[] { "Employee", "Manager" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Permissions.ApprovalsTeam, Permissions.AnalyticsHr]);

        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        PermissionsIn(session).Should().Equal("approvals.team", "analytics.hr");
        ClaimsOf(session).Should().Contain("perm_v", "1");
    }

    [Fact]
    public async Task AnAccountWhoseRolesGrantNothing_StillGetsThePermissionsVersion()
    {
        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        PermissionsIn(session).Should().BeEmpty();
        ClaimsOf(session).Should().Contain("perm_v", "1");
    }
```

In `tests/PeopleCore.Application.Tests/Api/ChangePasswordTests.cs`:

1. Add `using PeopleCore.Infrastructure.Identity;` if it's not already there.
2. Replace `_sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create());` with:

```csharp
        var permissions = new Mock<IRolePermissionReader>();
        permissions.Setup(p => p.GetPermissionsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create(), permissions.Object);
```

- [ ] **Step 2: Write the failing validator test**

In `tests/PeopleCore.Application.Tests/Api/AccountTokenValidatorTests.cs`:

1. Replace the `Token` helper with:

```csharp
    private static ClaimsPrincipal Token(string? userId = "u1", string? stamp = "stamp-1", string? permissionsVersion = "1")
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (stamp is not null) claims.Add(new Claim(AccountClaims.SecurityStamp, stamp));
        if (permissionsVersion is not null) claims.Add(new Claim("perm_v", permissionsVersion));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }
```

2. Add:

```csharp
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task ATokenFromBeforePermissionsExisted_IsRejected(string? permissionsVersion)
    {
        // Such a token carries no permission claims, so every permission-protected endpoint would
        // quietly refuse its holder. Rejecting it sends them to sign in and get a complete one.
        (await Check(Token(permissionsVersion: permissionsVersion))).Should().Be("The token was issued before permissions existed.");
    }
```

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~AuthTokenTests|FullyQualifiedName~ChangePasswordTests|FullyQualifiedName~AccountTokenValidatorTests"`
Expected: build error, because `AuthController` has no constructor taking four arguments.

- [ ] **Step 4: Issue permission claims**

In `src/PeopleCore.API/Controllers/Auth/AuthController.cs`:

1. Add `using PeopleCore.Application.Common.Authorization;`.
2. Replace the fields and constructor with:

```csharp
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly IRolePermissionReader _permissions;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        IRolePermissionReader permissions)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _permissions = permissions;
    }
```

3. Replace `IssueTokenAsync`, and the signature line of `GenerateJwtToken`, with:

```csharp
    private async Task<AuthTokenResponse> IssueTokenAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await _permissions.GetPermissionsAsync(roles);
        return new AuthTokenResponse(GenerateJwtToken(user, roles, permissions), user.Email, roles.ToList(), user.MustChangePassword);
    }

    private string GenerateJwtToken(ApplicationUser user, IList<string> roles, IReadOnlyList<string> permissions)
```

4. In `GenerateJwtToken`, directly after `claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));`, add:

```csharp
        // Access is decided by these, not by the role names above - which stay only so the client can
        // show them. Every token gets perm_v, even one granting nothing, so AccountTokenValidator can
        // tell a token from before permissions from one that simply has none.
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        claims.Add(new Claim(Permissions.VersionClaimType, Permissions.CurrentVersion));
```

- [ ] **Step 5: Reject tokens without the permissions version**

In `src/PeopleCore.API/Accounts/AccountTokenValidator.cs`:

1. Add `using PeopleCore.Application.Common.Authorization;`.
2. Directly after the `if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp)) return ...;` statement, add:

```csharp
        if (principal.FindFirstValue(Permissions.VersionClaimType) != Permissions.CurrentVersion)
            return "The token was issued before permissions existed.";
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~AuthTokenTests|FullyQualifiedName~ChangePasswordTests|FullyQualifiedName~AccountTokenValidatorTests"`
Expected: all pass. That includes `TheBearerPipeline_FailsARevokedToken_AndPassesAGoodOne`, whose principals now come from the updated `Token` helper.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.API tests/PeopleCore.Application.Tests/Api
git commit -m "feat(auth): put an account's permissions in its token"
```

---

### Task 5: Enforcing a permission on the API

**Files:**
- Create: `src/PeopleCore.API/Authorization/RequirePermissionAttribute.cs`
- Create: `src/PeopleCore.API/Authorization/PermissionPolicyProvider.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (DI)
- Test: `tests/PeopleCore.Application.Tests/Api/PermissionPolicyProviderTests.cs`

**Interfaces:**
- Consumes: `Permissions` and `PermissionPolicy` (Task 1).
- Produces (namespace `PeopleCore.API.Authorization`):
  - `sealed class RequirePermissionAttribute : AuthorizeAttribute`, constructed as `RequirePermissionAttribute(params string[] anyOf)`, exposing `IReadOnlyList<string> AnyOf`, with `Policy = PermissionPolicy.NameFor(anyOf)`.
  - `class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider`, registered as the singleton `IAuthorizationPolicyProvider`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/PermissionPolicyProviderTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// [RequirePermission] names a policy; the provider turns that name into "signed in and holding any
/// of these permissions". Evaluated through the real IAuthorizationService, as the framework does.
/// </summary>
public class PermissionPolicyProviderTests
{
    private readonly IAuthorizationService _authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorization()
        .AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal SignedInWith(params string[] permissions) =>
        new(new ClaimsIdentity(permissions.Select(p => new Claim(Permissions.ClaimType, p)), "Bearer"));

    private async Task<bool> Allowed(ClaimsPrincipal user, params string[] anyOf) =>
        (await _authorization.AuthorizeAsync(user, null, PermissionPolicy.NameFor(anyOf))).Succeeded;

    [Fact]
    public async Task HoldingThePermission_IsAllowed()
    {
        (await Allowed(SignedInWith(Permissions.PayrollManage), Permissions.PayrollManage)).Should().BeTrue();
    }

    [Fact]
    public async Task HoldingAnyOneOfSeveral_IsAllowed()
    {
        (await Allowed(SignedInWith(Permissions.ApprovalsTeam), Permissions.ApprovalsTeam, Permissions.ApprovalsAll)).Should().BeTrue();
    }

    [Fact]
    public async Task HoldingSomethingElse_IsRefused()
    {
        (await Allowed(SignedInWith(Permissions.ApprovalsTeam), Permissions.PayrollManage)).Should().BeFalse();
    }

    [Fact]
    public async Task ARoleClaimWithTheSameName_DoesNotCount()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Permissions.PayrollManage)], "Bearer"));

        (await Allowed(user, Permissions.PayrollManage)).Should().BeFalse();
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefused_EvenWithTheClaim()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.PayrollManage)]));

        (await Allowed(anonymous, Permissions.PayrollManage)).Should().BeFalse();
    }

    [Fact]
    public async Task APolicyNameThatIsNotAPermission_IsLeftToTheDefaultProvider()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        (await provider.GetPolicyAsync("SomeOtherPolicy")).Should().BeNull();
    }

    [Fact]
    public void TheAttribute_NamesThePolicyForItsPermissions_AndIsStillAnAuthorizeAttribute()
    {
        var attribute = new RequirePermissionAttribute(Permissions.ApprovalsTeam, Permissions.ApprovalsAll);

        attribute.Policy.Should().Be("permission:approvals.team|approvals.all");
        attribute.AnyOf.Should().Equal("approvals.team", "approvals.all");
        attribute.Should().BeAssignableTo<IAuthorizeData>("the password-change filter and the coverage test look for IAuthorizeData");
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PermissionPolicyProviderTests"`
Expected: build error, `The type or namespace name 'Authorization' does not exist in the namespace 'PeopleCore.API'`.

- [ ] **Step 3: Write the attribute and the provider**

Create `src/PeopleCore.API/Authorization/RequirePermissionAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.API.Authorization;

/// <summary>
/// Allows a signed-in caller holding any one of <see cref="AnyOf"/>. Replaces
/// [Authorize(Roles = "...")]: roles are data an admin can change, so an endpoint names what it
/// needs rather than who used to have it. Still an AuthorizeAttribute, so everything that looks for
/// IAuthorizeData - PasswordChangeRequiredFilter, ControllerAuthorizationCoverageTests - sees it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(params string[] anyOf) : base(PermissionPolicy.NameFor(anyOf))
    {
        AnyOf = anyOf;
    }

    public IReadOnlyList<string> AnyOf { get; }
}
```

Create `src/PeopleCore.API/Authorization/PermissionPolicyProvider.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.API.Authorization;

/// <summary>
/// Builds the policy behind every "permission:..." name on demand, so the permissions need no
/// registering one by one. Any other name falls through to the default provider.
/// </summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!PermissionPolicy.TryParse(policyName, out var anyOf))
            return base.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => anyOf.Any(key => context.User.HasClaim(Permissions.ClaimType, key)))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
```

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`:

1. Add `using Microsoft.AspNetCore.Authorization;` and `using PeopleCore.API.Authorization;`.
2. Directly after `services.AddScoped<AccountTokenValidator>();`, add:

```csharp
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PermissionPolicyProviderTests"`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.API/Authorization src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Application.Tests/Api/PermissionPolicyProviderTests.cs
git commit -m "feat(api): require a permission with [RequirePermission]"
```

---

### Task 6: In-code checks ask for permissions

**Files:**
- Modify: `src/PeopleCore.Application/Common/Interfaces/ICurrentUserService.cs`
- Modify: `src/PeopleCore.Infrastructure/Identity/CurrentUserService.cs`
- Modify: `src/PeopleCore.Application/Employees/Interfaces/IEmployeeAccessService.cs`
- Modify: `src/PeopleCore.Application/Employees/Services/EmployeeAccessService.cs`
- Modify: `src/PeopleCore.API/Controllers/Employees/EmployeesController.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Common/SignedInCaller.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Employees/EmployeeAccessServiceTests.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Employees/EmployeesControllerAuthorizationTests.cs`

**Interfaces:**
- Consumes: `Permissions` (Task 1); `SeededRoles.PermissionsOf` (Task 2, test-only use); `RequirePermissionAttribute` (Task 5).
- Produces:
  - `ICurrentUserService.HasPermission(string key)`. `IsInRole` is removed; `Roles` stays.
  - `IEmployeeAccessService.CanReachEveryone`, which replaces `IsHrStaff`.

The existing tests stay phrased in role names. Their fakes turn those names into permissions through `SeededRoles.PermissionsOf`, so every one of them is also a check that the seeded roles keep today's behaviour.

- [ ] **Step 1: Point the test fakes at permissions (this breaks the build until Step 4)**

In `tests/PeopleCore.Application.Tests/Common/SignedInCaller.cs`:

1. Add `using PeopleCore.Infrastructure.Identity;`.
2. Replace the body of `As` with:

```csharp
    public void As(Guid? employeeId, params string[] roles)
    {
        // Phrased in roles, granted as the seeded roles' permissions: every authorization test built
        // on this therefore also proves the seeded roles keep the reach the old role checks gave.
        var granted = SeededRoles.PermissionsOf(roles);
        CurrentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        CurrentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns((string key) => granted.Contains(key));
    }
```

In `tests/PeopleCore.Application.Tests/Employees/EmployeeAccessServiceTests.cs`:

1. Add `using PeopleCore.Infrastructure.Identity;`.
2. Replace the body of `SignInAs` with:

```csharp
    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        var granted = SeededRoles.PermissionsOf(roles);
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns((string key) => granted.Contains(key));
    }
```

3. Replace both `_sut.IsHrStaff` references with `_sut.CanReachEveryone`.
4. Add these tests at the end of the class:

```csharp
    // ---- Permissions, independent of the seeded roles ---------------------------------------

    private void Holding(Guid? employeeId, params string[] permissions)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns((string key) => permissions.Contains(key));
    }

    [Fact]
    public async Task ViewingEveryone_WithoutApprovingForEveryone_SeesButDecidesNothing()
    {
        Holding(Caller, "employees.view-all");

        (await _sut.CanViewAsync(Stranger)).Should().BeTrue();
        (await _sut.CanManageAsync(Stranger)).Should().BeFalse();
        _sut.GetUnfilteredListScope().Should().Be(EmployeeListScope.Everyone);
    }

    [Fact]
    public async Task ApprovingForEveryone_ReachesEveryone()
    {
        Holding(null, "approvals.all");

        (await _sut.CanManageAsync(Stranger)).Should().BeTrue();
        (await _sut.CanViewAsync(Stranger)).Should().BeTrue();
        _sut.CanReachEveryone.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovingForTheTeam_ReachesOnlyDirectReports()
    {
        Holding(Caller, "approvals.team");

        (await _sut.CanManageAsync(DirectReport)).Should().BeTrue();
        (await _sut.CanManageAsync(Stranger)).Should().BeFalse();
        _sut.GetUnfilteredListScope().Should().Be(EmployeeListScope.DirectReportsOf(Caller));
    }
```

In `tests/PeopleCore.Application.Tests/Employees/EmployeesControllerAuthorizationTests.cs`:

1. Add `using PeopleCore.Infrastructure.Identity;`.
2. Replace `_currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns(false);` in the constructor with:

```csharp
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(false);
```

3. Replace the body of `SignInAs` with:

```csharp
    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        var granted = SeededRoles.PermissionsOf(roles);
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns((string key) => granted.Contains(key));
    }
```

- [ ] **Step 2: Run the tests and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~EmployeeAccessServiceTests"`
Expected: build error, `'ICurrentUserService' does not contain a definition for 'HasPermission'`.

- [ ] **Step 3: Replace IsInRole with HasPermission**

Replace `src/PeopleCore.Application/Common/Interfaces/ICurrentUserService.cs` with:

```csharp
namespace PeopleCore.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? EmployeeId { get; }
    string? Email { get; }

    /// <summary>The caller's role names, for display. Never decide access with these - see <see cref="HasPermission"/>.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>True when the caller's token grants <paramref name="key"/> (a <c>Permissions</c> key).</summary>
    bool HasPermission(string key);
}
```

In `src/PeopleCore.Infrastructure/Identity/CurrentUserService.cs`:

1. Add `using PeopleCore.Application.Common.Authorization;`.
2. Replace `public bool IsInRole(string role) => Roles.Contains(role);` with:

```csharp
    public bool HasPermission(string key) =>
        _httpContextAccessor.HttpContext?.User?.HasClaim(Permissions.ClaimType, key) ?? false;
```

- [ ] **Step 4: Scope employee access by permission**

Replace `src/PeopleCore.Application/Employees/Services/EmployeeAccessService.cs` with:

```csharp
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;

namespace PeopleCore.Application.Employees.Services;

/// <inheritdoc cref="IEmployeeAccessService"/>
public class EmployeeAccessService : IEmployeeAccessService
{
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeRepository _employees;

    public EmployeeAccessService(ICurrentUserService currentUser, IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _employees = employees;
    }

    public bool CanReachEveryone =>
        _currentUser.HasPermission(Permissions.EmployeesViewAll) || _currentUser.HasPermission(Permissions.ApprovalsAll);

    public async Task<bool> CanManageAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (_currentUser.HasPermission(Permissions.ApprovalsAll))
            return true;

        return TeamManagerId() is { } managerId
               && await _employees.IsDirectReportAsync(employeeId, managerId, ct);
    }

    public async Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default)
        => _currentUser.EmployeeId == employeeId
           || _currentUser.HasPermission(Permissions.EmployeesViewAll)
           || await CanManageAsync(employeeId, ct);

    public EmployeeListScope GetUnfilteredListScope()
    {
        if (CanReachEveryone)
            return EmployeeListScope.Everyone;

        return TeamManagerId() is { } managerId
            ? EmployeeListScope.DirectReportsOf(managerId)
            : EmployeeListScope.Denied;
    }

    /// <summary>
    /// The caller's employee id when they may approve for their team, otherwise null. Null too for a
    /// team approver with no employee_id claim: nobody can report to an account that is not an employee.
    /// </summary>
    private Guid? TeamManagerId()
        => _currentUser.HasPermission(Permissions.ApprovalsTeam) ? _currentUser.EmployeeId : null;
}
```

In `src/PeopleCore.Application/Employees/Interfaces/IEmployeeAccessService.cs`:

1. Replace the interface's summary with:

```csharp
/// <summary>
/// Whose employee-scoped records (leave, attendance, overtime, schedules) the signed-in caller may
/// see and manage, by permission. "View all employees" sees everyone; "Approve for everyone" sees and
/// decides for everyone; "Approve for my team" sees and decides for the caller's direct reports - the
/// employees whose ReportingManagerId is the caller's own employee id. Everybody reaches themselves.
/// </summary>
```

2. Replace:

```csharp
    /// <summary>True when the caller holds Admin or HRManager.</summary>
    bool IsHrStaff { get; }
```

with:

```csharp
    /// <summary>True when the caller may view all employees or approve for everyone.</summary>
    bool CanReachEveryone { get; }
```

3. In the `CanManageAsync` summary, replace "HR staff for anyone, a Manager for a direct report" with "an approver for everyone for anyone, a team approver for a direct report".
4. In the `GetUnfilteredListScope` summary, replace "everyone for HR staff, a Manager's direct reports" with "everyone for a caller who reaches everyone, a team approver's direct reports".

- [ ] **Step 5: Convert EmployeesController's in-code checks**

In `src/PeopleCore.API/Controllers/Employees/EmployeesController.cs`:

1. Add `using PeopleCore.API.Authorization;` and `using PeopleCore.Application.Common.Authorization;`.
2. Delete the `HrRoles` and `PayrollReadRoles` constants and their doc comments.
3. Replace the `IsSelfOr` and `HoldsAnyOf` methods, and their doc comment, with:

```csharp
    /// <summary>
    /// True when the caller holds any of <paramref name="permissions"/> or is themselves the employee
    /// named by <paramref name="employeeId"/>. A caller with no employee_id claim and none of the
    /// permissions matches nothing: <see cref="ICurrentUserService.EmployeeId"/> is null there, and a
    /// null never equals the route's <see cref="Guid"/>.
    /// </summary>
    private bool IsSelfOr(Guid employeeId, params string[] permissions)
        => HoldsAnyOf(permissions) || _currentUser.EmployeeId == employeeId;

    private bool HoldsAnyOf(params string[] permissions) => permissions.Any(_currentUser.HasPermission);
```

4. In the class summary, replace "the caller must either hold a privileged role or be the employee named in the route" with "the caller must either hold the permission or be the employee named in the route".
5. Replace each call as follows:

| Action | Old | New |
|---|---|---|
| `GetAll` | `HoldsAnyOf(PayrollReadRoles)` | `HoldsAnyOf(Permissions.EmployeesViewAll, Permissions.PayrollManage)` |
| `GetById` | `IsSelfOr(PayrollReadRoles, id)` | `IsSelfOr(id, Permissions.EmployeesViewAll, Permissions.PayrollManage)` |
| `GetGovernmentIds` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesViewAll)` |
| `UpsertGovernmentId` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesManage)` |
| `GetEmergencyContacts` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesViewAll)` |
| `AddEmergencyContact` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesManage)` |
| `DeleteEmergencyContact` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesManage)` |
| `GetDocuments` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesViewAll)` |
| `UploadDocument` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesManage)` |
| `GetDownloadUrl` | `IsSelfOr(HrRoles, id)` | `IsSelfOr(id, Permissions.EmployeesViewAll)` |

6. Replace `[Authorize(Roles = "Admin,HRManager")]` on `Create`, `Update`, `Deactivate` and `DeleteDocument` with `[RequirePermission(Permissions.EmployeesManage)]`.
7. In the `GetById` doc comment, replace "HR and payroll staff may read anyone's" with "callers who view all employees or run payroll may read anyone's".

- [ ] **Step 6: Build, and find any other use of the removed members**

Run: `dotnet build PeopleCore.slnx`
Expected: 0 errors. If an error names `IsInRole` or `IsHrStaff` anywhere else, convert that use with the same mapping (Admin/HRManager → `employees.view-all` or `approvals.all`, Manager → `approvals.team`, PayrollService → `payroll.manage`) and list it in your report.

- [ ] **Step 7: Run the API tests**

Run: `dotnet test tests/PeopleCore.Application.Tests`
Expected: all pass, including every existing `*AuthorizationTests` class and the three new `EmployeeAccessServiceTests`.

- [ ] **Step 8: Commit**

```bash
git add src tests/PeopleCore.Application.Tests
git commit -m "feat(auth): decide employee access by permission, not by role"
```

---

### Task 7: Every API endpoint requires a permission, with proof nothing changed

**Files:**
- Test: `tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs`
- Modify the 20 controllers in the table in Step 3.

**Interfaces:**
- Consumes: `RequirePermissionAttribute` (Task 5); `Permissions` (Task 1); `SeededRoles` (Task 2).
- Produces: no `[Authorize(Roles = ...)]` remains anywhere in `src/PeopleCore.API`.

- [ ] **Step 1: Write the failing equivalence test**

Create `tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Moving from role names to permissions must not change who can reach what. LegacyRoles records,
/// endpoint by endpoint, the roles each one admitted before permissions existed. For every seeded
/// role and every endpoint, "may reach it now" - by the seeded role's permissions against the
/// endpoint's [RequirePermission] - must equal "could reach it then".
/// </summary>
public class PermissionEquivalenceTests
{
    private const string A = "Admin";
    private const string AH = "Admin,HRManager";
    private const string AHM = "Admin,HRManager,Manager";
    private const string AHP = "Admin,HRManager,PayrollService";
    private const string AHS = "Admin,HRManager,Service";

    /// <summary>Every endpoint that carried [Authorize(Roles = ...)] before permissions. All others were open to any signed-in user, or anonymous.</summary>
    private static readonly Dictionary<string, string> LegacyRoles = new()
    {
        ["UsersController.List"] = AH, ["UsersController.AssignableRoles"] = AH, ["UsersController.EmployeeLinks"] = AH,
        ["UsersController.Get"] = AH, ["UsersController.Create"] = AH, ["UsersController.SetRoles"] = AH,
        ["UsersController.LinkEmployee"] = AH, ["UsersController.Deactivate"] = AH, ["UsersController.Reactivate"] = AH,
        ["UsersController.ResetPassword"] = AH,

        ["ExecutiveAnalyticsController.GetWorkforceSummary"] = A, ["ExecutiveAnalyticsController.GetHiringTrend"] = A,
        ["ExecutiveAnalyticsController.GetAttritionRate"] = A, ["ExecutiveAnalyticsController.GetLeaveSummary"] = A,
        ["ExecutiveAnalyticsController.GetPerformanceOverview"] = A,

        ["HRAnalyticsController.GetHeadcount"] = AH, ["HRAnalyticsController.GetTurnover"] = AH,
        ["HRAnalyticsController.GetAttendance"] = AH, ["HRAnalyticsController.GetLeaveUtilization"] = AH,
        ["HRAnalyticsController.GetOvertime"] = AH, ["HRAnalyticsController.GetRecruitmentFunnel"] = AH,
        ["HRAnalyticsController.GetPerformanceDistribution"] = AH,

        ["AttendanceController.Sync"] = AHS, ["AttendanceController.Import"] = AH,
        ["HolidaysController.Create"] = AH, ["HolidaysController.Delete"] = AH,
        ["OvertimeController.Approve"] = AHM, ["OvertimeController.Reject"] = AHM,

        ["EmployeesController.Create"] = AH, ["EmployeesController.Update"] = AH,
        ["EmployeesController.Deactivate"] = AH, ["EmployeesController.DeleteDocument"] = AH,

        ["LeaveAccrualPoliciesController.GetPoliciesAsync"] = AH, ["LeaveAccrualPoliciesController.GetRules"] = AH,
        ["LeaveAccrualPoliciesController.CreatePolicyAsync"] = AH, ["LeaveAccrualPoliciesController.UpdatePolicyAsync"] = AH,
        ["LeaveAccrualPoliciesController.DeletePolicyAsync"] = AH,
        ["LeaveAccrualsController.RunAccrualsAsync"] = A,
        ["LeaveController.CreateLeaveType"] = AH, ["LeaveController.UpdateLeaveType"] = AH, ["LeaveController.DeleteLeaveType"] = AH,
        ["LeaveController.Approve"] = AHM, ["LeaveController.Reject"] = AHM,

        ["DepartmentsController.Create"] = AH, ["DepartmentsController.Update"] = AH, ["DepartmentsController.Delete"] = A,
        ["PositionsController.Create"] = AH, ["PositionsController.Update"] = AH, ["PositionsController.Delete"] = A,
        ["TeamsController.Create"] = AH, ["TeamsController.Update"] = AH, ["TeamsController.Delete"] = A,

        ["Bir2316Controller.GetYears"] = AHP, ["Bir2316Controller.GetPreview"] = AHP,
        ["Bir2316Controller.Generate"] = AHP, ["Bir2316Controller.GenerateAll"] = AHP,
        ["EmployeeCompensationController.GetByEmployee"] = AHP, ["EmployeeCompensationController.Upsert"] = AHP,
        ["PayrollRunsController.GetPaged"] = AHP, ["PayrollRunsController.GetById"] = AHP, ["PayrollRunsController.Create"] = AHP,
        ["PayrollRunsController.Compute"] = AHP, ["PayrollRunsController.Approve"] = AHP, ["PayrollRunsController.MarkPaid"] = AHP,
        ["PayrollSettingsController.GetByCompany"] = AHP, ["PayrollSettingsController.Update"] = AHP,
        ["ReportsController.GetPayslip"] = AHP, ["ReportsController.GetRunPayslips"] = AHP,
        ["PayrollExportController.GetEmployees"] = AHP, ["PayrollExportController.GetAttendanceSummary"] = AHP,
        ["PayrollExportController.GetApprovedLeaves"] = AHP, ["PayrollExportController.GetApprovedOvertime"] = AHP,
        ["PayrollExportController.GetStatusChanges"] = AHP,

        ["PerformanceController.CreateCycle"] = AH, ["PerformanceController.CloseCycle"] = AH,
        ["PerformanceController.CreateReview"] = AHM, ["PerformanceController.SubmitManagerReview"] = AHM,

        ["RecruitmentController.CreatePosting"] = AH, ["RecruitmentController.UpdatePosting"] = AH,
        ["RecruitmentController.PublishPosting"] = AH, ["RecruitmentController.ClosePosting"] = AH,
        ["RecruitmentController.CreateApplicant"] = AH, ["RecruitmentController.UpdateApplicantStatus"] = AH,
        ["RecruitmentController.ConvertToEmployee"] = AH, ["RecruitmentController.CreateInterview"] = AH,
        ["RecruitmentController.UpdateInterview"] = AH, ["RecruitmentController.DeleteInterview"] = AH,

        ["RotatingPatternsController.GetAll"] = AH, ["RotatingPatternsController.Create"] = AH, ["RotatingPatternsController.Delete"] = AH,
        ["ShiftAssignmentsController.AssignShift"] = AH, ["ShiftAssignmentsController.RemoveAssignment"] = AH,
        ["ShiftTemplatesController.GetAll"] = AH, ["ShiftTemplatesController.Create"] = AH,
        ["ShiftTemplatesController.Update"] = AH, ["ShiftTemplatesController.Delete"] = AH,
    };

    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints() =>
        from controller in typeof(AuthController).Assembly.GetTypes()
        where typeof(ControllerBase).IsAssignableFrom(controller) && !controller.IsAbstract
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where action.GetCustomAttributes<HttpMethodAttribute>().Any()
        select (controller, action);

    private static string KeyOf(Type controller, MethodInfo action) => $"{controller.Name}.{action.Name}";

    /// <summary>Every [RequirePermission] that applies - the controller's and the action's must all pass.</summary>
    private static List<RequirePermissionAttribute> RequirementsOf(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<RequirePermissionAttribute>(inherit: true))
            .ToList();

    [Fact]
    public void NoEndpoint_AuthorizesByRoleName_AnyMore()
    {
        var byRole = Endpoints()
            .Where(e => e.Controller.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(e.Action.GetCustomAttributes<AuthorizeAttribute>(true))
                .Any(a => !string.IsNullOrEmpty(a.Roles)))
            .Select(e => KeyOf(e.Controller, e.Action))
            .ToList();

        byRole.Should().BeEmpty();
    }

    [Fact]
    public void EveryEndpointTheOldRolesGuarded_StillExists()
    {
        var present = Endpoints().Select(e => KeyOf(e.Controller, e.Action)).ToHashSet();

        LegacyRoles.Keys.Where(k => !present.Contains(k)).Should().BeEmpty("a renamed or removed action must be reflected in LegacyRoles");
    }

    [Fact]
    public void EndpointsThatWereOpenToEveryone_RequireNoPermission()
    {
        var newlyGuarded = Endpoints()
            .Where(e => !LegacyRoles.ContainsKey(KeyOf(e.Controller, e.Action)) && RequirementsOf(e.Controller, e.Action).Count > 0)
            .Select(e => KeyOf(e.Controller, e.Action))
            .ToList();

        newlyGuarded.Should().BeEmpty();
    }

    [Fact]
    public void EverySeededRole_ReachesExactlyTheEndpointsItsOldRoleReached()
    {
        var differences = new List<string>();

        foreach (var (controller, action) in Endpoints())
        {
            if (!LegacyRoles.TryGetValue(KeyOf(controller, action), out var legacy)) continue;

            var requirements = RequirementsOf(controller, action);
            foreach (var role in SeededRoles.All)
            {
                var granted = SeededRoles.PermissionsOf([role]);
                var reachesNow = requirements.Count > 0 && requirements.All(r => r.AnyOf.Any(granted.Contains));
                var reachedThen = legacy.Split(',').Contains(role);
                if (reachesNow != reachedThen)
                    differences.Add($"{role} on {KeyOf(controller, action)}: then {reachedThen}, now {reachesNow}");
            }
        }

        differences.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~PermissionEquivalenceTests"`
Expected:
- `NoEndpoint_AuthorizesByRoleName_AnyMore` FAILS, listing the unconverted endpoints.
- `EverySeededRole_...` FAILS: no endpoint has a requirement yet, so every legacy entry reads "then True, now False" for the roles it admitted.
- The other two pass.

- [ ] **Step 3: Convert the controllers**

In each file below:
1. Add `using PeopleCore.API.Authorization;` and `using PeopleCore.Application.Common.Authorization;`.
2. Replace the `[Authorize(Roles = "...")]` line exactly as the table says.
3. Leave any bare `[Authorize]` attributes as they are.

All paths are under `src/PeopleCore.API/Controllers/`. "Class" means the attribute on the controller class.

| File | Where | Replace with |
|---|---|---|
| `Account/UsersController.cs` | Class | `[RequirePermission(Permissions.UsersManage)]` |
| `Analytics/ExecutiveAnalyticsController.cs` | Class | `[RequirePermission(Permissions.AnalyticsExecutive)]` |
| `Analytics/HRAnalyticsController.cs` | Class | `[RequirePermission(Permissions.AnalyticsHr)]` |
| `Attendance/AttendanceController.cs` | `Sync` | `[RequirePermission(Permissions.AttendanceDeviceSync)]` |
| `Attendance/AttendanceController.cs` | `Import` | `[RequirePermission(Permissions.AttendanceManage)]` |
| `Attendance/HolidaysController.cs` | `Create`, `Delete` | `[RequirePermission(Permissions.AttendanceManage)]` |
| `Attendance/OvertimeController.cs` | `Approve`, `Reject` | `[RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]` |
| `Leave/LeaveAccrualPoliciesController.cs` | Class | `[RequirePermission(Permissions.LeaveManage)]` |
| `Leave/LeaveAccrualsController.cs` | `RunAccrualsAsync` | `[RequirePermission(Permissions.LeaveRunAccruals)]` |
| `Leave/LeaveController.cs` | `CreateLeaveType`, `UpdateLeaveType`, `DeleteLeaveType` | `[RequirePermission(Permissions.LeaveManage)]` |
| `Leave/LeaveController.cs` | `Approve`, `Reject` | `[RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]` |
| `Organization/DepartmentsController.cs` | `Create`, `Update` | `[RequirePermission(Permissions.OrganizationManage)]` |
| `Organization/DepartmentsController.cs` | `Delete` | `[RequirePermission(Permissions.OrganizationDelete)]` |
| `Organization/PositionsController.cs` | `Create`, `Update` | `[RequirePermission(Permissions.OrganizationManage)]` |
| `Organization/PositionsController.cs` | `Delete` | `[RequirePermission(Permissions.OrganizationDelete)]` |
| `Organization/TeamsController.cs` | `Create`, `Update` | `[RequirePermission(Permissions.OrganizationManage)]` |
| `Organization/TeamsController.cs` | `Delete` | `[RequirePermission(Permissions.OrganizationDelete)]` |
| `Payroll/Bir2316Controller.cs` | Class | `[RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/EmployeeCompensationController.cs` | Class | `[RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/PayrollRunsController.cs` | Class | `[RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/PayrollSettingsController.cs` | Class | `[RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/ReportsController.cs` | `GetPayslip`, `GetRunPayslips` | `[RequirePermission(Permissions.PayrollManage)]` |
| `PayrollExport/PayrollExportController.cs` | Class | `[RequirePermission(Permissions.PayrollManage)]` |
| `Performance/PerformanceController.cs` | `CreateCycle`, `CloseCycle` | `[RequirePermission(Permissions.PerformanceManage)]` |
| `Performance/PerformanceController.cs` | `CreateReview`, `SubmitManagerReview` | `[RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]` |
| `Recruitment/RecruitmentController.cs` | the 10 actions in `LegacyRoles` | `[RequirePermission(Permissions.RecruitmentManage)]` |
| `Scheduling/RotatingPatternsController.cs` | Class | `[RequirePermission(Permissions.SchedulingManage)]` |
| `Scheduling/ShiftAssignmentsController.cs` | `AssignShift`, `RemoveAssignment` | `[RequirePermission(Permissions.SchedulingManage)]` |
| `Scheduling/ShiftTemplatesController.cs` | Class | `[RequirePermission(Permissions.SchedulingManage)]` |

`EmployeesController` was converted in Task 6.

Doc comments that say which roles may call something need updating too: `ReportsController`'s class summary and the comments on its actions, plus any `/// <summary>` that names roles next to a converted attribute. Change them to name the permission (for example "callers who run payroll").

In `ReportsController`, where the summary says it "deliberately carries NO class-level `[Authorize(Roles = ...)]`", make it say it carries no class-level permission requirement.

- [ ] **Step 4: Check no role-name authorization is left**

Run: `grep -rn "Authorize(Roles" src/PeopleCore.API --include=*.cs`
Expected: no output.

- [ ] **Step 5: Run the equivalence test and the whole API suite**

Run: `dotnet test tests/PeopleCore.Application.Tests`
Expected: all pass, including the four `PermissionEquivalenceTests` and `ControllerAuthorizationCoverageTests`.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.API/Controllers tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs
git commit -m "feat(api): require permissions on every endpoint, proving no role's reach changed"
```

---

### Task 8: Permission checks in the web client

**Files:**
- Create: `src/PeopleCore.Web/Auth/Permissions.cs`
- Create: `src/PeopleCore.Web/Auth/RequirePermissionAttribute.cs`
- Create: `src/PeopleCore.Web/Auth/PermissionPolicyProvider.cs`
- Create: `src/PeopleCore.Web/Auth/PermissionClaimsExtensions.cs`
- Create: `src/PeopleCore.Web/Auth/PermissionView.razor`
- Modify: `src/PeopleCore.Web/Program.cs`
- Create test support: `tests/PeopleCore.Web.Tests/TestSupport/SeededPermissions.cs`
- Test: `tests/PeopleCore.Web.Tests/Auth/PermissionViewTests.cs`
- Test: `tests/PeopleCore.Web.Tests/Auth/PermissionPolicyProviderTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/WebPermissionsMirrorTests.cs`

**Interfaces:**
- Consumes: the API catalogue (Task 1), read as text by the mirror test.
- Produces (namespace `PeopleCore.Web.Auth`):
  - `static class Permissions`: the same 18 key constants plus `ClaimType`, and `IReadOnlyList<string> AllKeys`.
  - `static class PermissionPolicy`: `NameFor` and `TryParse`, with the same format as the API.
  - `sealed class RequirePermissionAttribute : AuthorizeAttribute`.
  - `class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider`.
  - `ClaimsPrincipal.HasAnyPermission(params string[] anyOf)`: true only for an authenticated user holding any of the keys.
  - Component `PermissionView` with `[Parameter] string[] AnyOf` and `[Parameter] RenderFragment? ChildContent`.
  - Test helper `SeededPermissions.Of(params string[] roles)` and `SeededPermissions.ClaimsFor(params string[] roles)`, mirroring the seeded roles.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Web.Tests/TestSupport/SeededPermissions.cs`:

```csharp
using System.Security.Claims;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.TestSupport;

/// <summary>
/// The permissions each seeded role starts with, so page and menu tests can keep speaking in roles.
/// A copy of SeededRoles in PeopleCore.Infrastructure, which the web tests cannot reference;
/// PermissionEquivalenceTests there proves those seeds reproduce the old role checks.
/// </summary>
public static class SeededPermissions
{
    private static readonly Dictionary<string, string[]> Stored = new()
    {
        ["HRManager"] =
        [
            Permissions.EmployeesViewAll, Permissions.EmployeesManage, Permissions.OrganizationManage,
            Permissions.AttendanceManage, Permissions.AttendanceDeviceSync, Permissions.LeaveManage,
            Permissions.ApprovalsAll, Permissions.PerformanceManage, Permissions.PayrollManage,
            Permissions.RecruitmentManage, Permissions.SchedulingManage, Permissions.AnalyticsHr,
            Permissions.UsersManage,
        ],
        ["Manager"] = [Permissions.ApprovalsTeam],
        ["PayrollService"] = [Permissions.PayrollManage],
        ["Service"] = [Permissions.AttendanceDeviceSync],
    };

    public static IReadOnlyList<string> Of(params string[] roles) =>
        roles.Contains("Admin")
            ? Permissions.AllKeys
            : Permissions.AllKeys.Where(k => roles.Any(r => Stored.TryGetValue(r, out var keys) && keys.Contains(k))).ToList();

    public static Claim[] ClaimsFor(params string[] roles) =>
        Of(roles).Select(p => new Claim(Permissions.ClaimType, p)).ToArray();
}
```

Create `tests/PeopleCore.Web.Tests/Auth/PermissionViewTests.cs`:

```csharp
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.Auth;

public class PermissionViewTests : BunitContext
{
    private IRenderedComponent<PermissionView> Render(params string[] anyOf) =>
        Render<PermissionView>(p => p
            .Add(x => x.AnyOf, anyOf)
            .AddChildContent("""<p id="guarded">Payroll</p>"""));

    [Fact]
    public void HoldingThePermission_ShowsTheContent()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("pay@company.test");
        auth.SetClaims(new Claim(Permissions.ClaimType, Permissions.PayrollManage));

        Render(Permissions.PayrollManage).FindAll("#guarded").Should().ContainSingle();
    }

    [Fact]
    public void HoldingAnyOfSeveral_ShowsTheContent()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("manager@company.test");
        auth.SetClaims(new Claim(Permissions.ClaimType, Permissions.ApprovalsTeam));

        Render(Permissions.ApprovalsTeam, Permissions.ApprovalsAll).FindAll("#guarded").Should().ContainSingle();
    }

    [Fact]
    public void WithoutThePermission_ShowsNothing()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("someone@company.test");
        auth.SetClaims(new Claim(Permissions.ClaimType, Permissions.ApprovalsTeam));

        Render(Permissions.PayrollManage).Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void ASignedOutVisitor_SeesNothing()
    {
        AddAuthorization().SetNotAuthorized();

        Render(Permissions.PayrollManage).Markup.Trim().Should().BeEmpty();
    }
}
```

Create `tests/PeopleCore.Web.Tests/Auth/PermissionPolicyProviderTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.Auth;

/// <summary>The same policies as the API, so a [RequirePermission] page admits exactly who the API does.</summary>
public class PermissionPolicyProviderTests
{
    private readonly IAuthorizationService _authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorizationCore()
        .AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private async Task<bool> Allowed(ClaimsPrincipal user, params string[] anyOf) =>
        (await _authorization.AuthorizeAsync(user, null, PermissionPolicy.NameFor(anyOf))).Succeeded;

    [Fact]
    public async Task APageRequiringAnyOfSeveral_AdmitsAHolderOfOne_AndNobodyElse()
    {
        var teamApprover = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.ApprovalsTeam)], "jwt"));
        var payroll = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.PayrollManage)], "jwt"));

        (await Allowed(teamApprover, Permissions.ApprovalsTeam, Permissions.ApprovalsAll)).Should().BeTrue();
        (await Allowed(payroll, Permissions.ApprovalsTeam, Permissions.ApprovalsAll)).Should().BeFalse();
    }

    [Fact]
    public void TheAttribute_NamesThePermissionPolicy()
    {
        new RequirePermissionAttribute(Permissions.UsersManage).Policy.Should().Be("permission:users.manage");
    }
}
```

Create `tests/PeopleCore.Application.Tests/Api/WebPermissionsMirrorTests.cs`:

```csharp
using System.Text.RegularExpressions;
using FluentAssertions;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The Blazor client cannot reference the Application project, so it keeps its own copy of the
/// permission keys. A key missing or misspelt there would silently hide a menu or lock a page.
/// </summary>
public class WebPermissionsMirrorTests
{
    [Fact]
    public void TheWebClientsPermissionKeys_AreTheApisExactly()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PeopleCore.slnx")))
            root = root.Parent;
        root.Should().NotBeNull("the tests run from inside the repository");

        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "PeopleCore.Web", "Auth", "Permissions.cs"));
        var webKeys = Regex.Matches(source, @"public const string \w+ = ""([a-z]+\.[a-z-]+)"";")
            .Select(m => m.Groups[1].Value)
            .ToList();

        webKeys.Should().Equal(Permissions.AllKeys);
    }
}
```

- [ ] **Step 2: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~PermissionViewTests|FullyQualifiedName~PermissionPolicyProviderTests"`
Expected: build error, `The type or namespace name 'PermissionView' could not be found`.

- [ ] **Step 3: Write the client copy and the checks**

Create `src/PeopleCore.Web/Auth/Permissions.cs`:

```csharp
namespace PeopleCore.Web.Auth;

/// <summary>
/// Client copy of the API's permission keys (PeopleCore.Application.Common.Authorization.Permissions).
/// Menus and page guards name these; the signed-in token carries the ones the account holds as
/// "permission" claims. WebPermissionsMirrorTests fails if this list drifts from the API's.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string EmployeesViewAll = "employees.view-all";
    public const string EmployeesManage = "employees.manage";
    public const string OrganizationManage = "organization.manage";
    public const string OrganizationDelete = "organization.delete";
    public const string AttendanceManage = "attendance.manage";
    public const string AttendanceDeviceSync = "attendance.device-sync";
    public const string LeaveManage = "leave.manage";
    public const string LeaveRunAccruals = "leave.run-accruals";
    public const string ApprovalsTeam = "approvals.team";
    public const string ApprovalsAll = "approvals.all";
    public const string PerformanceManage = "performance.manage";
    public const string PayrollManage = "payroll.manage";
    public const string RecruitmentManage = "recruitment.manage";
    public const string SchedulingManage = "scheduling.manage";
    public const string AnalyticsHr = "analytics.hr";
    public const string AnalyticsExecutive = "analytics.executive";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public static readonly IReadOnlyList<string> AllKeys =
    [
        EmployeesViewAll, EmployeesManage, OrganizationManage, OrganizationDelete, AttendanceManage,
        AttendanceDeviceSync, LeaveManage, LeaveRunAccruals, ApprovalsTeam, ApprovalsAll,
        PerformanceManage, PayrollManage, RecruitmentManage, SchedulingManage, AnalyticsHr,
        AnalyticsExecutive, UsersManage, RolesManage,
    ];
}

/// <summary>Policy names in the API's format: "permission:" and the keys, any of which will do.</summary>
public static class PermissionPolicy
{
    public const string Prefix = "permission:";

    public static string NameFor(params string[] anyOf)
    {
        if (anyOf.Length == 0)
            throw new ArgumentException("A permission policy needs at least one permission.", nameof(anyOf));
        return Prefix + string.Join('|', anyOf);
    }

    public static bool TryParse(string policyName, out IReadOnlyList<string> anyOf)
    {
        anyOf = [];
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var keys = policyName[Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0 || keys.Any(k => !Permissions.AllKeys.Contains(k))) return false;

        anyOf = keys;
        return true;
    }
}
```

Create `src/PeopleCore.Web/Auth/RequirePermissionAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace PeopleCore.Web.Auth;

/// <summary>Guards a page the way the API guards the endpoints behind it: any one of these permissions will do.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(params string[] anyOf) : base(PermissionPolicy.NameFor(anyOf)) { }
}
```

Create `src/PeopleCore.Web/Auth/PermissionPolicyProvider.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace PeopleCore.Web.Auth;

/// <summary>Builds "signed in and holding any of these permissions" for every permission policy name a page uses.</summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!PermissionPolicy.TryParse(policyName, out var anyOf))
            return base.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasAnyPermission([.. anyOf]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
```

Create `src/PeopleCore.Web/Auth/PermissionClaimsExtensions.cs`:

```csharp
using System.Security.Claims;

namespace PeopleCore.Web.Auth;

public static class PermissionClaimsExtensions
{
    /// <summary>True for a signed-in user whose token grants any of <paramref name="anyOf"/>.</summary>
    public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] anyOf) =>
        user.Identity?.IsAuthenticated == true && anyOf.Any(key => user.HasClaim(Permissions.ClaimType, key));
}
```

Create `src/PeopleCore.Web/Auth/PermissionView.razor`:

```razor
@*
    Shows its content only to a signed-in user holding any of AnyOf. Used for menus and buttons in
    place of AuthorizeView Roles="...": access follows permissions, which roles merely bundle.
*@

@if (_allowed)
{
    @ChildContent
}

@code {
    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter, EditorRequired] public string[] AnyOf { get; set; } = [];

    [Parameter] public RenderFragment? ChildContent { get; set; }

    private bool _allowed;

    protected override async Task OnParametersSetAsync()
    {
        var state = AuthenticationState is null ? null : await AuthenticationState;
        _allowed = state?.User.HasAnyPermission(AnyOf) ?? false;
    }
}
```

In `src/PeopleCore.Web/Program.cs`:

1. Add `using Microsoft.AspNetCore.Authorization;`.
2. Directly after `builder.Services.AddAuthorizationCore();`, add:

```csharp
// Page guards name permissions ([RequirePermission]); this turns those names into policies.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~PermissionViewTests|FullyQualifiedName~PermissionPolicyProviderTests"`
Expected: 6 passed.

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~WebPermissionsMirrorTests"`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Web/Auth src/PeopleCore.Web/Program.cs tests/PeopleCore.Web.Tests tests/PeopleCore.Application.Tests/Api/WebPermissionsMirrorTests.cs
git commit -m "feat(web): check permissions in the client, in step with the API"
```

---

### Task 9: Menus, pages and dashboards follow permissions

**Files:**
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`
- Modify: `src/PeopleCore.Web/Layout/MobileFooterNav.razor`
- Modify: `src/PeopleCore.Web/Pages/Dashboard.razor`
- Modify: `src/PeopleCore.Web/Pages/Analytics/AnalyticsDashboard.razor`
- Modify: `src/PeopleCore.Web/Pages/HR/Employees.razor`
- Modify the page guards in Step 5.
- Modify tests: `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`, `Pages/DashboardTests.cs`, `Pages/Analytics/AnalyticsDashboardTests.cs`, `Pages/HR/EmployeesTests.cs`, `Pages/Admin/UsersTests.cs`, `Pages/Management/OvertimeApprovalsTests.cs`.

**Interfaces:**
- Consumes: `Permissions`, `RequirePermissionAttribute`, `PermissionView`, `HasAnyPermission` and `SeededPermissions` (Task 8).
- Produces: no `AuthorizeView Roles=`, `Authorize(Roles` or `IsInRole` remains in `src/PeopleCore.Web`.

- [ ] **Step 1: Switch the tests to permission claims (they fail until the markup changes)**

Make these replacements, keeping the role argument each call already passes:

| File | Old | New |
|---|---|---|
| `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs` (in `RenderAs`) | `_auth.SetRoles(roles);` | `_auth.SetClaims(SeededPermissions.ClaimsFor(roles));` |
| `tests/PeopleCore.Web.Tests/Pages/DashboardTests.cs` (both theories) | `_auth.SetRoles(role);` | `_auth.SetClaims(SeededPermissions.ClaimsFor(role));` |
| `tests/PeopleCore.Web.Tests/Pages/Analytics/AnalyticsDashboardTests.cs` (in `RenderAs`) | `auth.SetRoles(role);` | `auth.SetClaims(SeededPermissions.ClaimsFor(role));` |
| `tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs` (constructor) | `_auth.SetRoles("HRManager");` | `_auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));` |
| `tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs` (compensation theory) | `_auth.SetRoles(role);` | `_auth.SetClaims(SeededPermissions.ClaimsFor(role));` |
| `tests/PeopleCore.Web.Tests/Pages/Admin/UsersTests.cs` (constructor) | `auth.SetRoles("HRManager");` | `auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));` |
| `tests/PeopleCore.Web.Tests/Pages/Management/OvertimeApprovalsTests.cs` | `_auth.SetRoles("Manager");` | `_auth.SetClaims(SeededPermissions.ClaimsFor("Manager"));` |

Add `using PeopleCore.Web.Tests.TestSupport;` to any of those files that lack it.

- If a test in these files also calls `SetClaims` for another claim, such as `employee_id`, merge both into one `SetClaims(...)` call. For example: `_auth.SetClaims([.. SeededPermissions.ClaimsFor("Manager"), new Claim("employee_id", id)])`. bUnit's `SetClaims` replaces the claims each time it's called.
- Report every such merge you make.

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~NavMenuTests|FullyQualifiedName~DashboardTests|FullyQualifiedName~AnalyticsDashboardTests|FullyQualifiedName~EmployeesTests"`
Expected: FAIL. `NavMenuTests` (manager, payroll, admin, users-page), the HR dashboard theory, the executive analytics tests and the compensation theory fail, because the markup still checks roles.

- [ ] **Step 2: Convert the desktop menu**

In `src/PeopleCore.Web/Layout/NavMenu.razor`:

1. Replace the rendering loop:

```razor
@foreach (var section in Sections)
{
    if (section.Roles is null)
    {
        @RenderSection(section)
    }
    else
    {
        <AuthorizeView Roles="@section.Roles" Context="authContext">
            <Authorized>
                @RenderSection(section)
            </Authorized>
        </AuthorizeView>
    }
}
```

with:

```razor
@foreach (var section in Sections)
{
    if (section.AnyOf is null)
    {
        @RenderSection(section)
    }
    else
    {
        <PermissionView AnyOf="@section.AnyOf">
            @RenderSection(section)
        </PermissionView>
    }
}
```

2. Replace `private sealed record NavSection(string Label, string? Roles, NavEntry[] Entries);` with:

```csharp
    /// <summary>A menu section, shown to anyone holding any of <paramref name="AnyOf"/>, or to everyone when null.</summary>
    private sealed record NavSection(string Label, string[]? AnyOf, NavEntry[] Entries);
```

3. In `Sections`, replace each role string with a permission array:

| Section | Old | New |
|---|---|---|
| Organization | `"Admin,HRManager"` | `[Permissions.OrganizationManage]` |
| Employees | `"Admin,HRManager"` | `[Permissions.EmployeesViewAll]` |
| Management | `"Admin,HRManager,Manager"` | `[Permissions.ApprovalsTeam, Permissions.ApprovalsAll]` |
| Payroll | `"Admin,HRManager,PayrollService"` | `[Permissions.PayrollManage]` |
| Recruitment | `"Admin,HRManager"` | `[Permissions.RecruitmentManage]` |
| Analytics | `"Admin,HRManager"` | `[Permissions.AnalyticsHr]` |
| Administration | `"Admin,HRManager"` | `[Permissions.UsersManage]` |

"My HR" keeps `null`.

- [ ] **Step 3: Convert the mobile menu**

In `src/PeopleCore.Web/Layout/MobileFooterNav.razor`:

1. Replace the `@foreach (var group in MoreGroups) { <AuthorizeView ...> ... </AuthorizeView> }` block with:

```razor
            @foreach (var group in MoreGroups)
            {
                <PermissionView AnyOf="@group.AnyOf">
                    <p class="px-3 pb-1 pt-2 text-xs font-medium text-muted-foreground">@group.Label</p>
                </PermissionView>
                @foreach (var link in group.Links)
                {
                    <PermissionView AnyOf="@link.AnyOf">
                        <NavLink href="@link.Href"
                                 class="block rounded-md px-3 py-2.5 text-sm text-foreground transition-colors hover:bg-accent hover:text-accent-foreground"
                                 ActiveClass="bg-accent text-accent-foreground">
                            @link.Label
                        </NavLink>
                    </PermissionView>
                }
            }
```

2. Replace the two record declarations `MoreLink` and `MoreGroup` with:

```csharp
    /// <summary>A link shown to anyone holding any of <paramref name="AnyOf"/>.</summary>
    private sealed record MoreLink(string Href, string Label, string[] AnyOf);

    /// <summary>A group whose heading shows when any of its links would.</summary>
    private sealed record MoreGroup(string Label, MoreLink[] Links)
    {
        public string[] AnyOf { get; } = Links.SelectMany(l => l.AnyOf).Distinct().ToArray();
    }
```

3. Replace `MoreGroups` with:

```csharp
    private static readonly MoreGroup[] MoreGroups =
    [
        new("Organization",
        [
            new("employees", "Employees", [Permissions.EmployeesViewAll]),
            new("departments", "Departments", [Permissions.OrganizationManage]),
            new("positions", "Positions", [Permissions.OrganizationManage]),
            new("analytics", "Analytics", [Permissions.AnalyticsHr])
        ]),
        new("Management",
        [
            new("leave-approvals", "Leave Approvals", [Permissions.ApprovalsTeam, Permissions.ApprovalsAll]),
            new("overtime-approvals", "OT Approvals", [Permissions.ApprovalsTeam, Permissions.ApprovalsAll]),
            new("performance", "Performance", [Permissions.ApprovalsTeam, Permissions.ApprovalsAll])
        ]),
        new("Payroll",
        [
            new("payroll-runs", "Payroll Runs", [Permissions.PayrollManage]),
            new("bir-2316", "BIR Form 2316", [Permissions.PayrollManage])
        ]),
        new("Recruitment",
        [
            new("job-postings", "Job Postings", [Permissions.RecruitmentManage]),
            new("applicants", "Applicants", [Permissions.RecruitmentManage])
        ]),
        new("Administration",
        [
            new("admin/users", "Users", [Permissions.UsersManage])
        ])
    ];
```

If the file's `@using Microsoft.AspNetCore.Components.Authorization` is now unused, leave it.

- [ ] **Step 4: Convert the dashboards and the Employees page**

In `src/PeopleCore.Web/Pages/Dashboard.razor`:

1. Rename the field `_isHrStaff` to `_seesEveryone` everywhere in the file (3 places).
2. Replace `_seesEveryone = new[] { "Admin", "HRManager" }.Any(authState.User.IsInRole);` with:

```csharp
        _seesEveryone = authState.User.HasAnyPermission(Permissions.EmployeesViewAll, Permissions.ApprovalsAll);
```

3. In the comment above it, replace "which the API serves only to HR staff" with "which the API serves only to callers who reach every employee".

In `src/PeopleCore.Web/Pages/Analytics/AnalyticsDashboard.razor`:

1. Rename the field `isAdmin` to `showExecutive` everywhere in the file (4 places).
2. Replace `showExecutive = authState.User.IsInRole("Admin");` with:

```csharp
        showExecutive = authState.User.HasAnyPermission(Permissions.AnalyticsExecutive);
```

3. Replace the markup comment `<!-- Executive Section (Admin only) -->` with `<!-- Executive Section (executive analytics permission) -->`.

In `src/PeopleCore.Web/Pages/HR/Employees.razor`, replace:

```razor
                                <AuthorizeView Roles="Admin,HRManager,PayrollService">
                                    <Button Variant="ghost" Size="sm" OnClick="() => GoToCompensation(emp.Id)">Compensation</Button>
                                </AuthorizeView>
```

with:

```razor
                                <PermissionView AnyOf="@(new[] { Permissions.PayrollManage })">
                                    <Button Variant="ghost" Size="sm" OnClick="() => GoToCompensation(emp.Id)">Compensation</Button>
                                </PermissionView>
```

- [ ] **Step 5: Convert the page guards**

Replace each page's `@attribute [Authorize(Roles = "...")]` line. All paths are under `src/PeopleCore.Web/Pages/`.

| Page | New line |
|---|---|
| `Admin/Users.razor` | `@attribute [RequirePermission(Permissions.UsersManage)]` |
| `Analytics/AnalyticsDashboard.razor` | `@attribute [RequirePermission(Permissions.AnalyticsHr)]` |
| `HR/Employees.razor` | `@attribute [RequirePermission(Permissions.EmployeesViewAll)]` |
| `HR/LeaveApprovals.razor` | `@attribute [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]` |
| `Management/OvertimeApprovals.razor` | `@attribute [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]` |
| `Management/Performance.razor` | `@attribute [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]` |
| `Organization/Departments.razor` | `@attribute [RequirePermission(Permissions.OrganizationManage)]` |
| `Organization/Positions.razor` | `@attribute [RequirePermission(Permissions.OrganizationManage)]` |
| `Payroll/Bir2316.razor` | `@attribute [RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/EmployeeCompensation.razor` | `@attribute [RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/PayrollRunDetail.razor` | `@attribute [RequirePermission(Permissions.PayrollManage)]` |
| `Payroll/PayrollRuns.razor` | `@attribute [RequirePermission(Permissions.PayrollManage)]` |
| `Recruitment/Applicants.razor` | `@attribute [RequirePermission(Permissions.RecruitmentManage)]` |
| `Recruitment/JobPostings.razor` | `@attribute [RequirePermission(Permissions.RecruitmentManage)]` |

`PeopleCore.Web.Auth` is already imported in `_Imports.razor`, so no `@using` is needed.

- [ ] **Step 6: Check no role-name checks are left in the client**

Run: `grep -rnE 'AuthorizeView Roles|Authorize\(Roles|IsInRole' src/PeopleCore.Web --include=*.razor --include=*.cs`
Expected: no output.

- [ ] **Step 7: Run the web tests**

Run: `dotnet test tests/PeopleCore.Web.Tests`
Expected: all pass. The navigation tests keep their assertions unchanged, including Admin's 17 links and the Users page shown only to Admin and HR.

- [ ] **Step 8: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): show menus and guard pages by permission"
```

---

### Task 10: Whole-branch verification

**Files:**
- None, unless a check fails.

**Interfaces:**
- Consumes: everything above.
- Produces: evidence.

- [ ] **Step 1: Build and run every suite**

```bash
dotnet build PeopleCore.slnx
dotnet test tests/PeopleCore.Application.Tests
dotnet test tests/PeopleCore.Infrastructure.Tests
dotnet test tests/PeopleCore.Web.Tests
```

Expected: 0 warnings, 0 errors, and every test passes. Record the counts.

- [ ] **Step 2: Confirm no role-name authorization remains anywhere**

Run: `grep -rnE 'Authorize\(Roles|AuthorizeView Roles|IsInRole' src --include=*.cs --include=*.razor`
Expected: no output.

- [ ] **Step 3: Smoke-test against a throwaway database**

Start the API against a fresh database, never the developer's `peoplecore` one. Use `preview_start` with a launch entry that runs:

```
dotnet run --project src/PeopleCore.API/PeopleCore.API.csproj --launch-profile http -- --ConnectionStrings:Default=Host=localhost;Database=peoplecore_permissions_smoke;Username=postgres;Password=postgres --Seed:AdminPassword=SmokeAdmin2026
```

Do not commit that launch entry.

Then, over HTTP:
1. **Admin sign-in:** sign in as `admin@peoplecore.local`. Decode the token: it must hold all 18 `permission` claims and `perm_v` = `1`.
2. **Seeded HR account:** create an `HRManager` account through `POST api/users` and sign it in with the temporary password. Call `POST api/auth/change-password`, since a temporary password allows nothing else, and use the fresh token it returns for the checks below. Do the same for the Manager account in item 3.
   - Its token holds exactly the 13 seeded HRManager permissions.
   - `GET api/users` → 200.
   - `GET api/analytics/executive/workforce-summary?from=2026-01-01&to=2026-09-01` → 403.
   - `DELETE api/departments/{new guid}` → 403, not 404, because the permission check comes first.
3. **Seeded Manager account:** create a `Manager` account.
   - Its token holds only `approvals.team`.
   - `GET api/payroll-runs` → 403.
   - `GET api/leave-requests` (unfiltered) → 200 with an empty list, since it has no direct reports.
4. **Old token:** a token with `perm_v` removed can't be forged, so instead confirm with `AccountTokenValidatorTests` that pre-permissions tokens are rejected. Say so in the report rather than skipping silently.
5. **Seeded role data:** `SELECT r.name, count(c.id) FROM "AspNetRoles" r LEFT JOIN "AspNetRoleClaims" c ON c.role_id = r.id AND c.claim_type = 'permission' GROUP BY r.name ORDER BY r.name;` must show Admin 0, Employee 0, HRManager 13, Manager 1, PayrollService 1, Service 1.

Stop the server afterwards.

- [ ] **Step 4: Confirm the tree is clean**

Run: `git status --short`
Expected: empty.

---

## Spec coverage (phase 1)

| Spec requirement | Task |
|---|---|
| Permission catalogue (keys, groups, labels, descriptions) | 1 |
| `ApplicationRole` with `Description`, `IsSystem`; permissions as role claims | 2 |
| System roles Admin, Employee, Service; Admin's permissions computed | 2, 3 |
| Seeding reproduces today's access, never overwrites edited roles | 2 (with deviation 1) |
| Token carries `permission` claims and `perm_v`; old tokens rejected | 4 |
| `[RequirePermission]` + `PermissionPolicyProvider`, still `IAuthorizeData` | 5 |
| `ICurrentUserService.HasPermission`; `IsInRole` no longer used | 6 |
| `EmployeeAccessService` scoping by permission, `CanReachEveryone` | 6 |
| `EmployeesController` in-method checks | 6 |
| Every endpoint mapped; equivalence per seeded role | 7 |
| Web client copy, attribute, provider, catalogue equality test | 8 |
| Menus, page guards, dashboards by permission | 9 |
| No visible change; everyone signs in once | 7, 9, 10 |
| Out of this phase: Roles page and API, new granting rule, stamp rotation on role edit | phase 2 plan |
