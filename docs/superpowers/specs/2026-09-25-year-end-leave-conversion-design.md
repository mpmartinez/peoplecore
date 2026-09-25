# Year-end leave conversion - design

Date: 2026-09-25. Status: approved in brainstorming; awaiting spec review.

## Why

Labor Code Art. 95 makes unused Service Incentive Leave commutable to cash at the end of the year.
PeopleCore accrues SIL (5 days a year after one year of service) but only converts leave to cash
in final pay; at year-end unused SIL simply stays on the balance and, since SIL doesn't carry
over, is lost. The first deadline is December 2026.

## Decisions

- The conversion is paid on a **December regular payroll**: HR ticks "Convert unused leave" when
  creating the year's last payroll, as the 13th month is ticked today.
- Which types convert is a **per-type setting**, on for SIL and off for everything else until HR
  turns it on (a company that also converts VL at year-end can).

## Leave type

- New setting `LeaveType.ConvertsAtYearEnd` (bool, default false), editable on the Leave Types
  page.
- The statutory set creates SIL with it on. A data migration turns it on for existing leave types
  whose code is SIL (trimmed, case-insensitive).
- A type can't both carry over and convert: "A leave type can't both carry over and convert at
  year-end."

## The payroll

- `CreatePayrollRunRequest` gains `IncludeLeaveConversion` (bool, default false); it is stored on
  `PayrollRun.IncludesLeaveConversion` and honoured on every recompute.
- Only regular runs whose `PeriodEnd` is in December may carry it: "Year-end leave conversion goes
  on a December payroll."
- **Per employee on that run** (the year is `PeriodEnd.Year`):
  - For each active type with `ConvertsAtYearEnd`: days = that year's balance `RemainingDays`
    minus the days of the employee's Pending requests of that type charged to that year (the
    same pending-hold rule filing uses), floored at 0. No balance row means 0.
  - Rate: the employee's daily rate, as final pay uses it.
  - Tax: `FinalPayMath.LeaveConversion` - up to 10 days in total across types with
    `CountsAsVacationForDeMinimis` are de minimis (2316 Item 35); the rest is "other benefits"
    sharing the 90,000 exemption with the 13th month (exempt within what the year's earlier Paid
    runs and this run's 13th month leave, Item 34; taxable past it, Item 48).
  - The entry's existing fields carry it exactly as on a final pay: `LeaveConversionPay`,
    `LeaveConversionNonTaxable`, `LeaveConversionOtherBenefits`, `FinalPayNonTaxable`
    (= the de minimis part here). The payslip, 2316, 1601-C and 1604-C need no changes.
- **Once a year:** an employee whose leave for that year is already converted on another
  Regular run of any status (the employee's entry there has `LeaveConversionPay > 0`) is refused: "{name}'s leave for {year}
  was already converted in {RunNumber}."
- Employees who have left aren't on regular runs; final pay converts their leave.

## Paying

- Mark Paid records each employee's converted days as `LeaveBalance.UsedDays` on the balances
  they came from, reusing final pay's paid-out step (moved to a shared helper).
- It is refused while those balances no longer price, at the entry's daily rate, to its
  `LeaveConversionPay`: "{name}'s convertible leave has changed since this payroll was computed;
  recompute it before paying." (Final pay keeps its own wording.)

## Engine

`PayrollComputationService.Compute` takes an optional `LeaveConversionInput(decimal DeMinimis,
decimal OtherBenefits)`. The final-pay extras pass their leave amounts through it, so both paths
share one code path; the entry fields are filled exactly as final pay fills them today.

## Pages

- **Payroll runs, create:** a "Convert unused leave" tick box next to the 13th month, shown when
  the period ends in December.
- **Run detail:** a "Year-end leave conversion" badge and a Leave conversion column when the run
  has it.
- **Leave Types page:** the "Converts to cash at year-end" setting (hidden while carry-over is
  on, and carry-over hidden while it is on), "Year-end cash" in the rules summary. The SIL note
  becomes "Unused SIL is converted to cash on the December payroll (tick Convert unused leave)."

## Storage

One migration: `leave_types.converts_at_year_end` (bool, default false),
`payroll_runs.includes_leave_conversion` (bool, default false), and the SIL data update.

## Out of scope

- Converting only the days above a carry-over cap.
- Converting in January for the previous year.
- Partial conversion (some days kept, some paid).

## Testing

- Days: remaining minus pending holds, floor at 0, no balance row, inactive types skipped.
- Tax split: the 10-day de minimis cap across VL and SIL; the excess sharing the 90,000 with the
  13th month; the 2316 and 1601-C reconcile for a run with both.
- Refusals: a non-December run, a second conversion in the year, carry-over plus conversion.
- Recompute reproduces the conversion; Mark Paid draws the days down and refuses when a balance
  changed.
- Migration: existing SIL turned on, others untouched; the statutory set creates SIL with it on.
- Final pay's figures unchanged after the engine refactor.
- bUnit: the tick box (December only), the badge and column, the Leave Types setting and note.
