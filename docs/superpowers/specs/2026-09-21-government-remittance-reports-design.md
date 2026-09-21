# Government remittance reports (monthly) - design

## Problem

Every month a Philippine employer remits SSS, PhilHealth and Pag-IBIG contributions and files
BIR 1601-C for the tax it withheld. PeopleCore already computes every one of those amounts on
each payslip, but gives HR no way to see them per agency per month. Today they would have to
add up payslips by hand before keying the figures into each agency's portal.

## Goal

A **Government Reports** page under Payroll that shows, for a chosen month, each agency's
per-employee list and totals, flags anyone missing the ID number that agency needs, and
downloads each list as CSV. HR reviews the figures there and then enters or uploads them in the
agency's own portal.

## Out of scope

- **Agency upload files in their exact layouts** (SSS R-3 file, PhilHealth EPRS, Pag-IBIG MCRF
  file, BIR eFPS / eBIRForms). The review sheets come first; exact layouts can follow one agency at
  a time once confirmed against a real client's portal.
- **BIR 1604-C annual alphalist.** An annual report built on the existing BIR 2316 data; it gets
  its own spec.
- **PDF printouts.** CSV only for now.
- **Storing anything new.** Every figure is derived from paid payroll runs as they stand.

## Source data

- **Only Paid runs count**, as for BIR 2316. A draft, for-approval or approved run is not yet
  income and can still change.
- **Month basis differs by agency:**
  - SSS, PhilHealth and Pag-IBIG: the month the run's **period ends** in. Contributions are for
    the month the pay was earned (the "applicable month"), so the Dec 16-31 cutoff paid on
    Jan 5 is December's.
  - BIR 1601-C: the month the run was **paid** (`PayDate`). BIR taxes compensation when it is
    paid, and BIR 2316 already totals the year on the same basis.
- A semi-monthly employee's two cutoffs in the month are **added together** into one row.
- **Employee ID numbers** come from the `EmployeeGovernmentId` table, the same source that
  BIR 2316 and the payroll master-data export use. The flat fields on the M2NET.Core base
  `Employee` are not mapped and are always empty.
- The **employer header** comes from the Company page: name, address, TIN, RDO code, and the
  SSS, PhilHealth and Pag-IBIG employer numbers.

## The four reports

Each report has one row per employee paid in the month, sorted by last name then first name, and
a totals row.

### SSS contributions

| Column | From |
|---|---|
| Last name, first name, middle name | employee |
| SSS number | government ID `SSS` |
| Monthly salary credit (MSC) | worked back: employee share / 5%, rounded to the nearest 500 |
| Employee share | sum of `SSSEmployee` |
| Employer share (SS) | employer share less EC |
| EC | 10.00 when the MSC is below 15,000, otherwise 30.00 (Circular 2024-006) |
| Employer total | sum of `SSSEmployer` |
| Total | employee share + employer total |

The MSC and EC are worked back from the stored shares because entries do not store them. The
reverse only holds under the statutory schedule. When the company's payroll settings currently
override the SSS rates, the MSC and EC columns are left blank and a note says why; the shares
themselves are still shown.

### PhilHealth premiums

Name, PhilHealth number, employee share (`PhilHealthEmployee`), employer share
(`PhilHealthEmployer`), total.

### Pag-IBIG contributions

Last, first and middle name, date of birth, Pag-IBIG number, employee share (`PagIbigEmployee`),
employer share (`PagIbigEmployer`), total.

### BIR 1601-C

The form's lines for the month, as totals across the employees paid in it:

| Line | Computed as |
|---|---|
| Total amount of compensation | sum of `GrossPay` |
| Statutory minimum wage (MWEs) | 0 (minimum wage earners are not modelled yet; see BIR 2316) |
| Holiday, overtime, night differential and hazard pay (MWEs) | 0, as above |
| 13th month pay and other benefits (non-taxable) | the part of the month's `ThirteenthMonth` within the 90,000 exemption (below) |
| De minimis benefits | 0 (not modelled) |
| SSS, PhilHealth and Pag-IBIG employee shares | sum of the three employee shares |
| Other non-taxable compensation | sum of `NonTaxableAllowances` |
| Total non-taxable compensation | sum of the lines above |
| Total taxable compensation | total compensation less total non-taxable |
| Total taxes withheld | sum of `WithholdingTax` |

Under the totals there is a per-employee breakdown with the same columns plus each employee's TIN,
so HR can trace any line to the people in it.

**Non-taxable 13th month.** Payroll does not store how much of a 13th month was exempt, so it is
worked out the way payroll withheld it: the exemption left for an employee is 90,000 less the 13th
month in their Paid runs paid earlier in the same year (by `PayDate`). This month's non-taxable
part is the smaller of this month's 13th month and what is left of the exemption. That matches
`PayrollComputationService.ComputeThirteenthMonthTax`, so the taxable line agrees with the tax that
was withheld.

## Checks and messages

- **Missing numbers.** Anyone without the number a report needs (SSS, PhilHealth, Pag-IBIG or TIN)
  stays in the list, with that cell marked. A summary above the table says how many, for example
  "3 employees have no SSS number". Agencies reject a remittance line without a member number, so
  HR fixes the employee record first.
- **Nothing paid.** A month with no Paid runs on that report's basis says "No payroll was paid for
  March 2026" rather than showing an empty table. Runs for the month that are not Paid yet are
  counted and mentioned ("2 runs for this month aren't paid yet and aren't included").
- **Company details missing.** If the employer TIN or an agency's employer number is blank, the
  report still shows, with a prompt to complete the Company page.

## Architecture

**Application** (`PeopleCore.Application/Payroll`):
- `IGovernmentReportService` / `GovernmentReportService`: one method per report, taking a year
  and month and returning a report DTO: employer header, rows, totals, missing-number counts and
  the count of unpaid runs.
- The row arithmetic (combining cutoffs, working back MSC and EC, splitting the 13th month) sits
  in pure static helpers, so it can be tested without repositories.
- `GovernmentReportCsv`: writes each report DTO as CSV (a small writer; the Application project doesn't reference
  CsvHelper), cells that would run as Excel formulas neutralised, the header rows first, then
  the rows and the totals.

**Repository** (`IPayrollRunRepository`), two new queries, each loading entries with their
employee and government IDs:
- `GetPaidRunsByPeriodEndMonthAsync(year, month)` for SSS, PhilHealth and Pag-IBIG.
- `GetPaidRunsByPayMonthAsync(year, month)` for 1601-C. The earlier-in-year 13th month comes from
  the existing `GetPaidRunsInYearAsync`, filtered to pay dates before the month.
- `CountUnpaidRunsAsync(year, month, byPayDate)` for the "not paid yet" note: runs in the month on
  the same basis as the report, whose status is anything but Paid.

**API**: `GovernmentReportsController` at `api/reports/government`:
- `GET {report}?year=&month=` where `{report}` is `sss`, `philhealth`, `pagibig` or `1601c`,
  returning the DTO.
- The same with `&format=csv`, returning `text/csv` named
  `<report>-<yyyy>-<mm>.csv`, for example `sss-2026-03.csv`.
- Guarded by `Permissions.PayrollManage`, like BIR 2316.
- An unknown report name is 404. A month in the future, or a month outside 1-12, is 400.

**Web**:
- `Pages/Payroll/GovernmentReports.razor` at `/government-reports`, added to the Payroll section
  of the nav next to BIR Form 2316.
- A month picker defaulting to last month, since remittances are for the month just ended.
- Tabs for SSS, PhilHealth, Pag-IBIG and BIR 1601-C, each with the employer header, the
  missing-number summary, the table with its totals row, and **Download CSV**.
- `ApiClient` methods for the DTO and the CSV download, following the payslip download pattern.

## Testing

- **Helpers (unit):** two semi-monthly cutoffs combine into one row; the MSC and EC worked back
  across the EC step (14,500 gives 10.00, 15,000 gives 30.00) and at the 35,000 ceiling; blank
  MSC and EC when rates are overridden; the 13th month split with nothing, some, and all of the
  exemption used earlier in the year.
- **Service (unit, mocked repositories):**
  - the month basis: a Dec 16-31 run paid Jan 5 is on December's SSS list and January's 1601-C;
  - only Paid runs count;
  - missing numbers are counted;
  - the 1601-C lines add up (total = non-taxable + taxable) and match the entries' tax withheld.
- **Repository (Postgres):** each month query returns only Paid runs on its own date basis, with
  the government IDs loaded.
- **Controller:** permission required; 404 for an unknown report; 400 for a bad month; the CSV
  content type and file name.
- **Web (bUnit):** the page loads last month by default; switching tabs and months calls the API;
  the missing-number summary shows; Download CSV requests the CSV.
