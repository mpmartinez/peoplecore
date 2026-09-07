# Payroll Input Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reject impossible and absurd caller-supplied input before it reaches a payslip or a BIR Form 2316.

**Architecture:** Three static validators in `PeopleCore.Application/Payroll/Validation/`, each called on the first line of the service method it guards. Each accumulates every problem in a request and throws one `DomainException` naming all of them; `ExceptionHandlingMiddleware` already maps that to a 400 `application/problem+json`, and `ApiClient.ReadProblemDetailAsync` already surfaces its `Detail` to the user. Bounds are named constants in a new `PeopleCore.Domain.Payroll.PayrollInputLimits`.

**Tech Stack:** .NET 10, C# 13, xUnit, FluentAssertions, Moq. **No new package references.**

## Global Constraints

- **No new package reference.** Validation is hand-written C# using the existing `DomainException`.
- **`StatutoryCaps.cs`, `BirWithholdingTax.cs`, `DolePremiumRates.cs`, `SssContributionSchedule.cs` and `ContributionRates.cs` must not be modified.** These five Phase 1 files are byte-verified against their PayZen originals. New bounds go in a new file.
- **Validators must be `public`.** The test project has no `InternalsVisibleTo`, and the spec requires validators to be tested directly with no service, repository or mock.
- **`ExceptionHandlingMiddleware` must not be changed.** `DomainException` → 400 already works.
- **Every rule gets two tests:** one rejecting value and one accepting boundary value. Asserting only the rejection lets a bound drift inward unnoticed.
- Existing tests must stay green **without being edited**. The largest `BasicSalary` in any test is `500_000` and every `CreatePayrollRunRequest` fixture uses 2026-01-01 → 2026-01-15 semi-monthly with pay date 2026-01-20 (15 days, pay date after start), so nothing existing trips these rules. If a test does fail, it is a fixture with unrealistic values — fix the fixture, never loosen a rule.

---

### Task 1: Bounds, the failure accumulator, and compensation validation

**Files:**
- Create: `src/PeopleCore.Domain/Payroll/PayrollInputLimits.cs`
- Create: `src/PeopleCore.Application/Payroll/Validation/ValidationFailures.cs`
- Create: `src/PeopleCore.Application/Payroll/Validation/CompensationValidator.cs`
- Modify: `src/PeopleCore.Application/Payroll/Services/EmployeeCompensationService.cs` (in `UpsertAsync`, currently line 22)
- Test: `tests/PeopleCore.Application.Tests/Payroll/CompensationValidatorTests.cs`

**Interfaces:**
- Consumes: `UpsertCompensationRequest(decimal BasicSalary, PayFrequency PayFrequency, string TaxCode, int Dependents)` from `PeopleCore.Application.Payroll.DTOs`; `DomainException(string)` from `PeopleCore.Domain.Exceptions`.
- Produces, and **Tasks 2 and 3 depend on these exact signatures**:
  - `PeopleCore.Domain.Payroll.PayrollInputLimits` — a static class of `const` fields.
  - `PeopleCore.Application.Payroll.Validation.ValidationFailures` — `void AddIf(bool isInvalid, string message)`, `void AddMoney(string fieldName, decimal value, decimal max)`, `void ThrowIfAny()`.
  - `PeopleCore.Application.Payroll.Validation.CompensationValidator.Validate(UpsertCompensationRequest request)` — returns `void`, throws `DomainException`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Payroll/CompensationValidatorTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class CompensationValidatorTests
{
    private static UpsertCompensationRequest Valid(
        decimal basicSalary = 30_000m,
        PayFrequency frequency = PayFrequency.SemiMonthly,
        string taxCode = "ME",
        int dependents = 0)
        => new(basicSalary, frequency, taxCode, dependents);

    private static Action Validating(UpsertCompensationRequest request)
        => () => CompensationValidator.Validate(request);

    [Fact]
    public void Validate_AcceptsAnOrdinaryRequest()
    {
        Validating(Valid()).Should().NotThrow();
    }

    // ── BasicSalary ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNegativeBasicSalary()
    {
        Validating(Valid(basicSalary: -0.01m)).Should()
            .Throw<DomainException>().WithMessage("*Basic salary*negative*");
    }

    [Fact]
    public void Validate_AcceptsZeroBasicSalary()
    {
        // An unpaid position is possible; only a negative salary is impossible.
        Validating(Valid(basicSalary: 0m)).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsBasicSalaryAboveTheCeiling()
    {
        Validating(Valid(basicSalary: 10_000_000.01m)).Should()
            .Throw<DomainException>().WithMessage("*Basic salary*exceed*");
    }

    [Fact]
    public void Validate_AcceptsBasicSalaryExactlyAtTheCeiling()
    {
        // Pins the bound from inside. Without this, the ceiling could drift down unnoticed.
        Validating(Valid(basicSalary: 10_000_000m)).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsBasicSalaryWithMoreThanTwoDecimalPlaces()
    {
        // The column is numeric(18,2); a third decimal is silently rounded on save today, so the
        // stored salary differs from the submitted one with no error anywhere.
        Validating(Valid(basicSalary: 30_000.123m)).Should()
            .Throw<DomainException>().WithMessage("*Basic salary*decimal places*");
    }

    [Fact]
    public void Validate_AcceptsBasicSalaryWithExactlyTwoDecimalPlaces()
    {
        Validating(Valid(basicSalary: 30_000.12m)).Should().NotThrow();
    }

    // ── PayFrequency ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAnUndefinedPayFrequency()
    {
        // JSON binds an out-of-range number to an enum without complaint, and
        // PayrollComputationService treats anything that is not SemiMonthly as monthly - so an
        // unrecognised value silently doubles a period's pay.
        Validating(Valid(frequency: (PayFrequency)99)).Should()
            .Throw<DomainException>().WithMessage("*Pay frequency*");
    }

    [Theory]
    [InlineData(PayFrequency.Monthly)]
    [InlineData(PayFrequency.SemiMonthly)]
    public void Validate_AcceptsEveryDefinedPayFrequency(PayFrequency frequency)
    {
        Validating(Valid(frequency: frequency)).Should().NotThrow();
    }

    // ── Dependents ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNegativeDependents()
    {
        Validating(Valid(dependents: -1)).Should()
            .Throw<DomainException>().WithMessage("*Dependents*negative*");
    }

    [Fact]
    public void Validate_RejectsDependentsAboveTheCeiling()
    {
        Validating(Valid(dependents: 21)).Should()
            .Throw<DomainException>().WithMessage("*Dependents*exceed*");
    }

    [Fact]
    public void Validate_AcceptsDependentsExactlyAtTheCeiling()
    {
        Validating(Valid(dependents: 20)).Should().NotThrow();
    }

    // ── TaxCode ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAnEmptyTaxCode()
    {
        Validating(Valid(taxCode: "   ")).Should()
            .Throw<DomainException>().WithMessage("*Tax code is required*");
    }

    [Fact]
    public void Validate_RejectsATaxCodeLongerThanTheColumn()
    {
        // EmployeeCompensationConfiguration sets HasMaxLength(8); failing here means failing as
        // validation rather than as a database error.
        Validating(Valid(taxCode: "123456789")).Should()
            .Throw<DomainException>().WithMessage("*Tax code*8 characters*");
    }

    [Fact]
    public void Validate_AcceptsATaxCodeExactlyAtTheColumnLength()
    {
        Validating(Valid(taxCode: "12345678")).Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsAnUnrecognisedTaxCodeString()
    {
        // Deliberately NOT checked against S/ME/S1..ME4. Nothing reads TaxCode, and TRAIN
        // (RA 10963, 2018) abolished the personal and additional exemptions those codes encoded,
        // so a whitelist would enforce an obsolete rule with no consequence - and would imply to
        // the next reader that the field still drives withholding.
        Validating(Valid(taxCode: "Z9")).Should().NotThrow();
    }

    // ── Accumulation ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReportsEveryProblemInOneException()
    {
        // The requirement most likely to be quietly lost to a later refactor that returns early.
        // Correcting a form one field per round trip is what makes validation resented.
        var request = new UpsertCompensationRequest(-1m, (PayFrequency)99, "", -1);

        var message = Validating(request).Should().Throw<DomainException>().Which.Message;

        message.Should().Contain("Basic salary");
        message.Should().Contain("Pay frequency");
        message.Should().Contain("Tax code");
        message.Should().Contain("Dependents");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test PeopleCore.slnx --filter "FullyQualifiedName~CompensationValidatorTests"
```

Expected: the build fails with `CS0246: The type or namespace name 'Validation' does not exist in the namespace 'PeopleCore.Application.Payroll'`. A build failure is the correct "red" here — the type under test does not exist yet.

- [ ] **Step 3: Create the bounds**

Create `src/PeopleCore.Domain/Payroll/PayrollInputLimits.cs`:

```csharp
namespace PeopleCore.Domain.Payroll;

/// <summary>
/// Sanity bounds for caller-supplied payroll input.
/// <para>
/// Deliberately a separate file from <see cref="StatutoryCaps"/> and the other Phase 1 statutory
/// tables. Those carry legislated figures and are byte-verified against their originals; these are
/// engineering limits chosen to catch a fat-fingered extra zero. Mixing them would misrepresent
/// both - a reader would not know which numbers Congress set and which we did.
/// </para>
/// <para>
/// A bound that is too tight fails loudly with a message naming the limit, and is changed here in
/// one place. That is the trade being made against the silent corruption of having no bound.
/// </para>
/// </summary>
public static class PayrollInputLimits
{
    /// <summary>
    /// Ceiling for <c>EmployeeCompensation.BasicSalary</c>, which is a MONTHLY figure regardless of
    /// pay frequency - <c>PayrollComputationService</c> derives both
    /// <c>dailyRate = BasicSalary * 12 / factor</c> and <c>basePeriodPay = BasicSalary / periodsPerMonth</c>
    /// from it. Far above any realistic Philippine payroll, and still low enough to catch an extra
    /// zero on any salary up to a million.
    /// </summary>
    public const decimal MaxMonthlyBasicSalary = 10_000_000m;

    /// <summary>
    /// Ceiling for the annual figures on Form 2316: twelve times
    /// <see cref="MaxMonthlyBasicSalary"/>, since these cover a whole tax year.
    /// </summary>
    public const decimal MaxAnnualAmount = 120_000_000m;

    public const int MaxDependents = 20;

    /// <summary>Matches <c>EmployeeCompensationConfiguration</c>'s <c>HasMaxLength(8)</c>.</summary>
    public const int MaxTaxCodeLength = 8;

    /// <summary>Matches <c>CompanyConfiguration</c>'s <c>HasMaxLength(200)</c> on <c>Company.Name</c>.</summary>
    public const int MaxEmployerNameLength = 200;

    public const int MaxEmployerAddressLength = 200;

    /// <summary>The days a month can physically contain.</summary>
    public const decimal MaxDaysInPeriod = 31m;

    /// <summary>31 x 24 - the hours a month physically contains.</summary>
    public const decimal MaxOvertimeHoursInPeriod = 744m;

    public const int MaxSemiMonthlyPeriodDays = 16;

    public const int MaxMonthlyPeriodDays = 31;
}
```

- [ ] **Step 4: Create the failure accumulator**

Create `src/PeopleCore.Application/Payroll/Validation/ValidationFailures.cs`:

```csharp
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Collects every problem found in one request so a caller learns all of them at once.
/// <para>
/// Returning at the first failure would make a user correct a form one field per round trip, which
/// is the experience that makes validation resented. <see cref="DomainException"/> carries only a
/// message, so the message holds the list; a field-keyed structure would need a new exception type
/// and a change to <c>ExceptionHandlingMiddleware</c>, which is more than this needs.
/// </para>
/// </summary>
public sealed class ValidationFailures
{
    private readonly List<string> _messages = [];

    public void AddIf(bool isInvalid, string message)
    {
        if (isInvalid)
            _messages.Add(message);
    }

    /// <summary>
    /// The three rules every caller-supplied money field shares: not negative, not absurd, and not
    /// carrying precision the database will silently drop. Payroll money columns are
    /// <c>numeric(18,2)</c>, so a third decimal place is rounded away on save - the stored figure
    /// then differs from the submitted one with nothing reporting it.
    /// </summary>
    public void AddMoney(string fieldName, decimal value, decimal max)
    {
        AddIf(value < 0m, $"{fieldName} cannot be negative.");
        AddIf(value > max, $"{fieldName} cannot exceed {max:N2}.");
        AddIf(decimal.Round(value, 2) != value, $"{fieldName} cannot have more than 2 decimal places.");
    }

    public void ThrowIfAny()
    {
        if (_messages.Count > 0)
            throw new DomainException(string.Join(" ", _messages));
    }
}
```

- [ ] **Step 5: Create the compensation validator**

Create `src/PeopleCore.Application/Payroll/Validation/CompensationValidator.cs`:

```csharp
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Guards <c>EmployeeCompensationService.UpsertAsync</c>. Its request is copied straight onto the
/// entity and saved, so anything not rejected here is persisted, computed into a payslip, and
/// aggregated into a BIR Form 2316 that an officer of the company signs.
/// </summary>
public static class CompensationValidator
{
    public static void Validate(UpsertCompensationRequest request)
    {
        var failures = new ValidationFailures();

        failures.AddMoney("Basic salary", request.BasicSalary, PayrollInputLimits.MaxMonthlyBasicSalary);

        failures.AddIf(!Enum.IsDefined(request.PayFrequency),
            "Pay frequency is not a recognised value.");

        failures.AddIf(request.Dependents < 0, "Dependents cannot be negative.");
        failures.AddIf(request.Dependents > PayrollInputLimits.MaxDependents,
            $"Dependents cannot exceed {PayrollInputLimits.MaxDependents}.");

        // Length only, never a status-code whitelist: nothing reads TaxCode, and TRAIN abolished
        // the exemptions those codes encoded. See CompensationValidatorTests for the full reasoning.
        failures.AddIf(string.IsNullOrWhiteSpace(request.TaxCode), "Tax code is required.");
        failures.AddIf(request.TaxCode?.Length > PayrollInputLimits.MaxTaxCodeLength,
            $"Tax code cannot exceed {PayrollInputLimits.MaxTaxCodeLength} characters.");

        failures.ThrowIfAny();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test PeopleCore.slnx --filter "FullyQualifiedName~CompensationValidatorTests"
```

Expected: `Passed! - Failed: 0, Passed: 18` (16 facts plus 2 theory rows).

- [ ] **Step 7: Wire the validator into the service**

In `src/PeopleCore.Application/Payroll/Services/EmployeeCompensationService.cs`, add the using and make `Validate` the first statement of `UpsertAsync`:

```csharp
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Entities.Payroll;
```

```csharp
    public async Task<EmployeeCompensationDto> UpsertAsync(
        Guid employeeId, UpsertCompensationRequest request, CancellationToken ct = default)
    {
        CompensationValidator.Validate(request);

        var compensation = await _repo.GetByEmployeeIdAsync(employeeId, ct);
```

- [ ] **Step 8: Run the full suite**

```bash
dotnet test PeopleCore.slnx
```

Expected: `Failed: 0`, total 292 (274 before, 18 added). No existing test constructs an `UpsertCompensationRequest`, so nothing else can be affected.

- [ ] **Step 9: Commit**

```bash
git add src/PeopleCore.Domain/Payroll/PayrollInputLimits.cs src/PeopleCore.Application/Payroll/Validation src/PeopleCore.Application/Payroll/Services/EmployeeCompensationService.cs tests/PeopleCore.Application.Tests/Payroll/CompensationValidatorTests.cs
git commit -m "feat(payroll): validate employee compensation input"
```

---

### Task 2: Payroll run request validation

**Files:**
- Create: `src/PeopleCore.Application/Payroll/Validation/PayrollRunRequestValidator.cs`
- Modify: `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs` (in `CreateAsync`, lines 43-45)
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollRunRequestValidatorTests.cs`

**Interfaces:**
- Consumes: `ValidationFailures` and `PayrollInputLimits` from Task 1, with the exact members listed there.
- Consumes: `CreatePayrollRunRequest(DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PayDate, PayFrequency Frequency, IReadOnlyList<PayrollRunEmployeeInput> Employees, Guid? AttendancePeriodId = null)` and `PayrollRunEmployeeInput(Guid EmployeeId, decimal? DaysWorked = null, decimal? OvertimeHours = null, decimal? HolidayDays = null, bool IncludeThirteenthMonth = false)`.
- Produces: `PayrollRunRequestValidator.Validate(CreatePayrollRunRequest request)` — returns `void`, throws `DomainException`.

**Note on the existing check.** `PayrollRunService.CreateAsync` already opens with an at-least-one-employee guard throwing `DomainException`. It **moves into the validator** — it is not duplicated. `PayrollRunServiceTests` line 466 asserts that behaviour through the service and must keep passing unchanged.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Payroll/PayrollRunRequestValidatorTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollRunRequestValidatorTests
{
    private static readonly Guid AnEmployee = Guid.NewGuid();

    private static CreatePayrollRunRequest Valid(
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null,
        DateOnly? payDate = null,
        PayFrequency frequency = PayFrequency.SemiMonthly,
        IReadOnlyList<PayrollRunEmployeeInput>? employees = null)
        => new(
            periodStart ?? new DateOnly(2026, 1, 1),
            periodEnd ?? new DateOnly(2026, 1, 15),
            payDate ?? new DateOnly(2026, 1, 20),
            frequency,
            employees ?? [new PayrollRunEmployeeInput(AnEmployee)]);

    private static Action Validating(CreatePayrollRunRequest request)
        => () => PayrollRunRequestValidator.Validate(request);

    [Fact]
    public void Validate_AcceptsAnOrdinaryRequest()
    {
        Validating(Valid()).Should().NotThrow();
    }

    // ── Employees ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsARunWithNoEmployees()
    {
        Validating(Valid(employees: [])).Should()
            .Throw<DomainException>().WithMessage("*at least one employee*");
    }

    [Fact]
    public void Validate_RejectsTheSameEmployeeTwice()
    {
        // The same employee twice in one run pays them twice.
        Validating(Valid(employees: [new PayrollRunEmployeeInput(AnEmployee), new PayrollRunEmployeeInput(AnEmployee)]))
            .Should().Throw<DomainException>().WithMessage("*only once*");
    }

    [Fact]
    public void Validate_AcceptsTwoDifferentEmployees()
    {
        Validating(Valid(employees: [new PayrollRunEmployeeInput(AnEmployee), new PayrollRunEmployeeInput(Guid.NewGuid())]))
            .Should().NotThrow();
    }

    // ── Dates ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAPeriodThatEndsBeforeItStarts()
    {
        Validating(Valid(periodStart: new DateOnly(2026, 1, 15), periodEnd: new DateOnly(2026, 1, 1)))
            .Should().Throw<DomainException>().WithMessage("*Period end*before period start*");
    }

    [Fact]
    public void Validate_AcceptsASingleDayPeriod()
    {
        Validating(Valid(periodStart: new DateOnly(2026, 1, 1), periodEnd: new DateOnly(2026, 1, 1)))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAPayDateBeforeThePeriodStarts()
    {
        Validating(Valid(payDate: new DateOnly(2025, 12, 31)))
            .Should().Throw<DomainException>().WithMessage("*Pay date*before period start*");
    }

    [Fact]
    public void Validate_AcceptsAPayDateInsideThePeriod()
    {
        // Paying before the period closes is legitimate - advances are real - so the bound stops
        // at PeriodStart rather than PeriodEnd.
        Validating(Valid(payDate: new DateOnly(2026, 1, 10))).Should().NotThrow();
    }

    // ── Period length against frequency ──────────────────────────────────────

    [Fact]
    public void Validate_RejectsASemiMonthlyPeriodLongerThanSixteenDays()
    {
        // A frequency contradicting its own date range is a data-entry error, and the 2316
        // attributes income by pay date - so a 300-day "semi-monthly" run corrupts a tax year.
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 17),
                frequency: PayFrequency.SemiMonthly))
            .Should().Throw<DomainException>().WithMessage("*SemiMonthly*16 days*");
    }

    [Fact]
    public void Validate_AcceptsASemiMonthlyPeriodOfExactlySixteenDays()
    {
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 16),
                frequency: PayFrequency.SemiMonthly))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAMonthlyPeriodLongerThanThirtyOneDays()
    {
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 2, 1),
                frequency: PayFrequency.Monthly))
            .Should().Throw<DomainException>().WithMessage("*Monthly*31 days*");
    }

    [Fact]
    public void Validate_AcceptsAMonthlyPeriodOfExactlyThirtyOneDays()
    {
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 31),
                frequency: PayFrequency.Monthly))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAnUndefinedFrequency()
    {
        Validating(Valid(frequency: (PayFrequency)99)).Should()
            .Throw<DomainException>().WithMessage("*Pay frequency*");
    }

    // ── Per-employee overrides ───────────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsOmittedOverrides()
    {
        // Null means "use what the attendance bridge derived". Validation must not disturb that -
        // treating a missing override as zero is the exact failure the bridge exists to prevent.
        Validating(Valid(employees: [new PayrollRunEmployeeInput(AnEmployee)])).Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.5, null, null, "Days worked")]
    [InlineData(32, null, null, "Days worked")]
    [InlineData(null, -1, null, "Overtime hours")]
    [InlineData(null, 745, null, "Overtime hours")]
    [InlineData(null, null, -1, "Holiday days")]
    [InlineData(null, null, 32, "Holiday days")]
    public void Validate_RejectsOutOfRangeOverrides(
        double? daysWorked, double? overtimeHours, double? holidayDays, string expected)
    {
        var input = new PayrollRunEmployeeInput(
            AnEmployee,
            DaysWorked: (decimal?)daysWorked,
            OvertimeHours: (decimal?)overtimeHours,
            HolidayDays: (decimal?)holidayDays);

        Validating(Valid(employees: [input])).Should()
            .Throw<DomainException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Validate_AcceptsOverridesExactlyAtTheirBounds()
    {
        var input = new PayrollRunEmployeeInput(
            AnEmployee, DaysWorked: 31m, OvertimeHours: 744m, HolidayDays: 31m);

        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 31),
                frequency: PayFrequency.Monthly,
                employees: [input]))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsZeroOverrides()
    {
        var input = new PayrollRunEmployeeInput(
            AnEmployee, DaysWorked: 0m, OvertimeHours: 0m, HolidayDays: 0m);

        Validating(Valid(employees: [input])).Should().NotThrow();
    }

    // ── Accumulation ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReportsEveryProblemInOneException()
    {
        var request = new CreatePayrollRunRequest(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 1, 1),
            new DateOnly(2025, 12, 1),
            (PayFrequency)99,
            []);

        var message = Validating(request).Should().Throw<DomainException>().Which.Message;

        message.Should().Contain("Period end");
        message.Should().Contain("Pay date");
        message.Should().Contain("Pay frequency");
        message.Should().Contain("at least one employee");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test PeopleCore.slnx --filter "FullyQualifiedName~PayrollRunRequestValidatorTests"
```

Expected: build failure, `CS0103: The name 'PayrollRunRequestValidator' does not exist`.

- [ ] **Step 3: Create the validator**

Create `src/PeopleCore.Application/Payroll/Validation/PayrollRunRequestValidator.cs`:

```csharp
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Guards <c>PayrollRunService.CreateAsync</c>. A run's dates decide which tax year its income is
/// attributed to on Form 2316, so an incoherent period is not merely untidy - it moves money
/// between tax years.
/// </summary>
public static class PayrollRunRequestValidator
{
    public static void Validate(CreatePayrollRunRequest request)
    {
        var failures = new ValidationFailures();

        var hasEmployees = request.Employees is { Count: > 0 };
        failures.AddIf(!hasEmployees, "A payroll run must include at least one employee.");

        var frequencyIsKnown = Enum.IsDefined(request.Frequency);
        failures.AddIf(!frequencyIsKnown, "Pay frequency is not a recognised value.");

        var periodIsOrdered = request.PeriodEnd >= request.PeriodStart;
        failures.AddIf(!periodIsOrdered, "Period end cannot be before period start.");

        failures.AddIf(request.PayDate < request.PeriodStart,
            "Pay date cannot be before period start.");

        // Only meaningful once the period is ordered and the frequency is known - measuring the
        // length of an inverted period would report a second, confusing failure for one mistake.
        if (periodIsOrdered && frequencyIsKnown)
        {
            var days = request.PeriodEnd.DayNumber - request.PeriodStart.DayNumber + 1;
            var maxDays = request.Frequency == PayFrequency.SemiMonthly
                ? PayrollInputLimits.MaxSemiMonthlyPeriodDays
                : PayrollInputLimits.MaxMonthlyPeriodDays;

            failures.AddIf(days > maxDays,
                $"A {request.Frequency} period cannot span more than {maxDays} days; this one spans {days}.");
        }

        if (hasEmployees)
        {
            var duplicates = request.Employees
                .GroupBy(e => e.EmployeeId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            failures.AddIf(duplicates.Count > 0,
                $"An employee can appear only once in a run; duplicated: {string.Join(", ", duplicates)}.");

            foreach (var employee in request.Employees)
            {
                // Each override is checked ONLY when supplied. Null means "use what the attendance
                // bridge derived from punches, approved leave, approved overtime, the holiday
                // calendar and the shift schedule" - see PayrollRunEmployeeInput's own remarks.
                AddBoundsCheck(failures, employee.DaysWorked, 0m, PayrollInputLimits.MaxDaysInPeriod,
                    $"Days worked for employee {employee.EmployeeId}");
                AddBoundsCheck(failures, employee.OvertimeHours, 0m, PayrollInputLimits.MaxOvertimeHoursInPeriod,
                    $"Overtime hours for employee {employee.EmployeeId}");
                AddBoundsCheck(failures, employee.HolidayDays, 0m, PayrollInputLimits.MaxDaysInPeriod,
                    $"Holiday days for employee {employee.EmployeeId}");
            }
        }

        failures.ThrowIfAny();
    }

    private static void AddBoundsCheck(
        ValidationFailures failures, decimal? value, decimal min, decimal max, string fieldName)
    {
        if (value is not decimal supplied)
            return;

        failures.AddIf(supplied < min || supplied > max,
            $"{fieldName} must be between {min:N0} and {max:N0}.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test PeopleCore.slnx --filter "FullyQualifiedName~PayrollRunRequestValidatorTests"
```

Expected: `Passed! - Failed: 0, Passed: 23` (17 facts plus 6 theory rows).

- [ ] **Step 5: Wire the validator into the service, replacing the inline guard**

In `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs`, add `using PeopleCore.Application.Payroll.Validation;` to the usings, then replace the existing guard at the top of `CreateAsync`:

```csharp
    public async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        if (request.Employees is not { Count: > 0 })
            throw new DomainException("A payroll run must include at least one employee.");

        var year = request.PeriodStart.Year;
```

with:

```csharp
    public async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        PayrollRunRequestValidator.Validate(request);

        var year = request.PeriodStart.Year;
```

If `DomainException` is now unused in this file, the `using PeopleCore.Domain.Exceptions;` may still be needed by other methods — check before removing it, and leave it if anything else in the file throws.

- [ ] **Step 6: Run the full suite**

```bash
dotnet test PeopleCore.slnx
```

Expected: `Failed: 0`, total 315 (292 after Task 1, plus 23). `PayrollRunServiceTests` line 466 (`CreateAsync` with an empty employee list throws `DomainException`) must still pass — the check moved, its behaviour did not. Every other `CreatePayrollRunRequest` fixture uses 2026-01-01 → 2026-01-15 semi-monthly with pay date 2026-01-20: 15 days, within the 16-day bound, pay date after period start.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Application/Payroll/Validation/PayrollRunRequestValidator.cs src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs tests/PeopleCore.Application.Tests/Payroll/PayrollRunRequestValidatorTests.cs
git commit -m "feat(payroll): validate payroll run creation input"
```

---

### Task 3: BIR 2316 manual input validation

**Files:**
- Create: `src/PeopleCore.Application/Payroll/Validation/Bir2316ManualInputsValidator.cs`
- Modify: `src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs` (in `BuildAsync`, currently line 63)
- Test: `tests/PeopleCore.Application.Tests/Payroll/Bir2316ManualInputsValidatorTests.cs`

**Interfaces:**
- Consumes: `ValidationFailures` and `PayrollInputLimits` from Task 1.
- Consumes: `Bir2316ManualInputs`, a record with init-only properties — `string? PrevEmployerTin`, `string? PrevEmployerName`, `string? PrevEmployerAddress`, `string? PrevEmployerZipCode`, `decimal Item22_PrevTaxableCompensation`, `decimal Item25B_PrevTaxWithheld`, `decimal Item27_PeraTaxCredit`, `decimal Item35_DeMinimis`, `decimal Item33_HazardPayMwe`, `decimal StatutoryMinWagePerDay`, `decimal StatutoryMinWagePerMonth`.
- Produces: `Bir2316ManualInputsValidator.Validate(Bir2316ManualInputs manual)` — returns `void`, throws `DomainException`.

**Critical constraint.** `Bir2316Service.GetPreviewAsync` calls `BuildAsync(employeeId, year, new Bir2316ManualInputs(), ct)` — a **default instance, all zeros and nulls**. Validation is added to `BuildAsync`, so that default instance MUST pass. If it does not, preview breaks for every employee. There is a test below that pins exactly this.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Payroll/Bir2316ManualInputsValidatorTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316ManualInputsValidatorTests
{
    private static Action Validating(Bir2316ManualInputs manual)
        => () => Bir2316ManualInputsValidator.Validate(manual);

    [Fact]
    public void Validate_AcceptsTheEmptyOverlayThatPreviewUses()
    {
        // Bir2316Service.GetPreviewAsync calls BuildAsync with new Bir2316ManualInputs() - all
        // zeros and nulls. If validation rejected that, preview would break for every employee.
        Validating(new Bir2316ManualInputs()).Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsAFullyPopulatedOverlay()
    {
        var manual = new Bir2316ManualInputs
        {
            PrevEmployerTin = "123-456-789",
            PrevEmployerName = "Previous Employer Inc.",
            PrevEmployerAddress = "123 Ayala Avenue, Makati City",
            PrevEmployerZipCode = "1200",
            Item22_PrevTaxableCompensation = 250_000m,
            Item25B_PrevTaxWithheld = 12_500m,
            Item27_PeraTaxCredit = 5_000m,
            Item33_HazardPayMwe = 10_000m,
            Item35_DeMinimis = 15_000m,
            StatutoryMinWagePerDay = 610m,
            StatutoryMinWagePerMonth = 18_500m,
        };

        Validating(manual).Should().NotThrow();
    }

    // ── Money bounds ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Item22")]
    [InlineData("Item25B")]
    [InlineData("Item27")]
    [InlineData("Item33")]
    [InlineData("Item35")]
    [InlineData("MinWageDay")]
    [InlineData("MinWageMonth")]
    public void Validate_RejectsNegativeMoneyOnEveryField(string field)
    {
        // Negative money on a tax certificate is indefensible on any of these boxes.
        Validating(WithField(field, -0.01m)).Should()
            .Throw<DomainException>().WithMessage("*negative*");
    }

    [Theory]
    [InlineData("Item22")]
    [InlineData("Item25B")]
    [InlineData("Item27")]
    [InlineData("Item33")]
    [InlineData("Item35")]
    [InlineData("MinWageDay")]
    [InlineData("MinWageMonth")]
    public void Validate_RejectsMoneyAboveTheAnnualCeiling(string field)
    {
        Validating(WithField(field, 120_000_000.01m)).Should()
            .Throw<DomainException>().WithMessage("*exceed*");
    }

    [Theory]
    [InlineData("Item22")]
    [InlineData("Item27")]
    [InlineData("Item33")]
    [InlineData("Item35")]
    [InlineData("MinWageDay")]
    [InlineData("MinWageMonth")]
    public void Validate_AcceptsMoneyExactlyAtTheAnnualCeiling(string field)
    {
        // Item25B is excluded: at the ceiling it would exceed Item22, which the cross-field rule
        // below rejects for its own reason. It is covered at the ceiling in that test instead.
        Validating(WithField(field, 120_000_000m)).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsMoneyWithMoreThanTwoDecimalPlaces()
    {
        Validating(WithField("Item35", 1_000.005m)).Should()
            .Throw<DomainException>().WithMessage("*decimal places*");
    }

    [Fact]
    public void Validate_AcceptsMoneyWithExactlyTwoDecimalPlaces()
    {
        Validating(WithField("Item35", 1_000.05m)).Should().NotThrow();
    }

    // ── Cross-field ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsTaxWithheldExceedingTheCompensationItCameFrom()
    {
        // The only rule here that catches a PLAUSIBLE mistake: two figures each individually
        // reasonable and jointly impossible. You cannot withhold more tax than the pay it was
        // withheld from.
        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = 100_000m,
            Item25B_PrevTaxWithheld = 100_000.01m,
        };

        Validating(manual).Should()
            .Throw<DomainException>().WithMessage("*Item 25B*cannot exceed*Item 22*");
    }

    [Fact]
    public void Validate_AcceptsTaxWithheldEqualToTheCompensation()
    {
        // Absurd as a tax rate, but not impossible, and the bound is what is being pinned.
        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = 120_000_000m,
            Item25B_PrevTaxWithheld = 120_000_000m,
        };

        Validating(manual).Should().NotThrow();
    }

    // ── TIN ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    [InlineData("123 456 789")]
    [InlineData("123456789012")]
    [InlineData("123-456-789-000")]
    public void Validate_AcceptsAWellFormedTin(string tin)
    {
        Validating(new Bir2316ManualInputs { PrevEmployerTin = tin }).Should().NotThrow();
    }

    [Theory]
    [InlineData("12345678")]      // 8 digits
    [InlineData("1234567890")]    // 10 digits
    [InlineData("12345678901234")] // 14 digits
    [InlineData("123-456-78X")]   // a letter
    public void Validate_RejectsAMalformedTin(string tin)
    {
        Validating(new Bir2316ManualInputs { PrevEmployerTin = tin }).Should()
            .Throw<DomainException>().WithMessage("*TIN*");
    }

    // ── ZIP ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsAFourDigitZipCode()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerZipCode = "1200" }).Should().NotThrow();
    }

    [Theory]
    [InlineData("120")]
    [InlineData("12000")]
    [InlineData("12A0")]
    public void Validate_RejectsAZipCodeThatIsNotFourDigits(string zip)
    {
        // Bir2316FieldMap draws this into exactly 4 digit cells.
        Validating(new Bir2316ManualInputs { PrevEmployerZipCode = zip }).Should()
            .Throw<DomainException>().WithMessage("*ZIP*4 digits*");
    }

    // ── String lengths ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAnAbsurdlyLongEmployerName()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerName = new string('x', 201) }).Should()
            .Throw<DomainException>().WithMessage("*name*200 characters*");
    }

    [Fact]
    public void Validate_AcceptsAnEmployerNameExactlyAtTheLimit()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerName = new string('x', 200) })
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAnAbsurdlyLongEmployerAddress()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerAddress = new string('x', 201) }).Should()
            .Throw<DomainException>().WithMessage("*address*200 characters*");
    }

    [Fact]
    public void Validate_AcceptsAnEmployerAddressExactlyAtTheLimit()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerAddress = new string('x', 200) })
            .Should().NotThrow();
    }

    // ── Accumulation ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReportsEveryProblemInOneException()
    {
        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = -1m,
            Item35_DeMinimis = -1m,
            PrevEmployerTin = "nope",
            PrevEmployerZipCode = "12",
        };

        var message = Validating(manual).Should().Throw<DomainException>().Which.Message;

        message.Should().Contain("Item 22");
        message.Should().Contain("Item 35");
        message.Should().Contain("TIN");
        message.Should().Contain("ZIP");
    }

    private static Bir2316ManualInputs WithField(string field, decimal value) => field switch
    {
        "Item22" => new Bir2316ManualInputs { Item22_PrevTaxableCompensation = value },
        // Item22 is raised alongside Item25B so the cross-field rule does not fire and mask the
        // bound this test is actually pinning.
        "Item25B" => new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = value < 0m ? 0m : value,
            Item25B_PrevTaxWithheld = value,
        },
        "Item27" => new Bir2316ManualInputs { Item27_PeraTaxCredit = value },
        "Item33" => new Bir2316ManualInputs { Item33_HazardPayMwe = value },
        "Item35" => new Bir2316ManualInputs { Item35_DeMinimis = value },
        "MinWageDay" => new Bir2316ManualInputs { StatutoryMinWagePerDay = value },
        "MinWageMonth" => new Bir2316ManualInputs { StatutoryMinWagePerMonth = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field key."),
    };
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test PeopleCore.slnx --filter "FullyQualifiedName~Bir2316ManualInputsValidatorTests"
```

Expected: build failure, `CS0103: The name 'Bir2316ManualInputsValidator' does not exist`.

- [ ] **Step 3: Create the validator**

Create `src/PeopleCore.Application/Payroll/Validation/Bir2316ManualInputsValidator.cs`:

```csharp
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Guards the overlay a caller supplies to <c>Bir2316Service.BuildAsync</c>.
/// <para>
/// The 2316 phase was careful that DERIVED figures can never come from the request body - that is
/// what stops a caller posting any withheld-tax figure they like and receiving a certificate
/// stating it. The fields a caller legitimately supplies were never bounded, so a negative or
/// absurd figure still reached the stamped form. This closes that half.
/// </para>
/// <para>
/// The empty overlay must pass: <c>GetPreviewAsync</c> builds a preview by calling
/// <c>BuildAsync</c> with <c>new Bir2316ManualInputs()</c>, so rejecting an all-default instance
/// would break preview for every employee.
/// </para>
/// </summary>
public static class Bir2316ManualInputsValidator
{
    public static void Validate(Bir2316ManualInputs manual)
    {
        var failures = new ValidationFailures();
        const decimal Max = PayrollInputLimits.MaxAnnualAmount;

        failures.AddMoney("Previous employer taxable compensation (Item 22)",
            manual.Item22_PrevTaxableCompensation, Max);
        failures.AddMoney("Previous employer tax withheld (Item 25B)",
            manual.Item25B_PrevTaxWithheld, Max);
        failures.AddMoney("PERA tax credit (Item 27)", manual.Item27_PeraTaxCredit, Max);
        failures.AddMoney("Hazard pay (Item 33)", manual.Item33_HazardPayMwe, Max);
        failures.AddMoney("De minimis benefits (Item 35)", manual.Item35_DeMinimis, Max);
        failures.AddMoney("Statutory minimum wage per day", manual.StatutoryMinWagePerDay, Max);
        failures.AddMoney("Statutory minimum wage per month", manual.StatutoryMinWagePerMonth, Max);

        // No relationship is asserted between the two minimum-wage figures. IsMinimumWageEarner is
        // deliberately not exposed, so neither reaches a rendered certificate, and inventing a
        // day-to-month rule for fields nothing consumes would be guessing.

        failures.AddIf(manual.Item25B_PrevTaxWithheld > manual.Item22_PrevTaxableCompensation,
            "Tax withheld by the previous employer (Item 25B) cannot exceed the taxable "
            + "compensation it was withheld from (Item 22).");

        failures.AddIf(manual.PrevEmployerName?.Length > PayrollInputLimits.MaxEmployerNameLength,
            $"Previous employer name cannot exceed {PayrollInputLimits.MaxEmployerNameLength} characters.");

        failures.AddIf(manual.PrevEmployerAddress?.Length > PayrollInputLimits.MaxEmployerAddressLength,
            $"Previous employer address cannot exceed {PayrollInputLimits.MaxEmployerAddressLength} characters.");

        if (!string.IsNullOrWhiteSpace(manual.PrevEmployerTin))
        {
            // Philippine TINs are 9 digits, or 12 with a branch code. Separators are accepted
            // because that is how people write them, and Bir2316Stamper draws the digits into
            // fixed cells regardless of how they were typed.
            var tin = manual.PrevEmployerTin;
            var digitCount = tin.Count(char.IsDigit);
            var onlyDigitsAndSeparators = tin.All(c => char.IsDigit(c) || c is '-' or ' ');

            failures.AddIf(!onlyDigitsAndSeparators || (digitCount != 9 && digitCount != 12),
                "Previous employer TIN must be 9 digits, or 12 with a branch code.");
        }

        if (!string.IsNullOrWhiteSpace(manual.PrevEmployerZipCode))
        {
            var zip = manual.PrevEmployerZipCode;

            failures.AddIf(zip.Length != 4 || !zip.All(char.IsDigit),
                "Previous employer ZIP code must be exactly 4 digits.");
        }

        failures.ThrowIfAny();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test PeopleCore.slnx --filter "FullyQualifiedName~Bir2316ManualInputsValidatorTests"
```

Expected: `Passed! - Failed: 0, Passed: 44` (12 facts plus 32 theory rows).

- [ ] **Step 5: Wire the validator into the service**

In `src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs`, add `using PeopleCore.Application.Payroll.Validation;` to the usings and make `Validate` the first statement of `BuildAsync`:

```csharp
    public async Task<Bir2316Dto?> BuildAsync(
        Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default)
    {
        Bir2316ManualInputsValidator.Validate(manual);

        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct);
```

Placing it before the employee lookup is deliberate: a malformed overlay is the caller's error either way, and rejecting it without a database round trip keeps the failure cheap.

- [ ] **Step 6: Run the full suite**

```bash
dotnet test PeopleCore.slnx
```

Expected: `Failed: 0`, total 359 (315 after Task 2, plus 44). Watch `Bir2316ServiceTests` in particular — it exercises both `GetPreviewAsync` (empty overlay) and `BuildAsync` with populated overlays, and every one of those must still pass untouched.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Application/Payroll/Validation/Bir2316ManualInputsValidator.cs src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs tests/PeopleCore.Application.Tests/Payroll/Bir2316ManualInputsValidatorTests.cs
git commit -m "feat(payroll): validate BIR 2316 manual inputs"
```

---

## Definition of done

- [ ] All three validators exist, are `public`, and are called on the first line of the service method they guard.
- [ ] `dotnet build PeopleCore.slnx` reports `0 Error(s)`.
- [ ] `dotnet test PeopleCore.slnx` reports `Failed: 0`.
- [ ] No existing test file was edited.
- [ ] `PeopleCore.Application.csproj` has no new `PackageReference`.
- [ ] `StatutoryCaps.cs`, `BirWithholdingTax.cs`, `DolePremiumRates.cs`, `SssContributionSchedule.cs` and `ContributionRates.cs` are unmodified — confirm with `git diff --stat main -- src/PeopleCore.Domain/Payroll/` showing only `PayrollInputLimits.cs`.
- [ ] `ExceptionHandlingMiddleware.cs` is unmodified.
