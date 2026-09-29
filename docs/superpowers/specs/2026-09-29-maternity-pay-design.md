# Maternity pay in payroll - design

Date: 2026-09-29. Status: approved in brainstorming; awaiting spec review.

## Why

Maternity leave (RA 11210) is filed and approved in PeopleCore, but payroll treats it as ordinary
paid leave: full salary, fully taxed. The law splits it in two. SSS pays a maternity benefit (the
average daily salary credit x the leave days), which the employer advances in full within 30 days
and SSS reimburses. The employer pays the rest of the salary - the salary differential - unless
exempt. The SSS benefit is not compensation and isn't taxed.

## Decisions

- The benefit is advanced as a lump sum on a payroll HR chooses; the leave cutoffs then pay the
  salary less the part SSS covers.
- The daily allowance is entered by HR, with a suggestion from PeopleCore's own payroll history.
- Exempt employers are supported with a payroll setting.

## The claim

`MaternityClaim` - one per approved leave request of a maternity type (`LeaveType.IsMaternity`):

- `LeaveRequestId` (unique), `EmployeeId`.
- `DailyAllowance` (decimal?, set by HR).
- `Days` - the request's `TotalDays` (the maternity case's days, less any allocated to the father).
- `Benefit` = round(`DailyAllowance` x `Days`, 2), stored when the allowance is set.
- `Status`: `Draft`, `Advanced`, `Reimbursed`, `Denied`.
- `AdvanceRunId` (Guid?), `AdvancedAt` (DateOnly?).
- `ReimbursedOn` (DateOnly?), `ReimbursedAmount` (decimal?), `Note` (string?, up to 500).

Rules:

- A claim can only be created for an Approved maternity request, and only once.
- The allowance can be changed while the claim is `Draft`.
- **Suggested allowance:** the SSS formula - the 6 highest monthly salary credits (MSC) in the 12
  months before the semester of contingency, divided by 180. The semester is the two quarters
  ending with the quarter of the leave's start date. MSCs come from the employee's Paid regular
  runs (the MSC the SSS schedule assigns to the month's contributions, as the SSS remittance
  report derives it). The suggestion states how many months it found; HR confirms or overrides.

## The advance

- A regular payroll can advance an employee's benefit: `PayrollRunEmployeeInput` gains
  `AdvanceMaternityBenefit` (bool, default false).
- It is refused unless the employee has a `Draft` claim with a daily allowance: "{name} has no
  maternity claim ready to advance." And only once: "{name}'s maternity benefit was already
  advanced on {RunNumber}."
- The entry records `MaternityBenefitAdvance` = the claim's `Benefit`. It is tax-free, stays out
  of the withholding base and the contribution base, and is added to gross pay.
- When the run is marked Paid, the claim becomes `Advanced` with the run and the pay date.

## Leave cutoffs

- On any regular payroll whose period overlaps the employee's approved maternity leave, the entry
  records `MaternityBenefitOffset` = min(regular pay, the claim's `DailyAllowance` x the
  maternity calendar days inside the period). Regular pay is reduced by it before tax.
- No claim, or no allowance yet: no offset; the payroll page warns "Maternity benefit not set up
  yet for {name}."
- A claim with an allowance but not yet advanced: the offset applies; the page warns
  "Maternity benefit not advanced yet for {name}."
- **Exempt employers:** `PayrollSettings.ExemptFromMaternityDifferential` (bool, default false).
  When on, the offset is the whole regular pay for the maternity days (the days' share of the
  period's regular pay), so she receives only the SSS benefit for them.
- Contributions continue on the monthly basic, unchanged.
- Recompute reproduces both figures.

## Reimbursement

- HR records a reimbursement on an `Advanced` claim: the date and amount. An amount different
  from `Benefit` needs a note: "Explain why the reimbursement differs from the benefit."
- HR can record a denial on an `Advanced` claim, with a note: "Explain why SSS denied the claim."
- Outstanding receivable = the sum of `Benefit` over `Advanced` claims.

## Reports

- The SSS benefit is not the employer's compensation: `MaternityBenefitAdvance` is left out of the
  2316, 1601-C and 1604-C.
- The offset reduces regular pay, so the salary differential lands in basic salary with no report
  change.
- The payslip shows "SSS maternity benefit (advance)" as an earning and "Less: covered by SSS
  maternity benefit" against basic pay.

## Pages

- **Maternity claims** (`/maternity-claims`, `payroll.manage`): the list with employee, leave
  dates, days, benefit, status and run, and the outstanding total; create a claim from an approved
  maternity request; set the allowance (showing the suggestion and months found); record
  reimbursement or denial.
- **Create payroll:** "Advance maternity benefit" per employee, offered only for employees with a
  claim ready to advance.
- **Payroll page:** columns for the advance and the offset when present, and the two warnings.
- **Payroll settings:** the exemption switch.

## Storage

One migration: the `maternity_claims` table, `payroll_run_employees.maternity_benefit_advance`
and `maternity_benefit_offset` (decimal, default 0), and
`payroll_settings.exempt_from_maternity_differential` (bool, default false).

## Out of scope

- The 30-day unpaid extension.
- SSS sickness benefit.
- Filing with SSS (MAT-1/MAT-2) from PeopleCore.

## Testing

- The suggested allowance: the window and semester, the 6 highest MSCs, fewer months found.
- The benefit for 105, 120 and 60 days, and with days given to the father.
- The advance: once, tax-free, refused without a ready claim or twice; the claim becomes Advanced
  on Mark Paid.
- The offset: split across cutoffs by calendar days, the floor at regular pay, exempt employers,
  no claim, not yet advanced.
- Contributions unchanged; the 2316 and 1601-C reconcile without the benefit.
- Reimbursement and denial rules.
- Permissions and the pages.
