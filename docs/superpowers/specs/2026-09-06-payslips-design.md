# Payslips — Phase 4 Design

**Date:** 2026-09-06
**Status:** Approved for planning
**Phase:** 4 of 4 (payslips; BIR 2316 becomes its own phase)

## Context

Payroll now runs end to end in PeopleCore — computed from real attendance, operable from the browser.
But nothing comes out the other end. An employee has no way to see what they were paid, and HR has
no document to hand them.

PayZen already has the document: `PayslipDocument`, 209 lines of QuestPDF, with 4 tests.
`PayrollLineBuilder` — ported in Phase 1 and fully tested but so far called by nothing — produces
exactly the earning and deduction lines it renders.

**Goal:** produce payslip PDFs, downloadable by HR for a whole run and by an employee for their own.

## Scope

### In

- A `PeopleCore.Reports` project carrying `PayslipDocument` and its QuestPDF dependency.
- `GET /api/reports/payslip/{runId}/{employeeId}` — one payslip.
- `GET /api/reports/payslips/{runId}` — every payslip in the run, merged into one PDF.
- A **Download** action per row on the payroll register, and a **Download all** for the run.
- An ESS page where an employee gets their own payslip.
- The 4 ported `PayslipDocument` tests.

### Out — and why BIR 2316 is its own phase

The original Phase 4 bundled BIR 2316 with payslips. They are different problems.

A payslip renders one `PayrollRunEmployee` that already exists. **BIR 2316 is an annual tax
certificate**: it needs year-to-date aggregation across every run in a year — which nothing in
PeopleCore computes — plus a ~40-field `BIR2316Dto`, previous-employer figures, and four endpoints.
That aggregation is a design problem of the same shape as the attendance bridge, and it deserves its
own spec rather than riding along.

Payslips are needed every payday. 2316 is filed once, in January. Also deferred with it: the five
Form 2316 tests held back from Phase 1, which depend on `BIR2316Dto`.

## Architecture

```
src/PeopleCore.Reports/PeopleCore.Reports.csproj   QuestPDF; referenced by the API
src/PeopleCore.Reports/PayslipDocument.cs          ported from PayZen
src/PeopleCore.Application/Payroll/DTOs/           PayslipCompanyDto
src/PeopleCore.API/Controllers/Payroll/ReportsController.cs
src/PeopleCore.Web/Pages/Payroll/PayrollRunDetail.razor   download actions
src/PeopleCore.Web/Pages/ESS/MyPayslips.razor            employee self-service
```

`PayslipDocument` takes `PayrollRunDto`, `PayrollRunEmployeeDto` and a company record. The first two
already exist in `PeopleCore.Application.Payroll.DTOs` under the same names, so they port unchanged.

### The company record

PayZen passes a `CompanySettingsDto`. PeopleCore deliberately split that concept in Phase 1: company
identity lives on `Organization.Company`, statutory rates on `PayrollSettings`. The payslip needs
only the identity half, and reads exactly five fields. The names differ, so the mapping is explicit:

| `PayslipDocument` reads | PeopleCore source |
|---|---|
| `CompanyName` | `Company.Name` |
| `Address` | `Company.Address` |
| `City` | `Company.City` |
| `ContactNumber` | `Company.ContactPhone` |
| `Email` | `Company.ContactEmail` |

A new `PayslipCompanyDto` carries those five. Reusing `PayrollSettings` here would reintroduce the
two-competing-company-concepts problem the Phase 1 split existed to remove.

### QuestPDF licensing

PayZen sets `QuestPDF.Settings.License = LicenseType.Community` inside three separate controller
actions. Set it **once at startup** in `Program.cs` instead. Three copies of a global setting is
three places for one of them to be forgotten, and the failure mode is an exception at PDF generation
rather than at boot.

## Authorization — the part that matters

The two HR endpoints require `Admin`, `HRManager` or `PayrollService`, like every other payroll
endpoint.

**The ESS endpoint is different and is the security-critical piece of this phase.** An employee
fetching their own payslip must not be able to fetch anyone else's by editing a GUID in the URL.

So the ESS route takes **no employee id**. It resolves the caller's own employee from
`ICurrentUserService.EmployeeId` — the `employee_id` claim the JWT already carries — and serves only
that employee's payslip. The route id is never trusted, because a route parameter is user input.

If the caller has no `employee_id` claim, the endpoint returns 403 rather than falling back to
anything.

An employee's payslip is the most personal document this system produces. It is worth stating that
the check is server-side: hiding a button is not authorization.

## Data flow

```
PayrollRun + its entries  ->  PayrollRunDto / PayrollRunEmployeeDto   (existing)
Company                   ->  PayslipCompanyDto                       (five fields)
                                     |
                                     v
                          PayslipDocument (QuestPDF)
                                     |
                        one PDF, or Document.Merge for a whole run
```

Bulk download merges every entry's document into a single PDF with `Document.Merge`, as PayZen does —
one file HR can print, rather than a zip of hundreds.

## Error handling

- A run that does not exist → 404.
- An employee not in that run → 404.
- No company row → 400 naming the problem. The seeder creates one, so this means a misconfigured
  database rather than a normal state.
- An employee with no `employee_id` claim calling the ESS endpoint → 403.

## Testing

The 4 `PayslipDocumentTests` port from PayZen. They generate a PDF and assert on the produced bytes,
which is a real assertion — QuestPDF throws on a malformed composition, so a document that renders at
all is meaningfully verified.

Beyond those, the phase adds:

- A test proving the ESS endpoint serves the caller's own payslip and **ignores any employee id
  supplied by the caller**. This is the phase's most important test.
- A test proving a missing `employee_id` claim yields 403 rather than an arbitrary employee's payslip.

The Blazor pages are verified in a browser, as in Phase 3 — this repository still has no component
tests and this phase does not introduce them.

## Acceptance criteria

1. HR can download one employee's payslip and a merged PDF of a whole run, from the register.
2. An employee can download their own payslip from ESS.
3. An employee cannot obtain another employee's payslip by any route parameter, asserted by test.
4. The QuestPDF licence is set once at startup, not per action.
5. `dotnet build` is clean and the full suite is green.
6. The Phase 1 statutory files and their tests remain untouched.
