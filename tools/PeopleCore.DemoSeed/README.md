# Demo company seed

Fills a PeopleCore site with Bayanihan Trading: 20 fictional employees and their history from
1 January 2026 to today. It covers attendance, leave, overtime, semi-monthly payroll, a mid-year
review and recruitment, so every report has real numbers. Everything goes through the API, so
payroll is computed by the real engine.

## Run it

```bash
PEOPLECORE_URL=https://peoplecore.m2netsolutions.com \
PEOPLECORE_ADMIN_EMAIL=you@example.com \
PEOPLECORE_ADMIN_PASSWORD='your admin password' \
dotnet run --project tools/PeopleCore.DemoSeed
```

In PowerShell, set each variable with `$env:PEOPLECORE_URL = "..."` first.

It takes a few minutes. When it finishes, it prints the client's email and a temporary password.
The client signs in with those and chooses their own password.

## What to know

- It **refuses to run twice**. If any employee number starting `DEMO-` exists, it stops before
  changing anything.
- It **stops at the first refused request**, and names the step. Records created before that step
  stay on the site. The API offers no undo, so rehearse against a local API before running it
  against production.
- The 19 other employees' logins exist only to file their history. They are deactivated at the end.
- Everyone in it is fictional. The names are common Filipino names, and the IDs, phone numbers and
  emails (on `.example` domains) are made up.
- `PEOPLECORE_SEED` (optional) changes who the twenty people are. The default always builds the
  same company.
