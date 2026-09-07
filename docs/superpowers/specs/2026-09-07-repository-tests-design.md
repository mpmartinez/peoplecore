# Repository Tests — Design

**Date:** 2026-09-07
**Status:** Approved for planning
**Follows:** the payroll input validation phase

## Context

PeopleCore has 359 tests and **not one of them touches a database**. Every repository is a mock in
every test that needs one. The persistence layer — 26 repositories, 10 migrations, a snake-cased
Npgsql schema — is verified only by running the application by hand.

That gap is not evenly distributed. Most repositories are thin passes over `Repository<T>`, where a
mock costs little. `PayrollRunRepository` is not: it filters on `PayDate.Year`, deduplicates
employees across runs with a `SelectMany`/`Distinct` over an anonymous type, and replaces a run's
entries with `ExecuteDeleteAsync` inside a transaction followed by a hand-written detach loop. None
of that is exercised by anything.

**Goal:** prove the payroll and 2316 repositories return the right rows from a real PostgreSQL
database.

## Scope

### In

- A new `tests/PeopleCore.Infrastructure.Tests` project with a PostgreSQL test harness.
- `PayrollRunRepository` — every query, and both write paths.
- `EmployeeCompensationRepository` and `PayrollSettingsRepository`.
- Audit-column stamping, which `AppDbContext.SaveChangesAsync` performs and nothing verifies.

### Out

The other 23 repositories. Most are CRUD over the shared `Repository<T>` base, where a database
test would mostly re-prove Entity Framework. They deserve coverage once the harness exists and is
known to work; adding them here would triple the change without tripling what it catches.

Also out: HTTP-level or end-to-end tests. This tests the persistence layer directly, not through
controllers.

## Approach

**A real PostgreSQL instance per test run, via Testcontainers.**

The alternatives were considered and rejected for the same reason. The **EF Core InMemory
provider** is not a relational database — no foreign keys, no constraints, and LINQ evaluated in
memory rather than translated. Every query "passes", including ones that throw against Postgres,
and `ExecuteDeleteAsync` is not supported at all. It would produce coverage numbers without
coverage. **SQLite in-memory** is at least relational and would catch schema and foreign-key
mistakes, but it is a different dialect: `DateOnly.Year`, `numeric(18,2)` and the snake_case
convention all behave differently, so a green test would not mean the production query works.

Since the value of these tests is almost entirely "does this query translate and return the right
rows against the database we actually ship on", anything less than the real engine defeats the
exercise.

### Schema comes from the migrations

The harness calls `Database.MigrateAsync()`, not `EnsureCreated()`.

`EnsureCreated` builds a schema from the model, which can differ from what the migration chain
actually produces — so tests would pass against a schema no deployment has. Running the migrations
means the tests execute against the production schema, and the ten-migration chain gets its first
automated proof that it applies cleanly to an empty database. Nothing tests that today.

### One container, truncated between tests

Container startup dominates the cost, so one container is shared across the run and each test
truncates rather than rebuilding.

Isolation is not optional here. `CountForYearAsync(year)` and `GetPaidRunsInYearAsync(year)` are
global queries — they do not filter by employee — so rows left behind by one test would silently
change another test's count. The failure would appear as a flaky, order-dependent test, which is
worse than no test.

The truncation list is **derived from `Context.Model`, not hardcoded**. A hardcoded list silently
stops truncating a table the day someone adds an entity, reintroducing exactly the cross-test
bleed it was written to prevent.

## Architecture

```
tests/PeopleCore.Infrastructure.Tests/
  PeopleCore.Infrastructure.Tests.csproj    references PeopleCore.Infrastructure
  PostgresFixture.cs                        the container, migrations, truncation
  DatabaseTestBase.cs                       per-test reset, a fresh DbContext, seed helpers
  Payroll/PayrollRunRepositoryTests.cs
  Payroll/EmployeeCompensationRepositoryTests.cs
  Payroll/PayrollSettingsRepositoryTests.cs
  Payroll/AuditStampingTests.cs
```

`PostgresFixture` is an xUnit collection fixture: the container starts once, migrations run once,
and every test class in the collection shares it. `DatabaseTestBase` truncates before each test and
hands the test a fresh `AppDbContext`, because a context reused across a truncation would serve
stale tracked entities.

One new package: `Testcontainers.PostgreSql` 4.15.0. Entity Framework, Npgsql and
`EFCore.NamingConventions` all arrive transitively from `PeopleCore.Infrastructure`.

The project must be added to `PeopleCore.slnx` under the existing `/tests/` folder, or
`dotnet test` will not discover it.

## What is tested, and why a mock could not show it

### `PayrollRunRepository`

| Behaviour | Why it needs a database |
|---|---|
| `CountForYearAsync`, `GetPaidRunsForEmployeeInYearAsync`, `GetPaidRunsInYearAsync`, `GetPaidYearsForEmployeeAsync` | All filter on `PayDate.Year` or `PeriodStart.Year`, which Npgsql must translate to SQL. This is the 2316's tax-year attribution rule — the design's deliberate departure from PayZen, where income is attributed by pay date rather than period start — and it has only ever been asserted against mocks that return whatever the test handed them. A run paid in January for December's period must count in January's year. |
| `GetEmployeeIdsWithPaidRunsInYearAsync` | `SelectMany` into entries, `Distinct()` over an anonymous type carrying the employee's name, ordering by last then first name, then projecting the id back out. A non-obvious translation, and the deduplication must hold for an employee who appears in two paid runs in the same year. |
| `ReplaceEntriesAsync` | The highest-value test in the set. `ExecuteDeleteAsync` bypasses the change tracker, so the method detaches the stale entries by hand before adding replacements — without that, EF issues updates against rows it just deleted. Both steps share a transaction specifically so a failure between them cannot leave a run with no entries. Only a real database has a transaction, a cascade, or an `ExecuteDeleteAsync`. |
| `AddWithEntriesAsync` | Entries are added to both `PayrollRuns` and `PayrollRunEmployees` explicitly, because the navigation alone is not enough once `Compute` has assigned each entry an Id. Whether that actually persists both is a database question. |
| `GetWithEntriesAsync` | `AsSplitQuery()` issues several round trips; the test proves both `Include` chains — entries with their employee, and entries with their loan deduction lines — still arrive populated. |
| `GetPagedAsync` | Skip/take ordering and a total count that ignores paging. |

### `EmployeeCompensationRepository` and `PayrollSettingsRepository`

- A `numeric(18,2)` money column round-trips two decimal places and **drops a third**. The
  validation phase asserted this was the behaviour and bounded input because of it; nothing has
  ever demonstrated it.
- `GetDefaultAsync` returns the single settings row, and **throws when a second exists**. That
  guard is the entire multi-company safety story — it is what stops one company's rates being
  applied to another's payroll — and it is asserted nowhere against a real schema.
- The unique index introduced by `UniquePayrollSettingsCompany` is actually enforced by the
  database, not merely declared in a configuration class.

### Audit stamping

`AppDbContext.SaveChangesAsync` stamps `CreatedAt`, `CreatedBy`, `UpdatedAt` and `UpdatedBy` on
every `IAuditableEntity`. Those columns going unpopulated was a real defect fixed earlier in this
work, and nothing has verified the fix since. Tests cover an insert, an update, and the case that
`ICurrentUserService` is null — its constructor parameter is optional, so a null user must leave
the timestamps stamped and the user columns null rather than throwing.

## Risks

**CI needs Docker.** These tests cannot run without a container runtime. A CI environment without
Docker will fail the whole suite rather than skip these — which is the correct behaviour, since
silently skipping the only tests that touch the database would be worse, but it must be a
deliberate decision rather than a surprise.

**A full solution run gets slower** by roughly the container's startup. The separate project is the
mitigation: the 359-test Application suite still runs on its own in about a second.

**Testcontainers pulls a Postgres image.** The first run on a new machine downloads it.

The image tag is pinned to `postgres:17-alpine`, and this is a choice rather than a match: the
repository records no production PostgreSQL version anywhere - no compose file, no Dockerfile, and
a connection string pointing at `Host=localhost`. So the tests pin a current stable major and say
so, rather than claiming a fidelity that cannot be checked. If production runs a different major,
change the tag here - that is the point of pinning it in one place, and it is worth confirming
before these tests are trusted as a deployment gate.

## Acceptance criteria

1. `tests/PeopleCore.Infrastructure.Tests` exists, is listed in `PeopleCore.slnx`, and runs against
   a Testcontainers PostgreSQL instance.
2. The schema is created by `Database.MigrateAsync()`, so the migration chain is proven to apply.
3. Each test starts from an empty database, with the truncation list derived from the model.
4. Every `PayrollRunRepository` member has at least one test, including `ReplaceEntriesAsync`'s
   cascade and its detach behaviour.
5. A test demonstrates a run paid in January of the following year is attributed to that year, not
   to the period's year.
6. A test demonstrates `GetDefaultAsync` throwing on a second settings row.
7. A test demonstrates a third decimal place being dropped by a `numeric(18,2)` column.
8. Audit columns are asserted on insert and update, including with a null `ICurrentUserService`.
9. `dotnet test PeopleCore.slnx` is green, and the existing 359 tests are unchanged.
