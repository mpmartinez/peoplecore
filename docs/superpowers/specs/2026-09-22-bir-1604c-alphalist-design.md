# BIR 1604-C alphalist - design

## Problem

Every January a Philippine employer files BIR 1604-C, the annual information return of income
tax withheld on compensation. With it goes the alphalist: one line per employee paid in the year,
showing their compensation, its non-taxable and taxable parts, the tax due and the tax withheld.
PeopleCore already builds each employee's BIR 2316 for a year, and the alphalist is those
certificates side by side. HR has no way to see it today.

A second gap sits under it. The 2316 fields HR types in by hand, a previous employer's figures
above all, aren't stored anywhere. They're retyped each time a single 2316 is generated.
"Generate all" leaves them blank, and an alphalist built from the same data would too.

## Goal

- A **BIR 1604-C** tab on the Government Reports page. For a chosen year it shows the alphalist
  in BIR's groups, with totals and warnings, and downloads it as CSV.
- The **2316 manual inputs are saved** per employee per year. The single 2316, "Generate all"
  and the alphalist all read the same saved values, so they always agree.

HR reviews the alphalist in PeopleCore and then enters or imports it into BIR's Alphalist Data
Entry module, which produces the DAT file BIR accepts.

## Out of scope

- **The BIR DAT file** in its exact layout. It can follow once the layout is confirmed against a
  real client's filing.
- **The minimum wage earner schedule.** PeopleCore doesn't model minimum wage earners, and BIR
  2316 has that switched off (`Bir2316ManualInputs`). The page says the schedule is not included.
- **The 1604-C form's monthly remittance summary** (each month's tax remitted, with date and
  reference). PeopleCore doesn't record remittances.
- **A year-end tax adjustment in payroll.** The alphalist shows what the adjustment should be; it
  doesn't change any payslip.
- PDF printouts.

## Saved 2316 inputs

**Stored:** a `Bir2316Inputs` row per employee per year (unique on employee and year), holding
exactly the fields of `Bir2316ManualInputs`:
- previous employer TIN, name, address and ZIP code;
- previous taxable compensation (Item 22) and previous tax withheld (Item 25B);
- PERA tax credit (Item 27), de minimis (Item 35), hazard pay for MWEs (Item 33);
- statutory minimum wage per day and per month.

No derived figure is stored. The rule `Bir2316ManualInputs` exists to enforce still holds: a
certificate's money comes from payroll, and these rows only hold what a person supplies.

**Written:** when a single 2316 is generated (`POST api/reports/2316/generate/{employeeId}`), the
inputs it was generated with are saved for that employee and year, after the same validation
(`Bir2316ManualInputsValidator`). Generating again with different inputs replaces them.

**Read:**
- `GET api/reports/2316/inputs/{employeeId}?year=` returns the saved inputs, or empty inputs
  when none are saved. The 2316 page fills its form from this when an employee and year are
  chosen.
- The 2316 preview (`GetPreviewAsync`) uses the saved inputs instead of empty ones, so the
  on-screen totals match what the PDF will show.
- "Generate all" (`BuildAllAsync`) uses each employee's saved inputs instead of empty ones.
- The alphalist uses them through `BuildAllAsync`.

This changes "Generate all": bulk 2316s now carry the previous-employer figures HR has entered.

## The alphalist

**Source:** `Bir2316Service.BuildAllAsync(year)`, one `Bir2316Dto` per employee with a Paid run
paid in the year. The alphalist cannot disagree with the certificates because it is them.

**Groups**, following BIR's 1604-C schedules:
1. **Terminated before December 31:** the employee's `SeparationDate` falls within the year and
   before December 31.
2. **Employed as of December 31, no previous employer:** not in group 1, and their saved inputs
   carry no previous-employer taxable compensation or tax withheld (Items 22 and 25B both zero).
3. **Employed as of December 31, with previous employer:** not in group 1, and Item 22 or 25B is
   non-zero.

Within a group, rows are sorted by last name then first name, as in BIR 2316's bulk order.

**Columns** (every group; the two previous-employer columns only in group 3):

| Column | From the 2316 |
|---|---|
| TIN | `EmployeeTin` |
| Last name, first name, middle name | `EmployeeLastName`, `EmployeeFirstName`, `EmployeeMiddleName` |
| Employed from, employed to | the employee's `HireDate` and `SeparationDate`, clamped to the year |
| Gross compensation | `Item19_GrossCompensation` |
| 13th month and other benefits (non-taxable) | `Item34_ThirteenthMonthAndBenefits` |
| De minimis | `Item35_DeMinimis` |
| SSS, PhilHealth and Pag-IBIG employee shares | `Item36_SssPhicPagibigContributions` |
| Other non-taxable compensation | `Item37_SalariesOtherForms` |
| Total non-taxable | `Item38_TotalNonTaxable` |
| Basic salary (taxable) | `Item39_BasicSalary` |
| 13th month and other benefits (taxable) | `Item48_TaxableThirteenthMonth` |
| Other taxable compensation | `Item52_TotalTaxableCompensation` less the two above |
| Total taxable (present employer) | `Item52_TotalTaxableCompensation` |
| Previous employer's taxable compensation (group 3) | `Item22_PrevTaxableCompensation` |
| Previous employer's tax withheld (group 3) | `Item25B_PrevTaxWithheld` |
| Tax due | `Item24_TaxDue` |
| Tax withheld, January to November | present-employer tax withheld in runs paid January to November |
| Tax withheld, December | present-employer tax withheld in runs paid in December |
| Total tax withheld | `Item26_TotalTaxWithheld` (present plus previous employer) |
| To collect / (refund) | tax due less total tax withheld; negative is a refund |

The January-November and December split is not on the 2316. It comes from the same year's Paid
runs (`GetPaidRunsInYearAsync`), summing each employee's `WithholdingTax` by the month of
`PayDate`. The two parts add up to `Item25A_PresentTaxWithheld`.

**To collect / (refund).** Payroll doesn't make a year-end adjustment yet, so tax withheld through
the year rarely matches the tax due exactly. This column shows the difference HR has to settle:
collect it in December's pay, or refund it. The page explains it in one line above the tables.

Each group has a totals row of its money columns. A group with no employees shows "No employees in
this group" and no table.

## Checks and messages

- An employee with no TIN stays in the list with the cell marked. One warning counts them: "3
  employees have no TIN." BIR's module rejects a line without one.
- A blank company TIN or RDO code: "The company's TIN is blank. Add it on the Company page." (or
  the RDO code, or both in one message, as the monthly reports word it).
- Runs in the year that aren't paid yet: "2 payroll runs paid this year aren't paid yet and aren't
  included." This is counted by `PayDate` year, the basis the 2316 uses.
- Employees hired during the year who have no previous-employer entry, and so land in group 2:
  "4 employees hired this year have no previous employer entered. If they worked elsewhere earlier
  in the year, add it on their BIR 2316 so they move to the right group." This is advice, not an
  error.
- A year with no Paid runs: "No payroll was paid in 2026."
- A year after the current Philippine year, or outside 1-9999: refused (400).
- The page notes that the minimum wage earner schedule is not included.

## Architecture

**Domain / persistence:**
- `Bir2316Inputs` entity, with a table and migration, unique on `(EmployeeId, Year)`. Its
  configuration follows the other payroll configurations (`numeric(18,2)` for money).
- `IBir2316InputsRepository`: `GetAsync(employeeId, year)`, `GetForYearAsync(year)` returning a
  dictionary by employee, and `SaveAsync(employeeId, year, inputs)` as an upsert.

**Application:**
- `Bir2316Service`: a new `GenerateAsync(employeeId, year, inputs)` builds the 2316 and saves the
  inputs; the Generate endpoint calls it. `BuildAsync` itself saves nothing, so it stays
  side-effect free for its other callers. `GetPreviewAsync` and `BuildAllAsync` read saved inputs.
  `GetInputsAsync(employeeId, year)` returns them for the page.
- `GovernmentReportDto` gains `Sections`: a list of `GovernmentReportSectionDto(Title, Columns,
  Rows, Totals, EmptyMessage)`. The monthly reports leave it empty and keep using the top-level
  table. The alphalist leaves the top-level table empty and fills three sections. No existing
  field changes.
- An alphalist builder, `Bir1604CAlphalist`, separate from `GovernmentReportService` so that class
  doesn't grow a fifth report. It takes the 2316 DTOs, the employees' hire and separation dates,
  and the year's runs, and returns the report DTO. `GovernmentReportService` gains an annual entry
  point that calls it for `1604c`.
- `GovernmentReportCsv` writes each section in turn: a title line, the columns, the rows and the
  totals, with a blank line between sections. The same formula-injection neutralising applies.

**API:**
- `GET api/reports/government/1604c?year=` (JSON) and `&format=csv`, with the file name
  `1604c-<yyyy>.csv`. The monthly reports keep requiring `month`. For `1604c` a `month` is
  ignored, and for the others a missing month is a 400.
- `GET api/reports/2316/inputs/{employeeId}?year=` on `Bir2316Controller`, which already carries
  `PayrollManage`.

**Web:**
- Government Reports: a "BIR 1604-C" tab. On it the month picker is replaced by a year picker,
  defaulting to last year, since the alphalist is filed in January for the year just ended. It
  shows a table per section, each with its title, totals and empty message, plus the warnings and
  Download CSV.
- BIR 2316 page: after an employee and year are chosen, fill the manual-input form from the saved
  inputs.

## Testing

- **Alphalist builder (unit):**
  - grouping: separated in the year → group 1; separated on December 31 → not group 1; Item 22 or
    25B non-zero → group 3;
  - the columns come from the right 2316 items;
  - "Other taxable compensation" is the remainder;
  - tax withheld January-November and December add up to Item 25A;
  - to collect / (refund) sign;
  - totals per group;
  - the missing-TIN, company, unpaid-runs and hired-this-year warnings.
- **Bir2316Service (unit):**
  - Generate saves the inputs;
  - Preview and BuildAll read the saved inputs;
  - BuildAsync on its own saves nothing;
  - invalid inputs are refused before anything is saved.
- **Repository (Postgres):** upsert replaces rather than duplicates; the unique index holds;
  `GetForYearAsync` returns only that year.
- **CSV:** sections written in order with their titles; formula neutralising still applies.
- **Controller:** `1604c` needs no month; a monthly report without one is a 400; the CSV file
  name; the inputs endpoint.
- **Web (bUnit):**
  - the 1604-C tab shows the year picker and the three section tables;
  - the empty-group message shows;
  - Download CSV requests the annual CSV;
  - the 2316 page fills in saved previous-employer figures.
