# Unworked double regular holidays: 200% - design

Date: 2026-10-01. Status: approved in brainstorming; awaiting spec review.

## Why

When two regular holidays fall on one date (a double holiday), the DOLE handbook pays 200% for the
day if unworked and 300% if worked (390% on a rest day). PeopleCore already pays the worked case
(`DolePremiumRates`, the bridge's `DoubleRegularHoliday` day types). An unworked one is paid only
what an unworked single regular holiday is: the salary's 100%, with no absence booked
(`PayrollAttendanceBridge`, "isUnworkedRegularHoliday"). The other 100% is missing.

## What is paid

- Each double regular holiday on which the employee is scheduled and not present adds one extra
  day of pay, bringing the day to 200% in total: `dailyRate x (2.00 - alreadyPaid) x days`.
- `alreadyPaid` follows the rule worked days already use: 1.00, except 0 on a rest day when the
  daily-rate factor does not pay rest days (313 or 261; the 365 factor does pay them). So on a
  rest day the extra is 100% under 365 and 200% under 313 or 261. The rest day does not otherwise
  change the 200%.
- Paid whatever else the employee has that day (an approved leave, say): same as an unworked
  single regular holiday today.
- Unchanged: an unworked double special non-working day is "no work, no pay" (0%, and an absence
  as now); an unworked single regular holiday; every worked-day rate.
- Not implemented, as for single holidays today: the law's condition that the employee was not
  absent without pay the day before the holiday.

## The attendance bridge

`PayrollAttendanceBridge` counts, per employee, the dates in the period where the classified day
type is `DoubleRegularHoliday` or `DoubleRegularHolidayOnRestDay`, the shift schedule resolves
(a working day or a rest day), and the date was not worked. A date is worked when, on a rest day,
approved overtime exists for it (a rest day's work comes only from approved overtime, so a present
record alone does not count), and when, on a working day, any record for it is marked present. With
no schedule for the date (before the assignment starts, say) nothing is paid, as no absence is
derived for it. The count goes on that day type's `PremiumDayInput.UnworkedDays`.

A double regular holiday the employee worked is unchanged (`Days` or, on a rest day, `Hours`).
A rest day worked through approved overtime is worked and stays as now. A rest day with a present
record but no approved overtime earns nothing as work, so it is counted as unworked: she is paid
the same 200% as if she had stayed home, never less for turning up.

## The engine

`PremiumDayInput` gains `UnworkedDays` (default 0). In `PayrollComputationService.Compute` the
premium loop adds `dailyRate x (DolePremiumRates.UnworkedBaseRate(dayType) - alreadyPaid) x
UnworkedDays` to `holidayPay`, with `UnworkedBaseRate` 2.00 for the two double regular types and
0 for every other type (so it adds nothing for them). Rounded with the rest of `holidayPay`.

`holidayPay` already feeds gross, taxable pay, the loan budget and the payslip's holiday
line, so no total or report needs changing. `HolidayDays` on the entry (a display roll-up of worked
days) is unchanged.

## Storage

`PayrollRunPremiumDay` gains `UnworkedDays numeric(18,2) not null default 0`, mapped in both
directions (`PayrollRunService` where it copies premium days to and from the entry, `Merge`).
Recompute reprices from the stored rows as it does the worked days. One migration, adding the
column. Existing rows are 0.

A premium-day row is kept when `UnworkedDays` is the only non-zero figure.

## Payslip and run page

No change: the holiday-pay amount grows. The payslip shows one holiday-pay line today.

## Out of scope

- Single regular holidays, special days, or the "day before" rule.
- Showing the count of unworked double holidays anywhere.

## Testing

- Bridge: an unworked double regular holiday on a working day; the same on a rest day; worked
  (unchanged, no unworked day); an unworked double special day (nothing, still an absence);
  an unworked single regular holiday (nothing); no schedule that date (nothing); a present record
  among several for the date (worked); paid leave that date (still counted).
- Engine, with hand-derived figures in comments: one unworked double holiday under the 365 factor
  (adds 100% of the daily rate); on a rest day under 365 (100%) and under 313 (200%); two such
  days; mixed with a worked double holiday; gross and the withholding base include it, the 13th month (worked from regular pay, as for a worked holiday) does not; a request
  with no unworked days is unchanged.
- Storage: the column round-trips on Postgres, and a recompute of an unpaid run reprices the
  unworked days from the stored rows.
- The existing premium-day tests pass unchanged.
