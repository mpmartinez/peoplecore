# Demo company seed

Fills a PeopleCore site with Bayanihan Trading: 20 fictional employees and their history from
1 January 2026 to today. It covers attendance, leave, overtime, semi-monthly payroll, a mid-year
review and recruitment, so every report has real numbers. Everything goes through the API, so
payroll is computed by the real engine.

It runs between 1 February and 31 October 2026, the range its holiday calendar covers.

## Run it

Sign in with an **Admin** account that has already set its own password. An HR Manager login is
refused, because it can't run leave accruals.

Keep the admin password out of your shell history. In bash, type it at a hidden prompt:

```bash
read -rs PEOPLECORE_ADMIN_PASSWORD && export PEOPLECORE_ADMIN_PASSWORD
PEOPLECORE_URL=https://peoplecore.m2netsolutions.com \
PEOPLECORE_ADMIN_EMAIL=you@example.com \
dotnet run --project tools/PeopleCore.DemoSeed
unset PEOPLECORE_ADMIN_PASSWORD
```

In PowerShell:

```powershell
$env:PEOPLECORE_URL = "https://peoplecore.m2netsolutions.com"
$env:PEOPLECORE_ADMIN_EMAIL = "you@example.com"
$env:PEOPLECORE_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new("", (Read-Host "Admin password" -AsSecureString)).Password
dotnet run --project tools/PeopleCore.DemoSeed
Remove-Item Env:PEOPLECORE_ADMIN_PASSWORD
```

To fill in only the company details, on a site seeded before they were part of the run, add
`company` to the command: `dotnet run --project tools/PeopleCore.DemoSeed -- company`.

`PEOPLECORE_URL` must start with `https://`. Plain `http://` is accepted only for `localhost` or
`127.0.0.1`, for a rehearsal against a local API.

It takes a few minutes. When it finishes, it prints the client's email and a temporary password.
The client signs in with those and chooses their own password.

## What it adds to the site

The demo shares the site with any real records:

- **The company's employer details** (name, TIN, address, ZIP, RDO and agency numbers) for
  Bayanihan Trading Corporation, so payslips and BIR Form 2316 print an employer. All of them are
  made up. This happens only if the company has no TIN yet: real details are never overwritten.
- **Up to ten 2026 public holidays**, from New Year's Day to National Heroes Day, for any date the
  site doesn't already have. Holidays are company-wide, so **real staff's payroll uses them too**.
  The run prints which ones it added.
- Its six departments, their positions and teams, the "Day Shift 8-5" shift template, the mid-year
  review cycle, the job postings and 2026's semi-monthly payroll runs all appear in lists alongside
  real ones. Every run except the latest is marked paid. The latest is left as a draft.
- Vacation Leave and Sick Leave, with monthly accrual policies, if the site doesn't have them.
- Twenty `DEMO-` employees, and a login for each on `@bayanihantrading.example`.

## What to know

- It **checks before writing anything**, and refuses (exit code 2, nothing changed) if:
  - the account isn't an Admin, or is still on a temporary password;
  - any employee number starting `DEMO-` exists, or any login on `@bayanihantrading.example`;
  - the site has more than one company;
  - an existing VL or SL leave type is unpaid or limited to one gender, or its accrual policy
    wouldn't accrue 15 days a year from day one.
- It **stops at the first refused request**, and names the step. It then prints what it had
  created by then, which stays on the site, and switches off the demo logins it had created. A
  rerun is refused while those `DEMO-` employees exist, and the API can't delete them, so cleaning
  up needs direct database work. Rehearse against a local API before running it against production.
- The 19 other employees' logins exist only to file their history. They are deactivated at the end.
- Everyone in it is fictional. The names are common Filipino names, and the IDs, phone numbers and
  emails (on `.example` domains) are made up.
- `PEOPLECORE_SEED` (optional) changes who the twenty people are. The default always builds the
  same company.
