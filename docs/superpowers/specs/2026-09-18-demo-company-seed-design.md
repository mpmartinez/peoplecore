# Demo company seed

**Status:** approved, 2026-09-18

## The problem

A client has asked for a demo account. An empty PeopleCore shows nothing worth seeing: no payroll
to report on, no attendance to summarise, no leave to approve. The client needs a company that
looks lived-in, with enough history for every report to return real numbers.

## What we are building

A console program, `tools/PeopleCore.DemoSeed`, that fills a PeopleCore site with a fictional
company of 20 employees and their history from 1 January 2026 to the day it runs. It works only
through the HTTP API, signed in as an administrator, so every number goes through the same rules
and the same payroll engine a real customer's data would.

It runs against production (`https://peoplecore.m2netsolutions.com`), alongside the existing test
data. It can be pointed at any other PeopleCore site later, such as a dedicated demo site.

## Decisions

### Through the API, not into the database

Payroll in particular must be computed, not typed in. SSS, PhilHealth, Pag-IBIG and withholding tax
come from the engine. That makes payslips, the payroll register and BIR 2316 agree with each other
and with what a real company would see. It also means no database access to production, and no
dependence on the table layout.

### Fictional people, realistic names

Names are common Filipino first names and surnames, combined into people who do not exist. Every
other identifying detail is fake but shaped correctly:

- mobile numbers in `09xx xxx xxxx` form;
- SSS, PhilHealth, Pag-IBIG and TIN numbers in their real formats;
- emails on the fictional company's own domain.

Nothing is taken from a real person. The data will be shown to a client.

### The owner runs it against production

The program reads the site address and an administrator's email and password from environment
variables. Claude builds it and rehearses it against a throwaway local database. The owner runs it
against production with their own credentials, which never pass through Claude.

### One login per employee, then deactivated

Leave, overtime and self-evaluations can only be filed by the employee concerned. Approvals come
from the employee's own manager (or, for leave, a holder of `approvals.all`). The administrator
account belongs to no employee. So the program:

1. gives each of the 20 employees a login;
2. signs in as each of them to file their history, and as their managers to decide it;
3. at the end, deactivates 19 of those logins.

The 19 deactivated logins can no longer sign in, and the history they filed stays.

The twentieth is the HR Manager persona. It becomes **the client's account**, holding the seeded
`HRManager` role (HR-level access, including approving anyone's leave, but not managing roles or
system settings):
- Its password is reset at the end, and the program prints the temporary password for the owner to
  pass on.
- The client sets their own password at first sign-in, as every new account does.

### Constraints the API imposes

- **Salaries have no effective date.** Each employee has one salary for the whole year: no mid-year
  raises.
- **Allowances and loans have no API.** Payslips show basic pay, overtime, holiday pay and
  deductions, with no allowances or loan deductions.
- **The company cannot be created or renamed through the API.** The demo uses the site's existing
  company record.

## The company

Six departments, each with a head. Staff report to their head; heads report to the General Manager.

| Department | People |
|---|---|
| Executive | General Manager |
| Human Resources | HR Manager (the client's persona), HR Officer |
| Finance | Finance Manager, 2 accountants |
| Operations | Operations Manager, supervisor, 5 staff |
| Sales | Sales Manager, 3 account executives |
| IT | IT Lead, 2 developers |

That is 20 people.

- **Salaries:** monthly, paid semi-monthly, from ₱18,000 for staff to about ₱150,000 for the
  General Manager.
- **Hire dates:** from 2018 to mid-2026. Two people join during 2026, so the hiring trend shows
  movement.
- **Each person has:**
  - date of birth
  - gender
  - civil status
  - mobile number
  - the four government IDs
  - one emergency contact
  - a team, where the department has one
  - a tax code and number of dependents
- **Employee numbers** use a prefix of their own (`DEMO-0001` to `DEMO-0020`). They cannot collide
  with existing test data, and they let the program recognise a site it has already seeded.

## The history, 1 January 2026 to the run date

- **Holidays:** the 2026 Philippine regular and special non-working holidays, taken from the official
  proclamation.
- **Schedule:** one day shift, Monday to Friday, 08:00 to 17:00 with a one-hour break, assigned to
  each employee from their hire date or 1 January, whichever is later.
- **Attendance:** imported month by month through `api/attendance/import`.
  - Most days on time.
  - Realistic lates of 5 to 30 minutes, occasional absences, and some undertime.
  - No punches on holidays, weekends, or approved leave days.
- **Leave:**
  - **Types:** Vacation Leave and Sick Leave, each accruing monthly under an accrual policy.
    Accruals are run for each month from January to the current month.
  - **Requests:** each employee files 2 to 6 across the year. Most are approved and one or two are
    rejected.
  - **Pending:** a few September requests are left pending for the client to decide.
- **Overtime:** about two dozen approved requests, mostly in Operations and IT, approved by the
  requester's direct manager. Two are left pending.
- **Payroll:** semi-monthly runs from 1–15 January to the latest completed half-month.
  - Every run is created and computed by the engine.
  - Every run except the latest is approved and marked paid.
  - The latest is left awaiting approval, so the client can approve and pay it.
- **Performance:** a "2026 Mid-Year Review" cycle covering January to June.
  - Every employee has a review with KPI items.
  - Most are complete, with self-evaluation and manager review.
  - A few have a self-evaluation submitted and await their manager.
  - The cycle stays open.
- **Recruitment:** three job postings, two open and one closed, with about 12 applicants spread
  across every status. A few have interviews scheduled.

## How it runs

```
PEOPLECORE_URL=https://peoplecore.m2netsolutions.com
PEOPLECORE_ADMIN_EMAIL=...
PEOPLECORE_ADMIN_PASSWORD=...
dotnet run --project tools/PeopleCore.DemoSeed
```

1. Sign in as the administrator. Stop if that fails.
2. Stop if any `DEMO-` employee already exists. The site has been seeded; nothing is changed.
3. Create the organisation, employees, compensation, IDs and contacts.
4. Create holidays, the shift, and the assignments.
5. Create the logins.
6. Create leave types and policies, and run the accruals.
7. Import attendance.
8. File and decide leave and overtime.
9. Run payroll period by period.
10. Run the performance cycle.
11. Create the recruitment data.
12. Deactivate the 19 logins, and reset the HR Manager's password.
13. Print a summary: counts per area, and the client's email and temporary password.

- **Randomness** comes from a fixed seed, so every run against an empty site produces the same
  company.
- **On the first failed request**, the program stops. It prints the step, the endpoint, the status
  code and the API's message.
- **Partial runs:** the API offers no way to undo a partly seeded site. This is why the full run is
  rehearsed locally before it is run against production.
- **Security:** the program never prints or logs a password, except the one temporary password it
  prints at the end for the client.

## Testing

- **Unit tests**, in a new test project `tests/PeopleCore.DemoSeed.Tests`, cover the pure
  generators:
  - Names are drawn from the name lists.
  - Government IDs match their formats.
  - Mobile numbers match `09xx`.
  - The same seed gives the same company.
  - Attendance leaves out weekends, holidays and leave days.
  - Pay periods tile January to the run date with no gaps or overlaps.
- **Rehearsal**, before the owner runs it against production: a full run against the local API
  and a fresh throwaway database. Then these checks:
  - the payroll register and one payslip show computed amounts;
  - BIR 2316 lists 2026 for an employee;
  - the HR and executive analytics endpoints return non-empty results;
  - attendance summaries show lates and absences;
  - the client's account signs in with its temporary password, is made to change it, and afterwards
    sees the pending approvals;
  - a second run is refused without changing anything.

## Out of scope

- A dedicated demo site, and resetting a site to seed it again.
- Allowances, loans, and mid-year salary changes. The API doesn't offer them.
- Renaming the company.
- Any change to the application itself. If the rehearsal finds an endpoint that refuses reasonable
  demo data, that is reported, not worked around by changing the application.
