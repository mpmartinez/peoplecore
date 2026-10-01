# Rest-day double holiday: hours on top of the guaranteed 200% - design

Date: 2026-10-02. Status: approved in brainstorming; awaiting spec review.
Follows `2026-10-01-unworked-double-holiday-pay-design.md`.

## Why

A double regular holiday is paid 200% whether or not it is worked, and work adds a premium: on a
rest day 200% guaranteed plus 190% for the hours worked, 390% for a full eight hours. The first
feature counted a rest-day double holiday with approved overtime as worked and priced its hours
at `BaseRate - alreadyPaid`, so one hour of overtime paid about 136% of a day under the 365 factor
(about 49% under 313 or 261) against the 200% an unworked day gets. Working paid less than staying
home until about 2.8 hours (365) or 4.1 hours (313/261).

## The rule

- A rest-day double regular holiday (`DoubleRegularHolidayOnRestDay`) with a resolved schedule
  always counts one guaranteed day (`UnworkedDays` = 1), whether or not overtime is approved or a
  record is present. A working-day double holiday is unchanged: it counts when no record for the
  date is marked present.
- In the engine, hours of a day type that carries an unworked rate (the two double regular types)
  are priced `hourlyRate x (BaseRate - UnworkedRate) x Hours`, the work premium on top of the
  guarantee: 1.90 on the rest-day type. The guaranteed day is still `dailyRate x (UnworkedRate -
  alreadyPaid)`. Hours of every other day type keep `BaseRate - alreadyPaid`.
- A full eight-hour rest-day double holiday pays `dailyRate x (3.90 - alreadyPaid)`, as before.
  Shorter work pays at least the guarantee. Overtime past eight hours, night differential,
  working-day double holidays, single holidays and special days are unchanged.

## Accepted gap

A premium-day row stored before this change, with hours on a rest-day double holiday and no
guaranteed day, is underpaid if its unpaid run is recomputed (recompute reprices from stored rows,
and the hours are now priced as the premium only). The unworked-pay feature merged the day before;
the case needs a double holiday, a rest day, approved overtime and a not-yet-paid run. Not
backfilled; noted in the release notes.

## Testing

- Bridge: a rest-day double holiday with approved overtime counts one guaranteed day and keeps the
  hours; with a present record but no overtime counts one; with nothing counts one; a working-day
  double holiday with a present record counts none (unchanged); the earlier rest-day tests change
  only where this rule requires.
- Engine, hand-derived: one, four and eight hours on the rest-day type under 365 and under 313;
  eight hours equals the old total; the guarantee alone equals the old unworked pay; hours on other
  day types unchanged; working-day double holiday unchanged.
- The end-to-end run test still pays 1,200.00 for an unworked working-day double holiday.
