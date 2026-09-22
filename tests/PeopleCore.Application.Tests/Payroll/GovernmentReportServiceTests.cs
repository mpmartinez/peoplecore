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
        decimal thirteenth = 0, decimal tax = 0, decimal phEe = 0, decimal piEe = 0, decimal nonTaxAllow = 0)
        => new() { EmployeeId = e.Id, Employee = e, SSSEmployee = sssEe, SSSEmployer = sssEr, RegularPay = regularPay,
                   ThirteenthMonth = thirteenth, WithholdingTax = tax, PhilHealthEmployee = phEe, PagIbigEmployee = piEe,
                   NonTaxableAllowances = nonTaxAllow };

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
        report.Summary.Should().ContainInOrder(
            new GovernmentReportLineDto("Total amount of compensation", 151_000m),
            new GovernmentReportLineDto("13th month pay and other benefits", 90_000m),
            new GovernmentReportLineDto("SSS, PhilHealth and Pag-IBIG employee shares", 1_600m),
            new GovernmentReportLineDto("Other non-taxable compensation", 1_000m),
            new GovernmentReportLineDto("Total non-taxable compensation", 92_600m),
            new GovernmentReportLineDto("Total taxable compensation", 58_400m),
            new GovernmentReportLineDto("Total taxes withheld", 9_000m));
        report.Rows.Single().Cells.Should().Equal(
            "Cruz, Juan", "111-222-333-000", "151000.00", "90000.00", "1600.00", "1000.00", "58400.00", "9000.00");
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
}
