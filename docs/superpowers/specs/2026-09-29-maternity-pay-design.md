# Maternity pay in payroll - design

Date: 2026-09-29. Status: implemented (final-review fixes 2026-09-30).

## Why

Maternity leave (RA 11210) is filed and approved in PeopleCore, but payroll treated it as ordinary
paid leave: full salary, fully taxed. The law splits it in two. SSS pays a maternity benefit (the
average daily salary credit x the leave days), which the employer advances in full within 30 days
and SSS reimburses. The employer pays the rest of the salary for the leave days - the salary
differential - unless exempt. Neither the SSS benefit nor the salary differential is taxed: the
differential is part of the maternity benefit (RMC 105-2019).

## Decisions

- The benefit is advanced as a lump sum on a payroll HR chooses (a regular run or the final pay);
  the leave cutoffs then pay the salary less the part SSS covers.
- The daily allowance is entered by HR, with a suggestion from PeopleCore's own payroll history.
- Exempt employers are supported with a payroll setting.
- A covered cutoff never pays a negative net: the contribution shares it can't pay are deferred and
  collected later.

## The claim

`MaternityClaim` - one per approved leave request of a maternity type (`LeaveType.IsMaternity`):

- `LeaveRequestId` (unique), `EmployeeId`.
- `DailyAllowance` (decimal?, set by HR).
- `Days` - the request's `TotalDays` (the maternity case's days, less any allocated to the father).
- `Benefit` = round(`DailyAllowance` x `Days`, 2), stored when the allowance is set.
- `Status`: `Draft`, `Advanced`, `Reimbursed`, `Denied`, `Voided`, `NotQualified`.
- `AdvanceRunId` (Guid?), `AdvancedAt` (DateOnly?).
- `ReimbursedOn` (DateOnly?), `ReimbursedAmount` (decimal?), `Note` (string?, up to 500).

Rules:

- A claim can only be created for an Approved maternity request, and only once (the unique index
  answers a racing create with the same message).
- A maternity leave type must be per event, paid, and count calendar days ("A maternity leave type
  must count calendar days.").
- **Ready** means Draft, with an allowance, for a leave request that is still Approved, and no run
  (of any type or status) already carries its advance. `GET api/maternity-claims/ready` lists the
  employees with a ready claim; `GET api/maternity-claims/eligible` lists the Approved maternity
  requests with no claim.
- **Setting the allowance:** only while `Draft`; must be > 0; rounded to 2 dp; never above the
  statutory maximum, 20,000 x 6 / 180 = 666.67 ("The SSS daily maternity allowance can't exceed
  ₱666.67."). Refused while an unpaid run advances the benefit ("{RunNumber} advances this benefit;
  discard it or pay it first.") and once any Paid run has netted an offset for the claim ("{RunNumber}
  already netted this allowance; it can't change now.").
- **Suggested allowance:** the SSS formula - the 6 highest monthly salary credits (MSC) in the 12
  months before the semester of contingency, divided by 180. The semester is the two quarters
  ending with the quarter of the leave's start date. MSCs come from the employee's Paid runs (final
  pays included) whose period ends in the month, worked back from the month's SSS share as the SSS
  remittance report does; a month whose cutoffs aren't all paid is skipped; each MSC counts only up
  to the Regular SS ceiling of 20,000 (SSS Circular 2024-006). The suggestion states how many months
  it found. When the payroll settings override both SSS rates, no suggestion is given
  (`RatesOverridden`).
- **Void:** `PUT api/maternity-claims/{id}/void` with `{ note }`. Only a Draft claim ("Only a draft
  claim can be voided.") whose leave is no longer approved ("This leave is still approved; cancel the
  leave first, or correct the allowance."), that no unpaid run advances ("{RunNumber} advances this
  benefit; discard it or pay it first.") and no Paid run has netted ("{RunNumber} already netted this
  allowance; it can't change now."); the note is required ("Explain why the claim is voided."). A
  Voided claim is never ready, never offsets, and isn't outstanding. The claim list names the Paid
  run that netted a Draft claim (`NettedByRunNumber`), and the page offers Void only where it is
  allowed.
- **Not SSS-qualified:** `PUT api/maternity-claims/{id}/not-qualified` with `{ note }`, for an
  employee on approved maternity leave who doesn't qualify for the SSS benefit. Only a Draft claim
  ("Only a draft claim can be marked not SSS-qualified.") that no unpaid run advances, and while no
  Paid run has an offset against days of its leave, exempt or not ("{RunNumber} already paid this
  leave against the SSS benefit; the claim can't be marked not qualified."); the note is required ("Explain why she doesn't qualify for the SSS benefit."). Her leave
  days are then paid and taxed as ordinary salary: no advance, offset, differential or deferral - for
  an exempt employer too (the exemption is from the differential; without an SSS benefit she is paid
  her salary). Approval accepts the claim, no "not set up" or "not advanced" warning is given, and it
  is never ready or outstanding. `PUT api/maternity-claims/{id}/reopen` sets it back to Draft (the
  note cleared) while no Paid run covers days of its leave ("{RunNumber} already paid this leave as
  ordinary salary; the claim can't be reopened.").
- **Re-link:** `PUT api/maternity-claims/{id}/relink` with `{ leaveRequestId }`, for leave that was
  cancelled and refiled. Only when the claim's own leave is Cancelled or Rejected ("Only a claim whose
  leave was cancelled can be moved."), to an Approved maternity request of the same employee with no
  claim ("Choose an approved maternity leave of the same employee that has no claim."). `Days` takes
  the new request's `TotalDays`; a Draft claim's `Benefit` is recomputed, an Advanced or Reimbursed
  claim keeps the `Benefit` that was paid - and when its days change, the answer carries a `Warning`:
  "The benefit of ₱{Benefit} was paid for {old} days; this leave has {new}. Payroll will net {new}
  days.", which the page shows. The claim list says when a claim's leave was cancelled
  (`LeaveCancelled`). A move racing another claim for the same leave gets the same readable refusal
  (the unique index on the leave request is the guard).

## The advance

- A regular payroll (`PayrollRunEmployeeInput.AdvanceMaternityBenefit`) or a final pay
  (`FinalPayRequest.AdvanceMaternityBenefit`, default false) can advance an employee's benefit.
- It is refused unless the employee has a ready claim: "{name} has no maternity claim ready to
  advance." And only once: "{name}'s maternity benefit was already advanced on {RunNumber}."
- The entry records `MaternityBenefitAdvance` = the claim's `Benefit` and the claim
  (`MaternityClaimId`). It is tax-free, stays out of the withholding base, the contribution base and
  the 13th month, and is added to gross pay (not to the employer's cost).
- Approval and Mark Paid also work every entry's offset and differential out again from the claims
  as they are now (allowance, status, the exemption) and refuse a difference: "{name}'s maternity
  claim has changed since this payroll was computed; recompute it before approving." / "...; discard
  this payroll and create it again."
- Approval refuses a run whose advanced claim changed since it was computed; Mark Paid makes the
  claim `Advanced` with the run and the pay date, in the same single save as the run's other changes.

## Leave cutoffs (and the final pay's period)

For an employee with an Approved maternity request overlapping the period (each calendar day counted
once across overlapping requests):

- **Maternity-days pay** = round(regular pay before the offset x maternity days ÷ period calendar
  days, 2). Regular pay before the offset is after absences and tardiness.
- **Offset** = min(maternity-days pay, daily allowance x maternity days), for days whose claim (not
  Voided) has an allowance. It is capped at the maternity-days pay, so pay for days outside the leave
  is never reduced. `RegularPay` is reduced by it.
- **Salary differential** = maternity-days pay - offset (`MaternityDifferential`). It stays in
  `RegularPay` (so it counts toward the 13th month) but is left out of the withholding base.
- **Exempt employers** (`PayrollSettings.ExemptFromMaternityDifferential`): the offset is the whole
  maternity-days pay, whether or not a claim exists, and there is no differential.
- The entry records the claim whose allowance the offset nets (`MaternityClaimId`) when it advances
  none.
- No claim, or no allowance yet: no offset; the payroll warns "Maternity benefit not set up yet for
  {name}.", and **approval is refused** unless the employer is exempt: "Set up {name}'s maternity
  claim before approving; this payroll covers {n} maternity day(s)."
- A ready claim no run advances yet: the payroll warns "Maternity benefit not advanced yet for
  {name}." Warnings are rebuilt on every load of the run (not on the employee's payslip).
- A final pay applies the same rules to its own period, through the same calculator; one with no
  salary days has no leave days to offset.
- Contributions continue on the monthly basic, unchanged. Recompute reproduces every figure.

## Deferred contributions

- On an entry with an offset, the employee shares (SSS, PhilHealth, Pag-IBIG) stay in full in their
  fields (the employer remits them), but the part the entry's cash can't cover - gross less every
  other deduction, floor 0 - is `ContributionsDeferred`. Loans and HR's deductions are never
  collected from the advance.
- Outstanding for an employee = sum of `ContributionsDeferred` - sum of
  `DeferredContributionsCollected` over her Paid entries.
- Any later entry (regular or final pay) collects `DeferredContributionsCollected` = min(outstanding,
  the cash left after its own deductions); the maternity advance counts as cash for this only.
- `NetPay` = gross - total deductions + `ContributionsDeferred` - `DeferredContributionsCollected`.
- Mark Paid refuses an entry whose collection no longer matches what is outstanding now: "{name}'s
  deferred contributions have changed since this payroll was computed; recompute it before paying."
  Such an Approved regular run can be recomputed (back to Draft).
- The final pay is her last pay: what it can't collect (`DeferredContributionsUncollected` on its
  summary) is shown on the separation page as a warning, for HR to recover another way.

## Reimbursement

- HR records a reimbursement on an `Advanced` claim: the date and amount. An amount different
  from `Benefit` needs a note: "Explain why the reimbursement differs from the benefit."
- HR can record a denial on an `Advanced` claim, with a note: "Explain why SSS denied the claim."
- Outstanding receivable = the sum of `Benefit` over `Advanced` claims.

## Reports

- The SSS benefit is not the employer's compensation: `MaternityBenefitAdvance` is left out of the
  2316, 1601-C and 1604-C.
- The salary differential is non-taxable compensation: 2316 Item 37 takes it and Item 39 (taxable
  basic) leaves it out; the 1601-C puts it in "Other non-taxable"; the 1604-C follows the 2316.
- The payslip shows "SSS maternity benefit (advance)" and "Maternity salary differential
  (non-taxable)" as earnings, "Less: covered by SSS maternity benefit" against basic pay (shown net of
  the differential), and "Contributions deferred (collected later)" / "Deferred contributions
  collected" among the deductions. Earnings still sum to gross, and gross less deductions to net.

## Pages

- **Maternity claims** (`/maternity-claims`, `payroll.manage`): the list with employee, leave
  dates, days, benefit, status and run, and the outstanding total; create a claim from an approved
  maternity request; set the allowance (showing the suggestion and months found); record
  reimbursement or denial; void a Draft claim; mark a Draft claim not SSS-qualified, and reopen it;
  move a claim whose leave was cancelled to the refiled leave.
- **Create payroll:** "Advance maternity benefit" per employee, offered only for employees with a
  claim ready to advance, with the note "While her leave is covered by SSS, her contribution shares
  are deferred and collected from the advance or her next pay."
- **Payroll page:** columns for the advance, the offset and the differential when present, and the
  warnings; deductions shown so gross less deductions is net.
- **Separation page (final pay):** "Advance maternity benefit" when she has a ready claim; the
  summary shows the advance, the offset, the differential and the maternity warnings.
- **Payroll settings** (`/payroll-settings`, reading and writing `api/payroll-settings/default`, the
  row payroll computes from): the exemption switch.

## Storage

Three migrations:

- `AddMaternityPay`: the `maternity_claims` table, `payroll_run_employees.advance_maternity_benefit`,
  `maternity_benefit_advance` and `maternity_benefit_offset`, and
  `payroll_settings.exempt_from_maternity_differential`.
- `AddMaternityClaimToEntry`: `payroll_run_employees.maternity_claim_id` (FK, SET NULL).
- `AddMaternityDifferential`: `payroll_run_employees.maternity_differential`,
  `contributions_deferred` and `deferred_contributions_collected` (`numeric(18,2)`, default 0).

## Out of scope

- The 30-day unpaid extension.
- SSS sickness benefit.
- Filing with SSS (MAT-1/MAT-2) from PeopleCore.

## Testing

- The suggested allowance: the window and semester, the 6 highest MSCs, the ceiling, fewer months.
- The benefit for 105, 120 and 60 days, and the maximum allowance.
- The advance: once, tax-free, refused without a ready claim or twice; the claim becomes Advanced
  on Mark Paid, on a regular run or a final pay.
- The offset and differential: split across cutoffs by calendar days, capped at the leave days' pay,
  exempt employers, no claim, voided claims, cancelled leave; the approval refusal.
- Deferred contributions: deferring, collecting, the Mark Paid guard and the recompute.
- The 2316, 1601-C and 1604-C reconcile with the differential and without the benefit.
- Void, re-link, the allowance lock; permissions and the pages.
