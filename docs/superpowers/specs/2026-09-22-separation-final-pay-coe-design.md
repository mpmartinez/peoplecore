# Separation, final pay and Certificate of Employment - design

## Problem

When someone leaves a Philippine employer, three things are owed.

- **Clearance.** The company checks the employee has returned what they held and settled what they owe.
- **Final pay** within 30 days of separation (DOLE Labor Advisory 06-2020). It covers unpaid salary, the pro-rated 13th month, cash for unused convertible leave, any separation or retirement pay the law requires, and deductions for loans and accountabilities. The tax is settled as the employee's year-end adjustment.
- **A Certificate of Employment** within 3 days of the request (the same advisory).

Today PeopleCore only "deactivates" an employee and stamps a separation date. Everything else happens outside it. The final pay then never reaches the 2316, the 1601-C or the alphalist, unless someone enters it again by hand.

## Goal

- HR records each separation, tracks its clearance, and can see when final pay is due and whether it's overdue.
- Final pay is a **payroll run**, computed by the same engine as every payslip. So its tax, 13th month and contributions follow the same rules, and it flows into the 2316, the 1601-C and the 1604-C alphalist unchanged.
- HR can print a Certificate of Employment for any current or former employee.

## Out of scope

- Employees filing resignations in self-service, and manager approval of them.
- Exit interviews, quitclaims and releases.
- Tracking COE requests and the 3-day clock.
- Position history. The COE shows the current (or last) position.
- A company-wide clearance template editor. The default items are fixed; HR can add items per separation.

## The separation record

A `Separation` belongs to one employee, and each employee has at most one open separation.

**Fields:**
- **Type:**
  - Resignation;
  - Termination for just cause (Labor Code Art. 297);
  - Authorized cause, with a sub-type: redundancy, retrenchment, closure not due to serious losses, closure due to serious losses, installation of labor-saving devices, or disease (Art. 298-299);
  - End of contract;
  - Retirement;
  - Death.
- **Notice date:** when the resignation or notice was given.
- **Last working day.**
- **Reason:** a free-text note.
- **Status:** Notice given, then Separated.
- **Recorded by and when; separated by and when.**

**Behavior:**
- **Record separation** creates the record in "Notice given". The employee stays active.
- **Mark separated** is allowed on or after the last working day. It moves the record to "Separated", sets `Employee.SeparationDate` to the last working day, and deactivates the employee (`IsActive = false`). There is no background job; HR marks it.
- The existing **Deactivate** endpoint keeps working, but records a separation of the type the caller gives (default Resignation) and marks it separated in one step. That way every departure has a record.
- **Final pay due by** is shown on the record: the last working day plus 30 days. It's flagged "Overdue" when that date has passed and no final-pay run is Paid.
- A separation in "Notice given" can be cancelled (a withdrawn resignation). A Separated one can't be; rehiring is out of scope.

## Clearance

Each separation has clearance items. Five are created with it: **HR, IT, Finance, Immediate supervisor, Property / admin**. HR can add more by name, and delete any item not yet cleared.

An item is **cleared** with who cleared it, when, and an optional note ("laptop returned"). Clearing can be undone, with the undo recorded, until the final pay is Paid.

Clearance is complete when every item is cleared. A final-pay run can be computed and approved before that, but **cannot be marked Paid until clearance is complete**. Since clearance can turn up deductions (an unreturned laptop), an approved final pay can still be changed or recomputed; doing so sends it back to Draft, to be approved again. Only a Paid one is fixed. The message says which items are outstanding.

## Final pay as a payroll run

`PayrollRun` gains a **run type**: Regular (every run today) or Final pay. A final-pay run:
- has exactly one employee, who must have a separation record in either status;
- by default runs from the day after the employee's last Paid regular run's period end (never paid: the first of the last working day's month, or the hire date when later) to the last working day. HR can change the start, but not to after the last working day, nor into a Paid regular run ("Payroll {RunNumber} already paid up to {date}; start final pay after that.");
- when regular payroll has already paid past the last working day (that default start is after it), carries no salary: its period is the last working day alone, with no salary days and no attendance, and it pays only the 13th month, leave conversion, separation or retirement pay, loans, HR deductions and the tax settle;
- can't be created, nor its period changed, while a regular run that includes the employee and isn't Paid ends on or after the earlier of the final period's start and the first of the last working day's month - it would overlap the final period, pay salary after the separation, or take the month's contributions again - or starts on or before the last working day, since the final pay's 13th month and tax settle take it as the employee's last pay ("Payroll {RunNumber} covers {period} and isn't paid yet; pay it before creating final pay."; for a run starting after the last working day, "take them off it before creating final pay");
- once started, in any status, takes the rest of the employee's pay: regular runs refuse them whatever the dates ("{name}'s final pay has been started; the rest of their pay goes there. Take them off this payroll."), so no regular pay falls outside the final pay's 13th month, tax settle or contributions;
- pays on a date HR picks. The pay date decides the tax year, as for every run;
- has its own run number sequence prefix, `FP-<year>-<nnn>`;
- goes through Draft, Approve and Mark Paid like any run, with a payslip. It appears in the payroll run list, marked "Final pay".

### Earnings

1. **Salary to the last working day.** The period's regular pay through the normal computation and attendance bridge. The period is usually shorter than a cutoff; the engine already deducts absences and tardiness from the period's base pay, and the base pay of a short final period is the daily rate times the working days in the period (see "Base pay of a short period").
2. **13th month, pro-rated.** The run is computed with the 13th month included: one twelfth of the basic pay earned in the **last working day's year** (that year's Paid runs, selected by pay date as elsewhere, plus this run's regular pay), less any 13th month already paid that year. A final pay made after the year end still owes the year the employee worked; the tax settle stays on the pay date's year. The 90,000 exemption and its tax work as they do today.
3. **Leave conversion.**
   - `LeaveType` gains **Convertible to cash** (default off). Service Incentive Leave must be convertible by law; turning it on for vacation leave is company policy.
   - For each convertible type, the employee's remaining days in the current year (`LeaveBalance.RemainingDays`) are paid at the daily rate.
   - **Tax:** up to 10 days of converted *vacation-type* leave in the year is de minimis and non-taxable (RR 11-2018, as amended). The rest is taxable. This uses a per-leave-type setting, **Counts as vacation leave for de minimis** (default on for convertible types), so SIL and VL can be treated as the company's accountant advises.
   - The page shows the days and rate used.
4. **Separation pay** for an authorized cause (Labor Code Art. 298-299), from the sub-type:
   - one month's pay per year of service: redundancy and labor-saving devices;
   - half a month's pay per year of service: retrenchment, closure not due to serious losses, and disease;
   - none: closure due to serious losses.

   Where the article sets a minimum ("at least one month"), the minimum applies. Years of service run from hire date to the last working day, and **a fraction of at least six months counts as a whole year**. "Month's pay" is the current monthly basic salary. It's **non-taxable**: separation for a cause beyond the employee's control (NIRC Sec. 32(B)(6)(b)).
5. **Retirement pay** under RA 7641, when the type is Retirement, the employee is **60 to 65** on the last working day, and has **at least 5 years** of service:
   - 22.5 days' pay per year of service, with the same six-month rounding;
   - 22.5 days = 15 days' pay + 5 days of SIL + 1/12 of the 13th month (2.5 days);
   - a day's pay is the daily rate.

   It's **non-taxable** when those conditions hold. When they don't (for example an early retirement under a company plan), nothing is computed and HR enters an amount.
6. **Allowances**, each pro-rated over the final period's salary days like base pay: the monthly amount × 12 / daily-rate factor × salary days. They are taxable or non-taxable exactly as on a regular run; no salary days, no allowances.
7. **HR override.** HR can replace the computed separation or retirement amount, or add one where none is computed, with a required note. An overridden amount keeps the computed one's tax treatment only when the type is one of the non-taxable cases above; otherwise it is taxable. The note and both amounts are kept for audit.

### Deductions

- **SSS, PhilHealth and Pag-IBIG** top the separation month (the last working day's month) up to exactly one month's contributions on the monthly basic, employee and employer shares alike: the month's full contribution less what that month's Paid runs (those whose period ends in it, as the remittance reports count them) already deducted, never below zero. A month regular payroll already covered costs nothing more.
- **Loans:** every active loan's **remaining balance**, not the instalment, each capped so the total doesn't exceed what net pay can cover after statutory deductions. Any uncovered balance is shown as a warning on the run ("₱3,200 of the SSS loan can't be covered by final pay") and stays on the loan. Marking the run Paid retires what was deducted, as today.
- **Other deductions HR adds**, each with a label and amount (unreturned property, cash advance not in the loans module). They are taken after the loans, under the same cap.

### Tax: the separated employee's year-end adjustment

The final pay's withholding tax isn't the per-period calculation. It settles the whole year, as BIR requires for an employee separated before year end:

- the **annual tax due** on the year's taxable compensation;
- less the tax already withheld this year;
- less the previous employer's tax withheld, from the saved 2316 inputs.

The year's taxable compensation is:
- the taxable pay in the employee's Paid runs this pay year;
- plus this run's taxable pay;
- plus the previous employer's taxable compensation, from the saved 2316 inputs.

The result can be **negative, which is a refund** added to the final pay. A test pins that case. The PERA credit, if saved, is subtracted as on the 2316.

This makes the 2316 for the year agree with what was withheld: its tax due equals its total tax withheld once the final pay is in.

### Base pay of a short period

A final period is usually shorter than a full cutoff. Its base pay is the daily rate times the salary days in the period, and which days the salary pays follows the daily-rate factor the rate is derived with. On the 365 factor rest days are paid, so the salary days are every calendar day from the period start to the last working day, inclusive. On 313 and 261 they are the days the employee's shift schedules (Monday to Friday where no shift is assigned). Absences from the attendance bridge still come off once, per scheduled day absent. The engine's usual proration (half a month per semi-monthly cutoff) applies only to Regular runs. This rule is only for Final pay runs.

## Certificate of Employment

A PDF HR generates from an employee's page, for a **current or former** employee:

- **Letterhead:** company name, address and logo, from the Company page.
- **Body:** "This is to certify that **<full name>** has been employed by **<company>** as **<position>** from **<hire date>** to **<last working day, or 'present'>**."
- **Purpose line:** "This certification is issued upon the request of the employee for whatever legal purpose it may serve." HR can change the purpose text before generating.
- **Date, and signatory:** name and title, defaulting to the HR user's name and "HR Manager". Both are editable before generating.

The position is the current one, or the last one for a former employee; PeopleCore keeps no position history. Salary isn't shown by default. An option adds "with a monthly basic salary of ₱X", since some requests need it.

## Where it lives

**API**, behind these permissions:
- `Permissions.EmployeesManage` (`employees.manage`) for separations, clearance and the COE;
- `Permissions.PayrollManage` (`payroll.manage`) for creating and paying the final-pay run.
- `POST/GET api/separations`, `GET api/separations/{id}`, `POST api/separations/{id}/mark-separated`, `POST api/separations/{id}/cancel`.
- `POST api/separations/{id}/clearance`, `POST .../clearance/{itemId}/clear`, `POST .../clearance/{itemId}/undo`, `DELETE .../clearance/{itemId}`.
- `POST api/separations/{id}/final-pay`: creates the Draft final-pay run. The existing payroll-run endpoints compute, approve and pay it. Mark Paid checks clearance.
- Final-pay inputs: HR's override of separation or retirement pay, extra deductions and the period start, carried on the run's request and saved with it.
- `POST api/employees/{id}/coe`: purpose, signatory and the salary option in the body; returns the PDF.

**Web:**
- **HR → Separations:** the list, with employee, type, last working day, status, clearance "3 of 5", final pay status and "due by" with an Overdue flag. It has a "Record separation" form.
- **Separation detail:** the record, the clearance checklist, and the final-pay section. That section offers "Create final pay", then links to the run with a summary of the earnings, deductions and tax adjustment.
- **Employee page:** a Separation panel when one exists, and a "Certificate of Employment" action.
- **Payroll run detail:** for a final-pay run, shows the leave conversion, separation/retirement pay, extra deductions, the loan shortfall warning and the tax adjustment.
- **Leave types:** the two new settings. Leave types are API-only today; add them to whatever edits leave types, and to the demo seed.

## Storage

- New tables:
  - `separations`;
  - `separation_clearance_items`;
  - `final_pay_adjustments`: overrides and extra deductions, with labels, amounts and notes, linked to the run entry.
- `PayrollRun.RunType`.
- `LeaveType.IsConvertibleToCash` and `LeaveType.CountsAsVacationForDeMinimis`.
- `PayrollRunEmployee` gains:
  - `LeaveConversionPay` and its non-taxable part;
  - `SeparationPay`;
  - `RetirementPay`;
  - `TaxAdjustment`.

`GrossPay` includes the new earnings. The 2316's items take them in: non-taxable separation or retirement pay and the de minimis part of leave conversion go to the non-taxable other-compensation items, and the taxable part of leave conversion goes to taxable other compensation. `TaxAdjustment` counts as tax withheld. A test pins that the 2316 balances for a separated employee.

## Testing

- **Separation pay:**
  - each authorized-cause sub-type;
  - service years with the six-month rounding, for example 4 years 6 months counts as 5 and 4 years 5 months as 4;
  - the one-month minimum;
  - no pay for closure due to serious losses.
- **Retirement pay:**
  - 22.5 days per year;
  - refused under 60, over 65, or under 5 years;
  - the HR override and its note.
- **Leave conversion:**
  - only convertible types;
  - remaining days times the daily rate;
  - the 10-day de minimis split.
- **Loans:**
  - full balances deducted;
  - capped at net pay, with the shortfall warning;
  - Mark Paid retires only what was deducted.
- **Tax adjustment:**
  - an amount due;
  - a refund;
  - with a previous employer;
  - the 2316 balancing afterwards.
- **Short final period:** its base pay.
- **Clearance:**
  - it blocks Mark Paid with the outstanding items named;
  - undo;
  - added items.
- **Separation record:**
  - Mark separated sets the date and deactivates the employee;
  - a withdrawn resignation can be cancelled;
  - the old Deactivate endpoint creates a record;
  - final pay due by, and the overdue flag.
- **COE:** for a current employee ("present") and a former one; the purpose, signatory and salary options.
- **Pages:** the Separations list and detail, and the final-pay run detail.
