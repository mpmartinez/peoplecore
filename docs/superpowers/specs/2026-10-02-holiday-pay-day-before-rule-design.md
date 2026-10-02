# The day-before rule for regular holiday pay - design

Date: 2026-10-02. Status: approved in brainstorming; awaiting spec review.

## Why

Labor Code Article 94 and the Omnibus Rules (Rule IV, Section 6): an employee who is absent without
pay on the workday immediately before a regular holiday is not paid for the holiday. When that day
is the employee's rest day or a non-working day, the test moves back to the last workday before it.
`PayrollAttendanceBridge` pays every unworked regular holiday in full whatever happened before it.

## The rule

- Applies to a regular holiday (single or double) that the employee did not work, on a date where
  the shift schedule resolves. "Worked" is as the unworked-pay features define it: on a working day
  any present record; on a rest day approved overtime. A rest-day holiday is not a holiday pay
  case here: nothing is deducted for it (unchanged).
- The qualifying day is found by walking back from the holiday one day at a time, skipping days that
  are the employee's rest days (the schedule resolves to a rest day) or that fall on a holiday
  (any type on the calendar except special working days), to the first scheduled working day.
  - A holiday date in the walk that the employee worked (a present record) stops the walk as
    satisfied (Holy Thursday worked, so Good Friday is paid).
  - The first scheduled working day is satisfied when the employee has a present record that day,
    or an approved paid leave covering it. Approved unpaid leave, an unpaid or unrecorded day is
    absent without pay.
  - A date with no schedule (the assignment does not cover it) is skipped like a rest day. The walk
    stops after 14 days; if no scheduled working day is found, the employee stays entitled.
- Not entitled: the holiday books one absence (`AbsenceDays` +1, deducted at the daily rate as any
  absence is) and a double regular holiday counts no guaranteed day (`UnworkedDays`), so it pays
  nothing for the day. Entitled: unchanged (no absence; a double holiday's guarantee as now).
- A holiday the employee worked is paid as now, whatever the day before. Special non-working days,
  special working days and rest days are untouched.
- Applies to every employee: PeopleCore pays all employees by monthly salary and daily-rate factor
  and has no daily-paid type. DOLE guidance on monthly-paid employees is less clear-cut, so the
  accountant should confirm it; noted in the release notes. No company setting.

## The bridge

`PayrollAttendanceBridge.BuildAsync` loads attendance records, approved leave and shift assignments
from 14 days before the period start, and holidays for each year that range touches. The extra days
are used only to evaluate the rule: records before `from` add nothing to late minutes, undertime,
night hours, overtime, absences or premium days, and leave before `from` only feeds the check.
Nothing in the engine, storage, DTOs or migrations changes; the absence flows through
`AbsenceDays` as today.

## Out of scope

- A single regular holiday on a rest day under a factor that does not pay rest days.
- Special non-working days and any other premium.
- Showing why a holiday was deducted anywhere beyond the absence count.

## Testing

Bridge tests, with each branch genuinely exercised: absent the day before (deducted); present;
approved paid leave; approved unpaid leave (deducted); a holiday on a Monday with Friday the
qualifying day (weekend skipped by rest days); consecutive holidays (Holy Thursday and Good Friday
with the Wednesday present, absent, and the Thursday worked); the first day of a period with the
qualifying day in the previous period; nothing within 14 days (entitled); a worked holiday after an
absent day (no deduction, paid as now); a double regular holiday not entitled (no guaranteed day,
one absence); a special non-working day unchanged; a period starting 1 January (the walk reaches
the previous year's holidays); records before the period do not change late or undertime minutes.
One end-to-end run test: the deduction on a payroll entry.
