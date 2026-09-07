using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316ServiceTests
{
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();

    private readonly Mock<IPayrollRunRepository> _runRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<ICompanyRepository> _companyRepo = new();
    private readonly Bir2316Service _sut;

    public Bir2316ServiceTests()
    {
        _employeeRepo.Setup(r => r.GetByIdAsync(_employeeId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(() => TheEmployee());
        _companyRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => TheCompany());

        // Default: nothing paid. Tests that need runs call PaidRunsAre.
        PaidRunsAre();

        _sut = new Bir2316Service(_runRepo.Object, _employeeRepo.Object, _companyRepo.Object);
    }

    // ------------------------------------------------------------------
    // Aggregation across an employee's paid runs
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPreviewAsync_SumsRegularPayOvertimeAndWithholdingAcrossPaidRuns()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 20_000m, overtimePay: 1_500m, withholdingTax: 2_000m,
                    sss: 900m, philHealth: 500m, pagIbig: 100m),
                // Another employee's entry, riding along in the same run. GetPaidRunsForEmployeeInYearAsync
                // returns whole runs (see its remarks), so the aggregation has to pick out only its
                // own line - exactly as PayslipService.GetMyPayslipsAsync already does.
                Entry(_otherEmployeeId,
                    regularPay: 99_000m, overtimePay: 9_900m, withholdingTax: 9_900m,
                    sss: 9_900m, philHealth: 9_900m, pagIbig: 9_900m)
            ]),
            Run(payDate: new DateOnly(2026, 1, 31), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 21_000m, overtimePay: 500m, withholdingTax: 2_200m,
                    sss: 900m, philHealth: 500m, pagIbig: 100m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Year.Should().Be(2026);
        result.Item39_BasicSalary.Should().Be(41_000m);            // 20,000 + 21,000
        result.Item50_OvertimePay.Should().Be(2_000m);             // 1,500 + 500
        result.Item25A_PresentTaxWithheld.Should().Be(4_200m);     // 2,000 + 2,200
        result.Item36_SssPhicPagibigContributions.Should().Be(3_000m); // (900+500+100) x 2

        // Not one centavo of the other employee's line leaked in.
        result.Item39_BasicSalary.Should().NotBe(140_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_CertifiesHolidayNightDiffAndTaxableAllowances()
    {
        // PayrollComputationService withholds against
        // regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances. Every one of
        // those five components has to reach the certificate's taxable total, or Item 21/23
        // understates what tax was actually withheld against - manufacturing a false
        // "tax due < tax withheld" result for any employee who worked a holiday, drew night
        // differential, or received a taxable allowance.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 20_000m, overtimePay: 1_000m,
                    holidayPay: 800m, nightDiffPay: 300m, taxableAllowances: 1_500m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();

        // All three components are reflected somewhere in Section B's taxable items.
        result!.Item44A_OtherAmount.Should().Be(800m);
        result.Item44B_OtherAmount.Should().Be(300m);
        result.Item51A_OtherAmount.Should().Be(1_500m);

        // The acceptance test: the sum of the taxable Section B items the service populates must
        // equal exactly what PayrollComputationService treats as the taxable base, so Item 24 and
        // Item 25A can only diverge because withholding was genuinely wrong - never because the
        // certificate omitted income.
        result.Item52_TotalTaxableCompensation.Should().Be(
            20_000m + 1_000m + 800m + 300m + 1_500m);
    }

    [Fact]
    public async Task GetPreviewAsync_PutsNonTaxableAllowancesInTheNonTaxableSectionOnly()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId, regularPay: 20_000m, nonTaxableAllowances: 2_000m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();

        // Lands in Section A (non-taxable) ...
        result!.Item37_SalariesOtherForms.Should().Be(2_000m);
        result.Item38_TotalNonTaxable.Should().Be(2_000m);

        // ... and does NOT inflate taxable compensation.
        result.Item52_TotalTaxableCompensation.Should().Be(20_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_ExcludesRunsThatAreNotPaid()
    {
        // Only Paid counts. ComputeAsync resets a run to Draft on every recompute, and Approved
        // is still one recompute away from moving - so admitting anything short of Paid would let
        // a tax certificate change after it was issued.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]),
            Run(payDate: new DateOnly(2026, 1, 31), status: PayrollRunStatus.Draft, entries:
                [Entry(_employeeId, regularPay: 5_000m, withholdingTax: 500m)]),
            Run(payDate: new DateOnly(2026, 2, 15), status: PayrollRunStatus.ForApproval, entries:
                [Entry(_employeeId, regularPay: 6_000m, withholdingTax: 600m)]),
            Run(payDate: new DateOnly(2026, 2, 28), status: PayrollRunStatus.Approved, entries:
                [Entry(_employeeId, regularPay: 7_000m, withholdingTax: 700m)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item39_BasicSalary.Should().Be(20_000m);
        result.Item25A_PresentTaxWithheld.Should().Be(2_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_AttributesARunToTheYearItWasPaidIn()
    {
        // A run for period 2025-12-26..2026-01-10 with PayDate 2026-01-15 belongs to 2026,
        // NOT 2025. PayZen attributed by PeriodStart and would have put it in 2025.
        // BIR taxes compensation in the year it is PAID, and in a semi-monthly cycle a period
        // that straddles New Year is the ordinary case, not an edge case.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid,
                periodStart: new DateOnly(2025, 12, 26), periodEnd: new DateOnly(2026, 1, 10),
                entries: [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));

        var paidYear = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);
        var periodStartYear = await _sut.GetPreviewAsync(_employeeId, 2025, CancellationToken.None);

        paidYear.Should().NotBeNull();
        paidYear!.Item39_BasicSalary.Should().Be(20_000m);
        paidYear.Item25A_PresentTaxWithheld.Should().Be(2_000m);

        periodStartYear.Should().BeNull(
            "the run was paid in 2026, so none of it is 2025 income - PayZen's PeriodStart.Year " +
            "would have certified it as 2025");
    }

    [Theory]
    [InlineData(80_000, 80_000, 0)]
    [InlineData(90_000, 90_000, 0)]
    [InlineData(100_000, 90_000, 10_000)]
    public async Task GetPreviewAsync_SplitsThirteenthMonthAtTheExemptionCap(
        decimal total, decimal expectedNonTaxable, decimal expectedTaxable)
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 12, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, thirteenthMonth: total)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item34_ThirteenthMonthAndBenefits.Should().Be(expectedNonTaxable);
        result.Item48_TaxableThirteenthMonth.Should().Be(expectedTaxable);
        (result.Item34_ThirteenthMonthAndBenefits + result.Item48_TaxableThirteenthMonth)
            .Should().Be(total, "the split reallocates the 13th month, it never creates or loses any");
        StatutoryCaps.ThirteenthMonthExemption.Should().Be(90_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_WhenThereAreNoPaidRunsForTheYear_ReturnsNull()
    {
        PaidRunsAre();

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().BeNull("there is nothing to certify");
    }

    // ------------------------------------------------------------------
    // The security departure: derived figures are never taken from the caller
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_IgnoresDerivedFiguresSuppliedByTheCaller()
    {
        // Payroll says 4,321 was withheld this year. The caller is going to try to state
        // something else on the certificate, and supply a large previous-employer figure
        // alongside it.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 30_000m, overtimePay: 1_000m, withholdingTax: 4_321m,
                    sss: 900m, philHealth: 500m, pagIbig: 100m, thirteenthMonth: 100_000m)
            ]));

        var manual = new Bir2316ManualInputs
        {
            PrevEmployerTin = "111-222-333-000",
            PrevEmployerName = "Former Employer Inc.",
            PrevEmployerAddress = "1 Old Street, Makati City",
            PrevEmployerZipCode = "1200",
            Item22_PrevTaxableCompensation = 500_000m,
            Item25B_PrevTaxWithheld = 999_999m,
            Item35_DeMinimis = 7_000m,
            Item33_HazardPayMwe = 3_000m,
            Item27_PeraTaxCredit = 5_000m,
            StatutoryMinWagePerDay = 610m,
            StatutoryMinWagePerMonth = 15_910m
        };

        var result = await _sut.BuildAsync(_employeeId, 2026, manual, CancellationToken.None);

        result.Should().NotBeNull();

        // Every derived figure comes off the payroll records, whatever the request said.
        result!.Item25A_PresentTaxWithheld.Should().Be(4_321m);
        result.Item39_BasicSalary.Should().Be(30_000m);
        result.Item50_OvertimePay.Should().Be(1_000m);
        result.Item36_SssPhicPagibigContributions.Should().Be(1_500m);
        result.Item34_ThirteenthMonthAndBenefits.Should().Be(90_000m);
        result.Item48_TaxableThirteenthMonth.Should().Be(10_000m);

        // The manual half is overlaid exactly as supplied - that is what it is for.
        result.Item22_PrevTaxableCompensation.Should().Be(500_000m);
        result.Item25B_PrevTaxWithheld.Should().Be(999_999m);
        result.Item35_DeMinimis.Should().Be(7_000m);
        result.Item33_HazardPayMwe.Should().Be(3_000m);
        result.Item27_PeraTaxCredit.Should().Be(5_000m);
        result.PrevEmployerName.Should().Be("Former Employer Inc.");
        result.StatutoryMinWagePerDay.Should().Be(610m);

        // IsMinimumWageEarner has no manual input to overlay - see the doc comment on
        // Bir2316Dto.IsMinimumWageEarner - so it stays at its default even though nothing here
        // requests it explicitly. It is not derived and must not silently become true.
        result.IsMinimumWageEarner.Should().BeFalse();

        // Item 24 is the liability computed on Item 23, never the withheld total. Item 23 here is
        // 30,000 + 1,000 + 10,000 taxable 13th month + 500,000 previous = 541,000, so tax due is
        // 22,500 + 20% of the 141,000 over 400,000 = 50,700 - nothing like the 1,004,320 withheld.
        result.Item23_GrossTaxable.Should().Be(541_000m);
        result.Item24_TaxDue.Should().Be(50_700m);
        result.Item24_TaxDue.Should().Be(BirWithholdingTax.ComputeAnnualTaxDue(result.Item23_GrossTaxable));
        result.Item26_TotalTaxWithheld.Should().Be(1_004_320m);
        result.Item24_TaxDue.Should().NotBe(result.Item26_TotalTaxWithheld);

        // The type is the enforcement. Bir2316ManualInputs carries only what a human legitimately
        // supplies - there is no field on it capable of stating a derived figure, so there is no
        // request body a caller could craft to put one on a tax certificate. Pinned by name so
        // that adding such a field later fails here instead of shipping quietly.
        typeof(Bir2316ManualInputs).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(
            [
                nameof(Bir2316ManualInputs.PrevEmployerTin),
                nameof(Bir2316ManualInputs.PrevEmployerName),
                nameof(Bir2316ManualInputs.PrevEmployerAddress),
                nameof(Bir2316ManualInputs.PrevEmployerZipCode),
                nameof(Bir2316ManualInputs.Item22_PrevTaxableCompensation),
                nameof(Bir2316ManualInputs.Item25B_PrevTaxWithheld),
                nameof(Bir2316ManualInputs.Item27_PeraTaxCredit),
                nameof(Bir2316ManualInputs.Item33_HazardPayMwe),
                nameof(Bir2316ManualInputs.Item35_DeMinimis),
                nameof(Bir2316ManualInputs.StatutoryMinWagePerDay),
                nameof(Bir2316ManualInputs.StatutoryMinWagePerMonth)
            ]);
    }

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPreviewAsync_TakesTheEmployeeTinFromTheGovernmentIdRow()
    {
        // EmployeeConfiguration ignores the M2NET.Core base Employee.TIN column outright
        // ("PeopleCore uses GovernmentIds collection"), so the base property is never loaded from
        // the database and the EmployeeGovernmentId row is the only authoritative TIN. Set the
        // base property to something else here to prove which one reaches the certificate.
        var employee = TheEmployee();
        employee.TIN = "000-000-000-000";   // the M2NET.Core base property, unmapped by EF
        _employeeRepo.Setup(r => r.GetByIdAsync(_employeeId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(employee);

        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.EmployeeTin.Should().Be("123-456-789-000");
        result.EmployeeLastName.Should().Be("Dela Cruz");
        result.EmployeeFirstName.Should().Be("Juan");
        result.RdoCode.Should().Be("050");
        result.EmployerTin.Should().Be("987-654-321-000");
        result.EmployerName.Should().Be("M2NET Solutions Inc.");
        result.EmployerZipCode.Should().Be("1605");
        result.IsMainEmployer.Should().BeTrue();
    }

    [Fact]
    public async Task GetPreviewAsync_WhenNoCompanyIsConfigured_Throws()
    {
        _companyRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Company?)null);
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));

        var act = () => _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Stubs the repository to hand back <paramref name="runs"/> for ANY year, deliberately: the
    /// year and status filters under test are the service's own, not the mock's.
    /// </summary>
    private void PaidRunsAre(params PayrollRun[] runs)
        => _runRepo
            .Setup(r => r.GetPaidRunsForEmployeeInYearAsync(
                _employeeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runs);

    private static PayrollRun Run(
        DateOnly payDate,
        PayrollRunStatus status,
        IEnumerable<PayrollRunEmployee> entries,
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null)
    {
        var run = new PayrollRun
        {
            RunNumber = $"PR-{payDate:yyyyMMdd}",
            PayDate = payDate,
            PeriodStart = periodStart ?? payDate.AddDays(-15),
            PeriodEnd = periodEnd ?? payDate.AddDays(-1),
            Frequency = PayFrequency.SemiMonthly,
            Status = status
        };
        run.Employees.AddRange(entries);
        return run;
    }

    private static PayrollRunEmployee Entry(
        Guid employeeId,
        decimal regularPay = 0m,
        decimal overtimePay = 0m,
        decimal withholdingTax = 0m,
        decimal sss = 0m,
        decimal philHealth = 0m,
        decimal pagIbig = 0m,
        decimal thirteenthMonth = 0m,
        decimal holidayPay = 0m,
        decimal nightDiffPay = 0m,
        decimal taxableAllowances = 0m,
        decimal nonTaxableAllowances = 0m)
        => new()
        {
            EmployeeId = employeeId,
            RegularPay = regularPay,
            OvertimePay = overtimePay,
            WithholdingTax = withholdingTax,
            SSSEmployee = sss,
            PhilHealthEmployee = philHealth,
            PagIbigEmployee = pagIbig,
            ThirteenthMonth = thirteenthMonth,
            HolidayPay = holidayPay,
            NightDiffPay = nightDiffPay,
            TaxableAllowances = taxableAllowances,
            NonTaxableAllowances = nonTaxableAllowances
        };

    private Employee TheEmployee()
    {
        var employee = new Employee
        {
            Id = _employeeId,
            EmployeeNumber = "EMP-0001",
            FirstName = "Juan",
            MiddleName = "Protacio",
            LastName = "Dela Cruz",
            DateOfBirth = new DateOnly(1990, 4, 12),
            MobileNumber = "0917-555-0101",
            Address = "12 Mabini Street, Quezon City",
            ZipCode = "1101",
            RdoCode = "050"
        };
        employee.GovernmentIds.Add(new EmployeeGovernmentId
        {
            EmployeeId = _employeeId,
            IdType = GovernmentIdType.TIN,
            IdNumber = "123-456-789-000"
        });
        return employee;
    }

    private static Company TheCompany() => new()
    {
        Name = "M2NET Solutions Inc.",
        TIN = "987-654-321-000",
        Address = "8 Ortigas Avenue, Pasig City",
        City = "Pasig City",
        RdoCode = "043",
        ZipCode = "1605"
    };
}
