# Payroll opening balances - design

Date: 2026-09-30. Status: approved in brainstorming; awaiting spec review.

## Why

Everything PeopleCore adds up "earlier this year" comes from its own Paid payroll runs. For the
year a company goes live, the months paid before go-live are missing. That makes these wrong for
anyone employed before go-live:

- the 13th month (one twelfth of the basic salary earned in the year, less what was already paid);
- the ₱90,000 exemption already used by the 13th month and other benefits;
- the 2316 and the 1604-C built from it;
- the year's tax settle on a final pay;
- the 10-day de minimis cap on converted leave.

## Decision

Year-to-date opening balances: HR records, per employee and year, what was paid before
PeopleCore. Every "earlier this year" figure adds them in.

## The record

`PayrollOpeningBalance` (table `payroll_opening_balances`), unique on (`EmployeeId`, `Year`):

| Field | Meaning |
|---|---|
| `EmployeeId`, `Year` | whose, and which calendar year |
| `ThroughDate` (DateOnly) | the last pay date the figures include; must fall in `Year` |
| `BasicSalary` | basic salary earned, after unpaid absences and tardiness, before contributions (a run's `RegularPay` is also after them, and the two are added together) |
| `ThirteenthMonthPaid` | 13th month already paid |
| `OtherBenefitsPaid` | other benefits (bonuses and the like) paid, which count toward the ₱90,000 |
| `OtherTaxablePay` | overtime, holiday, night differential and taxable allowances, as one total |
| `DeMinimis` | de minimis benefits paid |
| `OtherNonTaxable` | other non-taxable compensation |
| `EmployeeContributions` | the employee's SSS, PhilHealth and Pag-IBIG shares, as one total |
| `TaxWithheld` | withholding tax deducted |
| `DeMinimisLeaveDays` | leave days already converted as de minimis |

All money is `numeric(18,2)`, default 0, never negative. Days are `numeric(6,2)`, 0 to 10. Audit
fields record who changed it and when.

## What adds them in

Wherever PeopleCore sums the year's Paid runs for an employee, it also adds her opening balance
for that year:

| Figure | Adds |
|---|---|
| 13th month due (regular runs, final pay) | `BasicSalary` to the basic earned; `ThirteenthMonthPaid` to the 13th month already paid |
| ₱90,000 exemption already used | `ThirteenthMonthPaid + OtherBenefitsPaid` |
| 2316 | Item 39 (taxable basic) += `BasicSalary − EmployeeContributions`; the "Others" taxable box += `OtherTaxablePay`; Item 34 (13th month and other benefits, non-taxable part) and Item 48 (taxable part) take `ThirteenthMonthPaid + OtherBenefitsPaid` split at the ₱90,000 exemption together with the year's runs; Item 35 += `DeMinimis`; Item 36 += `EmployeeContributions`; Item 37 += `OtherNonTaxable`; Item 25A += `TaxWithheld` |
| 1604-C | follows the 2316 |
| Final pay's tax settle | follows the 2316 |
| De minimis leave-days cap | `DeMinimisLeaveDays` counts as already used |

The year is the one each figure already uses (the pay-date year for tax and the 2316; the year the
13th month is computed for). The 1601-C's monthly columns are unchanged; the 13th month it treats as already exempt earlier in the year adds the balance's `ThirteenthMonthPaid + OtherBenefitsPaid`.

## Double-count warning

When an employee has a Paid PeopleCore run in the balance's `Year` with a `PayDate` on or before
`ThroughDate`, show: "{name}'s opening balance already covers pay through {ThroughDate:MMM d,
yyyy}; {RunNumber} was paid on {PayDate:MMM d, yyyy}." A regular run not paid yet (Draft,
Processing, For approval or Approved) with such a `PayDate` warns ahead of paying it: "{name}'s
opening balance already covers pay through {ThroughDate:MMM d, yyyy}; {RunNumber} pays on
{PayDate:MMM d, yyyy}." An unpaid final pay doesn't. It appears on the opening-balance page (the
list and a save's answer), in the import's warnings and on that run's page. It is a warning, not a
block.

## Editing

- Create, edit and delete at any time (`payroll.manage`).
- On save, when a Paid run of that year for her has already relied on the figures (any Paid run of
  hers in `Year` with a `PayDate` after `ThroughDate`), the page warns: "{RunNumber} used these figures; its 13th month and tax won't change. Reissue the
  2316 to pick up the change." Paid runs are never recomputed.
- A regular run not paid yet that includes her 13th month stores, on her entry, the 13th month
  already paid, the basic earned and the ₱90,000 exemption used earlier in the year that it was
  computed with. Mark Paid compares them with the figures now (her Paid runs plus her balance), so
  a balance created, edited or deleted after the run was computed refuses the payment. A change to
  the 13th month already paid gives the 13th month's own message; a change to the basic earned or
  the exemption used gives "{name}'s pay before PeopleCore has changed since this payroll was
  computed; recompute it before paying." An Approved run in that state can be recomputed (back to
  Draft). Entries computed before these figures were stored aren't checked.
- Validation: "Enter a year." / "The through date must fall in {Year}." / "Amounts can't be
  negative." / "Contributions can't be more than the basic salary." / "De minimis leave days must
  be between 0 and 10."

## CSV import

- `GET api/payroll-opening-balances/template` returns a CSV with the header row:
  `EmployeeNumber,Year,ThroughDate,BasicSalary,ThirteenthMonthPaid,OtherBenefitsPaid,OtherTaxablePay,DeMinimis,OtherNonTaxable,EmployeeContributions,TaxWithheld,DeMinimisLeaveDays`.
- `POST api/payroll-opening-balances/import` (multipart `file`) validates every row first. Any
  error refuses the whole file and returns every problem as "Row {n}: {message}", with messages
  such as "Unknown employee number {x}." plus the validation messages above. Otherwise it creates
  or updates each (employee, year) and returns the counts.
- Dates are `yyyy-MM-dd`; numbers use a dot and no thousands separator.
- The template and any echoed values go through the existing CSV formula-injection neutraliser.

## Pages

- **Opening balances** (`/opening-balances`, `payroll.manage`, nav under Payroll): a year picker
  and a list (employee, through date, basic, 13th month paid, tax withheld, warnings); a form to add
  or edit one employee's balance; the template download and the import with its row errors.
- **2316 page:** when the employee has an opening balance for the year, a line "Includes pay before
  PeopleCore through {date}."

## Storage

One migration: `payroll_opening_balances`, unique index on (`employee_id`, `year`), FK to
`employees` (Restrict).

## Out of scope

- Opening balances for anything but the go-live year (they work for any year, but nothing prompts
  for them).
- SSS contribution history for the maternity allowance suggestion (HR confirms that figure).
- Loan and leave balances (they already have their own set-up).

## Testing

- 13th month: basic and 13th month paid from the balance plus the year's runs.
- The ₱90,000 exemption already used.
- The 2316 item by item, reconciling Items 19, 21, 23 and 25A; the 1604-C follows.
- Final pay's tax settle and 13th month.
- The de minimis leave-days cap.
- The double-count warning.
- CSV import: all-or-nothing, per-row errors, update vs create, formula-injection neutralising.
- Validation messages; permissions pinned in `PermissionEquivalenceTests`.
- bUnit: list, form, import results, the 2316 note.
