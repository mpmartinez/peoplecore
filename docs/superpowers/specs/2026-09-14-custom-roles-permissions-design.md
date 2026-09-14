# Custom Roles & Permissions — Design

**Date:** 2026-09-14
**Status:** Approved for planning
**Builds on:** `2026-09-13-user-role-management-design.md` (shipped to `main`)

## Context

Accounts can now be given roles, but the roles themselves are fixed. What each role may do is
hard-coded: 54 `[Authorize(Roles = "...")]` checks across 22 API controllers, about 25 role checks in
the Blazor client (page guards, `NavMenu`, `MobileFooterNav`, `Dashboard`, `AnalyticsDashboard`),
`EmployeesController`'s in-method `HrRoles`/`PayrollReadRoles` checks, `EmployeeAccessService`'s
HR-versus-Manager scoping, and `AccountManagementPolicy`'s "privileged role" rule. A role created at
runtime could never appear in any of them.

**Goal:** an Admin can add, rename, edit and delete roles and choose what each one may do. The app
checks *permissions*, never role names.

## Decisions (made with the owner)

| Question | Decision |
|---|---|
| Granularity | A fixed catalogue of feature permissions, one per area of the app |
| Built-in roles | Admin locked (always every permission); the others become ordinary, editable roles |
| Granting limits | Nobody grants beyond their own powers (see Granting) |
| Deleting a role in use | Refused until no account holds it |
| Mechanism | Permissions stored as role claims and carried in the JWT; editing a role revokes its holders' tokens |

## Scope

### In

- A permission catalogue and a `[RequirePermission]` attribute replacing every role-name check.
- Roles as records: name, description, permissions, system flag.
- `/admin/roles` page: list, create, edit, delete.
- The granting rule generalised from "privileged roles" to "no granting beyond your own powers".
- Seeding that gives every existing role exactly today's access.

### Out

- **Per-record or field-level permissions** (e.g. "HR for the Manila office only"). Scoping stays as
  today: everyone, direct reports, or self.
- **Permissions assigned directly to accounts.** Only through roles.
- **An editor for the permission catalogue.** Permissions name code paths; adding one is a code change.
- **Audit log of role changes.** As in the previous spec, no audit infrastructure exists yet.

## Delivery

Two phases, each with its own implementation plan, each shippable alone.

1. **Permissions under the hood.** Catalogue, storage, seeding, token claims, `[RequirePermission]`,
   every *access* check converted. No visible change: every account keeps exactly its current access.
   `AccountManagementPolicy`'s role-name granting rule is left as it is in this phase (roles cannot
   be renamed yet, so its names stay valid); phase 2 replaces it.
2. **Managing roles.** Roles API and page; the new granting rule on the Users page.

## Permission catalogue

Keys are stable strings; labels and groups are for the UI. Seeding lists the roles that receive each
permission (Admin receives every permission implicitly — see Roles).

| Key | Group | Label | Grants | Seeded to |
|---|---|---|---|---|
| `employees.view-all` | People | View all employees | Any employee's full profile, government IDs, emergency contacts and documents; unfiltered attendance, leave, overtime and schedule records | HR Manager |
| `employees.manage` | People | Manage employees | Create, edit and deactivate employees; edit any employee's government IDs, emergency contacts and documents; delete documents | HR Manager |
| `organization.manage` | Organisation | Manage departments, positions and teams | Create and edit them | HR Manager |
| `organization.delete` | Organisation | Delete departments, positions and teams | Delete them | — (Admin only) |
| `attendance.manage` | Time | Manage attendance and holidays | Import attendance; create and delete holidays | HR Manager |
| `attendance.device-sync` | Time | Sync attendance devices | `POST api/attendance/sync` | HR Manager, Service |
| `leave.manage` | Time | Manage leave types and accrual policies | Leave types and accrual policy CRUD | HR Manager |
| `leave.run-accruals` | Time | Run leave accruals | `POST api/leave-accruals/run-manual` | — (Admin only) |
| `approvals.team` | Approvals | Approve for my team | Decide direct reports' leave and overtime; create and submit their performance reviews; see their records | Manager |
| `approvals.all` | Approvals | Approve for everyone | The same for every employee | HR Manager |
| `performance.manage` | Performance | Manage review cycles | Create and close review cycles | HR Manager |
| `payroll.manage` | Payroll | Run payroll | Payroll runs, settings, compensation, BIR 2316, payroll export, any employee's payslips, full employee list | HR Manager, Payroll |
| `recruitment.manage` | Recruitment | Manage recruitment | Job postings, applicants and interviews | HR Manager |
| `scheduling.manage` | Scheduling | Manage schedules | Shift templates, rotating patterns and assignments | HR Manager |
| `analytics.hr` | Analytics | HR analytics | `api/analytics/hr/*` | HR Manager |
| `analytics.executive` | Analytics | Executive analytics | `api/analytics/executive/*` | — (Admin only) |
| `users.manage` | Administration | Manage users | `api/users/*` and the Users page | HR Manager |
| `roles.manage` | Administration | Manage roles | `api/roles/*` and the Roles page (phase 2) | — (Admin only) |

"HR Manager", "Manager" and "Payroll" are the existing `HRManager`, `Manager` and `PayrollService`
roles; their names are unchanged by seeding and can be renamed afterwards.

### Endpoint mapping

Every role list in use today maps to one permission expression, so seeding reproduces current access
exactly. Actions that are open to any signed-in user today (self-service) are untouched.

| Today | Becomes |
|---|---|
| `Admin,HRManager` on users, HR analytics, leave accrual policies, leave types, holidays, attendance import, scheduling, recruitment writes, review cycles, employee create/update/deactivate/document delete, department/position/team create/update | the matching permission from the catalogue |
| `Admin` on executive analytics, run accruals, department/position/team delete | `analytics.executive`, `leave.run-accruals`, `organization.delete` |
| `Admin,HRManager,Manager` on leave and overtime approve/reject, performance review create and manager-review | `approvals.team` **or** `approvals.all` (the service then scopes as below) |
| `Admin,HRManager,PayrollService` on payroll runs, settings, compensation, 2316, export, others' payslips | `payroll.manage` |
| `Admin,HRManager,Service` on attendance sync | `attendance.device-sync` |
| `EmployeesController` `HrRoles` on reads of government IDs, emergency contacts, documents, download URL | self **or** `employees.view-all` |
| `EmployeesController` `HrRoles` on writes of government IDs, emergency contacts, documents | self **or** `employees.manage` |
| `EmployeesController` `PayrollReadRoles` (full list and full profile) | `employees.view-all` **or** `payroll.manage` |

`EmployeeAccessService` changes meaning, not shape:

- `CanView(employee)` — self, **or** `employees.view-all`, **or** (`approvals.team` **and** a direct report).
- `CanManage(employee)` (decide requests) — `approvals.all`, **or** (`approvals.team` **and** a direct report).
- Unfiltered list scope — everyone with `employees.view-all` or `approvals.all`; direct reports with `approvals.team`; otherwise denied.
- `IsHrStaff` is replaced by `CanViewEveryone` (`employees.view-all` or `approvals.all`).

HR Manager holds both `employees.view-all` and `approvals.all`, Manager holds `approvals.team`, so
every existing account sees and decides exactly what it does today.

## Roles

`ApplicationRole : IdentityRole` gains:

| Field | Type | Purpose |
|---|---|---|
| `Description` | `string?` (max 200) | Shown on the Roles page |
| `IsSystem` | `bool` | Locked: cannot be renamed, edited or deleted |

`AppDbContext` becomes `IdentityDbContext<ApplicationUser, ApplicationRole, string>`. Permissions are
rows in the existing `AspNetRoleClaims` table with claim type `permission`. One migration adds the two
columns; seeding (below) fills them.

**System roles:**

| Role | Permissions | Assignable in the UI |
|---|---|---|
| Admin | Every permission, computed — never stored, so a permission added in a future release applies to Admin automatically | Yes, by Admins only |
| Employee | None; the self-service baseline every account holds | Always present, cannot be removed (as today) |
| Service | `attendance.device-sync` only | No (as today) |

**Ordinary roles** (seeded, then fully editable): HR Manager, Manager, Payroll — with the permissions
in the catalogue table.

Role names are unique (case-insensitive), 1–50 characters. Accounts reference roles by id, so a
rename is safe; the role names in a token are only shown in the UI.

### Seeding

On startup, after the existing role seeding:

- Ensure the six roles exist, mark Admin, Employee and Service `IsSystem`.
- **Only when a role has no permission claims yet** (first run of this release), give it its seeded
  permissions. After that the database is the authority — seeding never overwrites an edited role.

## Tokens and enforcement

- `IssueTokenAsync` adds one `permission` claim per effective permission: the union across the
  account's roles, or the whole catalogue if it holds Admin. It also adds `perm_v` = `1`.
- `AccountTokenValidator` additionally rejects a token with no `perm_v` claim, so tokens issued
  before phase 1 fail (401) and everyone signs in once — as with `sec_stamp`.
- `[RequirePermission(params string[] anyOf)]` derives from `AuthorizeAttribute` and sets
  `Policy = "permission:" + string.Join("|", anyOf)`. A `PermissionPolicyProvider`
  (`IAuthorizationPolicyProvider`, falling back to the default provider for other names) builds a
  policy requiring an authenticated user with a `permission` claim equal to any listed key. Because
  the attribute is still `IAuthorizeData`, `ControllerAuthorizationCoverageTests` and
  `PasswordChangeRequiredFilter` keep working unchanged.
- `ICurrentUserService` gains `HasPermission(string key)`; `IsInRole` is no longer used for
  authorization anywhere.
- **Changes take effect immediately:** editing a role's permissions (phase 2) replaces the security
  stamp of every account holding it, so their next request is rejected and their next sign-in
  carries the new permissions.

### Web client

- A client copy of the permission keys (`PeopleCore.Web/Auth/Permissions.cs`), following the
  existing client-DTO-copy pattern, and the same `[RequirePermission]` attribute and policy provider
  registered in the WebAssembly host.
- Page guards use `[RequirePermission(...)]`; `NavMenu` and `MobileFooterNav` sections carry a
  permission expression instead of a role list; `AuthorizeView Policy="..."` replaces
  `AuthorizeView Roles="..."`; `Dashboard` and `AnalyticsDashboard` read permission claims.
- A test in `PeopleCore.Application.Tests` reads `PeopleCore.Web/Auth/Permissions.cs` as text and
  fails if its keys differ from the API catalogue, so the copies cannot drift.

## Granting (phase 2)

`AccountManagementPolicy` replaces "privileged roles" with permission sets. For an actor holding
`users.manage`:

- **Effective permissions** of an account = union of its roles' permissions (all, for Admin).
- **Grant or remove role R** only if the actor's permissions include all of R's, and R is not Admin
  unless the actor holds Admin.
- **Change account A at all** (roles, link, deactivate, reset) only if the actor's permissions
  include all of A's effective permissions, and A does not hold Admin unless the actor does.
- **Assignable roles** returned to the UI: every role satisfying the grant rule, excluding Service.
  Employee is always present and cannot be removed.
- **Unchanged guards:** no self-deactivation, no self-reset; the last active Admin can't lose Admin or
  be deactivated. "You can't remove your own Admin or HR Manager role" becomes: a caller cannot remove
  a role from themselves if doing so would remove their own `users.manage`.

**Behaviour change versus today, accepted with this rule.** HR still cannot touch an Admin account or
grant Admin. But because HR Manager holds every permission the HR Manager role contains, an HR
Manager *can* now grant the HR Manager role to another account and manage other HR Managers' accounts
— today only an Admin can. Anyone who should not be able to do that must hold a role with fewer
permissions than HR Manager. The same reasoning covers any custom role that holds `users.manage`.

`GET api/users/assignable-roles` returns `[{ name, grantable, reason }]` for every role except
Service, so the Users page can show ungrantable roles disabled with the reason.

## Roles API (phase 2) — `api/roles`

`[RequirePermission("roles.manage")]`.

| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/` | — | `RoleDto[]` ordered: system roles first, then by name |
| GET | `/permissions` | — | `PermissionDto[]` (key, label, group, description) in catalogue order |
| POST | `/` | `SaveRoleRequest` | 201, `RoleDto` |
| PUT | `/{id}` | `SaveRoleRequest` | `RoleDto` |
| DELETE | `/{id}` | — | 204 |

`RoleDto`: `Id`, `Name`, `Description`, `IsSystem`, `Permissions` (keys), `AccountCount`, `CanEdit`.
`SaveRoleRequest`: `Name`, `Description`, `Permissions` (keys).

Rules, each a 400 or 403 `ProblemDetails` with a sentence the page shows as it is:

- System roles cannot be edited or deleted (403).
- Unknown permission keys (400). Duplicate or empty name, name over 50, description over 200 (400).
- An actor may only tick permissions they hold themselves (403) — consistent with the granting rule.
- Delete refused while held: "12 accounts still have Recruiter — remove it from them first." (400).
- A permission change replaces the security stamp of every holder; a rename or description change
  does not.

## Roles page (phase 2) — `/admin/roles`

`[RequirePermission("roles.manage")]`; nav entry **Roles** (`bi-shield-lock`) under Administration.

- Table: name (with a "System" badge), description, permission count, accounts holding it.
- **New role** and **Edit** open a dialog: name, description, and permission tick-boxes grouped by
  the catalogue's groups, each with its label and grant description. Permissions the actor lacks are
  disabled. System roles open read-only.
- **Delete** asks for confirmation; the API's refusal (role in use) is shown as it is.
- On the Users page, role tick-boxes list every role from `assignable-roles`, ungrantable ones
  disabled with their reason as a tooltip.

## Testing

- **Endpoint mapping test** — enumerates every controller action and asserts its exact permission
  expression against a table; fails on any unmapped or changed action.
- **Access equivalence test** — for each seeded role (Admin, HR Manager, Manager, Payroll, Employee,
  Service), the set of actions it can reach under permissions equals the set it could reach under
  the old role lists (encoded in the test from the table in this spec).
- **Policy provider and attribute** — any-of matching, unauthenticated rejection, fallback to default
  policies.
- **Token** — permission claims equal the union across roles; Admin gets the whole catalogue;
  missing `perm_v` rejected.
- **`EmployeeAccessService`** — the three scoping rules under each permission combination.
- **Seeding** (Postgres) — first run grants seeded permissions; a later run leaves edited roles alone.
- **Granting policy** — subset rule, Admin-only Admin, self-lockout on `users.manage`, last Admin.
- **Roles API** — each rule above, including stamp rotation for holders on permission change only.
- **Web (bUnit)** — nav and page guards by permission; Roles page create/edit/delete, read-only
  system roles, disabled permissions the actor lacks; Users page disabled ungrantable roles;
  client/API permission catalogue equality.
