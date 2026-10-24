# Audit trail - design

Date: 2026-10-24. Status: approved in planning; being implemented.

## Why

PeopleCore keeps only `CreatedAt/By` and `UpdatedAt/By` on each row (`AppDbContext.StampAuditColumns`).
Nothing records what a value was before it changed, who deleted a row, who signed in or failed to,
who changed a role's permissions, or who downloaded a payslip or a 2316. Payroll disputes, DOLE and
BIR record-keeping and basic account security all need that history.

## Decisions

- Four kinds of event, one table: **data changes** (every insert, update and delete, field by
  field), **security events** (sign-in, sign-out, passwords, accounts, roles, permissions, access
  denied), **access** (sensitive reads and exports) and **actions** (every state-changing API call,
  by name, with its outcome).
- Sensitive values (salary, government ID numbers, bank details) are stored as they are. Only holders
  of the new `audit.view` permission can read the trail. Password hashes, security stamps and the
  SMTP password are never stored: a change to them reads `(changed)`.
- The trail is append-only and kept indefinitely. There is no purge.
- Viewer: an Administration page, `/admin/audit-trail`, and a History sheet on employees, payroll
  runs and separations.

## Storage

`AuditLog` (`audit_logs`), keyed by a bigint identity so rows sort in the order written:

- `occurred_at`, `category` (`DataChange | Security | Access | Action`), `action`, `summary`,
  `outcome` (`Success | Failure`), `failure_reason`.
- Actor snapshot: `user_id`, `user_email`, `actor_name` (`System: <job>` when no user is signed in),
  `ip_address`, `user_agent`.
- Request: `correlation_id` (the request's `TraceIdentifier`; an action and the data changes it made
  share it), `http_method`, `path`, `status_code`.
- Target: `entity_type`, `entity_id`, `subject_employee_id`.
- `changes` (jsonb list of `{field, old, new}`) and `metadata` (jsonb object).

A database trigger rejects every UPDATE and DELETE on the table, and `AppDbContext` refuses to save a
modified or deleted `AuditLog`.

## Data changes

`AppDbContext.SaveChanges(Async)` turns every Added, Modified and Deleted entry into an `AuditLog`
row in the same save, so a change and its record commit or roll back together.

- Added: every non-null value as `new`. Deleted: every value as `old`. Modified: only the properties
  whose value changed.
- An entity attached with `DbSet.Update` (a detached entity, as `Repository.UpdateAsync` does) has
  no original values; its before-values are read from the database.
- `CreatedAt/By` and `UpdatedAt/By` are left out (the row's own actor and time say the same).
- Not recorded: `AuditLog`, `DataProtectionKey`, `IdentityUserToken`. Binary values read `(binary)`
  and strings are cut at 4,000 characters.
- Subject employee: an `Employee` row's own id, otherwise the row's `EmployeeId`.
- The two writes EF does not track are recorded by `PayrollRunRepository` itself: linking a final
  pay run to its separation, and deleting a run's lines before a recompute (one row with the count).
  The recomputed lines are recorded as Created, so each compute leaves a snapshot.

## Security events

Recorded by `IAuditTrail`, which writes through its own context so the record survives a failed
request: sign-in succeeded and failed (with the reason - unknown email, inactive, wrong password,
locked out - while the response stays the same 401), sign-out (new `POST api/auth/logout`, called by
the web app), password changed, reset requested, reset done; account created, roles changed (added
and removed), employee linked, deactivated, reactivated, password reset by an admin, and refusals;
role created, changed (permissions added and removed), deleted; email settings changed; and every
403 for a signed-in user.

## Actions and access

- A global filter records every POST, PUT, PATCH and DELETE as an `Action` with its outcome, status
  code, route and query values and, on failure, the error message. Request bodies are never stored.
  `[AuditAs]` gives the important ones a readable name; the rest are named `Controller.Action`.
  The auth, users and roles controllers opt out with `[SkipAuditAction]`, since they record richer
  security events.
- `[AuditAccess]` on payslips, 2316s, government reports, the payroll export, COEs, documents,
  government IDs, compensation, final pay and the audit export records an `Access` row on success.

## Pages

- `/admin/audit-trail` (`audit.view`): filters (dates, category, action, record type, user, outcome,
  search), paging, a detail sheet with the field changes, the request and the other entries from
  the same request, and a CSV export (capped at 50,000 rows).
- History sheet (`RecordHistory`) from the employees list, the payroll run page and the separation
  page.

## Accepted gap

Children removed by a database cascade (an employee's government IDs when the employee is deleted)
are not recorded one by one; the parent's Deleted row stands for them.

## Testing

Database tests for the change capture (diffs, detached updates, redaction, subject employee,
rollback, the trigger, the bulk writes); controller and filter tests for security events, actions
and access; coverage tests that pin which controllers skip the action filter and which endpoints
carry `[AuditAccess]`; bUnit tests for the page, the history sheet and the menu entry.
