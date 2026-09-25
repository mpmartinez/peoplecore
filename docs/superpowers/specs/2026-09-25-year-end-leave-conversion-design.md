# Year-end leave conversion - design

Date: 2026-09-25. Status: implemented; updated after the final review to match the code.

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
- Only a paid Accrued type can convert: "Only paid accrued leave can convert at year-end." A
  per-event type has no yearly balance; a yearly-allowance type's balance row only exists once the
  employee first files, so an employee who never filed would convert nothing and one who filed a
  day would convert the rest; an unpaid type's days are worth nothing in cash.

## The payroll

- `CreatePayrollRunRequest` gains `IncludeLeaveConversion` (bool, default false); it is stored on
  `PayrollRun.IncludesLeaveConversion` and honoured on every recompute.
- Only regular runs whose `PeriodEnd` is in December may carry it: "Year-end leave conversion goes
  on a December payroll."
- It can be switched on or off after create: `PUT api/payroll-runs/{id}/leave-conversion` with
  `{ "include": bool }` (`IPayrollRunService.SetLeaveConversionAsync`), under `PayrollManage`,
  returning the run. It recomputes the run, and an Approved or For approval run goes back to
  Draft. Refused: a Paid run ("A paid payroll run can't be changed."), a final pay (switching on
  gets the December message; switching off, "A final pay's leave conversion can't be changed
  here."), and a request that changes nothing ("This payroll already converts unused leave." /
  "This payroll already doesn't convert unused leave.").
- **Per employee on that run** (the year is `PeriodEnd.Year`):
  - For each active type with `ConvertsAtYearEnd`: days = that year's balance `RemainingDays`
    minus the days of the employee's Pending requests of that type charged to that year (the
    same pending-hold rule filing uses), floored at 0. No balance row means 0.
  - Rate: the employee's daily rate, as final pay uses it.
  - Tax: `FinalPayMath.LeaveConversion` - vacation-type days (types with
    `CountsAsVacationForDeMinimis`) are de minimis (2316 Item 35) up to 10 days per tax year: the
    pay year's earlier Paid runs' de minimis leave (`LeaveConversionNonTaxable / DailyRate`, to the
    centi-day) uses some of them up (`LeavePayout.DeMinimisDaysLeft`), and final pay counts the
    same way. The rest is "other benefits" sharing the 90,000 exemption with the 13th month (exempt
    within what the year's earlier Paid runs and this run's 13th month leave, Item 34; taxable past
    it, Item 48).
  - The entry's existing fields carry it exactly as on a final pay: `LeaveConversionPay`,
    `LeaveConversionNonTaxable`, `LeaveConversionOtherBenefits`, `FinalPayNonTaxable`
    (= the de minimis part here). The payslip, 2316, 1601-C and 1604-C need no changes.
- **Once a year:** an employee whose leave for that year is already converted on another
  Regular run of any status (the employee's entry there has `LeaveConversionPay > 0`) is refused: "{name}'s leave for {year}
  was already converted in {RunNumber}."
- Employees who have left aren't on regular runs; final pay converts their leave.

## The 13th month on a regular run

The conversion shares the 90,000 exemption with the 13th month, which the web can now pay too
(plan Task 5b): an "Include 13th month" tick on the create form, and
`PUT api/payroll-runs/{id}/thirteenth-month` with `{ "include": bool }`
(`IPayrollRunService.SetThirteenthMonthAsync`), under `PayrollManage`, which sets every entry,
recomputes the run and sends an Approved or For approval run back to Draft. It is allowed in any
month (an advance nets out of the 13th month paid later), and refused on a Paid run, a final pay,
or a request that changes nothing. Computing a regular run that includes it is refused when:

- **its pay date is in another year than its period end** - the 13th month is due by Dec 24
  (PD 851) and is worked out from the pay year's basic, so a Dec 16-31 run paid Jan 5 would
  underpay it and count it as the next year's: "The {PeriodEnd.Year} 13th month must be paid by
  Dec 24, {PeriodEnd.Year}; give this payroll a pay date in {PeriodEnd.Year}." Checked on create,
  on every recompute and when it's switched on; switching it off stays allowed.
- **another unpaid regular run of the pay year also includes it**, for the same employee: "{name}'s
  13th month is already on {RunNumber}, which isn't paid yet; pay it or leave it out there first."
- **an earlier cutoff is unpaid** - the employee is on another regular run of the pay year with an
  earlier pay date that isn't Paid, whose basic the 13th month (worked out from Paid runs only)
  would leave out: "{name} is on {RunNumber}, which isn't paid yet; pay it before computing the
  13th month."

Employees who aren't 13th-month eligible are paid none of it, so neither of the last two checks
holds them. Each entry keeps the 13th month already paid elsewhere in the pay year when it was
computed (`ThirteenthMonthPaidEarlierInYear`); Mark Paid refuses the run once the pay year's other
Paid runs hold more than that: "{name}'s 13th month was paid on {RunNumber} after this payroll was
computed; recompute it before paying." An Approved regular run that includes the 13th month can
be recomputed (back to Draft) for that reason.

## Paying

- Mark Paid records each employee's converted days as `LeaveBalance.UsedDays` on the balances
  they came from with `LeavePayout.Apply`, the step final pay uses too (moved out of
  `FinalPayService`; the plan called it `RecordAsync`). It changes the balances without saving;
  `IPayrollRunRepository.SavePaidAsync` saves the run's status, the retired loans and the balances
  in one save. The balances' `UpdatedAt` comes from the injected `TimeProvider`.
- It is refused while those balances no longer price, at the entry's daily rate and with the
  de minimis days left now, to its `LeaveConversionPay` and its de minimis part: "{name}'s
  convertible leave has changed since this payroll was computed; recompute it before paying."
  (Final pay keeps its own wording.) An Approved run with the conversion can be recomputed (back
  to Draft) for that reason.
- **Approve** makes the same check, so leave that changed since the run was computed is caught
  before anyone approves figures that can't be paid.

## Engine

`PayrollComputationService.Compute` takes an optional `LeaveConversionInput(decimal DeMinimis,
decimal OtherBenefits)`. The final-pay extras pass their leave amounts through it, so both paths
share one code path; the entry fields are filled exactly as final pay fills them today.

## Pages

- **Payroll runs, create:** a "Year-end pay" group with "Include 13th month" (every period,
  noted "Due by Dec 24 (PD 851).") and "Convert unused leave", shown when the period ends in
  December and ticked by default only when it ends on Dec 31 (a Dec 1-15 cutoff doesn't convert
  early); the user can tick or untick it, and a tick is sent only while the period ends in December.
- **Run detail:** a "Year-end leave conversion" badge and a Leave conversion column when the run
  has it, and a toggle to switch it on or off; a "13th month" badge and column, and a toggle whose
  include confirmation also says "Due by Dec 24 (PD 851)." Each toggle asks first and says an
  Approved or For approval run goes back to Draft.
- **Leave Types page:** the "Converts to cash at year-end" setting, offered only for a paid Accrued
  type (hidden while carry-over is on, and carry-over hidden while it is on; a tick left behind by
  another kind isn't sent), "Year-end cash" in the rules summary. The SIL note becomes "Unused SIL
  is converted to cash on the December payroll (tick Convert unused leave)."

## Storage

Two migrations:

- `AddYearEndLeaveConversion`: `leave_types.converts_at_year_end` (bool, default false),
  `payroll_runs.includes_leave_conversion` (bool, default false), and the SIL data update
  (`upper(btrim(code)) = 'SIL'`).
- `AddThirteenthMonthPaidEarlierInYear`: `payroll_run_employees.thirteenth_month_paid_earlier_in_year`
  (numeric(18,2), nullable) - null on entries that computed no 13th month and on older entries,
  which Mark Paid doesn't check.

## Out of scope

- Converting only the days above a carry-over cap.
- Converting in January for the previous year.
- Partial conversion (some days kept, some paid).

## Testing

- Days: remaining minus pending holds, floor at 0, no balance row, inactive types skipped.
- Tax split: the 10-day de minimis cap across VL and SIL; the excess sharing the 90,000 with the
  13th month; the 2316 and 1601-C reconcile for a run with both.
- Refusals: a non-December run, a second conversion in the year, carry-over plus conversion, a
  type that isn't paid Accrued; the 13th month on a run paid in another year, on two unpaid runs,
  and while an earlier cutoff is unpaid.
- Recompute reproduces the conversion; Approve and Mark Paid refuse when a balance changed, and
  Mark Paid draws the days down.
- Migration: existing SIL turned on, others untouched; the statutory set creates SIL with it on.
- Final pay's figures unchanged after the engine refactor.
- bUnit: the tick boxes (Convert unused leave December only, ticked by default for a Dec 31 period end), the badges,
  columns and toggles, the Leave Types setting (paid Accrued only) and note.
