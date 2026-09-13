# User & Role Management — Design

**Date:** 2026-09-13
**Status:** Approved for planning

## Context

Startup seeds six roles (`Admin`, `HRManager`, `Manager`, `Employee`, `PayrollService`, `Service`)
and one account, the admin, which is the only account that ever receives a role. About 25
controllers and the web navigation are gated on those roles, but nothing creates an account,
assigns or removes a role, or links a login to an employee. `ApplicationUser.EmployeeId` exists and
login already emits an `employee_id` claim from it — nothing ever sets it.

In practice only the seeded admin can sign in with a role, so every role-scoped feature built so far
(manager scoping, approvals, payroll access) is unreachable for real users.

**Goal:** let Admin and HR create accounts, link them to employees, manage their roles, and revoke
access — with changes taking effect immediately.

## Scope

### In

- Account list, create, role editing, employee linking, deactivate/reactivate, password reset.
- A permission matrix that limits HRManager to non-privileged accounts and roles.
- Server-generated temporary passwords, shown once, with a forced change at first sign-in.
- Per-request security-stamp validation so role changes and deactivation apply immediately.
- `/admin/users` page and a Create login action on the Employees page.

### Out

- **Email invites / self-service password reset.** There is no email infrastructure. The temporary
  password is handed over out of band.
- **Custom roles or per-permission grants.** The role list stays fixed.
- **The `Service` role.** Seeded but used by no endpoint; it is not assignable through this feature.
- **Changing an account's email.** The email is the sign-in username; out of scope as in My Profile.
- **Audit log of account changes.** Worth doing, but no audit-trail infrastructure exists for
  Identity tables yet.

## Data model

`ApplicationUser` gains:

| Field | Type | Default | Purpose |
|---|---|---|---|
| `IsActive` | `bool` | `true` | Deactivated accounts cannot sign in and their tokens are rejected. |
| `MustChangePassword` | `bool` | `false` | Set on create and on reset; cleared by a successful change-password. |

A unique filtered index on `EmployeeId` (`WHERE "EmployeeId" IS NOT NULL`) — one login per employee.

One EF migration. Existing rows get `IsActive = true`, `MustChangePassword = false`.

`IsActive` is deliberately separate from Identity lockout: failed-attempt lockout writes
`LockoutEnd`, and sharing that field would let a lockout expiry silently reactivate an account.

## Roles

**Assignable:** `Admin`, `HRManager`, `Manager`, `Employee`, `PayrollService`.

**Privileged:** `Admin`, `HRManager`. An account is *privileged* if it holds either.

`Employee` is always present on every account created or edited through this feature. A role set
submitted without it has it added; it cannot be removed. (`EmployeesController` already documents
`Employee` as a role every authenticated user holds.)

## Permission matrix — `AccountManagementPolicy`

A pure class in `PeopleCore.API` with no Identity dependency. Inputs are plain values: the caller's
id and roles, the target's id, roles and active state, and the number of active Admins. Every
decision returns either allowed or a human-readable reason, which becomes the `ProblemDetails.Detail`
of a 403.

| Action | Admin | HRManager |
|---|---|---|
| List / view accounts | ✓ | ✓ |
| Create account | ✓ any assignable roles | ✓ only non-privileged roles |
| Grant/remove `Manager`, `PayrollService` | ✓ | ✓ on non-privileged targets |
| Grant/remove `Admin`, `HRManager` | ✓ | ✗ |
| Link/unlink employee | ✓ | ✓ on non-privileged targets |
| Deactivate / reactivate | ✓ | ✓ on non-privileged targets |
| Reset password | ✓ | ✓ on non-privileged targets |

Guards that apply to every caller, Admin included:

1. **No self-lockout.** A caller cannot remove their own `Admin` or `HRManager` role, nor deactivate
   their own account.
2. **Last Admin.** The last active account holding `Admin` cannot lose `Admin` or be deactivated.
3. **Self-reset refused.** Reset password on your own account is refused; use change-password.

Anyone else (`Manager`, `Employee`, `PayrollService`) gets 403 from the controller's
`[Authorize(Roles = "Admin,HRManager")]` before the policy is consulted.

`GET /api/users/assignable-roles` returns the roles the *caller* may grant (all five for Admin;
`Employee`, `Manager`, `PayrollService` for HRManager), so the UI never offers a checkbox the API
would refuse.

## API — `UsersController` (`api/users`)

`[Authorize(Roles = "Admin,HRManager")]`. Uses `UserManager<ApplicationUser>` directly, following
`AuthController` and `ProfileController`.

| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/` | query `search`, `page`, `pageSize` | `PagedResult<UserAccountDto>` |
| GET | `/{id}` | — | `UserAccountDto` |
| GET | `/assignable-roles` | — | `string[]` |
| GET | `/employee-links` | — | `EmployeeLinkDto[]` (`EmployeeId`, `UserId`, `IsActive`) |
| POST | `/` | `CreateUserAccountRequest` | 201, `CreatedUserAccountDto` |
| PUT | `/{id}/roles` | `{ roles: string[] }` | `UserAccountDto` |
| PUT | `/{id}/employee` | `{ employeeId: Guid? }` | `UserAccountDto` |
| POST | `/{id}/deactivate` | — | `UserAccountDto` |
| POST | `/{id}/reactivate` | — | `UserAccountDto` |
| POST | `/{id}/reset-password` | — | `TemporaryPasswordDto` |

`UserAccountDto`: `Id`, `Email`, `FirstName`, `LastName`, `Roles`, `IsActive`, `MustChangePassword`,
`EmployeeId`, `EmployeeName`, `CanManage` (whether the *caller* may edit this account — drives
disabled buttons in the UI).

`CreateUserAccountRequest`: `Email`, `FirstName`, `LastName`, `EmployeeId?`, `Roles`.

`CreatedUserAccountDto`: `Account` (`UserAccountDto`) and `TemporaryPassword`.

`search` matches email, first name and last name, case-insensitively. Results are ordered by email.

### Validation (400, `ProblemDetails`)

- Email missing, malformed, or already used by another account.
- A role outside the assignable list (including `Service`).
- `EmployeeId` that does not exist, or that is already linked to another account.
- Names: same rules as `ProfileController` (trimmed, max `ApplicationUser.NameMaxLength`).
- Identity errors from `CreateAsync` surfaced verbatim.

Unknown account id → 404. Policy refusal → 403 with the policy's reason.

### Temporary passwords

Generated server-side with `RandomNumberGenerator`: 16 characters from an alphabet without
look-alikes (`0/O`, `1/l/I`), guaranteed to contain a digit, an upper and a lower case letter, so it
always satisfies the configured Identity password options. Returned only in the create and reset
responses; never logged, never retrievable afterwards.

Reset uses `GeneratePasswordResetTokenAsync` + `ResetPasswordAsync`, sets `MustChangePassword`, and
clears any failed-attempt lockout (`SetLockoutEndDateAsync(null)`, `ResetAccessFailedCountAsync`).

### Immediate effect — security stamp

- `GenerateJwtToken` adds a `sec_stamp` claim holding `user.SecurityStamp`, and a
  `must_change_password` claim when set.
- `JwtBearerEvents.OnTokenValidated` loads the user by `NameIdentifier` and fails the request (401)
  if the user is missing, `IsActive` is false, or the claim does not match the stored stamp. Tokens
  without a `sec_stamp` claim fail — so everyone signs in once after deploy.
- Every mutation in `UsersController` calls `UpdateSecurityStampAsync` on the target: role changes,
  link changes (the `employee_id` claim is now wrong), deactivate, reset.
  Reactivate does too, harmlessly.
- The stamp check lives in a small `SecurityStampValidator` class (not Identity's cookie validator of
  the same idea) so it can be unit tested apart from the JWT pipeline.

### Login and change-password changes

- **Login** refuses inactive accounts with the same generic `401 Invalid credentials.` — no account
  enumeration. The response adds `mustChangePassword`.
- **Change-password** already changes the stamp (Identity does so on `ChangePasswordAsync`), which
  would now invalidate the caller's own token. It therefore clears `MustChangePassword` and returns
  `200 { token, email, roles, mustChangePassword: false }` instead of `204`, so the client swaps in
  the fresh token.

### Forced change — `PasswordChangeRequiredFilter`

A global MVC filter. When the authenticated principal carries `must_change_password`, any action
other than `AuthController.ChangePassword` and `ProfileController.Get` returns 403 with
`ProblemDetails.Title = "Password change required"`. Anonymous endpoints are unaffected.

## Seeding

The seeded admin keeps `MustChangePassword = false` (its password comes from configuration) and
`IsActive = true`. Seeding also ensures it holds `Employee` alongside `Admin`, so the "every account
has Employee" invariant holds for it too.

## Web

### Client

`ApiClient` gains methods for each endpoint, with client-side DTOs mirroring the API. `JwtAuthStateProvider`
reads `mustChangePassword` from the login response and stores the token returned by
change-password.

### Forced change

When the stored session has `mustChangePassword`, `MainLayout` renders a full-page "Set a new
password" form (reusing the existing change-password form and show-password toggles) in place of the
routed page. Success stores the fresh token and releases the app.

### `/admin/users`

`[Authorize(Roles = "Admin,HRManager")]`. New nav section **Administration** (`Admin,HRManager`)
with entry **Users** (`bi-person-gear`), also added to the mobile footer's More panel.

- Table: email, name, roles (badges), linked employee, status (Active / Deactivated /
  Must change password). Search box and paging, matching the Departments page.
- **New account** dialog: email, first/last name, optional employee picker, role checkboxes from
  `assignable-roles` (`Employee` checked and disabled).
- Row actions — disabled with a tooltip when `CanManage` is false:
  **Edit roles** (checkbox dialog), **Link employee**, **Reset password**, **Deactivate** /
  **Reactivate** (confirm dialog).
- **Temporary password dialog** after create or reset: the password in a monospace field with a
  copy button, and the text "This password is shown once. Share it with the user securely; they
  will be asked to change it when they sign in." Closing it discards the password from memory.
- The API's `ProblemDetails.Detail` is shown on failure, as elsewhere.

### Employees page

On load, also fetch `employee-links`. Each row gets:

- **Create login** when unlinked — opens the New account dialog prefilled with the work email,
  names, the employee link and `Employee`.
- **Account** when linked — navigates to `/admin/users?search=<work email>`; muted if deactivated.

## Error handling summary

| Situation | Response | UI |
|---|---|---|
| Not Admin/HRManager | 403 | Page hidden by nav and `[Authorize]` |
| Policy refusal | 403 + reason | Toast with reason; buttons usually already disabled |
| Validation | 400 + detail | Inline under dialog |
| Unknown id | 404 | Toast, list reloads |
| Stale/revoked token | 401 | Existing expired-session handling: back to login |
| Password change pending | 403 "Password change required" | Forced-change screen already shown |

## Testing

- **`AccountManagementPolicyTests`** — the full matrix: each action × caller (Admin, HRManager) ×
  target (self, privileged, non-privileged), plus last-Admin and self-lockout guards.
- **`UsersControllerTests`** — mocked `UserManager`, following `ChangePasswordTests`: create returns a
  password and sets `MustChangePassword`; duplicate email and already-linked employee are 400;
  mutations update the security stamp; `Employee` is re-added when omitted; `Service` is rejected.
- **`TemporaryPasswordGeneratorTests`** — length, character classes, alphabet, passes Identity's
  `PasswordValidator` with the app's options.
- **`SecurityStampValidatorTests`** — missing user, inactive, stamp mismatch, missing claim, match.
- **`PasswordChangeRequiredFilterTests`** — blocks other actions, allows the two exempt ones, ignores
  principals without the claim.
- **`ChangePasswordTests`/login tests** updated — inactive login refused; change-password returns a
  token and clears the flag.
- **Infrastructure** — the unique `EmployeeId` index rejects a second link (Postgres fixture).
- **Web (bUnit)** — Users page renders, disables actions when `CanManage` is false, shows the
  temporary password once; Employees page shows Create login / Account; forced-change screen
  replaces routed content; nav shows Administration only to Admin/HRManager.
