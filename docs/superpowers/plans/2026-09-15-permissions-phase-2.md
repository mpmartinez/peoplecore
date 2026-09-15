# Permissions Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin add, rename, edit and delete roles and choose each role's permissions. Replace the "privileged roles" rule for assigning roles with "nobody grants beyond their own powers".

**Architecture:**
- **Infrastructure:** an `IRoleCatalog` reads every role with its effective permissions and holder count. An `IRoleEditor` writes roles, and in the same database save it replaces the security stamp of every holder when a role's permissions change.
- **Policies:** two pure policies decide who may do what. `AccountManagementPolicy` is rewritten for accounts, and a new `RoleManagementPolicy` covers roles. Both take the catalogue as plain values.
- **API and pages:** `UsersController` asks the catalogue instead of a fixed role list, and a new `RolesController` serves `/admin/roles`. On the Users page, roles the signed-in user can't grant show as disabled, with the reason.
- **Seeder:** `RoleSeeder` stops recreating deleted roles.

**Tech Stack:** .NET 10, ASP.NET Core Identity + JWT, EF Core 10 / Npgsql (snake_case), Blazor WebAssembly, xUnit + Moq + FluentAssertions, bUnit 2.10.

**Spec:** `docs/superpowers/specs/2026-09-14-custom-roles-permissions-design.md` (sections "Granting (phase 2)", "Roles API (phase 2)", "Roles page (phase 2)")

**Builds on:** phase 1, merged at `e978000`.

## Global Constraints

- **The granting rule.** A caller with `users.manage`:
  - may grant or remove role R only if their permissions include all of R's, and R is not `Admin` unless they hold `Admin`;
  - may change an account at all (roles, employee link, deactivate, reactivate, reset password) only if their permissions include all of that account's effective permissions, and the account does not hold `Admin` unless they do.
- **Effective permissions** of an account = the union of its roles' permissions. An account holding `Admin` has every permission.
- **Guards that stay:**
  - no self-deactivation and no self-reset;
  - the last active `Admin` can't lose `Admin` or be deactivated;
  - a caller can't make a change that leaves them without `users.manage`.
- **Role assignment rules:**
  - `Employee` is on every account and can't be removed.
  - `Service` is never assignable.
  - Accounts send roles by name.
- **System roles** `Admin`, `Employee` and `Service` can't be renamed, edited or deleted.
- **Roles API rules:**
  - Every endpoint requires `roles.manage`.
  - A role name is trimmed, 1–50 characters, and unique (case-insensitive).
  - A description is trimmed, at most 200 characters, and empty becomes null.
  - Permission keys must be in the catalogue.
  - A caller may only put permissions they hold on a role, and may only edit or delete a role whose permissions they hold.
  - Deleting a role that accounts hold is refused with: "12 accounts still have Recruiter — remove it from them first." Use "1 account still has" for one.
- **Token revocation:** changing a role's permissions replaces the security stamp of every account holding it, in the same database save. A rename or description change doesn't.
- **Assignable roles:** `GET api/users/assignable-roles` returns `[{ name, grantable, reason }]` for every role except `Service`.
- **Seeder:** `RoleSeeder` always ensures the system roles exist. It creates the ordinary seeded roles (HRManager, Manager, PayrollService) only when `AspNetRoles` has no rows at all.
- **Refusals** are 403 `ProblemDetails` whose `Detail` is the policy's sentence. Validation failures are 400 `ProblemDetails`. Pages show `Detail` as it is.
- **House style:** comments explain *why*; test names read as sentences.
- **Test commands:** `dotnet test tests/PeopleCore.Application.Tests`, `dotnet test tests/PeopleCore.Infrastructure.Tests` (needs the local `m2net-postgres` container), and `dotnet test tests/PeopleCore.Web.Tests`.

## Decisions made while planning

1. **The self-lockout guard is about `users.manage` and `roles.manage`, not role names.** The old rule said "can't remove your own Admin or HR Manager role". Once roles are data, what matters is the power, so the new rule is: no change may leave you without `users.manage` (on the Users page) or `roles.manage` (on the Roles page). Admins always keep every permission, so the guard can't trip for them.
2. **The role editor replaces holders' security stamps directly in its own save,** instead of calling `UserManager.UpdateSecurityStampAsync` once per holder. That keeps the permission change and the revocation atomic: either both happen or neither does.
3. **The policies take the catalogue as `IReadOnlyList<RoleGrant>`** (name plus effective permissions, in catalogue order). They stay free of Identity and the database, like phase 1's policy.
4. **`AccountRoles` is deleted.** Its constants are the same as `SeededRoles`'s, and its assignable and privileged lists are exactly what this phase replaces.
5. **Catalogue order is:** system roles first (`Admin`, `Employee`, `Service`), then the rest by name. Tick-boxes and the Roles page use this order.
6. **Behaviour change the owner accepted:** an HR Manager can now grant the HR Manager role and manage other HR Managers' accounts. HR still can't touch an Admin.

## File Map

**Infrastructure** (`src/PeopleCore.Infrastructure/Identity`)
- Modify `RoleSeeder.cs`.
- Create `IRoleCatalog.cs` and `RoleCatalog.cs`: `RoleRecord`, reads.
- Create `IRoleEditor.cs` and `RoleEditor.cs`: writes plus stamp rotation.

**API** (`src/PeopleCore.API`)
- Replace `Accounts/AccountManagementPolicy.cs`; delete `Accounts/AccountRoles.cs`.
- Create `Accounts/RoleManagementPolicy.cs`.
- Modify `Controllers/Account/UsersController.cs`.
- Create `Controllers/Account/RolesController.cs`.
- Modify `Extensions/ServiceExtensions.cs` (DI).

**Application** (`src/PeopleCore.Application`)
- Modify `Common/Authorization/Permissions.cs`: one description.

**Web** (`src/PeopleCore.Web`)
- Modify `Services/ApiClient.cs`, `Components/Accounts/RoleCheckboxes.razor`, `Components/Accounts/CreateAccountDialog.razor`, `Pages/Admin/Users.razor`, `Layout/NavMenu.razor`, `Layout/MobileFooterNav.razor`.
- Create `Pages/Admin/Roles.razor` and `Components/Accounts/RoleEditorDialog.razor`.

**Tests**
- Infrastructure: modify `RoleSeedingTests.cs`; create `RoleCatalogTests.cs` and `RoleEditorTests.cs`.
- Application: replace `Api/AccountManagementPolicyTests.cs`; create `Api/RoleManagementPolicyTests.cs` and `Api/RolesControllerTests.cs`. Modify `Api/UsersControllerTestBase.cs`, `Api/UsersControllerReadTests.cs`, `Api/UsersControllerChangeTests.cs`, `Api/PermissionEquivalenceTests.cs` and `Api/TokenToPolicyTests.cs`.
- Web: create `Pages/Admin/RolesTests.cs`; modify `Pages/Admin/UsersTests.cs`, `Pages/HR/EmployeesTests.cs`, `Layout/NavMenuTests.cs` and `Services/ApiClientAccountsTests.cs`.

---

### Task 1: The seeder stops recreating deleted roles

**Files:**
- Modify: `src/PeopleCore.Infrastructure/Identity/RoleSeeder.cs`
- Modify: `src/PeopleCore.Application/Common/Authorization/Permissions.cs` (the `ApprovalsAll` description)
- Modify test: `tests/PeopleCore.Infrastructure.Tests/RoleSeedingTests.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Api/TokenToPolicyTests.cs`

**Interfaces:**
- Consumes: `SeededRoles` and `ApplicationRole` (phase 1).
- Produces: `RoleSeeder.SeedAsync` behaves as follows.
  - The system roles (`Admin`, `Employee`, `Service`) are created whenever they're missing.
  - The ordinary seeded roles are created only when `AspNetRoles` had no rows before seeding started.
  - Existing roles are never touched.

- [ ] **Step 1: Write the failing tests**

In `tests/PeopleCore.Infrastructure.Tests/RoleSeedingTests.cs`, replace the test `TheSeeder_NeverTouchesARoleThatAlreadyExists` with these three:

```csharp
    [Fact]
    public async Task TheSeeder_NeverTouchesARoleThatAlreadyExists()
    {
        // An admin who has emptied Manager's permissions must find it still empty after a restart.
        Context.Roles.Add(new ApplicationRole { Name = "Manager", NormalizedName = "MANAGER" });
        await Context.SaveChangesAsync();

        await new RoleSeeder(Context).SeedAsync();

        (await StoredPermissionsByRoleAsync()).Should().NotContainKey("Manager");
    }

    [Fact]
    public async Task ADeletedOrdinaryRole_IsNotRecreated_OnTheNextStartup()
    {
        // Once roles can be deleted, recreating HRManager on restart would hand its 13 permissions -
        // users.manage among them - back to a role an admin deliberately removed.
        await new RoleSeeder(Context).SeedAsync();
        await using (var delete = NewContext())
        {
            var hr = await delete.Roles.SingleAsync(r => r.NormalizedName == "HRMANAGER");
            delete.RoleClaims.RemoveRange(delete.RoleClaims.Where(c => c.RoleId == hr.Id));
            delete.Roles.Remove(hr);
            await delete.SaveChangesAsync();
        }

        await new RoleSeeder(NewContext()).SeedAsync();

        await using var read = NewContext();
        (await read.Roles.AnyAsync(r => r.NormalizedName == "HRMANAGER")).Should().BeFalse();
    }

    [Fact]
    public async Task AMissingSystemRole_IsAlwaysRestored()
    {
        // The app relies on Admin, Employee and Service by name; they are not an admin's to remove.
        Context.Roles.Add(new ApplicationRole { Name = "Recruiter", NormalizedName = "RECRUITER" });
        await Context.SaveChangesAsync();

        await new RoleSeeder(Context).SeedAsync();

        await using var read = NewContext();
        var names = await read.Roles.Select(r => r.Name).ToListAsync();
        names.Should().Contain(["Admin", "Employee", "Service", "Recruiter"]);
        names.Should().NotContain(["HRManager", "Manager", "PayrollService"], "ordinary roles are seeded only into an empty table");
        (await read.Roles.Where(r => r.IsSystem).Select(r => r.Name).ToListAsync()).Should().BeEquivalentTo("Admin", "Employee", "Service");
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RoleSeedingTests"`
Expected: `ADeletedOrdinaryRole_IsNotRecreated_OnTheNextStartup` and `AMissingSystemRole_IsAlwaysRestored` FAIL, because HRManager is recreated.

- [ ] **Step 3: Seed ordinary roles only into an empty table**

Replace the class body of `RoleSeeder` (keep the usings), including its summary, with:

```csharp
/// <summary>
/// Makes sure the roles PeopleCore relies on exist. The system roles - Admin, Employee, Service - are
/// restored whenever they are missing, because the app refers to them by name. The ordinary seeded
/// roles (HRManager, Manager, PayrollService) are created only into an empty roles table, i.e. on a
/// brand-new database: once roles are editable, a missing ordinary role is one an admin deleted, and
/// bringing it back on restart would undo that. A role that exists is never touched. Existing
/// databases got their permissions once, from the AddRolePermissions migration.
/// </summary>
public class RoleSeeder
{
    private readonly AppDbContext _db;

    public RoleSeeder(AppDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var freshDatabase = !await _db.Roles.AnyAsync(ct);
        var toEnsure = freshDatabase ? SeededRoles.All : SeededRoles.System;

        foreach (var name in toEnsure)
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

- [ ] **Step 4: Two small carry-overs from the phase 1 review**

In `src/PeopleCore.Application/Common/Authorization/Permissions.cs`, replace the `ApprovalsAll` description string with:

```csharp
            "Decide anyone's leave, write anyone's performance review, and see everyone's attendance, leave, overtime and schedules. Overtime itself is decided by each employee's own manager."
```

In `tests/PeopleCore.Application.Tests/Api/TokenToPolicyTests.cs`, replace `new JsonWebTokenHandler()` with `new JsonWebTokenHandler { MapInboundClaims = true }`. This matches JwtBearer's default, which maps inbound claims, so the test runs the same way the API does.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RoleSeedingTests"`
Expected: all pass. That includes the unchanged fresh-database test and the migration SQL test.

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~TokenToPolicyTests|FullyQualifiedName~PermissionsCatalogueTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Infrastructure/Identity/RoleSeeder.cs src/PeopleCore.Application/Common/Authorization/Permissions.cs tests/PeopleCore.Infrastructure.Tests/RoleSeedingTests.cs tests/PeopleCore.Application.Tests/Api/TokenToPolicyTests.cs
git commit -m "fix(identity): never recreate a role an admin deleted"
```

---

### Task 2: Reading and writing roles

**Files:**
- Create: `src/PeopleCore.Infrastructure/Identity/IRoleCatalog.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/RoleCatalog.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/IRoleEditor.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/RoleEditor.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (DI)
- Test: `tests/PeopleCore.Infrastructure.Tests/RoleCatalogTests.cs`
- Test: `tests/PeopleCore.Infrastructure.Tests/RoleEditorTests.cs`

**Interfaces:**
- Consumes: `Permissions` (phase 1); `ApplicationRole`, `SeededRoles` and `RoleSeeder` (phase 1 and Task 1).
- Produces (namespace `PeopleCore.Infrastructure.Identity`):
  - `sealed record RoleRecord(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount)`. `Permissions` holds the role's effective permissions in catalogue order, which for `Admin` is every permission.
  - `interface IRoleCatalog`:
    - `Task<IReadOnlyList<RoleRecord>> GetRolesAsync(CancellationToken ct = default)`, ordered with system roles first (Admin, Employee, Service), then the rest by name.
    - `Task<RoleRecord?> GetAsync(string roleId, CancellationToken ct = default)`.
  - `interface IRoleEditor`:
    - `Task<string> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)` returns the new role's id.
    - `Task<int> UpdateAsync(string roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)` returns how many accounts had their security stamp replaced. That count is 0 when the permissions didn't change.
    - `Task DeleteAsync(string roleId, CancellationToken ct = default)`.
    - `Task<bool> NameTakenAsync(string name, string? exceptRoleId, CancellationToken ct = default)`.
  - Both are registered scoped.

- [ ] **Step 1: Write the failing catalogue tests**

Create `tests/PeopleCore.Infrastructure.Tests/RoleCatalogTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>Every role as the Roles page and the granting rules see it, read from Postgres.</summary>
public class RoleCatalogTests : DatabaseTestBase
{
    public RoleCatalogTests(PostgresFixture fixture) : base(fixture) { }

    private RoleCatalog Catalog() => new(NewContext());

    private async Task<ApplicationUser> AccountWithAsync(params string[] roleNames)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var user = new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        Context.Users.Add(user);
        foreach (var name in roleNames)
        {
            var role = Context.Roles.Single(r => r.Name == name);
            Context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        }
        await Context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Roles_ListSystemRolesFirst_ThenTheRestByName()
    {
        await new RoleSeeder(Context).SeedAsync();
        Context.Roles.Add(new ApplicationRole { Name = "Auditor", NormalizedName = "AUDITOR" });
        await Context.SaveChangesAsync();

        (await Catalog().GetRolesAsync()).Select(r => r.Name)
            .Should().Equal("Admin", "Employee", "Service", "Auditor", "HRManager", "Manager", "PayrollService");
    }

    [Fact]
    public async Task EachRole_CarriesItsEffectivePermissions_WithAdminHoldingEverything()
    {
        await new RoleSeeder(Context).SeedAsync();

        var roles = (await Catalog().GetRolesAsync()).ToDictionary(r => r.Name);

        roles["Admin"].Permissions.Should().Equal(Permissions.AllKeys);
        roles["Manager"].Permissions.Should().Equal(Permissions.ApprovalsTeam);
        roles["Employee"].Permissions.Should().BeEmpty();
        roles["HRManager"].Permissions.Should().Equal(SeededRoles.PermissionsOf(["HRManager"]));
        roles["Admin"].IsSystem.Should().BeTrue();
        roles["HRManager"].Description.Should().Be(SeededRoles.Descriptions["HRManager"]);
    }

    [Fact]
    public async Task EachRole_CountsTheAccountsHoldingIt()
    {
        await new RoleSeeder(Context).SeedAsync();
        await AccountWithAsync("Employee", "Manager");
        await AccountWithAsync("Employee");

        var roles = (await Catalog().GetRolesAsync()).ToDictionary(r => r.Name);

        roles["Employee"].AccountCount.Should().Be(2);
        roles["Manager"].AccountCount.Should().Be(1);
        roles["HRManager"].AccountCount.Should().Be(0);
    }

    [Fact]
    public async Task OneRole_IsFoundById_OrNull()
    {
        await new RoleSeeder(Context).SeedAsync();
        var manager = Context.Roles.Single(r => r.Name == "Manager");

        (await Catalog().GetAsync(manager.Id))!.Name.Should().Be("Manager");
        (await Catalog().GetAsync("no-such-role")).Should().BeNull();
    }
}
```

- [ ] **Step 2: Write the failing editor tests**

Create `tests/PeopleCore.Infrastructure.Tests/RoleEditorTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run them and watch them fail to compile**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RoleCatalogTests|FullyQualifiedName~RoleEditorTests"`
Expected: build error: `The type or namespace name 'RoleCatalog' could not be found`.

- [ ] **Step 4: Write the catalogue**

Create `src/PeopleCore.Infrastructure/Identity/IRoleCatalog.cs`:

```csharp
namespace PeopleCore.Infrastructure.Identity;

/// <summary>A role as the Roles page and the granting rules see it.</summary>
/// <param name="Permissions">What the role allows, in catalogue order - every permission for Admin.</param>
/// <param name="AccountCount">How many accounts hold the role.</param>
public sealed record RoleRecord(
    string Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int AccountCount);

public interface IRoleCatalog
{
    /// <summary>Every role: system roles first (Admin, Employee, Service), then the rest by name.</summary>
    Task<IReadOnlyList<RoleRecord>> GetRolesAsync(CancellationToken ct = default);

    Task<RoleRecord?> GetAsync(string roleId, CancellationToken ct = default);
}
```

Create `src/PeopleCore.Infrastructure/Identity/RoleCatalog.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IRoleCatalog"/>
public class RoleCatalog : IRoleCatalog
{
    private readonly AppDbContext _db;

    public RoleCatalog(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleRecord>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.AsNoTracking().ToListAsync(ct);
        var records = await ToRecordsAsync(roles, ct);

        return records
            .OrderBy(r => r.IsSystem ? SeededRoles.System.ToList().IndexOf(r.Name) : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RoleRecord?> GetAsync(string roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == roleId, ct);
        return role is null ? null : (await ToRecordsAsync([role], ct))[0];
    }

    // Two batched queries for all the roles - their permission claims and their holder counts.
    private async Task<IReadOnlyList<RoleRecord>> ToRecordsAsync(IReadOnlyList<ApplicationRole> roles, CancellationToken ct)
    {
        var roleIds = roles.Select(r => r.Id).ToList();

        var claims = await _db.RoleClaims.AsNoTracking()
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == Permissions.ClaimType)
            .Select(c => new { c.RoleId, c.ClaimValue })
            .ToListAsync(ct);

        var counts = await _db.UserRoles.AsNoTracking()
            .Where(ur => roleIds.Contains(ur.RoleId))
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        return roles.Select(role =>
        {
            // Admin's permissions are a rule, not data: nothing is stored for it.
            var permissions = role.Name == SeededRoles.Admin
                ? Permissions.AllKeys
                : Permissions.AllKeys.Where(key => claims.Any(c => c.RoleId == role.Id && c.ClaimValue == key)).ToList();

            return new RoleRecord(role.Id, role.Name!, role.Description, role.IsSystem, permissions, counts.GetValueOrDefault(role.Id));
        }).ToList();
    }
}
```

- [ ] **Step 5: Write the editor**

Create `src/PeopleCore.Infrastructure/Identity/IRoleEditor.cs`:

```csharp
namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// Writes roles. Deliberately no rules here - who may change what is RoleManagementPolicy's call,
/// made before these are reached.
/// </summary>
public interface IRoleEditor
{
    /// <returns>The new role's id.</returns>
    Task<string> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default);

    /// <returns>
    /// How many accounts had their security stamp replaced: every holder when the permissions changed,
    /// none when only the name or description did.
    /// </returns>
    Task<int> UpdateAsync(string roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default);

    Task DeleteAsync(string roleId, CancellationToken ct = default);

    /// <summary>True when another role already has <paramref name="name"/>, ignoring case.</summary>
    Task<bool> NameTakenAsync(string name, string? exceptRoleId, CancellationToken ct = default);
}
```

Create `src/PeopleCore.Infrastructure/Identity/RoleEditor.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IRoleEditor"/>
public class RoleEditor : IRoleEditor
{
    private readonly AppDbContext _db;

    public RoleEditor(AppDbContext db) => _db = db;

    public async Task<string> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var role = new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant(), Description = description };
        _db.Roles.Add(role);
        AddPermissions(role.Id, permissions);
        await _db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task<int> UpdateAsync(string roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var role = await _db.Roles.SingleAsync(r => r.Id == roleId, ct);
        role.Name = name;
        role.NormalizedName = name.ToUpperInvariant();
        role.Description = description;

        var stored = await _db.RoleClaims.Where(c => c.RoleId == roleId && c.ClaimType == Permissions.ClaimType).ToListAsync(ct);
        var unchanged = stored.Select(c => c.ClaimValue!).ToHashSet().SetEquals(permissions);

        var signedOut = 0;
        if (!unchanged)
        {
            _db.RoleClaims.RemoveRange(stored);
            AddPermissions(roleId, permissions);

            // Every holder's token still lists the old permissions. Replacing the stamps in this same
            // save means the permission change and the revocation land together or not at all.
            var holderIds = _db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
            var holders = await _db.Users.Where(u => holderIds.Contains(u.Id)).ToListAsync(ct);
            foreach (var holder in holders)
            {
                holder.SecurityStamp = Guid.NewGuid().ToString("N");
                holder.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
            signedOut = holders.Count;
        }

        await _db.SaveChangesAsync(ct);
        return signedOut;
    }

    public async Task DeleteAsync(string roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.SingleAsync(r => r.Id == roleId, ct);
        _db.RoleClaims.RemoveRange(await _db.RoleClaims.Where(c => c.RoleId == roleId).ToListAsync(ct));
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> NameTakenAsync(string name, string? exceptRoleId, CancellationToken ct = default)
    {
        var normalized = name.ToUpperInvariant();
        return _db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != exceptRoleId, ct);
    }

    private void AddPermissions(string roleId, IEnumerable<string> permissions)
    {
        foreach (var key in permissions.Distinct())
            _db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = roleId, ClaimType = Permissions.ClaimType, ClaimValue = key });
    }
}
```

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, directly after `services.AddScoped<IRolePermissionReader, RolePermissionReader>();`, add:

```csharp
        services.AddScoped<IRoleCatalog, RoleCatalog>();
        services.AddScoped<IRoleEditor, RoleEditor>();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~RoleCatalogTests|FullyQualifiedName~RoleEditorTests"`
Expected: 9 passed.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Infrastructure/Identity src/PeopleCore.API/Extensions/ServiceExtensions.cs tests/PeopleCore.Infrastructure.Tests/RoleCatalogTests.cs tests/PeopleCore.Infrastructure.Tests/RoleEditorTests.cs
git commit -m "feat(identity): read and write roles, revoking holders' tokens when permissions change"
```

---

### Task 3: The granting rule — no granting beyond your own powers, on the Users API

**Files:**
- Replace: `src/PeopleCore.API/Accounts/AccountManagementPolicy.cs`
- Delete: `src/PeopleCore.API/Accounts/AccountRoles.cs`
- Replace test: `tests/PeopleCore.Application.Tests/Api/AccountManagementPolicyTests.cs`
- Modify: `src/PeopleCore.API/Controllers/Account/UsersController.cs`
- Modify tests: `tests/PeopleCore.Application.Tests/Api/UsersControllerTestBase.cs`, `UsersControllerReadTests.cs`, `UsersControllerChangeTests.cs`

**Interfaces:**
- Consumes: `SeededRoles` (Admin, Employee, Service names) and `Permissions.UsersManage`.
- Produces (namespace `PeopleCore.API.Accounts`):
  - `sealed record RoleGrant(string Name, IReadOnlyList<string> Permissions)`, one role and its effective permissions.
  - `sealed record AccountActor(string UserId, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions)`, with `bool IsAdmin`.
  - `sealed record AccountTarget(string UserId, IReadOnlyCollection<string> Roles, bool IsActive)`, with `bool IsAdmin`.
  - `readonly record struct PolicyDecision(bool Allowed, string? Reason)`, with `Allow` and `Deny(string)`. It's unchanged.
  - `sealed record AssignableRole(string Name, bool Grantable, string? Reason)`.
  - `static class AccountManagementPolicy`. Each method takes `IReadOnlyList<RoleGrant> catalog`:
    - `IReadOnlyList<string> PermissionsOf(IEnumerable<string> roles, IReadOnlyList<RoleGrant> catalog)`
    - `PolicyDecision CanGrant(AccountActor actor, string role, IReadOnlyList<RoleGrant> catalog)`
    - `IReadOnlyList<AssignableRole> AssignableRoles(AccountActor actor, IReadOnlyList<RoleGrant> catalog)`
    - `PolicyDecision CanManage(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog)`
    - `PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> roles, IReadOnlyList<RoleGrant> catalog)`
    - `PolicyDecision CanSetRoles(AccountActor actor, AccountTarget target, IReadOnlyCollection<string> rolesAfter, IReadOnlyList<RoleGrant> catalog, int activeAdmins)`
    - `PolicyDecision CanLinkEmployee(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog)`
    - `PolicyDecision CanDeactivate(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog, int activeAdmins)`
    - `PolicyDecision CanReactivate(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog)`
    - `PolicyDecision CanResetPassword(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog)`

  - `UsersController(UserManager<ApplicationUser>, IUserAccountDirectory, IEmployeeRepository, IRoleCatalog)`. `GET api/users/assignable-roles` now returns `AssignableRoleDto(string Name, bool Grantable, string? Reason)[]`.

- [ ] **Step 1: Write the failing tests**

Replace the whole of `tests/PeopleCore.Application.Tests/Api/AccountManagementPolicyTests.cs` with:

```csharp
using FluentAssertions;
using PeopleCore.API.Accounts;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Who may change which account, now that roles are data. The rule: nobody grants, removes or
/// manages beyond the permissions they hold themselves, and only an Admin touches Admin. The guards
/// against locking yourself or the organisation out still apply.
/// </summary>
public class AccountManagementPolicyTests
{
    private const string AdminId = "admin-1";
    private const string HrId = "hr-1";
    private const string OtherId = "other-1";

    private static readonly string[] HrPermissions =
    [
        Permissions.EmployeesViewAll, Permissions.EmployeesManage, Permissions.OrganizationManage,
        Permissions.AttendanceManage, Permissions.AttendanceDeviceSync, Permissions.LeaveManage,
        Permissions.ApprovalsAll, Permissions.PerformanceManage, Permissions.PayrollManage,
        Permissions.RecruitmentManage, Permissions.SchedulingManage, Permissions.AnalyticsHr,
        Permissions.UsersManage,
    ];

    private static readonly IReadOnlyList<RoleGrant> Catalog =
    [
        new("Admin", Permissions.AllKeys),
        new("Employee", []),
        new("Service", [Permissions.AttendanceDeviceSync]),
        new("Auditor", [Permissions.AnalyticsExecutive]),
        new("HRManager", HrPermissions),
        new("Manager", [Permissions.ApprovalsTeam]),
        new("PayrollService", [Permissions.PayrollManage]),
    ];

    private static readonly AccountActor Admin = new(AdminId, ["Admin", "Employee"], Permissions.AllKeys);
    private static readonly AccountActor Hr = new(HrId, ["HRManager", "Employee"], HrPermissions);

    private static AccountTarget Account(params string[] roles) => new(OtherId, ["Employee", .. roles], IsActive: true);

    private static AccountTarget Self(AccountActor actor) => new(actor.UserId, actor.Roles, IsActive: true);

    // --- Effective permissions and assignable roles -------------------------------------------

    [Fact]
    public void AnAccountsPermissions_AreTheUnionOfItsRoles_AndEverythingForAdmin()
    {
        AccountManagementPolicy.PermissionsOf(["Manager", "PayrollService"], Catalog)
            .Should().BeEquivalentTo(Permissions.ApprovalsTeam, Permissions.PayrollManage);
        AccountManagementPolicy.PermissionsOf(["Employee", "Admin"], Catalog).Should().BeEquivalentTo(Permissions.AllKeys);
    }

    [Fact]
    public void AssignableRoles_ListEveryRoleButService_InCatalogueOrder_SayingWhichHrMayGrant()
    {
        var roles = AccountManagementPolicy.AssignableRoles(Hr, Catalog);

        roles.Select(r => r.Name).Should().Equal("Admin", "Employee", "Auditor", "HRManager", "Manager", "PayrollService");
        roles.Where(r => r.Grantable).Select(r => r.Name).Should().Equal("Employee", "HRManager", "Manager", "PayrollService");
        roles.Single(r => r.Name == "Admin").Reason.Should().Be("Only an administrator can grant or remove the Admin role.");
        roles.Single(r => r.Name == "Auditor").Reason.Should().Be("You can't grant or remove the Auditor role: it allows things you can't do yourself.");
    }

    [Fact]
    public void AnAdmin_MayGrantEveryAssignableRole()
    {
        AccountManagementPolicy.AssignableRoles(Admin, Catalog).Should().OnlyContain(r => r.Grantable);
    }

    [Fact]
    public void NobodyMayGrantTheServiceRole()
    {
        AccountManagementPolicy.CanGrant(Admin, "Service", Catalog).Reason
            .Should().Be("The Service role is for attendance devices and can't be assigned.");
    }

    // --- Create -------------------------------------------------------------------------------

    [Fact]
    public void HR_MayCreateAnHrManager_BecauseItGrantsNothingHrLacks()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "HRManager"], Catalog).Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotCreateAnAccountWithARoleThatDoesMoreThanHr()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Auditor"], Catalog).Reason
            .Should().Be("You can't grant or remove the Auditor role: it allows things you can't do yourself.");
    }

    [Fact]
    public void HR_MayNotCreateAnAdmin()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Admin"], Catalog).Reason
            .Should().Be("Only an administrator can grant or remove the Admin role.");
    }

    // --- Manage -------------------------------------------------------------------------------

    [Fact]
    public void HR_MayManageAnotherHrManager()
    {
        AccountManagementPolicy.CanManage(Hr, Account("HRManager"), Catalog).Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotManageAnAdmin()
    {
        AccountManagementPolicy.CanManage(Hr, Account("Admin"), Catalog).Reason
            .Should().Be("Only an administrator can change an administrator's account.");
    }

    [Fact]
    public void HR_MayNotManageAnAccountWhoseRolesDoMoreThanHr()
    {
        AccountManagementPolicy.CanManage(Hr, Account("Auditor"), Catalog).Reason
            .Should().Be("You can't change this account: its roles allow things you can't do yourself.");
    }

    [Fact]
    public void AnAdmin_MayManageAnyone()
    {
        AccountManagementPolicy.CanManage(Admin, Account("Admin", "Auditor"), Catalog).Allowed.Should().BeTrue();
    }

    // --- Set roles ----------------------------------------------------------------------------

    [Fact]
    public void HR_MayGrantAndRemoveRolesItCovers()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Account("PayrollService"), ["Employee", "Manager", "HRManager"], Catalog, activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotGrantARoleThatDoesMoreThanHr()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Account(), ["Employee", "Auditor"], Catalog, activeAdmins: 1).Reason
            .Should().Be("You can't grant or remove the Auditor role: it allows things you can't do yourself.");
    }

    [Fact]
    public void ARoleNobodyCanAssign_ThatTheAccountAlreadyHolds_DoesNotBlockAnEdit()
    {
        var target = new AccountTarget(OtherId, ["Employee", "Service"], IsActive: true);

        AccountManagementPolicy.CanSetRoles(Admin, target, ["Employee", "Service", "Manager"], Catalog, activeAdmins: 2)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void NobodyMayRemoveTheirOwnPermissionToManageUsers()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Self(Hr), ["Employee", "Manager"], Catalog, activeAdmins: 1).Reason
            .Should().Be("You can't remove your own permission to manage users.");
    }

    [Fact]
    public void RemovingOneOfYourOwnRoles_IsFine_WhileAnotherStillLetsYouManageUsers()
    {
        var both = new AccountActor(HrId, ["HRManager", "Manager", "Employee"], [.. HrPermissions, Permissions.ApprovalsTeam]);

        AccountManagementPolicy.CanSetRoles(both, Self(both), ["HRManager", "Employee"], Catalog, activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void AnAdmin_MayDropAdmin_OnlyWhileAnotherRoleStillLetsThemManageUsers_AndAnotherAdminRemains()
    {
        var adminAndHr = new AccountActor(AdminId, ["Admin", "HRManager", "Employee"], Permissions.AllKeys);

        AccountManagementPolicy.CanSetRoles(adminAndHr, Self(adminAndHr), ["HRManager", "Employee"], Catalog, activeAdmins: 2)
            .Allowed.Should().BeTrue();
        AccountManagementPolicy.CanSetRoles(Admin, Self(Admin), ["Employee"], Catalog, activeAdmins: 2).Reason
            .Should().Be("You can't remove your own permission to manage users.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotLoseTheAdminRole()
    {
        AccountManagementPolicy.CanSetRoles(Admin, Account("Admin"), ["Employee"], Catalog, activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void ADeactivatedAdmin_IsNotTheLastActiveAdmin()
    {
        var inactiveAdmin = new AccountTarget(OtherId, ["Admin", "Employee"], IsActive: false);

        AccountManagementPolicy.CanSetRoles(Admin, inactiveAdmin, ["Employee"], Catalog, activeAdmins: 1).Allowed.Should().BeTrue();
    }

    // --- Deactivate, reactivate, reset, link --------------------------------------------------

    [Fact]
    public void NobodyMayDeactivateTheirOwnAccount()
    {
        AccountManagementPolicy.CanDeactivate(Admin, Self(Admin), Catalog, activeAdmins: 3).Reason
            .Should().Be("You can't deactivate your own account.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotBeDeactivated()
    {
        AccountManagementPolicy.CanDeactivate(Admin, Account("Admin"), Catalog, activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void HR_MayDeactivateReactivateResetAndLink_AnAccountItCovers_ButNotOneItDoesNot()
    {
        var covered = Account("Manager");
        var beyond = Account("Auditor");

        AccountManagementPolicy.CanDeactivate(Hr, covered, Catalog, activeAdmins: 1).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanReactivate(Hr, covered, Catalog).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanResetPassword(Hr, covered, Catalog).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanLinkEmployee(Hr, covered, Catalog).Allowed.Should().BeTrue();

        AccountManagementPolicy.CanDeactivate(Hr, beyond, Catalog, activeAdmins: 1).Allowed.Should().BeFalse();
        AccountManagementPolicy.CanReactivate(Hr, beyond, Catalog).Allowed.Should().BeFalse();
        AccountManagementPolicy.CanResetPassword(Hr, beyond, Catalog).Allowed.Should().BeFalse();
        AccountManagementPolicy.CanLinkEmployee(Hr, beyond, Catalog).Allowed.Should().BeFalse();
    }

    [Fact]
    public void NobodyMayResetTheirOwnPassword_ThatIsWhatChangePasswordIsFor()
    {
        AccountManagementPolicy.CanResetPassword(Admin, Self(Admin), Catalog).Reason
            .Should().Be("Use Change Password on My Profile to change your own password.");
    }
}
```

- [ ] **Step 2: Watch them fail to compile**

Run: `dotnet build tests/PeopleCore.Application.Tests 2>&1 | grep -m3 "error"`
Expected: errors such as `The type or namespace name 'RoleGrant' could not be found`.

- [ ] **Step 3: Write the policy**

Delete `src/PeopleCore.API/Accounts/AccountRoles.cs`.

Replace the whole of `src/PeopleCore.API/Accounts/AccountManagementPolicy.cs` with:

```csharp
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Accounts;

/// <summary>One role and what it allows - every permission for Admin.</summary>
public sealed record RoleGrant(string Name, IReadOnlyList<string> Permissions);

/// <summary>The signed-in caller acting on an account.</summary>
/// <param name="Permissions">What the caller's token says they may do.</param>
public sealed record AccountActor(string UserId, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions)
{
    public bool IsAdmin => Roles.Contains(SeededRoles.Admin);
}

/// <summary>The account being acted on, as it stands before the change.</summary>
public sealed record AccountTarget(string UserId, IReadOnlyCollection<string> Roles, bool IsActive)
{
    public bool IsAdmin => Roles.Contains(SeededRoles.Admin);
}

public readonly record struct PolicyDecision(bool Allowed, string? Reason)
{
    public static PolicyDecision Allow => new(true, null);

    public static PolicyDecision Deny(string reason) => new(false, reason);
}

/// <summary>A role a caller may be offered, and whether - and if not, why not - they may grant it.</summary>
public sealed record AssignableRole(string Name, bool Grantable, string? Reason);

/// <summary>
/// Who may change which account. Roles are data now, so the rule is about power rather than names:
/// nobody grants, removes or manages beyond the permissions they hold themselves, and only an Admin
/// touches Admin. Plain values in - the caller, the account, the roles and what each allows - and a
/// decision with a sentence out, shown to the caller as it is.
/// </summary>
public static class AccountManagementPolicy
{
    private const string AdminTarget = "Only an administrator can change an administrator's account.";
    private const string BeyondTarget = "You can't change this account: its roles allow things you can't do yourself.";
    private const string AdminRole = "Only an administrator can grant or remove the Admin role.";
    private const string ServiceRole = "The Service role is for attendance devices and can't be assigned.";
    private const string SelfLockout = "You can't remove your own permission to manage users.";
    private const string SelfDeactivation = "You can't deactivate your own account.";
    private const string SelfReset = "Use Change Password on My Profile to change your own password.";
    private const string LastAdmin = "This is the last active administrator. Make another account an Admin first.";

    public static IReadOnlyList<string> PermissionsOf(IEnumerable<string> roles, IReadOnlyList<RoleGrant> catalog)
    {
        var held = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return held.Contains(SeededRoles.Admin)
            ? Permissions.AllKeys
            : catalog.Where(r => held.Contains(r.Name)).SelectMany(r => r.Permissions).Distinct().ToList();
    }

    public static PolicyDecision CanGrant(AccountActor actor, string role, IReadOnlyList<RoleGrant> catalog)
    {
        if (string.Equals(role, SeededRoles.Service, StringComparison.OrdinalIgnoreCase)) return PolicyDecision.Deny(ServiceRole);
        if (string.Equals(role, SeededRoles.Admin, StringComparison.OrdinalIgnoreCase))
            return actor.IsAdmin ? PolicyDecision.Allow : PolicyDecision.Deny(AdminRole);

        var permissions = catalog.FirstOrDefault(r => string.Equals(r.Name, role, StringComparison.OrdinalIgnoreCase))?.Permissions ?? [];
        return Covers(actor, permissions)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny($"You can't grant or remove the {role} role: it allows things you can't do yourself.");
    }

    public static IReadOnlyList<AssignableRole> AssignableRoles(AccountActor actor, IReadOnlyList<RoleGrant> catalog) =>
        catalog.Where(r => r.Name != SeededRoles.Service)
            .Select(r =>
            {
                var decision = CanGrant(actor, r.Name, catalog);
                return new AssignableRole(r.Name, decision.Allowed, decision.Reason);
            })
            .ToList();

    /// <summary>Whether the actor may change the target at all. Every other check starts here.</summary>
    public static PolicyDecision CanManage(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog)
    {
        if (target.IsAdmin && !actor.IsAdmin) return PolicyDecision.Deny(AdminTarget);
        return Covers(actor, PermissionsOf(target.Roles, catalog)) ? PolicyDecision.Allow : PolicyDecision.Deny(BeyondTarget);
    }

    public static PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> roles, IReadOnlyList<RoleGrant> catalog) =>
        FirstRefusal(actor, roles, catalog);

    public static PolicyDecision CanSetRoles(
        AccountActor actor, AccountTarget target, IReadOnlyCollection<string> rolesAfter, IReadOnlyList<RoleGrant> catalog, int activeAdmins)
    {
        var manage = CanManage(actor, target, catalog);
        if (!manage.Allowed) return manage;

        var before = target.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = rolesAfter.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A role nobody can assign (Service) that the account already holds and keeps is not a change.
        var changed = before.Except(after, StringComparer.OrdinalIgnoreCase)
            .Concat(after.Except(before, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var refusal = FirstRefusal(actor, changed, catalog);
        if (!refusal.Allowed) return refusal;

        if (actor.UserId == target.UserId
            && PermissionsOf(before, catalog).Contains(Permissions.UsersManage)
            && !PermissionsOf(after, catalog).Contains(Permissions.UsersManage))
            return PolicyDecision.Deny(SelfLockout);

        if (IsLastActiveAdmin(target, activeAdmins) && !after.Contains(SeededRoles.Admin))
            return PolicyDecision.Deny(LastAdmin);

        return PolicyDecision.Allow;
    }

    public static PolicyDecision CanLinkEmployee(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog) =>
        CanManage(actor, target, catalog);

    public static PolicyDecision CanDeactivate(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog, int activeAdmins)
    {
        var manage = CanManage(actor, target, catalog);
        if (!manage.Allowed) return manage;
        if (actor.UserId == target.UserId) return PolicyDecision.Deny(SelfDeactivation);
        return IsLastActiveAdmin(target, activeAdmins) ? PolicyDecision.Deny(LastAdmin) : PolicyDecision.Allow;
    }

    public static PolicyDecision CanReactivate(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog) =>
        CanManage(actor, target, catalog);

    public static PolicyDecision CanResetPassword(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog) =>
        actor.UserId == target.UserId ? PolicyDecision.Deny(SelfReset) : CanManage(actor, target, catalog);

    private static bool Covers(AccountActor actor, IEnumerable<string> permissions) =>
        actor.IsAdmin || permissions.All(actor.Permissions.Contains);

    private static PolicyDecision FirstRefusal(AccountActor actor, IEnumerable<string> roles, IReadOnlyList<RoleGrant> catalog) =>
        roles.Select(role => CanGrant(actor, role, catalog)).FirstOrDefault(d => !d.Allowed, PolicyDecision.Allow);

    private static bool IsLastActiveAdmin(AccountTarget target, int activeAdmins) =>
        target.IsActive && target.IsAdmin && activeAdmins <= 1;
}
```

`ARoleNobodyCanAssign_ThatTheAccountAlreadyHolds_DoesNotBlockAnEdit` passes because Service is in both `before` and `after`, so it never appears among `changed`.

- [ ] **Step 4: Point the Users controller's test fixture at the catalogue and permissions**

In `tests/PeopleCore.Application.Tests/Api/UsersControllerTestBase.cs`:

1. Add the usings `using PeopleCore.Application.Common.Authorization;`. `PeopleCore.Infrastructure.Identity` is already imported.
2. Add this field next to `Employees`:

```csharp
    protected readonly Mock<IRoleCatalog> Roles = new();
```

3. In the constructor, replace `Sut = new UsersController(Users.Object, Directory.Object, Employees.Object);` with:

```csharp
        // The seeded roles, as the catalogue returns them: system roles first, then by name.
        Roles.Setup(r => r.GetRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new[] { "Admin", "Employee", "Service", "HRManager", "Manager", "PayrollService" }
                .Select(name => new RoleRecord(name.ToLowerInvariant(), name, null, SeededRoles.System.Contains(name),
                    SeededRoles.PermissionsOf([name]), 0))
                .ToList());

        Sut = new UsersController(Users.Object, Directory.Object, Employees.Object, Roles.Object);
```

4. Replace the body of `SignInAs` with the following. It keeps signing in by role and adds the seeded permissions that a real token would carry:

```csharp
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
```

- [ ] **Step 5: Update the Users controller tests whose expectations change (failing)**

In `tests/PeopleCore.Application.Tests/Api/UsersControllerReadTests.cs`, replace `AssignableRoles_AreTheOnesTheCallerMayGrant` with:

```csharp
    [Fact]
    public async Task AssignableRoles_ListEveryRoleButService_SayingWhichTheCallerMayGrant()
    {
        SignInAs("HRManager", "Employee");

        var roles = OkValue(await Sut.AssignableRoles(CancellationToken.None));

        roles.Select(r => r.Name).Should().Equal("Admin", "Employee", "HRManager", "Manager", "PayrollService");
        roles.Where(r => r.Grantable).Select(r => r.Name).Should().Equal("Employee", "HRManager", "Manager", "PayrollService");
        roles.Single(r => r.Name == "Admin").Reason.Should().Be("Only an administrator can grant or remove the Admin role.");
    }
```

In `tests/PeopleCore.Application.Tests/Api/UsersControllerChangeTests.cs`, replace `SetRoles_ByHrOnAnHrManager_IsRefused` with:

```csharp
    [Fact]
    public async Task SetRoles_ByHrOnAnotherHrManager_IsAllowed_BecauseItGrantsNothingHrLacks()
    {
        SignInAs("HRManager", "Employee");
        var user = Account("HRManager", "Employee");

        var result = await Sut.SetRoles(TargetId, new SetRolesRequest(["HRManager", "Manager"]), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        StampWasReplaced(user);
    }

    [Fact]
    public async Task SetRoles_ByHrOnAnAdmin_IsRefused()
    {
        SignInAs("HRManager", "Employee");
        Account("Admin", "Employee");

        var result = await Sut.SetRoles(TargetId, new SetRolesRequest(["Admin", "Manager"]), CancellationToken.None);

        ForbiddenDetail(result).Should().Be("Only an administrator can change an administrator's account.");
        StampWasNotReplaced();
    }

    [Fact]
    public async Task SetRoles_ThatWouldTakeAwayYourOwnPermissionToManageUsers_IsRefused()
    {
        SignInAs("HRManager", "Employee");
        CallersOwnAccount("HRManager", "Employee");

        var result = await Sut.SetRoles(CallerId, new SetRolesRequest(["Manager"]), CancellationToken.None);

        ForbiddenDetail(result).Should().Be("You can't remove your own permission to manage users.");
        StampWasNotReplaced();
    }
```

Run: `dotnet build tests/PeopleCore.Application.Tests 2>&1 | grep -m5 "error"`
Expected: build errors in `UsersController.cs`, which still uses `AccountRoles` and the old policy signatures, and in the test fixture's four-argument constructor.

- [ ] **Step 6: Rewire the Users controller**

In `src/PeopleCore.API/Controllers/Account/UsersController.cs`:

1. Replace the fields and the constructor with:

```csharp
    private readonly UserManager<ApplicationUser> _users;
    private readonly IUserAccountDirectory _directory;
    private readonly IEmployeeRepository _employees;
    private readonly IRoleCatalog _roles;

    public UsersController(
        UserManager<ApplicationUser> users, IUserAccountDirectory directory, IEmployeeRepository employees, IRoleCatalog roles)
    {
        _users = users;
        _directory = directory;
        _employees = employees;
        _roles = roles;
    }
```

2. Replace `List`'s last two lines, `var (rows, total) = ...` and `return Ok(...)`, with:

```csharp
        var (rows, total) = await _directory.SearchAsync(search, page, pageSize, ct);
        var catalog = await CatalogAsync(ct);
        return Ok(PagedResult<UserAccountDto>.Create(rows.Select(row => ToDto(row, catalog)).ToList(), total, page, pageSize));
```

3. Replace the `AssignableRoles` action with:

```csharp
    [HttpGet("assignable-roles")]
    public async Task<ActionResult<IReadOnlyList<AssignableRoleDto>>> AssignableRoles(CancellationToken ct) =>
        Ok(AccountManagementPolicy.AssignableRoles(Caller, await CatalogAsync(ct))
            .Select(role => new AssignableRoleDto(role.Name, role.Grantable, role.Reason))
            .ToList());
```

4. In `Create`, replace these two lines:

```csharp
        if (NormalizeRoles(request.Roles, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var decision = AccountManagementPolicy.CanCreate(Caller, roles);
```

with:

```csharp
        var catalog = await CatalogAsync(ct);
        if (NormalizeRoles(request.Roles, catalog, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var decision = AccountManagementPolicy.CanCreate(Caller, roles, catalog);
```

In the same method, replace `new CreatedUserAccountDto(ToDto(row!), password)` with `new CreatedUserAccountDto(ToDto(row!, catalog), password)`.

5. In `SetRoles`, replace everything from the first line down to and including the `toRemove` line:

```csharp
        if (NormalizeRoles(request.Roles, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var held = await _users.GetRolesAsync(user);

        var decision = AccountManagementPolicy.CanSetRoles(Caller, Target(user, held), roles, await ActiveAdminsAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        // Only assignable roles are compared, so a role nobody can assign (Service) is left alone.
        var toAdd = roles.Except(held).ToList();
        var toRemove = held.Where(role => AccountRoles.Assignable.Contains(role)).Except(roles).ToList();
```

with:

```csharp
        var catalog = await CatalogAsync(ct);
        if (NormalizeRoles(request.Roles, catalog, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var held = await _users.GetRolesAsync(user);

        // Service can't be assigned, so the request never names it - but an account that already
        // holds it keeps it, and the policy must judge the roles the account will actually end up with.
        var keptService = held.Where(role => string.Equals(role, SeededRoles.Service, StringComparison.OrdinalIgnoreCase));
        var rolesAfter = roles.Concat(keptService).ToList();

        var decision = AccountManagementPolicy.CanSetRoles(Caller, Target(user, held), rolesAfter, catalog, await ActiveAdminsAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        var toAdd = roles.Except(held, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = held.Except(rolesAfter, StringComparer.OrdinalIgnoreCase).ToList();
```

6. In `LinkEmployee`, replace `AccountManagementPolicy.CanLinkEmployee(Caller, Target(user, await _users.GetRolesAsync(user)))` with:

```csharp
AccountManagementPolicy.CanLinkEmployee(Caller, Target(user, await _users.GetRolesAsync(user)), await CatalogAsync(ct))
```

7. In `ResetPassword`, replace `AccountManagementPolicy.CanResetPassword(Caller, Target(user, await _users.GetRolesAsync(user)))` with:

```csharp
AccountManagementPolicy.CanResetPassword(Caller, Target(user, await _users.GetRolesAsync(user)), await CatalogAsync(ct))
```

8. Replace the `Caller` property and its comment, `ToDto`, and `AccountAsync` with:

```csharp
    // Roles and permissions come from the token. That is safe to trust because AccountTokenValidator
    // rejects any token issued before the account's roles, or a role's permissions, last changed.
    private AccountActor Caller => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
        User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList());

    private async Task<IReadOnlyList<RoleGrant>> CatalogAsync(CancellationToken ct) =>
        (await _roles.GetRolesAsync(ct)).Select(role => new RoleGrant(role.Name, role.Permissions)).ToList();

    private UserAccountDto ToDto(UserAccountRow row, IReadOnlyList<RoleGrant> catalog) => new(
        row.Id, row.Email, row.FirstName, row.LastName, row.Roles, row.IsActive, row.MustChangePassword,
        row.EmployeeId, row.EmployeeName,
        CanManage: AccountManagementPolicy.CanManage(Caller, new AccountTarget(row.Id, row.Roles, row.IsActive), catalog).Allowed);

    private async Task<ActionResult<UserAccountDto>> AccountAsync(string id, CancellationToken ct)
    {
        var row = await _directory.GetAsync(id, ct);
        return row is null ? NotFound() : Ok(ToDto(row, await CatalogAsync(ct)));
    }
```

Keep `Target(...)` as it is.

9. In `SetActiveAsync`, replace the decision lines with:

```csharp
        var target = Target(user, await _users.GetRolesAsync(user));
        var catalog = await CatalogAsync(ct);
        var decision = active
            ? AccountManagementPolicy.CanReactivate(Caller, target, catalog)
            : AccountManagementPolicy.CanDeactivate(Caller, target, catalog, await ActiveAdminsAsync(ct));
```

10. Replace `ActiveAdminsAsync` and `NormalizeRoles`, with its summary, with:

```csharp
    private Task<int> ActiveAdminsAsync(CancellationToken ct) => _directory.CountActiveInRoleAsync(SeededRoles.Admin, ct);

    /// <summary>
    /// The requested roles, spelt and ordered as the catalogue has them, always with Employee.
    /// Returns a problem naming the first role that isn't one an account can be given - an unknown
    /// name, or Service.
    /// </summary>
    private static string? NormalizeRoles(IReadOnlyList<string>? requested, IReadOnlyList<RoleGrant> catalog, out IReadOnlyList<string> roles)
    {
        requested ??= [];
        var assignable = catalog.Where(role => role.Name != SeededRoles.Service).Select(role => role.Name).ToList();

        roles = assignable
            .Where(name => name == SeededRoles.Employee || requested.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var unknown = requested.FirstOrDefault(role => !assignable.Contains(role, StringComparer.OrdinalIgnoreCase));
        return unknown is null ? null : $"{unknown} is not a role that can be assigned.";
    }
```

11. Add this record beside the other DTOs at the bottom of the file:

```csharp
public record AssignableRoleDto(string Name, bool Grantable, string? Reason);
```

- [ ] **Step 7: Build and run the API tests**

Run: `dotnet build PeopleCore.slnx`
Expected: 0 errors. If any other file still refers to `AccountRoles`, replace it with the `SeededRoles` constant of the same name and list the change in your report.

Run: `dotnet test tests/PeopleCore.Application.Tests`
Expected: all pass. That includes the new `AccountManagementPolicyTests`, the updated Users controller tests, and the unchanged `PermissionEquivalenceTests` (the `AssignableRoles` action keeps its name).

- [ ] **Step 8: Commit**

```bash
git add -A src/PeopleCore.API tests/PeopleCore.Application.Tests/Api
git commit -m "feat(api): grant only what you can do yourself, by permission not by role name"
```


---

### Task 4: The Roles API

**Files:**
- Create: `src/PeopleCore.API/Accounts/RoleManagementPolicy.cs`
- Create: `src/PeopleCore.API/Controllers/Account/RolesController.cs`
- Modify test: `tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/RoleManagementPolicyTests.cs`
- Test: `tests/PeopleCore.Application.Tests/Api/RolesControllerTests.cs`

**Interfaces:**
- Consumes:
  - `AccountActor`, `PolicyDecision` and `AccountManagementPolicy.PermissionsOf` (Task 3)
  - `IRoleCatalog`, `RoleRecord` and `IRoleEditor` (Task 2)
  - `IUserAccountDirectory.GetAsync` (existing)
  - `Permissions` and `RequirePermissionAttribute` (phase 1)
- Produces:
  - `sealed record RoleSnapshot(string Name, bool IsSystem, IReadOnlyList<string> Permissions)` (namespace `PeopleCore.API.Accounts`)
  - `static class RoleManagementPolicy`, with these methods:
    - `CanEdit(AccountActor, RoleSnapshot)`
    - `CanCreate(AccountActor, IReadOnlyCollection<string> permissions)`
    - `CanUpdate(AccountActor, RoleSnapshot role, IReadOnlyCollection<string> newPermissions, IReadOnlyCollection<string> actorPermissionsAfter)`
    - `CanDelete(AccountActor, RoleSnapshot)`
  - `RolesController` (route `api/roles`, requires `roles.manage`), with these actions:
    - `List(CancellationToken)`
    - `Get(string id, CancellationToken)`
    - `PermissionCatalogue()`, on route `GET api/roles/permissions`
    - `Create(SaveRoleRequest, CancellationToken)`
    - `Update(string id, SaveRoleRequest, CancellationToken)`
    - `Delete(string id, CancellationToken)`
  - DTOs:
    - `RoleDto(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount, bool CanEdit)`
    - `PermissionDto(string Key, string Group, string Label, string Description)`
    - `SaveRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions)`

- [ ] **Step 1: Write the failing policy tests**

Create `tests/PeopleCore.Application.Tests/Api/RoleManagementPolicyTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.API.Accounts;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Who may change which role. The same principle as accounts: nobody puts on a role, or changes a
/// role that already has, a permission they don't hold. System roles are the app's, not an admin's.
/// </summary>
public class RoleManagementPolicyTests
{
    private static readonly AccountActor Admin = new("admin-1", ["Admin", "Employee"], Permissions.AllKeys);

    // Holds roles.manage and users.manage through a custom role, plus recruitment.
    private static readonly AccountActor Delegate = new("delegate-1", ["Delegates", "Employee"],
        [Permissions.RolesManage, Permissions.UsersManage, Permissions.RecruitmentManage]);

    private static RoleSnapshot Role(params string[] permissions) => new("Recruiter", IsSystem: false, permissions);

    [Fact]
    public void SystemRoles_CannotBeChanged_EvenByAnAdmin()
    {
        var admin = new RoleSnapshot("Admin", IsSystem: true, Permissions.AllKeys);

        RoleManagementPolicy.CanEdit(Admin, admin).Reason.Should().Be("System roles can't be changed.");
        RoleManagementPolicy.CanDelete(Admin, admin).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ARoleThatAllowsMoreThanTheCaller_CannotBeChangedOrDeleted_ByThem()
    {
        var beyond = Role(Permissions.PayrollManage);

        RoleManagementPolicy.CanEdit(Delegate, beyond).Reason.Should().Be("You can't change a role that allows things you can't do yourself.");
        RoleManagementPolicy.CanDelete(Delegate, beyond).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ARoleWithinTheCallersPermissions_MayBeChangedAndDeleted()
    {
        RoleManagementPolicy.CanEdit(Delegate, Role(Permissions.RecruitmentManage)).Allowed.Should().BeTrue();
        RoleManagementPolicy.CanDelete(Delegate, Role(Permissions.RecruitmentManage)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void NobodyMayGiveARolePermissionsTheyLack()
    {
        RoleManagementPolicy.CanCreate(Delegate, [Permissions.RecruitmentManage]).Allowed.Should().BeTrue();
        RoleManagementPolicy.CanCreate(Delegate, [Permissions.PayrollManage]).Reason
            .Should().Be("You can't give a role permissions you don't have yourself.");
        RoleManagementPolicy.CanUpdate(Delegate, Role(Permissions.RecruitmentManage), [Permissions.PayrollManage],
            actorPermissionsAfter: Delegate.Permissions).Reason
            .Should().Be("You can't give a role permissions you don't have yourself.");
    }

    [Fact]
    public void NobodyMayRemoveTheirOwnPermissionToManageRoles()
    {
        var theirOwn = new RoleSnapshot("Delegates", IsSystem: false, [Permissions.RolesManage, Permissions.UsersManage]);

        RoleManagementPolicy.CanUpdate(Delegate, theirOwn, [Permissions.UsersManage],
            actorPermissionsAfter: [Permissions.UsersManage, Permissions.RecruitmentManage]).Reason
            .Should().Be("You can't remove your own permission to manage roles.");
    }

    [Fact]
    public void NobodyMayRemoveTheirOwnPermissionToManageUsers()
    {
        var theirOwn = new RoleSnapshot("Delegates", IsSystem: false, [Permissions.RolesManage, Permissions.UsersManage]);

        RoleManagementPolicy.CanUpdate(Delegate, theirOwn, [Permissions.RolesManage],
            actorPermissionsAfter: [Permissions.RolesManage, Permissions.RecruitmentManage]).Reason
            .Should().Be("You can't remove your own permission to manage users.");
    }

    [Fact]
    public void AnAdmin_KeepsEveryPermission_SoTheLockoutGuardNeverTripsForThem()
    {
        RoleManagementPolicy.CanUpdate(Admin, Role(Permissions.RolesManage), [], actorPermissionsAfter: Permissions.AllKeys)
            .Allowed.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Write the failing controller tests**

Create `tests/PeopleCore.Application.Tests/Api/RolesControllerTests.cs`:

```csharp
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

    [Fact]
    public async Task Create_WithAnUnknownPermission_IsRejected()
    {
        Detail(await _sut.Create(new SaveRoleRequest("Recruiter", null, ["reports.everything"]), CancellationToken.None), 400)
            .Should().Be("reports.everything is not a permission.");
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
```

- [ ] **Step 3: Register the new endpoints with the equivalence test (failing)**

In `tests/PeopleCore.Application.Tests/Api/PermissionEquivalenceTests.cs`:

1. Add `using PeopleCore.Application.Common.Authorization;` if it's not already there.
2. Directly after the `LegacyRoles` dictionary, add:

```csharp
    /// <summary>
    /// Endpoints added after role-name checks were removed. They had no "before" to be equivalent to,
    /// so each is pinned to exactly the permission it requires.
    /// </summary>
    private static readonly Dictionary<string, string[]> AddedEndpoints = new()
    {
        ["RolesController.List"] = [Permissions.RolesManage],
        ["RolesController.Get"] = [Permissions.RolesManage],
        ["RolesController.PermissionCatalogue"] = [Permissions.RolesManage],
        ["RolesController.Create"] = [Permissions.RolesManage],
        ["RolesController.Update"] = [Permissions.RolesManage],
        ["RolesController.Delete"] = [Permissions.RolesManage],
    };
```

3. In `EndpointsThatWereOpenToEveryone_RequireNoPermission`, change the `Where` so it skips added endpoints:

```csharp
            .Where(e => !LegacyRoles.ContainsKey(KeyOf(e.Controller, e.Action))
                        && !AddedEndpoints.ContainsKey(KeyOf(e.Controller, e.Action))
                        && RequirementsOf(e.Controller, e.Action).Count > 0)
```

4. Add this test:

```csharp
    [Fact]
    public void EndpointsAddedSincePermissions_RequireExactlyTheirPermission()
    {
        var present = Endpoints().ToDictionary(e => KeyOf(e.Controller, e.Action));

        foreach (var (key, expected) in AddedEndpoints)
        {
            present.Should().ContainKey(key);
            var (controller, action) = present[key];
            RequirementsOf(controller, action).SelectMany(r => r.AnyOf).Should().Equal(expected, key);
        }
    }
```

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~RoleManagementPolicyTests|FullyQualifiedName~RolesControllerTests|FullyQualifiedName~PermissionEquivalenceTests"`
Expected: build error, `The type or namespace name 'RolesController' could not be found`.

- [ ] **Step 4: Write the policy**

Create `src/PeopleCore.API/Accounts/RoleManagementPolicy.cs`:

```csharp
using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.API.Accounts;

/// <summary>A role as it stands, for deciding whether a caller may change it.</summary>
public sealed record RoleSnapshot(string Name, bool IsSystem, IReadOnlyList<string> Permissions);

/// <summary>
/// Who may change which role. Nobody puts a permission on a role, or changes a role that already has
/// one, unless they hold that permission themselves - otherwise "manage roles" would be a way to give
/// yourself anything. System roles are the app's to define. And nobody may edit away their own power
/// to manage roles or users, which would lock them out mid-task.
/// </summary>
public static class RoleManagementPolicy
{
    private const string SystemRole = "System roles can't be changed.";
    private const string BeyondRole = "You can't change a role that allows things you can't do yourself.";
    private const string BeyondGrant = "You can't give a role permissions you don't have yourself.";
    private const string OwnRolesManage = "You can't remove your own permission to manage roles.";
    private const string OwnUsersManage = "You can't remove your own permission to manage users.";

    public static PolicyDecision CanEdit(AccountActor actor, RoleSnapshot role)
    {
        if (role.IsSystem) return PolicyDecision.Deny(SystemRole);
        return Covers(actor, role.Permissions) ? PolicyDecision.Allow : PolicyDecision.Deny(BeyondRole);
    }

    public static PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> permissions) =>
        Covers(actor, permissions) ? PolicyDecision.Allow : PolicyDecision.Deny(BeyondGrant);

    /// <param name="actorPermissionsAfter">What the caller would be able to do once the change is saved.</param>
    public static PolicyDecision CanUpdate(
        AccountActor actor, RoleSnapshot role, IReadOnlyCollection<string> newPermissions, IReadOnlyCollection<string> actorPermissionsAfter)
    {
        var edit = CanEdit(actor, role);
        if (!edit.Allowed) return edit;
        if (!Covers(actor, newPermissions)) return PolicyDecision.Deny(BeyondGrant);

        if (!actor.IsAdmin)
        {
            if (actor.Permissions.Contains(Permissions.RolesManage) && !actorPermissionsAfter.Contains(Permissions.RolesManage))
                return PolicyDecision.Deny(OwnRolesManage);
            if (actor.Permissions.Contains(Permissions.UsersManage) && !actorPermissionsAfter.Contains(Permissions.UsersManage))
                return PolicyDecision.Deny(OwnUsersManage);
        }

        return PolicyDecision.Allow;
    }

    /// <summary>Whether the role may be deleted as far as the caller goes; the controller separately refuses roles still held.</summary>
    public static PolicyDecision CanDelete(AccountActor actor, RoleSnapshot role) => CanEdit(actor, role);

    private static bool Covers(AccountActor actor, IEnumerable<string> permissions) =>
        actor.IsAdmin || permissions.All(actor.Permissions.Contains);
}
```

- [ ] **Step 5: Write the controller**

Create `src/PeopleCore.API/Controllers/Account/RolesController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Accounts;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// Roles: what each allows, and who may change them. Whether the caller may make a change is
/// <see cref="RoleManagementPolicy"/>'s decision; this controller validates, asks, then writes.
/// Changing a role's permissions signs out everyone who holds it (see <see cref="IRoleEditor"/>).
/// </summary>
[ApiController]
[RequirePermission(Permissions.RolesManage)]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private const int NameMaxLength = 50;

    private readonly IRoleCatalog _catalog;
    private readonly IRoleEditor _editor;
    private readonly IUserAccountDirectory _directory;

    public RolesController(IRoleCatalog catalog, IRoleEditor editor, IUserAccountDirectory directory)
    {
        _catalog = catalog;
        _editor = editor;
        _directory = directory;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct) =>
        Ok((await _catalog.GetRolesAsync(ct)).Select(ToDto).ToList());

    [HttpGet("{id}")]
    public async Task<ActionResult<RoleDto>> Get(string id, CancellationToken ct) =>
        await _catalog.GetAsync(id, ct) is { } role ? Ok(ToDto(role)) : NotFound();

    [HttpGet("permissions")]
    public ActionResult<IReadOnlyList<PermissionDto>> PermissionCatalogue() =>
        Ok(Permissions.All.Select(p => new PermissionDto(p.Key, p.Group, p.Label, p.Description)).ToList());

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] SaveRoleRequest request, CancellationToken ct)
    {
        if (Validate(request, out var name, out var description, out var permissions) is { } problem) return RoleProblem(problem);

        var decision = RoleManagementPolicy.CanCreate(Caller, permissions);
        if (!decision.Allowed) return Refused(decision);

        if (await _editor.NameTakenAsync(name, exceptRoleId: null, ct)) return RoleProblem($"A role called {name} already exists.");

        var id = await _editor.CreateAsync(name, description, permissions, ct);
        var created = await _catalog.GetAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id }, ToDto(created!));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RoleDto>> Update(string id, [FromBody] SaveRoleRequest request, CancellationToken ct)
    {
        var role = await _catalog.GetAsync(id, ct);
        if (role is null) return NotFound();

        // The system-role and beyond-the-caller refusals come before validation, so a caller who may
        // not touch the role learns that rather than a complaint about the name they typed.
        var edit = RoleManagementPolicy.CanEdit(Caller, Snapshot(role));
        if (!edit.Allowed) return Refused(edit);

        if (Validate(request, out var name, out var description, out var permissions) is { } problem) return RoleProblem(problem);

        var decision = RoleManagementPolicy.CanUpdate(Caller, Snapshot(role), permissions, await CallerPermissionsAfterAsync(role, permissions, ct));
        if (!decision.Allowed) return Refused(decision);

        if (await _editor.NameTakenAsync(name, exceptRoleId: id, ct)) return RoleProblem($"A role called {name} already exists.");

        await _editor.UpdateAsync(id, name, description, permissions, ct);
        return Ok(ToDto((await _catalog.GetAsync(id, ct))!));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var role = await _catalog.GetAsync(id, ct);
        if (role is null) return NotFound();

        var decision = RoleManagementPolicy.CanDelete(Caller, Snapshot(role));
        if (!decision.Allowed) return Refused(decision);

        if (role.AccountCount > 0)
            return RoleProblem(role.AccountCount == 1
                ? $"1 account still has {role.Name} — remove it from them first."
                : $"{role.AccountCount} accounts still have {role.Name} — remove it from them first.");

        await _editor.DeleteAsync(id, ct);
        return NoContent();
    }

    private AccountActor Caller => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
        User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList());

    private RoleDto ToDto(RoleRecord role) => new(
        role.Id, role.Name, role.Description, role.IsSystem, role.Permissions, role.AccountCount,
        CanEdit: RoleManagementPolicy.CanEdit(Caller, Snapshot(role)).Allowed);

    private static RoleSnapshot Snapshot(RoleRecord role) => new(role.Name, role.IsSystem, role.Permissions);

    /// <summary>
    /// What the caller could do once <paramref name="role"/> allows <paramref name="newPermissions"/>
    /// instead. Uses the caller's roles as stored now, not as their token lists them.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> CallerPermissionsAfterAsync(RoleRecord role, IReadOnlyCollection<string> newPermissions, CancellationToken ct)
    {
        var callerRoles = (await _directory.GetAsync(Caller.UserId, ct))?.Roles ?? Caller.Roles.ToList();
        var catalog = (await _catalog.GetRolesAsync(ct))
            .Select(r => new RoleGrant(r.Name, r.Id == role.Id ? newPermissions.ToList() : r.Permissions))
            .ToList();
        return AccountManagementPolicy.PermissionsOf(callerRoles, catalog);
    }

    private static string? Validate(SaveRoleRequest request, out string name, out string? description, out IReadOnlyCollection<string> permissions)
    {
        name = request.Name?.Trim() ?? string.Empty;
        description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        permissions = (request.Permissions ?? []).Distinct().ToList();

        if (name.Length == 0) return "Enter a role name.";
        if (name.Length > NameMaxLength) return $"A role name must be {NameMaxLength} characters or fewer.";
        if (description?.Length > ApplicationRole.DescriptionMaxLength)
            return $"A description must be {ApplicationRole.DescriptionMaxLength} characters or fewer.";
        return permissions.FirstOrDefault(key => !Permissions.AllKeys.Contains(key)) is { } unknown
            ? $"{unknown} is not a permission."
            : null;
    }

    private BadRequestObjectResult RoleProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Role not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });

    private ObjectResult Refused(PolicyDecision decision) =>
        StatusCode(StatusCodes.Status403Forbidden,
            new ProblemDetails { Title = "Not allowed", Detail = decision.Reason, Status = StatusCodes.Status403Forbidden });
}

public record RoleDto(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount, bool CanEdit);

public record PermissionDto(string Key, string Group, string Label, string Description);

public record SaveRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~RoleManagementPolicyTests|FullyQualifiedName~RolesControllerTests|FullyQualifiedName~PermissionEquivalenceTests|FullyQualifiedName~ControllerAuthorizationCoverageTests"`
Expected: all pass.

Run: `dotnet test tests/PeopleCore.Application.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.API tests/PeopleCore.Application.Tests/Api
git commit -m "feat(api): create, change and delete roles, never beyond the caller's own permissions"
```

---

### Task 5: Web client for roles, and ungrantable roles on the Users page

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs`
- Modify: `src/PeopleCore.Web/Components/Accounts/RoleCheckboxes.razor`
- Modify: `src/PeopleCore.Web/Components/Accounts/CreateAccountDialog.razor`
- Modify: `src/PeopleCore.Web/Pages/Admin/Users.razor`
- Modify tests: `tests/PeopleCore.Web.Tests/Services/ApiClientAccountsTests.cs`, `tests/PeopleCore.Web.Tests/Pages/Admin/UsersTests.cs`, `tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs`

**Interfaces:**
- Consumes: the JSON shapes from Tasks 3 and 4.
- Produces (namespace `PeopleCore.Web.Services`):
  - `Task<IReadOnlyList<AssignableRoleDto>?> GetAssignableRolesAsync()`. The return type changes from a list of strings.
  - `Task<IReadOnlyList<RoleDto>?> GetRolesAsync()`
  - `Task<IReadOnlyList<PermissionDto>?> GetPermissionCatalogueAsync()`
  - `Task<RoleDto?> CreateRoleAsync(SaveRoleRequest request)`
  - `Task<RoleDto?> UpdateRoleAsync(string roleId, SaveRoleRequest request)`
  - `Task DeleteRoleAsync(string roleId)`
  - Records:
    - `AssignableRoleDto(string Name, bool Grantable, string? Reason)`
    - `RoleDto(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount, bool CanEdit)`
    - `PermissionDto(string Key, string Group, string Label, string Description)`
    - `SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions)`
  - `RoleCheckboxes.Roles` becomes `IReadOnlyList<AssignableRoleDto>`. A role that isn't grantable is disabled, and its `title` is the reason. `RoleCheckboxes.Label(string)` stays.

- [ ] **Step 1: Write the failing client tests**

In `tests/PeopleCore.Web.Tests/Services/ApiClientAccountsTests.cs`, add:

```csharp
    [Fact]
    public async Task AssignableRoles_SayWhichTheCallerMayGrant_AndWhyNot()
    {
        _api.On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK,
            """[{"name":"Admin","grantable":false,"reason":"Only an administrator can grant or remove the Admin role."},{"name":"Manager","grantable":true,"reason":null}]""");

        var roles = await CreateClient().GetAssignableRolesAsync();

        roles!.Select(r => (r.Name, r.Grantable)).Should().Equal(("Admin", false), ("Manager", true));
        roles![0].Reason.Should().Contain("Only an administrator");
    }

    [Fact]
    public async Task Roles_AreListed_Created_Updated_AndDeleted()
    {
        const string role = """{"id":"r1","name":"Recruiter","description":null,"isSystem":false,"permissions":["recruitment.manage"],"accountCount":0,"canEdit":true}""";
        _api.On(HttpMethod.Get, "/api/roles", HttpStatusCode.OK, $"[{role}]")
            .On(HttpMethod.Get, "/api/roles/permissions", HttpStatusCode.OK,
                """[{"key":"recruitment.manage","group":"Recruitment","label":"Manage recruitment","description":"Job postings."}]""")
            .On(HttpMethod.Post, "/api/roles", HttpStatusCode.Created, role)
            .On(HttpMethod.Put, "/api/roles/r1", HttpStatusCode.OK, role)
            .On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.NoContent);
        var client = CreateClient();

        (await client.GetRolesAsync())!.Single().Permissions.Should().Equal("recruitment.manage");
        (await client.GetPermissionCatalogueAsync())!.Single().Label.Should().Be("Manage recruitment");
        (await client.CreateRoleAsync(new SaveRoleRequest("Recruiter", null, ["recruitment.manage"])))!.Id.Should().Be("r1");
        (await client.UpdateRoleAsync("r1", new SaveRoleRequest("Recruiter", "Hires.", ["recruitment.manage"])))!.Name.Should().Be("Recruiter");
        await client.DeleteRoleAsync("r1");

        var post = JsonDocument.Parse(_api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]!).RootElement;
        post.GetProperty("name").GetString().Should().Be("Recruiter");
        post.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("recruitment.manage");
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath == "/api/roles/r1");
    }

    [Fact]
    public async Task DeletingARoleStillInUse_ThrowsWithTheApisReason()
    {
        _api.On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"2 accounts still have Recruiter — remove it from them first."}""");

        var act = () => CreateClient().DeleteRoleAsync("r1");

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().StartWith("2 accounts still have Recruiter");
    }
```

- [ ] **Step 2: Switch the page tests to the new assignable-roles shape (they fail until Step 5)**

In `tests/PeopleCore.Web.Tests/Pages/Admin/UsersTests.cs`, replace the body of `AssignableRolesLoad` with:

```csharp
        _api.On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK,
            """[{"name":"Admin","grantable":false,"reason":"Only an administrator can grant or remove the Admin role."},{"name":"Employee","grantable":true,"reason":null},{"name":"Manager","grantable":true,"reason":null},{"name":"PayrollService","grantable":true,"reason":null}]""");
```

Then add this test:

```csharp
    [Fact]
    public void ARoleTheCallerMayNotGrant_IsShownDisabled_WithTheReason()
    {
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Roles").Click();

        var admin = cut.Find("[data-roles-dialog] [data-role=Admin]");
        admin.HasAttribute("disabled").Should().BeTrue();
        admin.GetAttribute("title").Should().Be("Only an administrator can grant or remove the Admin role.");
        cut.Find("[data-roles-dialog] [data-role=Manager]").HasAttribute("disabled").Should().BeFalse();
    }
```

In `tests/PeopleCore.Web.Tests/Pages/HR/EmployeesTests.cs`, replace the assignable-roles stub body `"""["Manager","Employee","PayrollService"]"""` with:

```csharp
"""[{"name":"Employee","grantable":true,"reason":null},{"name":"Manager","grantable":true,"reason":null},{"name":"PayrollService","grantable":true,"reason":null}]"""
```

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~ApiClientAccountsTests|FullyQualifiedName~UsersTests|FullyQualifiedName~EmployeesTests"`
Expected: build error. `GetRolesAsync` and `SaveRoleRequest` don't exist yet.

- [ ] **Step 3: Add the client methods and records**

In `src/PeopleCore.Web/Services/ApiClient.cs`:

1. Replace:

```csharp
    public async Task<IReadOnlyList<string>?> GetAssignableRolesAsync()
        => await GetJsonAsync<List<string>>("api/users/assignable-roles");
```

with:

```csharp
    /// <summary>Every role an account could be given, saying which the signed-in user may grant and, if not, why.</summary>
    public async Task<IReadOnlyList<AssignableRoleDto>?> GetAssignableRolesAsync()
        => await GetJsonAsync<List<AssignableRoleDto>>("api/users/assignable-roles");

    // Roles (anyone with Manage roles)
    public async Task<IReadOnlyList<RoleDto>?> GetRolesAsync()
        => await GetJsonAsync<List<RoleDto>>("api/roles");

    public async Task<IReadOnlyList<PermissionDto>?> GetPermissionCatalogueAsync()
        => await GetJsonAsync<List<PermissionDto>>("api/roles/permissions");

    public Task<RoleDto?> CreateRoleAsync(SaveRoleRequest request)
        => SendJsonAsync<RoleDto>(HttpMethod.Post, "api/roles", request);

    public Task<RoleDto?> UpdateRoleAsync(string roleId, SaveRoleRequest request)
        => SendJsonAsync<RoleDto>(HttpMethod.Put, $"api/roles/{Uri.EscapeDataString(roleId)}", request);

    // No body comes back from a delete, so there is nothing to read after the status check.
    public async Task DeleteRoleAsync(string roleId)
        => await EnsureSuccessAsync(await _http.DeleteAsync($"api/roles/{Uri.EscapeDataString(roleId)}"));
```

2. Next to `public record EmployeeLinkDto(...)`, add:

```csharp
public record AssignableRoleDto(string Name, bool Grantable, string? Reason);
public record RoleDto(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount, bool CanEdit);
public record PermissionDto(string Key, string Group, string Label, string Description);
public record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
```

- [ ] **Step 4: Show ungrantable roles disabled**

Replace the whole of `src/PeopleCore.Web/Components/Accounts/RoleCheckboxes.razor` with:

```razor
@*
    Tick-boxes for the roles an account could be given. Employee is on every account, so it is shown
    ticked and cannot be unticked. A role the signed-in user may not grant - one that allows more than
    they can do, or Admin for a non-admin - is shown disabled with the reason, rather than hidden, so
    it is clear why it can't be ticked.
*@

<div class="space-y-2" data-role-checkboxes>
    @foreach (var role in Roles)
    {
        var locked = role.Name == EmployeeRole;
        <Checkbox Value="@(locked || Selected.Contains(role.Name))"
                  Disabled="@(locked || Disabled || !role.Grantable)"
                  ValueChanged="@(on => Toggle(role.Name, on))"
                  data-role="@role.Name"
                  title="@(role.Grantable ? null : role.Reason)">
            @Label(role.Name)
            @if (locked)
            {
                <span class="ml-1 text-xs font-normal text-muted-foreground">(every account)</span>
            }
        </Checkbox>
    }
</div>

@code {
    private const string EmployeeRole = "Employee";

    [Parameter] public IReadOnlyList<AssignableRoleDto> Roles { get; set; } = [];

    /// <summary>The ticked role names. Changed in place, so the owner reads it when saving.</summary>
    [Parameter] public HashSet<string> Selected { get; set; } = [];

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Friendlier names for the seeded roles; any other role shows as it is named.</summary>
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

- [ ] **Step 5: Use the new shape on the Users page and in the create dialog**

In `src/PeopleCore.Web/Components/Accounts/CreateAccountDialog.razor`, replace `private IReadOnlyList<string> _assignable = [];` with:

```csharp
    private IReadOnlyList<AssignableRoleDto> _assignable = [];
```

In `src/PeopleCore.Web/Pages/Admin/Users.razor`:

1. Replace `private IReadOnlyList<string> _assignable = [];` with `private IReadOnlyList<AssignableRoleDto> _assignable = [];`.
2. In `SaveRoles`, replace:

```csharp
        // Only roles this user may grant are sent; any others the account holds are the API's to keep.
        var roles = _rolesSelected.Where(r => _assignable.Contains(r)).Append("Employee").Distinct().ToList();
```

with:

```csharp
        // Only roles an account can be given are sent; one it holds that can't be (Service) is the API's to keep.
        var assignable = _assignable.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = _rolesSelected.Where(assignable.Contains).Append("Employee").Distinct().ToList();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests`
Expected: all pass. That includes the unchanged `EditingRoles_SendsTheTickedRoles_AlwaysWithEmployee` and the create-login tests on the Employees page.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): call the roles API, and show roles the user can't grant as disabled"
```

---

### Task 6: The Roles page

**Files:**
- Create: `src/PeopleCore.Web/Components/Accounts/RoleEditorDialog.razor`
- Create: `src/PeopleCore.Web/Pages/Admin/Roles.razor`
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`
- Modify: `src/PeopleCore.Web/Layout/MobileFooterNav.razor`
- Test: `tests/PeopleCore.Web.Tests/Pages/Admin/RolesTests.cs`
- Modify test: `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`

**Interfaces:**
- Consumes:
  - `ApiClient` role methods and records (Task 5).
  - `RoleCheckboxes.Label` (Task 5).
  - `Permissions`, `RequirePermissionAttribute`, `PermissionView` and `SeededPermissions` (phase 1).
- Produces:
  - Page `/admin/roles`.
  - Component `RoleEditorDialog`, with these parameters:
    - `bool IsOpen`
    - `RoleDto? Role` (null means a new role)
    - `IReadOnlyList<PermissionDto> Catalogue`
    - `IReadOnlyCollection<string> HeldPermissions`
    - `bool Busy`
    - `string? Error`
    - `EventCallback<SaveRoleRequest> OnSave`
    - `EventCallback OnClose`
  - Test hooks:
    - the form is `form[data-role-editor]`;
    - the inputs are `#role-name` and `#role-description`;
    - each permission tick-box carries `data-permission="<key>"`;
    - errors show in `[data-role-editor-error]`;
    - the table rows carry `data-role-row="<name>"`;
    - failed actions show in `[data-action-error]`.

- [ ] **Step 1: Write the failing page tests**

Create `tests/PeopleCore.Web.Tests/Pages/Admin/RolesTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

public class RolesTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private readonly Bunit.TestDoubles.BunitAuthorizationContext _auth;

    private const string Catalogue = """
        [{"key":"recruitment.manage","group":"Recruitment","label":"Manage recruitment","description":"Job postings, applicants and interviews."},
         {"key":"analytics.executive","group":"Analytics","label":"Executive analytics","description":"Workforce summary."}]
        """;

    private static string Role(string id, string name, bool system, string[] permissions, int accounts, bool canEdit) =>
        $$"""{"id":"{{id}}","name":"{{name}}","description":"About {{name}}.","isSystem":{{(system ? "true" : "false")}},"permissions":[{{string.Join(",", permissions.Select(p => $"\"{p}\""))}}],"accountCount":{{accounts}},"canEdit":{{(canEdit ? "true" : "false")}}}""";

    private static readonly string TwoRoles =
        $"[{Role("admin", "Admin", true, ["recruitment.manage", "analytics.executive"], 1, false)},{Role("r1", "Recruiter", false, ["recruitment.manage"], 3, true)}]";

    public RolesTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("admin@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("Admin"));
        _api.On(HttpMethod.Get, "/api/roles/permissions", HttpStatusCode.OK, Catalogue);
    }

    private IRenderedComponent<Roles> RenderPage(string roles)
    {
        _api.On(HttpMethod.Get, "/api/roles", HttpStatusCode.OK, roles);
        var cut = Render<Roles>();
        cut.WaitForAssertion(() => cut.FindAll("[data-role-row]").Should().NotBeEmpty());
        return cut;
    }

    private static IElement ButtonIn(IElement scope, string text) =>
        scope.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    [Fact]
    public void EveryRole_IsListed_WithItsDescription_AccountCount_AndASystemBadge()
    {
        var cut = RenderPage(TwoRoles);

        cut.Find("[data-role-row=Admin]").TextContent.Should().Contain("System").And.Contain("About Admin.");
        cut.Find("[data-role-row=Recruiter]").TextContent.Should().Contain("3").And.NotContain("System");
    }

    [Fact]
    public void ASystemRole_OpensReadOnly()
    {
        var cut = RenderPage(TwoRoles);

        ButtonIn(cut.Find("[data-role-row=Admin]"), "View").Click();

        cut.FindAll("[data-role-editor] [data-permission]").Should().OnlyContain(b => b.HasAttribute("disabled"));
        cut.Find("[data-role-editor]").QuerySelectorAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Role");
        ButtonIn(cut.Find("[data-role-row=Admin]"), "Delete").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void CreatingARole_SendsItsNameDescriptionAndPermissions_AndReloadsTheList()
    {
        _api.On(HttpMethod.Post, "/api/roles", HttpStatusCode.Created, Role("r2", "Auditor", false, ["analytics.executive"], 0, true));
        var cut = RenderPage(TwoRoles);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").Click();
        cut.Find("#role-name").Input("Auditor");
        cut.Find("#role-description").Input("Reads the numbers.");
        cut.Find("[data-permission='analytics.executive']").Click();
        cut.Find("form[data-role-editor]").Submit();

        cut.WaitForAssertion(() => cut.FindAll("form[data-role-editor]").Should().BeEmpty());
        var index = _api.Requests.FindIndex(r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
        body.GetProperty("name").GetString().Should().Be("Auditor");
        body.GetProperty("description").GetString().Should().Be("Reads the numbers.");
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("analytics.executive");
        _api.Requests.Count(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/roles").Should().Be(2);
    }

    [Fact]
    public void APermissionTheCallerLacks_CannotBeTicked()
    {
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
        var cut = RenderPage(TwoRoles);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").Click();

        cut.Find("[data-permission='analytics.executive']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-permission='recruitment.manage']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ASaveTheApiRefuses_ShowsItsReason_InTheDialog()
    {
        _api.On(HttpMethod.Put, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"A role called Payroll already exists."}""");
        var cut = RenderPage(TwoRoles);

        ButtonIn(cut.Find("[data-role-row=Recruiter]"), "Edit").Click();
        cut.Find("#role-name").Input("Payroll");
        cut.Find("form[data-role-editor]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-role-editor-error]").TextContent.Should().Contain("A role called Payroll already exists."));
    }

    [Fact]
    public void DeletingARoleStillInUse_AfterConfirming_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"3 accounts still have Recruiter — remove it from them first."}""");
        var cut = RenderPage(TwoRoles);

        ButtonIn(cut.Find("[data-role-row=Recruiter]"), "Delete").Click();
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Delete").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should().Contain("3 accounts still have Recruiter"));
    }
}
```

In `tests/PeopleCore.Web.Tests/Layout/NavMenuTests.cs`:

1. In `AnAdmin_SeesEverySection`, change `HaveCount(17)` to `HaveCount(18)`.
2. Add this test:

```csharp
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HRManager", false)]
    [InlineData("Manager", false)]
    public void TheRolesPage_IsListedOnlyForThoseWhoManageRoles(string role, bool listed)
    {
        Links(RenderAs(role)).Contains("/admin/roles").Should().Be(listed);
    }
```

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~RolesTests|FullyQualifiedName~NavMenuTests"`
Expected: build error, `The type or namespace name 'Roles' does not exist in the namespace 'PeopleCore.Web.Pages.Admin'`.

- [ ] **Step 2: Write the editor dialog**

Create `src/PeopleCore.Web/Components/Accounts/RoleEditorDialog.razor`:

```razor
@*
    Create or change a role: its name, description and permissions. A permission the signed-in user
    doesn't hold can't be ticked - the API refuses it anyway - and a role they may not change opens
    read-only, so they can still see what it allows.
*@

<Dialog IsOpen="@IsOpen" OnClose="Close" Title="@Title" Description="@Subtitle" Size="lg">
    <ChildContent>
        <form class="space-y-4" data-role-editor @onsubmit="Submit" @onsubmit:preventDefault>
            @if (Error is not null)
            {
                <Alert Variant="destructive" data-role-editor-error>@Error</Alert>
            }

            <div class="grid gap-4 sm:grid-cols-2">
                <FormField LabelText="Name" Id="role-name">
                    <Input TValue="string" Id="role-name" @bind-Value="_name" MaxLength="50" Disabled="@(ReadOnly || Busy)" />
                </FormField>
                <FormField LabelText="Description" Id="role-description">
                    <Input TValue="string" Id="role-description" @bind-Value="_description" MaxLength="200" Disabled="@(ReadOnly || Busy)" />
                </FormField>
            </div>

            <div class="space-y-4">
                @foreach (var group in Catalogue.GroupBy(p => p.Group))
                {
                    <fieldset class="space-y-2">
                        <legend class="text-sm font-semibold">@group.Key</legend>
                        @foreach (var permission in group)
                        {
                            var held = HeldPermissions.Contains(permission.Key);
                            <Checkbox Value="@_permissions.Contains(permission.Key)"
                                      Disabled="@(ReadOnly || Busy || !held)"
                                      ValueChanged="@(on => Toggle(permission.Key, on))"
                                      data-permission="@permission.Key"
                                      title="@(held ? permission.Description : "You don't have this permission yourself.")">
                                <span>@permission.Label</span>
                                <span class="block text-xs font-normal text-muted-foreground">@permission.Description</span>
                            </Checkbox>
                        }
                    </fieldset>
                }
            </div>

            @if (!ReadOnly)
            {
                <p class="text-xs text-muted-foreground">Changing permissions signs out everyone who has this role, so they pick up the change straight away.</p>
            }
            <div class="flex justify-end gap-2 pt-2">
                <Button Variant="outline" Size="sm" OnClick="Close">@(ReadOnly ? "Close" : "Cancel")</Button>
                @if (!ReadOnly)
                {
                    <Button Type="submit" Variant="primary" Size="sm" Loading="@Busy">Save Role</Button>
                }
            </div>
        </form>
    </ChildContent>
</Dialog>

@code {
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>The role being changed, or null for a new one.</summary>
    [Parameter] public RoleDto? Role { get; set; }

    [Parameter] public IReadOnlyList<PermissionDto> Catalogue { get; set; } = [];

    /// <summary>What the signed-in user may do; only these can be ticked.</summary>
    [Parameter] public IReadOnlyCollection<string> HeldPermissions { get; set; } = [];

    [Parameter] public bool Busy { get; set; }
    [Parameter] public string? Error { get; set; }
    [Parameter] public EventCallback<SaveRoleRequest> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private string _name = string.Empty;
    private string _description = string.Empty;
    private HashSet<string> _permissions = [];
    private bool _wasOpen;

    private bool ReadOnly => Role is { CanEdit: false };

    private string Title => Role is null ? "New role" : ReadOnly ? Role.Name : $"Edit {Role.Name}";

    private string? Subtitle => Role switch
    {
        { IsSystem: true } => "System roles can't be changed.",
        { CanEdit: false } => "This role allows things you can't do yourself, so you can't change it.",
        _ => null
    };

    protected override void OnParametersSet()
    {
        var opening = IsOpen && !_wasOpen;
        _wasOpen = IsOpen;
        if (!opening) return;

        // Start from the role being edited, or blank - never from whatever was open last.
        _name = Role?.Name ?? string.Empty;
        _description = Role?.Description ?? string.Empty;
        _permissions = Role?.Permissions.ToHashSet() ?? [];
    }

    private void Toggle(string key, bool on)
    {
        if (on) _permissions.Add(key);
        else _permissions.Remove(key);
    }

    private Task Submit() =>
        OnSave.InvokeAsync(new SaveRoleRequest(
            _name.Trim(),
            string.IsNullOrWhiteSpace(_description) ? null : _description.Trim(),
            Catalogue.Select(p => p.Key).Where(_permissions.Contains).ToList()));

    private Task Close() => OnClose.InvokeAsync();
}
```

- [ ] **Step 3: Write the page**

Create `src/PeopleCore.Web/Pages/Admin/Roles.razor`:

```razor
@page "/admin/roles"
@attribute [RequirePermission(Permissions.RolesManage)]
@inject ApiClient Api

<PageTitle>Roles</PageTitle>

<PageHeader Title="Roles" Section="Administration" Description="What each role allows. An account can hold several roles and may do everything they allow together.">
    <Actions>
        <Button Variant="primary" OnClick="OpenNew">
            <i class="bi bi-plus-lg mr-1"></i> New Role
        </Button>
    </Actions>
</PageHeader>

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
        else if (_roles is null)
        {
            <div class="p-6 flex justify-center"><Spinner /></div>
        }
        else
        {
            <Table>
                <TableHeader>
                    <TableRow>
                        <TableHead Class="px-4">Role</TableHead>
                        <TableHead Class="px-4">Permissions</TableHead>
                        <TableHead Class="px-4">Accounts</TableHead>
                        <TableHead Class="px-4">Actions</TableHead>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    @foreach (var role in _roles)
                    {
                        <TableRow data-role-row="@role.Name">
                            <TableCell Class="px-4 py-3">
                                <div class="flex items-center gap-2 font-medium">
                                    @RoleCheckboxes.Label(role.Name)
                                    @if (role.IsSystem)
                                    {
                                        <Badge Variant="secondary">System</Badge>
                                    }
                                </div>
                                <div class="text-xs text-muted-foreground">@role.Description</div>
                            </TableCell>
                            <TableCell Class="px-4 py-3 text-muted-foreground">@role.Permissions.Count of @_catalogue.Count</TableCell>
                            <TableCell Class="px-4 py-3">@role.AccountCount</TableCell>
                            <TableCell Class="px-4 py-3">
                                <div class="flex gap-1">
                                    <Button Variant="ghost" Size="sm" OnClick="() => OpenExisting(role)">@(role.CanEdit ? "Edit" : "View")</Button>
                                    <Button Variant="ghost" Size="sm" Disabled="@(!role.CanEdit)"
                                            Title="@(role.CanEdit ? null : role.IsSystem ? "System roles can't be changed." : "This role allows things you can't do yourself.")"
                                            OnClick="() => _deleting = role">Delete</Button>
                                </div>
                            </TableCell>
                        </TableRow>
                    }
                </TableBody>
            </Table>
        }
    </CardContent>
</Card>

<RoleEditorDialog IsOpen="@_editorOpen"
                  Role="@_editing"
                  Catalogue="@_catalogue"
                  HeldPermissions="@_held"
                  Busy="@_busy"
                  Error="@_dialogError"
                  OnSave="Save"
                  OnClose="CloseEditor" />

<AlertDialog IsOpen="@(_deleting is not null)"
             Title="@($"Delete {_deleting?.Name}?")"
             Description="This can't be undone."
             ConfirmText="Delete"
             ConfirmVariant="destructive"
             Loading="@_busy"
             OnConfirm="DeleteConfirmed"
             OnCancel="() => _deleting = null" />

@code {
    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    private IReadOnlyList<RoleDto>? _roles;
    private IReadOnlyList<PermissionDto> _catalogue = [];
    private IReadOnlyCollection<string> _held = [];
    private string? _loadError;
    private string? _actionError;
    private string? _dialogError;
    private bool _busy;
    private bool _editorOpen;
    private RoleDto? _editing;
    private RoleDto? _deleting;

    protected override async Task OnInitializedAsync()
    {
        var state = AuthenticationState is null ? null : await AuthenticationState;
        _held = state?.User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToHashSet() ?? [];

        await Task.WhenAll(LoadRoles(), LoadCatalogue());
    }

    private async Task LoadRoles()
    {
        try
        {
            _roles = await Api.GetRolesAsync() ?? [];
            _loadError = null;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
    }

    private async Task LoadCatalogue()
    {
        try
        {
            _catalogue = await Api.GetPermissionCatalogueAsync() ?? [];
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
    }

    private void OpenNew()
    {
        _dialogError = null;
        _editing = null;
        _editorOpen = true;
    }

    private void OpenExisting(RoleDto role)
    {
        _dialogError = null;
        _editing = role;
        _editorOpen = true;
    }

    private void CloseEditor() => _editorOpen = false;

    private async Task Save(SaveRoleRequest request)
    {
        _busy = true;
        _dialogError = null;
        try
        {
            if (_editing is null) await Api.CreateRoleAsync(request);
            else await Api.UpdateRoleAsync(_editing.Id, request);

            _editorOpen = false;
            await LoadRoles();
        }
        catch (Exception ex)
        {
            _dialogError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteConfirmed()
    {
        if (_deleting is not { } role) return;

        _busy = true;
        _actionError = null;
        try
        {
            await Api.DeleteRoleAsync(role.Id);
            await LoadRoles();
        }
        catch (Exception ex)
        {
            _actionError = ex.Message;
        }
        finally
        {
            _busy = false;
            _deleting = null;
        }
    }
}
```

- [ ] **Step 4: Add Roles to both menus**

In `src/PeopleCore.Web/Layout/NavMenu.razor`:

1. Replace `private sealed record NavEntry(string Href, string Label, string Icon, bool Exact = false);` with:

```csharp
    /// <summary>A menu link, shown to anyone who can see its section - or, when <paramref name="AnyOf"/> is set, only to holders of one of those.</summary>
    private sealed record NavEntry(string Href, string Label, string Icon, bool Exact = false, string[]? AnyOf = null);
```

2. In `RenderSection`, wrap each entry. Replace the `@foreach (var entry in section.Entries) { <ShadcnSidebarMenuItem> ... </ShadcnSidebarMenuItem> }` loop with the version below, moving the existing `<ShadcnSidebarMenuItem>` block unchanged into a new `RenderEntry` fragment:

```razor
                    @foreach (var entry in section.Entries)
                    {
                        if (entry.AnyOf is null)
                        {
                            @RenderEntry(entry)
                        }
                        else
                        {
                            <PermissionView AnyOf="@entry.AnyOf">@RenderEntry(entry)</PermissionView>
                        }
                    }
```

```csharp
    private RenderFragment RenderEntry(NavEntry entry) =>
        @<ShadcnSidebarMenuItem>
            @* the existing ShadcnSidebarMenuButton markup, moved here unchanged *@
        </ShadcnSidebarMenuItem>;
```

3. Replace the Administration section with:

```csharp
        new("Administration", [Permissions.UsersManage, Permissions.RolesManage],
        [
            new("/admin/users", "Users", "bi-person-gear", AnyOf: [Permissions.UsersManage]),
            new("/admin/roles", "Roles", "bi-shield-lock", AnyOf: [Permissions.RolesManage])
        ])
```

In `src/PeopleCore.Web/Layout/MobileFooterNav.razor`, replace the Administration group with:

```csharp
        new("Administration",
        [
            new("admin/users", "Users", [Permissions.UsersManage]),
            new("admin/roles", "Roles", [Permissions.RolesManage])
        ])
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.Web.Tests`
Expected: all pass, including the six `RolesTests` and the updated navigation tests.


- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): add, change and delete roles from an Administration page"
```

---

### Task 7: Whole-branch verification

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

- [ ] **Step 2: Confirm nothing still refers to the deleted role lists**

Run: `grep -rnE "AccountRoles|Privileged|IsPrivileged" src tests --include=*.cs --include=*.razor`
Expected: no output.

- [ ] **Step 3: Smoke-test against a throwaway database**

Start the API with `preview_start` against a fresh database named `peoplecore_roles_smoke`. Use `--Seed:AdminPassword=SmokeAdmin2026`. Never point it at the developer's `peoplecore` database. Don't commit the launch entry.

Then, over HTTP:

1. **Admin creates two roles.** `Recruiter` gets `recruitment.manage`. `Auditor` gets `analytics.executive`. Both come back 201.
2. **HR's view of assignable roles.** Create an `HRManager` account and sign it in; its temporary password must be changed first. `GET api/users/assignable-roles` shows Auditor and Admin as not grantable, with reasons, and Recruiter as grantable.
3. **HR assigns roles.** HR creates an account with `Recruiter`: 201. HR creates an account with `Auditor`: 403, with the Auditor reason.
4. **HR on the Roles API.** HR calls `GET api/roles`: 403, because HR lacks `roles.manage`.
5. **Editing a held role signs its holders out.** Admin changes `Recruiter` to `recruitment.manage` plus `analytics.hr`. The Recruiter account's token now gets 401.
6. **Deleting a role in use.** Admin deletes `Recruiter` while the account still holds it: 400, with "1 account still has Recruiter — remove it from them first."
7. **Deleting an unused role.** Admin deletes the unused `Auditor`: 204.
8. **System roles.** Admin tries to update `Employee`: 403 "System roles can't be changed."
9. **Deleted roles stay deleted.**
   - Admin removes every account from `Manager`, if any hold it, then deletes `Manager`: 204.
   - Stop and start the API.
   - `GET api/roles` must not list `Manager`. `Admin`, `Employee` and `Service` are still there.

Stop the server afterwards.

- [ ] **Step 4: Confirm the tree is clean**

Run: `git status --short`
Expected: empty.

---

## Spec coverage (phase 2)

| Spec requirement | Task |
|---|---|
| Granting: subset rule, Admin-only Admin | 3 |
| Granting: managing an account requires covering its permissions | 3 |
| Unchanged guards (self-deactivate, self-reset, last Admin); `users.manage` self-lockout | 3 |
| `assignable-roles` returns name, grantable, reason | 3, 5 |
| Roles API: list, permission catalogue, create, update, delete, with every rule | 4 |
| Only permissions the caller holds; only roles the caller covers; system roles locked | 4 |
| Delete refused while held, with the exact message | 4 |
| Permission change revokes holders' tokens; rename does not | 2 |
| Roles page: table, System badge, account counts, editor grouped by catalogue group | 6 |
| Permissions the actor lacks disabled; system and beyond roles read-only | 6 |
| Users page shows ungrantable roles disabled with the reason | 5 |
| Nav entry Roles (`bi-shield-lock`) under Administration | 6 |
| Seeder never recreates deleted roles (phase 1 final-review finding) | 1 |
| Phase 1 carry-overs: `approvals.all` wording, `MapInboundClaims` in the token test | 1 |
