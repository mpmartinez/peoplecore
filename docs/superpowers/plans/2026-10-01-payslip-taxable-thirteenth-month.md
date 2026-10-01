# Payslip Taxable 13th Month Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The payslip and the payroll page show the taxable part of the 13th month separately from the exempt part, and two small opening-balances follow-ups are fixed.

**Architecture:** The engine, which already splits the 13th month when it works out the tax, stores the taxable part on each entry (`ThirteenthMonthTaxable`, null on entries computed before). The payslip line builder and the run page read it. No tax figure, total or report changes.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-10-01-payslip-taxable-thirteenth-month-design.md`. It holds the exact rules, wording and cases, and binds every task.

## Global Constraints

- `PayrollRunEmployee.ThirteenthMonthTaxable` is `decimal?`, `numeric(18,2)`. The engine sets it to `max(0, ThirteenthMonth − max(0, 90,000 − exemptUsedEarlierInYear))`, the 13th month filling the exemption first. It changes no tax, gross or other figure.
- Payslip lines: null or 0 keeps the single "13th Month Pay" non-taxable line; above 0 gives "13th Month Pay (non-taxable)" (only when its amount is above 0) and "13th Month Pay (taxable portion)" flagged taxable. Earnings still sum to `GrossPay`.
- The 2316, 1601-C and 1604-C are unchanged.
- Build/test: `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Migrations need `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

---

### Task 1: The engine stores the taxable part

**Files:** `src/PeopleCore.Domain/Entities/Payroll/PayrollRunEmployee.cs` (the column and a computed `ThirteenthMonthExempt`), its EF configuration, `PayrollComputationService.cs` (set it where `exemptUsedEarlierInYear` is known, around the `ComputeThirteenthMonthTax` call), `PayrollRunDtos.cs` and the DTO mapping (a trailing `decimal? ThirteenthMonthTaxable`), migration `AddThirteenthMonthTaxable`.

- [ ] Failing tests with hand-derived figures in comments: a 13th month wholly within the exemption (0); partly over; wholly over; with exemption already used earlier (earlier runs and an opening balance); with leave conversion in the same pool (the 13th month fills the exemption first); a final pay; and every tax and total unchanged against the existing tests. A Postgres test that the column round-trips and a recompute stores it.
- [ ] Run and confirm they fail; implement; run the whole solution. The migration's `Up` adds only the one nullable column.
- [ ] Commit `feat(payroll): store the taxable part of the 13th month on each payroll line`.

---

### Task 2: The payslip and the payroll page

**Files:** `src/PeopleCore.Application/Payroll/Services/PayslipLineBuilder.cs`, `src/PeopleCore.Web/Services/ApiClient.cs` (the mirror DTO, in order), `src/PeopleCore.Web/Pages/Payroll/PayrollRunDetail.razor`.

- [ ] Failing tests: the line builder for null, 0, partial and full cases with earnings summing to `GrossPay` and gross less deductions equal to net; the payslip document renders the split; the run page shows "of which taxable ₱{n}" beneath the 13th month amount only when above 0; the web mirror matches the API field by field.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `feat(payroll): the payslip and payroll page show the taxable part of the 13th month`.

---

### Task 3: The two opening-balances follow-ups

**Files:** `PayrollRunService.cs` (`EnsureEarlierInYearUnchangedAsync`), `Bir2316Service.cs` (`GetAvailableYearsAsync`) and the 2316 page's default year choice.

- [ ] Failing tests: Mark Paid names another Paid run of hers (a 13th month or not) paid after the entry was computed ("{name}'s pay was changed by {RunNumber}, paid after this payroll was computed; recompute it before paying."), and otherwise keeps the opening-balance wording; the 2316 page's default year is the latest available year not after the current Philippine year, with later years still listed.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `fix(payroll): Mark Paid names the run that changed the pay before PeopleCore, and the 2316 page doesn't default to a future year`.

---

### Task 4: Verification

- [ ] The whole solution passes with no compiler warnings.
- [ ] `AddThirteenthMonthTaxable`'s `Up` adds only the one column.
- [ ] Browser check needs a signed-in account; record it as not done if no one can sign in.
