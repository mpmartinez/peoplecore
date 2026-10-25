# PeopleCore.E2E.Tests

End-to-end tests: a real browser (Playwright) drives the published web app against the real API
and a real PostgreSQL database, written as xUnit tests.

## What a run does

`PeopleCoreApp` (the collection fixture, started once per run):

1. Publishes `src/PeopleCore.Web` with `-c Release`, the same output the Dockerfile ships. Skip
   this by setting `PEOPLECORE_E2E_WEB_DIR` to an existing publish directory, given as an
   **absolute** path.
2. Drops and recreates the `peoplecore_e2e` database on the server named by
   `PEOPLECORE_TEST_POSTGRES` (default: `localhost:5432`, `postgres`/`postgres`, the
   m2net-postgres container). The name is fixed, so the suite never touches another database.
3. Serves the published app from a local static host on a free port. Every
   `appsettings*.json` it serves points at the test API, never at production.
4. Starts the API in-process on a real Kestrel port via `WebApplicationFactory`. It runs its
   migrations and seeds the admin in `E2ECredentials`, just as a fresh deployment would.

After the run, the database is dropped again.

## Running

You need Docker's m2net-postgres (or any PostgreSQL 13+) plus a Playwright browser. Install the
browser once after building, and again whenever the `Microsoft.Playwright.Xunit` version changes:

```bash
dotnet build tests/PeopleCore.E2E.Tests
```

```bash
pwsh tests/PeopleCore.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

```bash
dotnet test tests/PeopleCore.E2E.Tests
```

Playwright's environment variables apply: `HEADED=1` to watch the browser, `BROWSER=firefox` or
`BROWSER=webkit` to switch browsers (install that browser first).

## Writing a test

Derive from `E2ETest` and take `PeopleCoreApp` in the constructor. Each test gets its own
browser context, so it starts signed out. Call `SignInAsync()` first.

- Find elements the way a user would: `GetByRole`, `GetByLabel`, `GetByPlaceholder`. If a field
  can't be found by its label, the label isn't attached to its input (`<FormField Id>` has to
  match the `<Input Id>`). Fix the page rather than reaching for a CSS selector.
- Tests share one database and run one at a time. Give anything you create a `Unique(...)` name,
  and assert on that name, not on row counts.
- Assert with `Expect(...)`. It retries until the 30-second timeout, which a WebAssembly app
  needs on its first load.

## When a test fails

The failing test's Playwright trace is saved to `bin/<Configuration>/net10.0/playwright-traces/`.
CI uploads these as the `playwright-traces` artifact. Open one with
`pwsh bin/Debug/net10.0/playwright.ps1 show-trace <file>.zip` or at https://trace.playwright.dev.
