# PeopleCore go-live checklist

Written 2026-10-02. Goal: a clear finish line for the first real payroll and leave cycle.
Feature work stops here; anything not under "Must work on day one" waits until a real
employee needs it.

Production is `peoplecore.m2netsolutions.com`. Deploys from `main` lag up to about 20 minutes and
the PWA can serve a cached build, so hard-refresh (or clear site data) before checking.

## 1. Set-up (once, before anyone logs in)

- [ ] Company details (`/admin/company`) and email settings (`/admin/email`): send a test email.
- [ ] Roles and users (`/admin/roles`, `/admin/users`): HR, payroll, approvers and employees each get the right role; log in as one of each and confirm the menu matches.
- [ ] Departments and positions (`/departments`, `/positions`).
- [ ] Employees (`/employees`): every employee has hire date, TIN, SSS, PhilHealth and Pag-IBIG numbers, and a compensation record (`/employees/{id}/compensation`) with basic salary and pay frequency.
- [ ] Shifts: every employee has a shift assignment that starts on or before their hire date (the rules for absences, holidays and the day-before rule all read it).
- [ ] Holiday calendar for the year, with each regular holiday and special day entered. Two regular holidays on one date is how a double holiday is recorded.
- [ ] Payroll settings (`/payroll-settings`): the daily-rate factor (365, 313 or 261) matches your company policy; this decides how rest days and holidays are paid.
- [ ] Leave types (`/leave-types`): the statutory leave (service incentive leave, maternity, paternity, solo parent and so on) is set up, and the types that should convert at year end are marked.
- [ ] Opening balances (`/opening-balances`): for every employee paid before go-live this year, enter or import year-to-date pay, 13th month, contributions and tax withheld. Skip only if go-live is 1 January.

## 2. Must work on day one

Run each as a real user, not as admin, and note any wrong figure or error.

**Leave**
- [ ] An employee files a leave request (`/my-leave`) and sees the correct balance.
- [ ] The approver approves it (`/leave-approvals`); the balance drops and the day is not treated as absent in payroll.
- [ ] A rejected or cancelled request gives the balance back.
- [ ] An unpaid leave day is deducted in payroll; a paid one is not.

**Attendance**
- [ ] Records arrive (`/attendance-import` or the device) and show in `/attendance-records` and `/my-attendance`.
- [ ] A correction (`/attendance-corrections`) and an overtime request (`/overtime-approvals`) go through approval.

**Payroll**
- [ ] Create a payroll run for a real period (`/payroll-runs`) and open it: every employee is on it, with no unexpected warnings.
- [ ] Check three employees by hand against a calculator: basic pay, absences and lateness, overtime, SSS, PhilHealth, Pag-IBIG, withholding tax, net pay.
- [ ] A run containing a regular holiday and one containing a rest-day or overtime day pay what you expect.
- [ ] Approve and mark the run paid; an employee sees their payslip (`/my-payslips`) and it matches the run page.
- [ ] A recompute of an unpaid run gives the same figures.

**Government reports**
- [ ] The remittance reports (`/government-reports`) open and their totals match the paid runs.

## 3. Sign-off by an accountant or HR specialist

These are legal interpretations; automated tests cannot settle them. Give them the figures from
section 2.

- [ ] Withholding tax and the 13th month: the ₱90,000 exemption is shared with other benefits; a 13th month above it is taxed and the payslip shows the taxable part.
- [ ] Maternity pay: the salary differential is treated as exempt (RMC 105-2019) - confirm with the BIR rulings you rely on.
- [ ] The day-before rule for regular holiday pay (Labor Code Art. 94): it is applied to all employees, monthly-paid included - confirm that is your policy.
- [ ] Final pay and the 2316 for one resigned employee (`/separations`, `/bir-2316`).

## 4. Can wait until a real case needs it

Do not build or test these before go-live.

- Recruitment (`/applicants`, `/job-postings`), performance (`/performance`) and analytics (`/analytics`): use them if they work; none blocks payroll.
- Unworked single regular holiday on a rest day under a 313 or 261 factor is not paid (rare).
- Worked hours on a rest-day double holiday for rows stored before 2 October 2026: an unpaid run that is recomputed can underpay them.
- The payroll export does not show unworked double holidays.
- Look-back loading in the attendance bridge reads company-wide records; fine for now, slower on large payrolls.
- The day-before rule does not stop at a hire date: a backdated shift assignment can read days before hire as absences.

## 5. Day-one watch list

- [ ] Take a database backup (Neon) before the first real payroll is marked paid.
- [ ] After the first paid run, compare payslips to what payroll would have computed by hand for a sample; fix data, not code, first.
- [ ] Keep a list of every wrong figure found; each one is a bug to fix, and this list ends feature work only when it is empty.
