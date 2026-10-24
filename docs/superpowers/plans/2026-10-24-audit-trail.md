# Audit Trail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A complete, append-only audit trail of data changes, security events, sensitive access and actions, with an admin viewer and per-record history.

**Architecture:** One `audit_logs` table. `AppDbContext` records every tracked change in the same save; `IAuditTrail` writes security events through its own context; MVC filters record actions and access; `AuditLogsController` serves the Blazor page and history sheet.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit, FluentAssertions, Moq, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-24-audit-trail-design.md` binds the tasks.

## Global Constraints

- The trail is append-only; nothing updates or deletes an `AuditLog`.
- Never store a password hash, security stamp, concurrency stamp, the SMTP password or a request body.
- A login failure's response is unchanged; only the trail knows the reason.
- Build/test: `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Storage, context and permission

**Files:** `src/PeopleCore.Domain/Entities/System/AuditLog.cs`, its configuration, migration `AddAuditTrail` (with the append-only trigger), `src/PeopleCore.Application/Common/Auditing/*`, `src/PeopleCore.Infrastructure/Auditing/HttpAuditContext.cs`, `Permissions.cs` (API and Web).

- [ ] Entity, configuration, migration; `IAuditContext` and its HTTP implementation; `audit.view`.
- [ ] Commit `feat(audit): audit log table, request context and audit.view permission`.

### Task 2: Data changes

**Files:** `src/PeopleCore.Infrastructure/Auditing/AuditChangeCollector.cs`, `AppDbContext.cs`, `PayrollRunRepository.cs`, `tests/PeopleCore.Infrastructure.Tests/AuditTrailCaptureTests.cs`.

- [ ] Failing database tests per the spec; implement; run the whole solution.
- [ ] Commit `feat(audit): record every data change with its before and after values`.

### Task 3: Security events

**Files:** `src/PeopleCore.Infrastructure/Auditing/AuditTrail.cs`, `AuthController.cs`, `UsersController.cs`, `RolesController.cs`, `EmailSettingsController.cs`, the access-denied handler, tests in `tests/PeopleCore.Application.Tests/Api/`.

- [ ] Failing tests; implement; run the whole solution.
- [ ] Commit `feat(audit): record sign-ins, account, role and permission changes`.

### Task 4: Actions and access

**Files:** `src/PeopleCore.API/Auditing/*`, `Program.cs`, the controllers named in the spec.

- [ ] Failing filter and coverage tests; implement; run the whole solution.
- [ ] Commit `feat(audit): record every API action and sensitive download`.

### Task 5: Viewer API

**Files:** `src/PeopleCore.Application/Auditing/*`, `src/PeopleCore.Infrastructure/Persistence/Repositories/AuditLogRepository.cs`, `src/PeopleCore.API/Controllers/Audit/AuditLogsController.cs`.

- [ ] Failing tests; implement; run the whole solution.
- [ ] Commit `feat(audit): audit log list, detail, facets and CSV export`.

### Task 6: Pages

**Files:** `src/PeopleCore.Web/Pages/Admin/AuditTrail.razor`, `Components/Audit/RecordHistory.razor`, `Employees.razor`, `PayrollRunDetail.razor`, `SeparationDetail.razor`, `NavMenu.razor`, `ApiClient.cs`, `JwtAuthStateProvider.cs`.

- [ ] Failing bUnit tests; implement; run the whole solution.
- [ ] Commit `feat(audit): audit trail page and record history`.

### Task 7: Verification

- [ ] The whole solution passes with no compiler warnings; the app shows each kind of event end to end.
