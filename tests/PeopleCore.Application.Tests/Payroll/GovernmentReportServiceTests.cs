using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.Interfaces;
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
        _sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)));
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
    {
        var run = new PayrollRun { RunNumber = "PAY", PeriodStart = periodStart, PeriodEnd = periodEnd,
                                   PayDate = payDate, Status = PayrollRunStatus.Paid };
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
            RunForPeriod(new(2026, 3, 1), new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 1_000m, sssEr: 2_030m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Should().ContainSingle().Which.Cells.Should().Equal(
            "Cruz, Juan", "34-1234567-8", "20000.00", "1000.00", "2000.00", "30.00", "2030.00", "3030.00");
        report.Warnings.Should().NotContain(w => w.Contains("whole month"));
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
        report.Rows.Single().Cells[1].Should().Be("111-222-333-000");
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
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, clock);

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 30_000m));
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
    public async Task AMonthThatIsInvalidOrStillAhead_IsRefused(int year, int month)
    {
        var act = () => _sut.BuildAsync("sss", year, month);

        await act.Should().ThrowAsync<DomainException>();
    }
}
