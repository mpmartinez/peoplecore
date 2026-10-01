using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportServiceTests
{
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IPayrollSettingsRepository> _settings = new();
    private readonly Mock<IBir2316Service> _bir2316 = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly GovernmentReportService _sut;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public GovernmentReportServiceTests()
    {
        _companies.Setup(c => c.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Company
        {
            Name = "Acme Inc.", TIN = "123-456-789-000", SSSNumber = "03-9999999-1",
            PhilHealthNumber = "20-000000001-2", PagIbigNumber = "2000-0000-0001", RdoCode = "050"
        });
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _bir2316.Setup(b => b.BuildAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)), _bir2316.Object, _employees.Object);
    }

    private static Employee Person(string last, string first, params (GovernmentIdType Type, string Number)[] ids)
    {
        var e = new Employee { LastName = last, FirstName = first, DateOfBirth = new DateOnly(1990, 5, 1) };
        foreach (var (type, number) in ids)
            e.GovernmentIds.Add(new EmployeeGovernmentId { EmployeeId = e.Id, IdType = type, IdNumber = number });
        return e;
    }

    private static PayrollRun Run(DateOnly periodEnd, DateOnly payDate, params PayrollRunEmployee[] entries)
        => RunForPeriod(periodEnd.AddDays(-14), periodEnd, payDate, entries);

    private static PayrollRun RunForPeriod(DateOnly periodStart, DateOnly periodEnd, DateOnly payDate, params PayrollRunEmployee[] entries)
        => RunForPeriod(PayFrequency.SemiMonthly, periodStart, periodEnd, payDate, entries);

    private static PayrollRun RunForPeriod(PayFrequency frequency, DateOnly periodStart, DateOnly periodEnd, DateOnly payDate,
        params PayrollRunEmployee[] entries)
    {
        var run = new PayrollRun { RunNumber = "PAY", PeriodStart = periodStart, PeriodEnd = periodEnd,
                                   PayDate = payDate, Status = PayrollRunStatus.Paid, Frequency = frequency };
        run.Employees.AddRange(entries);
        return run;
    }

    private static PayrollRunEmployee Entry(Employee e, decimal sssEe = 0, decimal sssEr = 0, decimal regularPay = 0,
        decimal thirteenth = 0, decimal tax = 0, decimal phEe = 0, decimal piEe = 0, decimal nonTaxAllow = 0,
        decimal leaveConversionPay = 0, decimal leaveConversionNonTaxable = 0, decimal separationPay = 0,
        decimal retirementPay = 0, decimal finalPayNonTaxable = 0)
        => new() { EmployeeId = e.Id, Employee = e, SSSEmployee = sssEe, SSSEmployer = sssEr, RegularPay = regularPay,
                   ThirteenthMonth = thirteenth, WithholdingTax = tax, PhilHealthEmployee = phEe, PagIbigEmployee = piEe,
                   NonTaxableAllowances = nonTaxAllow, LeaveConversionPay = leaveConversionPay,
                   LeaveConversionNonTaxable = leaveConversionNonTaxable, SeparationPay = separationPay,
                   RetirementPay = retirementPay, FinalPayNonTaxable = finalPayNonTaxable };

    [Fact]
    public async Task Sss_CombinesAMonthsCutoffsIntoOneRow_AndWorksBackTheMscAndEc()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20), Entry(juan, sssEe: 500m, sssEr: 1_015m)),
            RunForPeriod(new(2026, 3, 16), new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Columns.Should().Equal("Employee", "SSS number", "MSC", "Employee share", "Employer share", "EC", "Employer total", "Total");
        report.Rows.Should().ContainSingle().Which.Cells.Should().Equal(
            "Cruz, Juan", "34-1234567-8", "20000.00", "1000.00", "2000.00", "30.00", "2030.00", "3030.00");
        report.Totals.Should().Equal("Total", "", "", "1000.00", "2000.00", "30.00", "2030.00", "3030.00");
        report.Employer.AgencyNumber.Should().Be("03-9999999-1");
    }

    [Fact]
    public async Task Sss_LeavesMscAndEcBlank_WhenOnlyOneCutoffOfTheMonthIsPaid()
    {
        // Only the 1-15 cutoff is in - working the MSC back from half the month's share would
        // understate it, so it (and the EC that goes with it) is left blank instead.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Should().ContainSingle().Which.Cells.Should().Equal(
            "Cruz, Juan", "34-1234567-8", "", "500.00", "", "", "1015.00", "1515.00");
        report.Totals.Should().Equal("Total", "", "", "500.00", "", "", "1015.00", "1515.00");
        report.Warnings.Should().Contain(
            "The MSC and EC are left blank for 1 employee whose pay for the whole month isn't paid yet.");
    }

    [Fact]
    public async Task Sss_ShowsValues_WhenASemiMonthlyEmployeesFinalPayIsTheMonthsOnlyRun()
    {
        // A semi-monthly employee leaves on Mar 13 with February paid and no March cutoff: the
        // final pay tops March up to the whole month (employee 1,750, employer 3,530 on a 36,500
        // basic), so the month is complete - MSC 1,750 / 5% = 35,000, EC 30, employer SS 3,500.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        var finalPay = RunForPeriod(PayFrequency.SemiMonthly, new(2026, 3, 1), new(2026, 3, 13), new(2026, 3, 31),
                                    Entry(juan, sssEe: 1_750m, sssEr: 3_530m));
        finalPay.RunType = PayrollRunType.FinalPay;
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([finalPay]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Should().ContainSingle().Which.Cells.Should().Equal(
            "Cruz, Juan", "34-1234567-8", "35000.00", "1750.00", "3500.00", "30.00", "3530.00", "5280.00");
        report.Totals.Should().Equal("Total", "", "", "1750.00", "3500.00", "30.00", "3530.00", "5280.00");
        report.Warnings.Should().NotContain(w => w.Contains("whole month"));
    }

    [Fact]
    public async Task Sss_ShowsValues_WhenASingleMonthlyRunCoversTheWholeMonth()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(PayFrequency.Monthly, new(2026, 3, 1), new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 1_000m, sssEr: 2_030m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Should().ContainSingle().Which.Cells.Should().Equal(
            "Cruz, Juan", "34-1234567-8", "20000.00", "1000.00", "2000.00", "30.00", "2030.00", "3030.00");
        report.Warnings.Should().NotContain(w => w.Contains("whole month"));
    }

    [Fact]
    public async Task Sss_ShowsValues_ForBothCutoffsOfAMonthThatDontFollowTheCalendar()
    {
        // 26th-10th and 11th-25th cutoffs never cover the 26th-31st of the month they end in, yet
        // both of the month's cutoffs - and so its whole SSS - are in.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(new(2026, 2, 26), new(2026, 3, 10), new(2026, 3, 15), Entry(juan, sssEe: 500m, sssEr: 1_015m)),
            RunForPeriod(new(2026, 3, 11), new(2026, 3, 25), new(2026, 3, 30), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Single().Cells[2].Should().Be("20000.00");
        report.Warnings.Should().NotContain(w => w.Contains("whole month"));
    }

    [Fact]
    public async Task Sss_LeavesMscAndEcBlank_ForOneOfTwoNonCalendarCutoffs()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(new(2026, 3, 11), new(2026, 3, 25), new(2026, 3, 30), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Single().Cells[2].Should().BeEmpty();
        report.Warnings.Should().Contain(w => w.Contains("whole month"));
    }

    [Fact]
    public async Task Sss_ShowsValues_ForAMonthlyRunThatDoesntFollowTheCalendar()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(PayFrequency.Monthly, new(2026, 2, 21), new(2026, 3, 20), new(2026, 3, 25), Entry(juan, sssEe: 1_000m, sssEr: 2_030m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Single().Cells[2].Should().Be("20000.00");
    }

    [Fact]
    public async Task Sss_LeavesTheEmployerShareAndEcTotalsBlank_WhenAnyRowIsBlank()
    {
        // Totals over only the rows that have values would disagree with the Employer total column.
        var ana = Person("Santos", "Ana", (GovernmentIdType.SSS, "34-7654321-0"));
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20),
                Entry(juan, sssEe: 500m, sssEr: 1_015m), Entry(ana, sssEe: 500m, sssEr: 1_015m)),
            RunForPeriod(new(2026, 3, 16), new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Totals.Should().Equal("Total", "", "", "1500.00", "", "", "3045.00", "4545.00");
    }

    [Fact]
    public async Task Sss_LeavesMscAndEcBlank_WhenTheCompanyOverridesTheRates()
    {
        _settings.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PayrollSettings { SSSEmployeeRate = 0.045m, SSSEmployerRate = 0.095m });
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 900m, sssEr: 1_900m))]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Single().Cells.Should().Equal("Cruz, Juan", "34-1234567-8", "", "900.00", "", "", "1900.00", "2800.00");
        report.Warnings.Should().Contain(w => w.Contains("override"));
    }

    [Fact]
    public async Task AnEmployeeWithoutTheAgencysNumber_IsListedAndCounted()
    {
        var ana = Person("Reyes", "Ana");
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(ana, phEe: 500m))]);

        var report = await _sut.BuildAsync("philhealth", 2026, 3);

        report.Rows.Single().MissingNumber.Should().BeTrue();
        report.Warnings.Should().Contain("1 employee has no PhilHealth number.");
    }

    [Fact]
    public async Task Bir1601C_CountsTheMonthPaid_AndItsLinesAddUp()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        // Paid in April for a March period: 1601-C for April, not March.
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 31), new(2026, 4, 5),
                Entry(juan, regularPay: 50_000m, thirteenth: 100_000m, tax: 9_000m,
                      sssEe: 875m, phEe: 625m, piEe: 100m, nonTaxAllow: 1_000m))
        ]);

        var report = await _sut.BuildAsync("1601c", 2026, 4);

        // Compensation 151,000 = 50,000 + 100,000 13th month + 1,000 allowances. Non-taxable:
        // 90,000 of the 13th month + 1,600 employee shares + 1,000 allowances = 92,600.
        report.Columns.Should().Equal("Employee", "TIN", "Compensation", "13th month and other benefits (non-taxable)",
            "De minimis", "Employee shares", "Other non-taxable", "Taxable", "Tax withheld");
        report.Summary.Should().ContainInOrder(
            new GovernmentReportLineDto("Total amount of compensation", 151_000m),
            new GovernmentReportLineDto("13th month pay and other benefits", 90_000m),
            new GovernmentReportLineDto("SSS, PhilHealth and Pag-IBIG employee shares", 1_600m),
            new GovernmentReportLineDto("Other non-taxable compensation", 1_000m),
            new GovernmentReportLineDto("Total non-taxable compensation", 92_600m),
            new GovernmentReportLineDto("Total taxable compensation", 58_400m),
            new GovernmentReportLineDto("Total taxes withheld", 9_000m));
        // No de minimis (leave conversion) here, so the row's own arithmetic ties out with a zero
        // in that column: 151,000 - 90,000 (13th) - 0 (de minimis) - 1,600 (shares) -
        // 1,000 (other non-taxable) = 58,400.
        report.Rows.Single().Cells.Should().Equal(
            "Cruz, Juan", "111-222-333-000", "151000.00", "90000.00", "0.00", "1600.00", "1000.00", "58400.00", "9000.00");
        report.Totals.Should().Equal(
            "Total", "", "151000.00", "90000.00", "0.00", "1600.00", "1000.00", "58400.00", "9000.00");
    }

    [Fact]
    public async Task Bir1601C_LeavesTheMaternityAdvanceOut_AndTakesTheReducedBasic()
    {
        // An engine-computed April entry: 30,000 salary, 6,000 covered by SSS maternity (regular
        // pay 24,000), the 70,000.35 benefit advanced. Gross pay 94,000.35 carries the advance; the
        // 1601-C must not. Compensation 24,000; shares 1,500 + 750 + 200 = 2,450; taxable
        // 24,000 - 2,450 = 21,550; withheld 107.50 ((21,550 x 12 - 250,000) x 15% / 12).
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var april = MaternityRun(juan, new DateOnly(2026, 4, 1), advance: 70_000.35m, offset: 6_000m);
        april.Employees.Single().GrossPay.Should().Be(94_000.35m);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([april]);

        var report = await _sut.BuildAsync("1601c", 2026, 4);

        report.Summary.Should().ContainInOrder(
            new GovernmentReportLineDto("Total amount of compensation", 24_000m),
            new GovernmentReportLineDto("13th month pay and other benefits", 0m),
            new GovernmentReportLineDto("De minimis benefits", 0m),
            new GovernmentReportLineDto("SSS, PhilHealth and Pag-IBIG employee shares", 2_450m),
            new GovernmentReportLineDto("Other non-taxable compensation", 0m),
            new GovernmentReportLineDto("Total non-taxable compensation", 2_450m),
            new GovernmentReportLineDto("Total taxable compensation", 21_550m),
            new GovernmentReportLineDto("Total taxes withheld", 107.50m));
        report.Rows.Single().Cells.Should().Equal(
            "Cruz, Juan", "111-222-333-000", "24000.00", "0.00", "0.00", "2450.00", "0.00", "21550.00", "107.50");
    }

    [Fact]
    public async Task Bir1601C_PutsTheMaternityDifferentialInOtherNonTaxable()
    {
        // April: 30,000 salary, 6,000 covered by SSS and 4,000 of salary differential (regular pay
        // 24,000), the 70,000.35 advance. Compensation 24,000; shares 2,450; other non-taxable
        // 4,000 (RMC 105-2019); taxable 24,000 - 2,450 - 4,000 = 17,550, the engine's base, so
        // nothing withheld (210,600 a year).
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var april = MaternityRun(juan, new DateOnly(2026, 4, 1), advance: 70_000.35m, offset: 6_000m, differential: 4_000m);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([april]);

        var report = await _sut.BuildAsync("1601c", 2026, 4);

        report.Rows.Single().Cells.Should().Equal(
            "Cruz, Juan", "111-222-333-000", "24000.00", "0.00", "0.00", "2450.00", "4000.00", "17550.00", "0.00");
        report.Summary.Should().Contain(new GovernmentReportLineDto("Other non-taxable compensation", 4_000m));
        report.Summary.Should().Contain(new GovernmentReportLineDto("Total taxable compensation", 17_550m));
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_WithAMaternityDifferential_AgreesWithTheMonthly1601C()
    {
        // March: an ordinary 30,000 month - taxable 27,550. April: 6,000 covered, 4,000 of
        // differential - taxable 17,550 and 4,000 other non-taxable. The year: compensation
        // 54,000, non-taxable 2,450 + 2,450 + 4,000 = 8,900, taxable 45,100. The 1604-C follows
        // the 2316 (Item 37 takes the 4,000): basic 27,550 + 17,550 = 45,100.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        juan.HireDate = new DateOnly(2020, 1, 6);
        var march = MaternityRun(juan, new DateOnly(2026, 3, 1), advance: 0m, offset: 0m);
        var april = MaternityRun(juan, new DateOnly(2026, 4, 1), advance: 70_000.35m, offset: 6_000m, differential: 4_000m);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([march]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([april]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([march, april]);
        _runs.Setup(r => r.GetEmployeeIdsWithPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([juan.Id]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([juan]);
        var inputs = new Mock<IBir2316InputsRepository>();
        inputs.Setup(i => i.GetForYearAsync(2026, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>());
        var bir2316 = new PeopleCore.Application.Payroll.Services.Bir2316Service(
            _runs.Object, _employees.Object, _companies.Object, inputs.Object);
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)), bir2316, _employees.Object);

        decimal monthlyTaxable = 0m, monthlyOtherNonTaxable = 0m;
        foreach (var month in new[] { 3, 4 })
        {
            var summary = (await sut.BuildAsync("1601c", 2026, month)).Summary;
            monthlyTaxable += summary.Single(l => l.Label == "Total taxable compensation").Amount;
            monthlyOtherNonTaxable += summary.Single(l => l.Label == "Other non-taxable compensation").Amount;
        }
        monthlyTaxable.Should().Be(45_100m);          // 27,550 + 17,550
        monthlyOtherNonTaxable.Should().Be(4_000m);

        var section = (await sut.BuildAnnualAsync("1604c", 2026)).Sections
            .Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        string Cell(string column) => row.Cells[columns.IndexOf(column)];

        Cell("Gross compensation").Should().Be("54000.00");
        Cell("Total non-taxable").Should().Be("8900.00");
        Cell("Basic salary").Should().Be("45100.00");
        Cell("Total taxable (present employer)").Should().Be(GovernmentReportMath.Money(monthlyTaxable));
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_WithAMaternityAdvance_AgreesWithTheMonthly1601C()
    {
        // March: an ordinary 30,000 month - compensation 30,000, shares 2,450, taxable 27,550.
        // April: 6,000 covered by SSS and the 70,000.35 advance - compensation 24,000, taxable
        // 21,550. The year: compensation 54,000, non-taxable 4,900, taxable 49,100. The 1604-C,
        // built from the real 2316, certifies the same year with no trace of the advance.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        juan.HireDate = new DateOnly(2020, 1, 6);
        var march = MaternityRun(juan, new DateOnly(2026, 3, 1), advance: 0m, offset: 0m);
        var april = MaternityRun(juan, new DateOnly(2026, 4, 1), advance: 70_000.35m, offset: 6_000m);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([march]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([april]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([march, april]);
        _runs.Setup(r => r.GetEmployeeIdsWithPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([juan.Id]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([juan]);
        var inputs = new Mock<IBir2316InputsRepository>();
        inputs.Setup(i => i.GetForYearAsync(2026, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>());
        var bir2316 = new PeopleCore.Application.Payroll.Services.Bir2316Service(
            _runs.Object, _employees.Object, _companies.Object, inputs.Object);
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)), bir2316, _employees.Object);

        decimal monthlyTaxable = 0m, monthlyCompensation = 0m;
        foreach (var month in new[] { 3, 4 })
        {
            var summary = (await sut.BuildAsync("1601c", 2026, month)).Summary;
            monthlyTaxable += summary.Single(l => l.Label == "Total taxable compensation").Amount;
            monthlyCompensation += summary.Single(l => l.Label == "Total amount of compensation").Amount;
        }
        monthlyCompensation.Should().Be(54_000m);     // 30,000 + 24,000
        monthlyTaxable.Should().Be(49_100m);          // 27,550 + 21,550

        var section = (await sut.BuildAnnualAsync("1604c", 2026)).Sections
            .Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        string Cell(string column) => row.Cells[columns.IndexOf(column)];

        Cell("Gross compensation").Should().Be(GovernmentReportMath.Money(monthlyCompensation));
        Cell("SSS, PhilHealth and Pag-IBIG employee shares").Should().Be("4900.00");
        Cell("Total non-taxable").Should().Be("4900.00");
        Cell("Basic salary").Should().Be("49100.00");                     // 27,550 + 21,550
        Cell("Total taxable (present employer)").Should().Be(GovernmentReportMath.Money(monthlyTaxable));
        row.Cells.Should().NotContain("70000.35");
    }

    /// <summary>
    /// A Paid monthly run for the month starting <paramref name="monthStart"/>, paid on its last
    /// day, with one entry the real engine computed for a 30,000 salary and the given maternity
    /// figures.
    /// </summary>
    private static PayrollRun MaternityRun(Employee employee, DateOnly monthStart, decimal advance, decimal offset,
        decimal differential = 0m)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var run = RunForPeriod(PayFrequency.Monthly, monthStart, monthEnd, monthEnd);
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employee.Id, BasicSalary = 30_000m, PayFrequency = PayFrequency.Monthly
        };
        var entry = new PeopleCore.Application.Payroll.Services.PayrollComputationService()
            .Compute(compensation, run,
                maternity: new PeopleCore.Application.Payroll.Services.MaternityInput(advance, offset, differential));
        entry.Employee = employee;
        run.Employees.Add(entry);
        return run;
    }

    [Fact]
    public async Task Bir1601C_ShowsFinalPayDeMinimisAndOtherNonTaxable_AndExcludesThemFromTaxable()
    {
        // A final-pay run paid in April: leave conversion of 6,000 (4,000 de minimis, 2,000
        // beyond it) and separation pay of 150,000, fully non-taxable, alongside 10,000 of regular
        // pay and 500 of SSS. GrossPay (10,000 + 6,000 + 150,000 = 166,000) already carries the
        // final-pay earnings; de minimis (4,000) and the rest of FinalPayNonTaxable
        // (154,000 - 4,000 = 150,000) must come back out so taxable compensation isn't overstated.
        // The 2,000 beyond de minimis is "other benefits", inside the 90,000 exemption it shares
        // with the 13th month (none paid this year), so it's on the 13th-month line.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 31), new(2026, 4, 5),
                Entry(juan, regularPay: 10_000m, tax: 1_000m, sssEe: 500m,
                      leaveConversionPay: 6_000m, leaveConversionNonTaxable: 4_000m,
                      separationPay: 150_000m, finalPayNonTaxable: 154_000m))
        ]);

        var report = await _sut.BuildAsync("1601c", 2026, 4);

        report.Summary.Should().ContainInOrder(
            new GovernmentReportLineDto("Total amount of compensation", 166_000m),
            new GovernmentReportLineDto("13th month pay and other benefits", 2_000m),
            new GovernmentReportLineDto("De minimis benefits", 4_000m),
            new GovernmentReportLineDto("SSS, PhilHealth and Pag-IBIG employee shares", 500m),
            new GovernmentReportLineDto("Other non-taxable compensation", 150_000m),
            new GovernmentReportLineDto("Total non-taxable compensation", 156_500m),
            new GovernmentReportLineDto("Total taxable compensation", 9_500m),
            new GovernmentReportLineDto("Total taxes withheld", 1_000m));
        // The row's own arithmetic ties out too: 166,000 - 2,000 (13th month and other benefits)
        // - 4,000 (de minimis) - 500 (shares) - 150,000 (other non-taxable) = 9,500.
        report.Rows.Single().Cells.Should().Equal(
            "Cruz, Juan", "111-222-333-000", "166000.00", "2000.00", "4000.00", "500.00", "150000.00", "9500.00", "1000.00");
        report.Totals.Should().Equal(
            "Total", "", "166000.00", "2000.00", "4000.00", "500.00", "150000.00", "9500.00", "1000.00");
    }

    [Fact]
    public async Task Bir1601C_LeaveBeyondDeMinimis_UsesWhatTheYearLeftOfThe90000()
    {
        // 85,000 of 13th month paid in May. December's final pay: 1,000 more 13th month and
        // 9,000 of leave beyond de minimis. Left of the exemption: 90,000 - 85,000 = 5,000, so
        // 5,000 of December's 10,000 is non-taxable and 5,000 taxable.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var may = Run(new(2026, 5, 15), new(2026, 5, 20), Entry(juan, thirteenth: 85_000m));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18),
            Entry(juan, thirteenth: 1_000m, leaveConversionPay: 12_000m, leaveConversionNonTaxable: 3_000m,
                  finalPayNonTaxable: 3_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([may, december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var clock = new FixedClock(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, clock,
            _bir2316.Object, _employees.Object);

        var report = await sut.BuildAsync("1601c", 2026, 12);

        // Compensation 13,000 = 1,000 + 12,000; taxable 13,000 - 5,000 - 3,000 de minimis = 5,000.
        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 5_000m));
        report.Summary.Should().Contain(new GovernmentReportLineDto("Total taxable compensation", 5_000m));
    }

    [Fact]
    public async Task Bir1601C_Uses13thMonthPaidEarlierInTheYearAgainstTheExemption()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var mayAdvance = Run(new(2026, 5, 15), new(2026, 5, 20), Entry(juan, thirteenth: 60_000m));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18), Entry(juan, thirteenth: 60_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([mayAdvance, december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var clock = new FixedClock(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, clock,
            _bir2316.Object, _employees.Object);

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 30_000m));
    }

    /// <summary>The 1601-C as the app builds it in January 2027, reading earlier 13th months with <paramref name="balances"/>.</summary>
    private GovernmentReportService InJanuary2027WithOpeningBalances(params PayrollOpeningBalance[] balances)
        => new(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero)), _bir2316.Object, _employees.Object,
            new PeopleCore.Application.Payroll.Services.PayrollYearToDate(_runs.Object,
                OpeningBalanceFakes.Holding(balances).Object));

    [Fact]
    public async Task Bir1601C_CountsTheOpeningBalances13thMonthAndOtherBenefitsAgainstTheExemption()
    {
        // December pays Juan a 60,000 13th month. Before PeopleCore he was paid 40,000 of 13th month
        // and 20,000 of other benefits: 60,000 of the exemption used, 30,000 left. So 30,000 of
        // December's 60,000 is non-taxable (without the balance it would be all 60,000).
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18), Entry(juan, thirteenth: 60_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var sut = InJanuary2027WithOpeningBalances(
            OpeningBalanceFakes.OpeningBalance(juan.Id, thirteenthMonthPaid: 40_000m, otherBenefitsPaid: 20_000m));

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 30_000m));
        // Compensation 60,000, all 13th month: 60,000 - 30,000 non-taxable = 30,000 taxable.
        report.Summary.Should().Contain(new GovernmentReportLineDto("Total taxable compensation", 30_000m));
    }

    [Fact]
    public async Task Bir1601C_ForTheMonthOfTheBalancesThroughDate_DoesNotCountTheBalanceAsEarlier()
    {
        // Go-live mid-December: the balance runs through December 10 with 40,000 of 13th month and
        // 20,000 of other benefits. December's 1601-C is the month the balance ends in, so it isn't
        // paid earlier than December: all of December's 60,000 is inside the 90,000.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18), Entry(juan, thirteenth: 60_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var sut = InJanuary2027WithOpeningBalances(
            OpeningBalanceFakes.OpeningBalance(juan.Id, thirteenthMonthPaid: 40_000m, otherBenefitsPaid: 20_000m,
                throughDate: new DateOnly(2026, 12, 10)));

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 60_000m));
    }

    [Fact]
    public async Task Bir1601C_AddsTheOpeningBalanceToThe13thMonthPaidEarlierOnRuns()
    {
        // May paid 60,000 of 13th month; the balance 10,000 more and 5,000 of other benefits:
        // 75,000 used, 15,000 left of December's 60,000.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var mayAdvance = Run(new(2026, 5, 15), new(2026, 5, 20), Entry(juan, thirteenth: 60_000m));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18), Entry(juan, thirteenth: 60_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([mayAdvance, december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var sut = InJanuary2027WithOpeningBalances(
            OpeningBalanceFakes.OpeningBalance(juan.Id, thirteenthMonthPaid: 10_000m, otherBenefitsPaid: 5_000m));

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 15_000m));
    }

    [Fact]
    public async Task Bir1601C_WithOnlyAnotherYearsOrAnotherEmployeesBalance_SplitsAsTheRunsAloneDo()
    {
        // Bir1601C_Uses13thMonthPaidEarlierInTheYearAgainstTheExemption's 30,000, unchanged.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var mayAdvance = Run(new(2026, 5, 15), new(2026, 5, 20), Entry(juan, thirteenth: 60_000m));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18), Entry(juan, thirteenth: 60_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([mayAdvance, december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var sut = InJanuary2027WithOpeningBalances(
            OpeningBalanceFakes.OpeningBalance(juan.Id, year: 2025, thirteenthMonthPaid: 90_000m),
            OpeningBalanceFakes.OpeningBalance(Guid.NewGuid(), thirteenthMonthPaid: 90_000m));

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 30_000m));
    }

    [Fact]
    public async Task Bir1601C_DoesNotLoadEarlierRuns_WhenNoEntryHasAThirteenthMonth()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(juan, regularPay: 50_000m, tax: 5_000m))]);

        await _sut.BuildAsync("1601c", 2026, 4);

        _runs.Verify(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PagIbig_SplitsTheNameAndShowsTheDateOfBirth()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.PagIbig, "1234-5678-9012"));
        juan.MiddleName = "Santos";
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(juan, piEe: 200m))]);

        var report = await _sut.BuildAsync("pagibig", 2026, 3);

        report.Rows.Single().Cells.Take(5).Should().Equal("Cruz", "Juan", "Santos", "1990-05-01", "1234-5678-9012");
    }

    [Fact]
    public async Task TwoEmployees_AreSortedByLastNameThenFirstName()
    {
        var ana = Person("Santos", "Ana");
        var juan = Person("Cruz", "Juan");
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(ana, phEe: 100m), Entry(juan, phEe: 200m))]);

        var report = await _sut.BuildAsync("philhealth", 2026, 3);

        report.Rows.Select(r => r.Cells[0]).Should().Equal("Cruz, Juan", "Santos, Ana");
    }

    [Fact]
    public async Task Sss_UsesThePeriodEndMonth_ForARunPaidTheFollowingMonth()
    {
        // A December 16-31 run only gets paid on January 5, but it still belongs to December's SSS
        // report - the month the pay was earned, not the month it was paid.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2025, 12, It.IsAny<CancellationToken>())).ReturnsAsync([
            RunForPeriod(new(2025, 12, 16), new(2025, 12, 31), new(2026, 1, 5), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2025, 12);

        report.Rows.Should().ContainSingle();
        _runs.Verify(r => r.GetPaidRunsByPeriodEndMonthAsync(2025, 12, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnpaidRunsForTheMonth_AreMentioned()
    {
        _runs.Setup(r => r.CountUnpaidRunsAsync(2026, 3, false, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Warnings.Should().Contain("2 payroll runs for this month aren't paid yet and aren't included.");
    }

    [Fact]
    public async Task AnUnknownReport_IsNotFound()
    {
        var act = () => _sut.BuildAsync("gsis", 2026, 3);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(2026, 5)]   // after April 2026, the clock's month
    [InlineData(0, 3)]      // year missing from the query string binds to 0
    public async Task AMonthThatIsInvalidOrStillAhead_IsRefused(int year, int month)
    {
        var act = () => _sut.BuildAsync("sss", year, month);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Sss_WarnsAboutABlankTin_WhenTheSssNumberIsFilled()
    {
        _companies.Setup(c => c.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Company
        {
            Name = "Acme Inc.", TIN = "", SSSNumber = "03-9999999-1",
            PhilHealthNumber = "20-000000001-2", PagIbigNumber = "2000-0000-0001", RdoCode = "050"
        });

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Warnings.Should().Contain("The company's TIN is blank. Add it on the Company page.");
    }

    // ------------------------------------------------------------------
    // BuildAnnualAsync — the 1604-C alphalist
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAnnualAsync_1604C_BuildsTheAlphalistFromThe2316Forms()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = employeeId, LastName = "Cruz", FirstName = "Juan",
            DateOfBirth = new DateOnly(1990, 1, 1), HireDate = new DateOnly(2020, 1, 6)
        };
        var form = new Bir2316Dto
        {
            EmployeeId = employeeId, EmployeeLastName = "Cruz", EmployeeFirstName = "Juan",
            EmployeeTin = "111-222-333-000"
        };
        _bir2316.Setup(b => b.BuildAllAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([form]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([employee]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 20), new(2026, 3, 20), Entry(employee, tax: 2_000m)),
            Run(new(2026, 12, 18), new(2026, 12, 18), Entry(employee, tax: 500m))
        ]);
        _runs.Setup(r => r.CountUnpaidRunsPaidInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var report = await _sut.BuildAnnualAsync("1604c", 2026);

        report.Sections.Should().HaveCount(3);
        var section = report.Sections.Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        row.Cells[columns.IndexOf("Tax withheld, January to November")].Should().Be("2000.00");
        row.Cells[columns.IndexOf("Tax withheld, December")].Should().Be("500.00");
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_ExcludesNonPaidAndOtherYearRunsFromTheWithheldSplit()
    {
        // GetPaidRunsInYearAsync's name promises Paid runs in the year, but nothing stops a future
        // change to that query from widening it - the same reason Bir2316Service.BuildAsync
        // re-applies this predicate for Item 25A. This split must not silently disagree with that
        // figure by counting a run Item 25A itself would not count.
        var employeeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = employeeId, LastName = "Cruz", FirstName = "Juan",
            DateOfBirth = new DateOnly(1990, 1, 1), HireDate = new DateOnly(2020, 1, 6)
        };
        var form = new Bir2316Dto
        {
            EmployeeId = employeeId, EmployeeLastName = "Cruz", EmployeeFirstName = "Juan",
            EmployeeTin = "111-222-333-000"
        };
        _bir2316.Setup(b => b.BuildAllAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([form]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([employee]);

        var notPaid = Run(new(2026, 3, 20), new(2026, 3, 20), Entry(employee, tax: 9_000m));
        notPaid.Status = PayrollRunStatus.Draft;
        var decemberLastYear = Run(new(2025, 12, 20), new(2025, 12, 20), Entry(employee, tax: 8_000m));

        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 20), new(2026, 3, 20), Entry(employee, tax: 2_000m)),
            notPaid,
            decemberLastYear
        ]);
        _runs.Setup(r => r.CountUnpaidRunsPaidInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var report = await _sut.BuildAnnualAsync("1604c", 2026);

        var section = report.Sections.Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        row.Cells[columns.IndexOf("Tax withheld, January to November")].Should().Be("2000.00");
        row.Cells[columns.IndexOf("Tax withheld, December")].Should().Be("0.00");
    }

    [Fact]
    public async Task BuildAnnualAsync_AnUnknownReport_IsNotFound()
    {
        var act = () => _sut.BuildAnnualAsync("sss", 2026);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task BuildAnnualAsync_AYearThatHasNotStartedYet_IsRefused()
    {
        var act = () => _sut.BuildAnnualAsync("1604c", 2027);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_FromTheReal2316_AgreesWithTheYearsMonthly1601C()
    {
        // Two months of pay, each 50,000 basic + 1,000 non-taxable allowance, with 1,600 of
        // employee contributions (875 + 625 + 100). Each month's 1601-C: compensation 51,000,
        // non-taxable 2,600, taxable 48,400. The 1604-C, built from the real 2316s, has to certify
        // the same year - contributions non-taxable once, not also inside the taxable basic.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        juan.HireDate = new DateOnly(2020, 1, 6);
        var march = Run(new(2026, 3, 31), new(2026, 3, 31),
            Entry(juan, regularPay: 50_000m, sssEe: 875m, phEe: 625m, piEe: 100m, nonTaxAllow: 1_000m, tax: 5_000m));
        var april = Run(new(2026, 4, 5), new(2026, 4, 5),
            Entry(juan, regularPay: 50_000m, sssEe: 875m, phEe: 625m, piEe: 100m, nonTaxAllow: 1_000m, tax: 5_000m));
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([march]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([april]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([march, april]);
        _runs.Setup(r => r.GetEmployeeIdsWithPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([juan.Id]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([juan]);
        var inputs = new Mock<IBir2316InputsRepository>();
        inputs.Setup(i => i.GetForYearAsync(2026, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>());
        var bir2316 = new PeopleCore.Application.Payroll.Services.Bir2316Service(
            _runs.Object, _employees.Object, _companies.Object, inputs.Object);
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)), bir2316, _employees.Object);

        decimal monthlyTaxable = 0m, monthlyCompensation = 0m;
        foreach (var month in new[] { 3, 4 })
        {
            var summary = (await sut.BuildAsync("1601c", 2026, month)).Summary;
            monthlyTaxable += summary.Single(l => l.Label == "Total taxable compensation").Amount;
            monthlyCompensation += summary.Single(l => l.Label == "Total amount of compensation").Amount;
        }
        monthlyTaxable.Should().Be(96_800m);          // 2 x 48,400
        monthlyCompensation.Should().Be(102_000m);    // 2 x 51,000

        var section = (await sut.BuildAnnualAsync("1604c", 2026)).Sections
            .Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        string Cell(string column) => row.Cells[columns.IndexOf(column)];

        Cell("Gross compensation").Should().Be(GovernmentReportMath.Money(monthlyCompensation));
        Cell("SSS, PhilHealth and Pag-IBIG employee shares").Should().Be("3200.00");
        Cell("Total non-taxable").Should().Be("5200.00");                 // 3,200 shares + 2,000 allowances
        Cell("Basic salary").Should().Be("96800.00");                     // 2 x (50,000 - 1,600)
        Cell("Total taxable (present employer)").Should().Be(GovernmentReportMath.Money(monthlyTaxable));
        // Gross = non-taxable + taxable: 5,200 + 96,800 = 102,000.
    }

    /// <summary>
    /// The 1604-C over the real 2316, in January 2027, the 2316 reading <paramref name="balances"/>;
    /// <paramref name="runs"/> are 2026's Paid runs, all Juan's.
    /// </summary>
    private async Task<Func<string, string>> Juans1604CRowWithOpeningBalances(Employee juan, PayrollRun[] runs,
        params PayrollOpeningBalance[] balances)
    {
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(runs);
        _runs.Setup(r => r.GetEmployeeIdsWithPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()))
             .ReturnsAsync(runs.Length == 0 ? [] : [juan.Id]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([juan]);
        var inputs = new Mock<IBir2316InputsRepository>();
        inputs.Setup(i => i.GetForYearAsync(2026, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>());
        var bir2316 = new PeopleCore.Application.Payroll.Services.Bir2316Service(
            _runs.Object, _employees.Object, _companies.Object, inputs.Object, OpeningBalanceFakes.Holding(balances).Object);
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero)), bir2316, _employees.Object);

        var section = (await sut.BuildAnnualAsync("1604c", 2026)).Sections
            .Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        return column => row.Cells[columns.IndexOf(column)];
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_FollowsThe2316WithAnOpeningBalance()
    {
        // January to March before PeopleCore: 200,000 basic, 1,000 de minimis, 4,800 of
        // contributions, 6,000 withheld. Then April and December runs, each 50,000 basic with
        // 1,600 of contributions (875 + 625 + 100) and 2,500 withheld.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        juan.HireDate = new DateOnly(2020, 1, 6);
        var april = Run(new(2026, 4, 30), new(2026, 4, 30),
            Entry(juan, regularPay: 50_000m, sssEe: 875m, phEe: 625m, piEe: 100m, tax: 2_500m));
        var december = Run(new(2026, 12, 18), new(2026, 12, 18),
            Entry(juan, regularPay: 50_000m, sssEe: 875m, phEe: 625m, piEe: 100m, tax: 2_500m));

        var cell = await Juans1604CRowWithOpeningBalances(juan, [april, december],
            OpeningBalanceFakes.OpeningBalance(juan.Id, basicSalary: 200_000m, deMinimis: 1_000m,
                employeeContributions: 4_800m, taxWithheld: 6_000m, throughDate: new DateOnly(2026, 3, 31)));

        cell("Gross compensation").Should().Be("301000.00");               // 200,000 + 1,000 + 2 x 50,000
        cell("De minimis").Should().Be("1000.00");
        cell("SSS, PhilHealth and Pag-IBIG employee shares").Should().Be("8000.00");   // 4,800 + 2 x 1,600
        cell("Total non-taxable").Should().Be("9000.00");
        cell("Basic salary").Should().Be("292000.00");                     // (200,000 - 4,800) + 2 x 48,400
        cell("Total taxable (present employer)").Should().Be("292000.00");
        cell("Tax due").Should().Be("6300.00");                            // (292,000 - 250,000) x 15%
        // The balance's tax was withheld January to March: 6,000 + April's 2,500.
        cell("Tax withheld, January to November").Should().Be("8500.00");
        cell("Tax withheld, December").Should().Be("2500.00");
        cell("Total tax withheld").Should().Be("11000.00");                // the 2316's Item 26
        cell("To collect / (refund)").Should().Be("-4700.00");             // 6,300 - 11,000
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_IncludesSomeoneWithAnOpeningBalanceButNoPaidRun()
    {
        // Paid January to March before PeopleCore and not on any PeopleCore run in 2026: 200,000
        // basic, 4,800 of contributions, 6,000 withheld.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        juan.HireDate = new DateOnly(2020, 1, 6);

        var cell = await Juans1604CRowWithOpeningBalances(juan, [],
            OpeningBalanceFakes.OpeningBalance(juan.Id, basicSalary: 200_000m, employeeContributions: 4_800m,
                taxWithheld: 6_000m, throughDate: new DateOnly(2026, 3, 31)));

        cell("Gross compensation").Should().Be("200000.00");
        cell("Basic salary").Should().Be("195200.00");                     // 200,000 - 4,800
        cell("Tax withheld, January to November").Should().Be("6000.00");
        cell("Tax withheld, December").Should().Be("0.00");
        cell("Total tax withheld").Should().Be("6000.00");
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_ABalanceThroughDecember_StillCountsItsTaxInJanuaryToNovember()
    {
        // Go-live in late December: the balance runs through December 10 with 30,000 withheld;
        // PeopleCore paid only December 18's run, with 2,500 withheld. The balance is months of
        // withholding, nearly all before December, so it goes in January to November.
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        juan.HireDate = new DateOnly(2020, 1, 6);
        var december = Run(new(2026, 12, 18), new(2026, 12, 18), Entry(juan, regularPay: 50_000m, tax: 2_500m));

        var cell = await Juans1604CRowWithOpeningBalances(juan, [december],
            OpeningBalanceFakes.OpeningBalance(juan.Id, basicSalary: 550_000m, taxWithheld: 30_000m,
                throughDate: new DateOnly(2026, 12, 10)));

        cell("Tax withheld, January to November").Should().Be("30000.00");
        cell("Tax withheld, December").Should().Be("2500.00");
        cell("Total tax withheld").Should().Be("32500.00");                // 30,000 + 2,500
    }

    [Fact]
    public async Task BuildAnnualAsync_1604C_TakesTheBalancesTaxFromThe2316sOwnFigure()
    {
        // A form carrying 6,000 withheld before PeopleCore; March's run withheld 2,000 and
        // December's 500. January to November = 2,000 + 6,000 = 8,000, read from the form's
        // OpeningBalanceTaxWithheld, not worked back from Item 25A (left at 0 here, so a residual
        // would give 2,000 + (0 - 2,500) = -500).
        var employeeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = employeeId, LastName = "Cruz", FirstName = "Juan",
            DateOfBirth = new DateOnly(1990, 1, 1), HireDate = new DateOnly(2020, 1, 6)
        };
        var form = new Bir2316Dto
        {
            EmployeeId = employeeId, EmployeeLastName = "Cruz", EmployeeFirstName = "Juan",
            EmployeeTin = "111-222-333-000",
            OpeningBalanceThrough = new DateOnly(2026, 2, 28), OpeningBalanceTaxWithheld = 6_000m
        };
        _bir2316.Setup(b => b.BuildAllAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([form]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([employee]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 20), new(2026, 3, 20), Entry(employee, tax: 2_000m)),
            Run(new(2026, 12, 18), new(2026, 12, 18), Entry(employee, tax: 500m))
        ]);

        var report = await _sut.BuildAnnualAsync("1604c", 2026);

        var section = report.Sections.Single(s => s.Title == "Employed as of December 31, no previous employer");
        var row = section.Rows.Should().ContainSingle().Subject;
        var columns = section.Columns.ToList();
        row.Cells[columns.IndexOf("Tax withheld, January to November")].Should().Be("8000.00");
        row.Cells[columns.IndexOf("Tax withheld, December")].Should().Be("500.00");
    }
}
