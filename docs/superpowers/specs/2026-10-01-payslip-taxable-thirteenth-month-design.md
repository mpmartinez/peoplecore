# Payslip: the taxable part of the 13th month - design

Date: 2026-10-01. Status: built and reviewed.

## Why

The engine already splits a run's 13th month into an exempt part and a taxable excess when it
works out the tax (`ComputeThirteenthMonthTax`): the exemption is ₱90,000 a year, shared with other
benefits and with what earlier runs used. The payslip never sees that split. It prints one
"13th Month Pay" line flagged non-taxable, so an employee whose 13th month went over the
exemption is shown a figure that was partly taxed with no sign of it.

## The figure

`PayrollRunEmployee.ThirteenthMonthTaxable` (`decimal?`, `numeric(18,2)`, null by default):

- Worked out in `PayrollComputationService.Compute`, where `exemptUsedEarlierInYear` and the
  exemption left are already known: `exemptionLeft = max(0, 90,000 − exemptUsedEarlierInYear)`;
  the 13th month fills the exemption first, so `ThirteenthMonthTaxable = max(0, ThirteenthMonth −
  exemptionLeft)`. Leave beyond de minimis ("other benefits") takes whatever exemption remains
  after it, as it does today. The total taxable excess and the tax are unchanged.
- Stored on every entry the engine computes, 0 when nothing is taxable. A final pay re-bases it on
  the pay year's exemption used (other benefits and the opening balance included), as its tax and
  the 2316 do.
- Null on entries computed before this change. Their payslips keep the single non-taxable line;
  recomputing an unpaid run gives it the split. Paid runs are never recomputed.
- `ThirteenthMonthExempt` (computed property) = `ThirteenthMonth − (ThirteenthMonthTaxable ?? 0)`.
- Not part of `GrossPay`, taxable pay or any tax formula; the 2316, 1601-C and 1604-C already
  split the 13th month at the year level and are unchanged.
- One migration: the nullable column on `payroll_run_employees`.

## The payslip

`PayslipLineBuilder.Earnings`:

- `ThirteenthMonthTaxable` null or 0: one line "13th Month Pay", non-taxable, as now.
- Greater than 0: two lines - "13th Month Pay (non-taxable)" for `ThirteenthMonthExempt` (only
  when it is above 0) and "13th Month Pay (taxable portion)" for `ThirteenthMonthTaxable`,
  flagged taxable.
- The earning lines still add up to `GrossPay`, and gross less deductions equals net.

`PayrollRunEmployeeDto` (API and web mirror) gains a trailing `decimal? ThirteenthMonthTaxable`;
`PayslipDocument` prints whatever the builder returns.

## The payroll page

The run detail's 13th month column shows "of which taxable ₱{n}" beneath the amount when
`ThirteenthMonthTaxable > 0`. Nothing else changes.

## Two follow-ups from the opening-balances review

1. **Mark Paid's refusal for pay before PeopleCore.** `EnsureEarlierInYearUnchangedAsync` always
   says "{name}'s pay before PeopleCore has changed since this payroll was computed; recompute it
   before paying." Another Paid run of hers, with a 13th month or not, paid after this
   entry was computed (`run.UpdatedAt > entry.CreatedAt`) can also have moved the figures (any Paid
   run moves her basic earned), and
   the message then blames the wrong thing. When such a run exists, name it: "{name}'s pay was
   changed by {RunNumber}, paid after this payroll was computed; recompute it before paying."
   Otherwise keep the opening-balance wording. The refusal and the recompute path are unchanged.
2. **The 2316 page's default year.** A balance dated in a future year (a typo such as 2099)
   becomes the default because the available years now include balance years. The default is the
   latest available year that is not after the current Philippine year; later years stay in the
   list.

## Out of scope

- Splitting the 13th month on payslips of runs computed before this change.
- Showing the taxable part on the 2316 (it already reports Item 48).
- The year-end leave conversion's other-benefits line (still flagged as the 13th month's is).

## Testing

- The engine: a 13th month wholly within the exemption (0 taxable); partly over (hand-derived
  split); wholly over; with exemption already used earlier in the year (earlier runs and an
  opening balance); with leave conversion in the same pool (13th month fills the exemption first).
  The tax and every other figure are unchanged.
- The payslip lines: null, 0, partial and full cases; earnings still sum to gross.
- Postgres: the column round-trips and recompute stores it.
- The payroll page: the "of which taxable" line, present only when above 0.
- The two follow-ups, each branch tested.
