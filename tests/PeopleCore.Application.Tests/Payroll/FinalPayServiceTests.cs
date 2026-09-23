using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// FinalPayService: creating, updating and recomputing a single-employee final-pay run from a
/// separation. The payroll engine and the 2316 are the real ones (only their repositories are
/// mocked), so the figures below are what the whole chain produces, not what a mock was told.
/// <para>
/// The default world is the plan's worked example: 36,500 a month (daily rate 1,200.00 under the
/// 365 factor), hired 2021-03-01, made redundant with a last working day of Friday 2026-03-13,
/// one Paid regular run for February (RegularPay 36,500, tax withheld 2,000), 5 days of
/// convertible vacation leave, one loan with 3,000 left, paid on 2026-03-31. No payroll settings
/// are saved, so the daily-rate factor is the default 365 - rest days are paid, and the final
/// period's salary days are its calendar days. No shift is assigned and there is no attendance.
/// </para>
/// </summary>
public class FinalPayServiceTests
{
    private static readonly DateOnly LastDay = new(2026, 3, 13);   // a Friday
    private static readonly DateOnly PayDate = new(2026, 3, 31);

    private readonly Mock<ISeparationRepository> _separations = new();
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly Mock<IEmployeeCompensationRepository> _compensations = new();
    private readonly Mock<IEmployeeLoanRepository> _loans = new();
    private readonly Mock<IEmployeeAllowanceRepository> _allowances = new();
    private readonly List<EmployeeAllowance> _allowanceList = [];
    private readonly Mock<ILeaveBalanceRepository> _leaveBalances = new();
    private readonly Mock<ILeaveTypeRepository> _leaveTypes = new();
    // Leave types with no balance for the employee; the balances' own types count too.
    private readonly List<LeaveType> _otherLeaveTypes = [];
    private readonly Mock<IShiftService> _shifts = new();
    private readonly Mock<IPayrollAttendanceBridge> _attendance = new();
    private readonly Mock<IPayrollSettingsRepository> _settings = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IBir2316InputsRepository> _bir2316Inputs = new();

    private readonly Employee _employee;
    private readonly Separation _separation;
    private readonly EmployeeCompensation _compensation;
    private readonly PayrollRun _februaryRun;
    private readonly List<PayrollRun> _paidRuns;
    private readonly List<EmployeeLoan> _activeLoans;
    private readonly List<LeaveBalance> _balances;
    private PayrollRun? _savedRun;

    private readonly FinalPayService _sut;

    public FinalPayServiceTests()
    {
        _employee = new Employee
        {
            FirstName = "Maria",
            LastName = "Santos",
            EmployeeNumber = "E-001",
            HireDate = new DateOnly(2021, 3, 1),
            DateOfBirth = new DateOnly(1985, 6, 15),
            Is13thMonthEligible = true,
        };

        _separation = new Separation
        {
            EmployeeId = _employee.Id,
            Employee = _employee,
            Type = SeparationType.AuthorizedCause,
            AuthorizedCause = AuthorizedCause.Redundancy,
            NoticeDate = LastDay.AddDays(-30),
            LastWorkingDay = LastDay,
            Status = SeparationStatus.Separated,
            RecordedBy = "hr@company.test",
            ClearanceItems =
            [
                new SeparationClearanceItem { Name = "IT equipment", SortOrder = 1, ClearedAt = DateTime.UtcNow, ClearedBy = "it" },
                new SeparationClearanceItem { Name = "Finance", SortOrder = 2 },
                new SeparationClearanceItem { Name = "HR exit interview", SortOrder = 3 },
            ],
        };

        _compensation = new EmployeeCompensation
        {
            EmployeeId = _employee.Id,
            BasicSalary = 36_500m,
            PayFrequency = PayFrequency.Monthly,
        };

        _februaryRun = new PayrollRun
        {
            RunNumber = "PAY-2026-002",
            PeriodStart = new DateOnly(2026, 2, 1),
            PeriodEnd = new DateOnly(2026, 2, 28),
            PayDate = new DateOnly(2026, 2, 28),
            Frequency = PayFrequency.Monthly,
            Status = PayrollRunStatus.Paid,
        };
        _februaryRun.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = _februaryRun.Id,
            EmployeeId = _employee.Id,
            RegularPay = 36_500m,
            WithholdingTax = 2_000m,
        });
        _paidRuns = [_februaryRun];

        _activeLoans =
        [
            new EmployeeLoan
            {
                EmployeeId = _employee.Id,
                LoanType = LoanType.SSSLoan,
                TotalAmount = 12_000m,
                MonthlyDeduction = 1_000m,
                RemainingBalance = 3_000m,
                StartDate = new DateOnly(2025, 6, 1),
                IsActive = true,
            },
        ];

        _balances =
        [
            Balance("Vacation Leave", totalDays: 5m, convertible: true, countsAsVacation: true),
            // Not convertible: must not be paid out.
            Balance("Sick Leave", totalDays: 7m, convertible: false, countsAsVacation: false),
        ];

        _separations.Setup(r => r.GetAsync(_separation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_separation);
        _compensations.Setup(r => r.GetByEmployeeIdAsync(_employee.Id, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(() => _compensation);
        _loans.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => _activeLoans.Where(l => l.IsActive).ToList());
        _loans.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                  _activeLoans.Where(l => ids.Contains(l.Id)).ToList());
        _allowances.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() => _allowanceList);
        _leaveBalances.Setup(r => r.GetByEmployeeAsync(_employee.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(() => _balances);
        _leaveTypes.Setup(r => r.CountAsync(It.IsAny<System.Linq.Expressions.Expression<Func<LeaveType, bool>>?>(),
                                            It.IsAny<CancellationToken>()))
                   .ReturnsAsync((System.Linq.Expressions.Expression<Func<LeaveType, bool>>? predicate, CancellationToken _) =>
                       _balances.Select(b => b.LeaveType).Concat(_otherLeaveTypes)
                           .Count(predicate?.Compile() ?? (_ => true)));

        // No shift assigned anywhere: Monday to Friday.
        _shifts.Setup(s => s.ResolveShiftForDayAsync(_employee.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DailyScheduleDto?)null);

        // No attendance: the bridge derives nothing for the employee.
        _attendance.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((IReadOnlyList<Guid> ids, DateOnly _, DateOnly _, CancellationToken _) =>
                       new AttendanceBridgeResult(ids.ToDictionary(id => id, _ => new PayrollAttendanceInput()), []));

        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync(() => _paidRuns);
        // As the repository does: every Paid run whose period ends in the month.
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((int year, int month, CancellationToken _) => _paidRuns
                 .Where(r => r.Status == PayrollRunStatus.Paid && r.PeriodEnd.Year == year && r.PeriodEnd.Month == month)
                 .OrderBy(r => r.PeriodEnd)
                 .ToList());
        _runs.Setup(r => r.GetPaidRunsForEmployeeInYearAsync(_employee.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Guid _, int year, CancellationToken _) =>
                 _paidRuns.Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year).ToList());
        _runs.Setup(r => r.GetLastFinalPaySequenceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _runs.Setup(r => r.AddFinalPayRunAsync(It.IsAny<PayrollRun>(), It.IsAny<Separation>(), It.IsAny<CancellationToken>()))
             .Callback((PayrollRun run, Separation _, CancellationToken _) => _savedRun = run)
             .Returns(Task.CompletedTask);
        _runs.Setup(r => r.GetWithEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Guid id, CancellationToken _) => _savedRun?.Id == id ? _savedRun : null);

        _employees.Setup(r => r.GetByIdAsync(_employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_employee);
        _companies.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new Company { Name = "PeopleCore Inc." });

        var bir2316 = new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object);

        _sut = new FinalPayService(
            _separations.Object, _runs.Object, _compensations.Object, _loans.Object, _allowances.Object,
            _leaveBalances.Object, _leaveTypes.Object,
            _shifts.Object, _attendance.Object, _settings.Object, bir2316, new PayrollComputationService(),
            TimeProvider.System);
    }

    private LeaveBalance Balance(string name, decimal totalDays, bool convertible, bool countsAsVacation,
        decimal usedDays = 0m) => new()
    {
        EmployeeId = _employee.Id,
        Year = 2026,
        TotalDays = totalDays,
        UsedDays = usedDays,
        LeaveType = new LeaveType
        {
            Name = name,
            Code = name[..2].ToUpperInvariant(),
            IsConvertibleToCash = convertible,
            CountsAsVacationForDeMinimis = countsAsVacation,
        },
    };

    private static FinalPayRequest Request(
        DateOnly? payDate = null,
        DateOnly? periodStart = null,
        decimal? separationPayOverride = null,
        decimal? retirementPayOverride = null,
        string? overrideNote = null,
        IReadOnlyList<FinalPayDeductionDto>? deductions = null)
        => new(payDate ?? PayDate, periodStart, separationPayOverride, retirementPayOverride, overrideNote,
               deductions ?? []);

    private PayrollRunEmployee SavedEntry => _savedRun!.Employees.Single();

    // ------------------------------------------------------------------
    // The worked example
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WorkedExample_ProducesTheFiguresWorkedOutByHand()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request());

        var entry = SavedEntry;

        // Daily rate = 36,500 x 12 / 365 = 1,200.00.
        entry.DailyRate.Should().Be(1_200m);

        // Period: the day after February's Paid regular run ended (2026-02-28) is 2026-03-01, a
        // Sunday, to the last working day, Friday 2026-03-13. The 365 factor pays rest days, so
        // every calendar day of the period is a salary day: Mar 1-13 = 13.
        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 1));
        summary.PeriodEnd.Should().Be(LastDay);
        summary.WorkingDays.Should().Be(13m);
        summary.NoSalaryDays.Should().BeFalse();
        _savedRun!.FinalPayInputs!.WorkingDays.Should().Be(13m);

        // RegularPay = daily rate x salary days = 1,200 x 13 = 15,600.00 (no attendance, so no
        // absence or tardiness comes off).
        entry.RegularPay.Should().Be(15_600m);

        // 13th month = (basic earned earlier in the year + this period's basic) / 12
        //            = (36,500 + 15,600) / 12 = 52,100 / 12 = 4,341.666... -> 4,341.67
        // Nothing was paid as 13th month earlier in 2026, so all of it is due now.
        entry.ThirteenthMonth.Should().Be(4_341.67m);

        // Leave: 5 days of vacation leave, convertible, x 1,200 = 6,000.00. All 5 days are within
        // the 10-day de minimis ceiling, so all 6,000 is non-taxable. Sick leave (7 days) is not
        // convertible and is not paid out.
        entry.LeaveConversionPay.Should().Be(6_000m);
        entry.LeaveConversionNonTaxable.Should().Be(6_000m);

        // Service years: 2021-03-01 to 2026-03-13 is 5 years and 12 days -> 5.
        // Redundancy pays one month per year of service: 36,500 x 5 = 182,500.00, non-taxable.
        summary.ServiceYears.Should().Be(5);
        entry.SeparationPay.Should().Be(182_500m);
        entry.RetirementPay.Should().Be(0m);
        entry.FinalPayNonTaxable.Should().Be(188_500m);   // 6,000 leave + 182,500 separation

        // Statutory deductions on the 36,500 monthly basic (Monthly frequency, so not halved):
        //   SSS: 34,750 and over -> MSC 35,000 -> employee 1,750.00
        //   PhilHealth: 36,500 x 5% / 2 = 912.50
        //   Pag-IBIG: min(36,500, 10,000) x 2% = 200.00
        entry.SSSEmployee.Should().Be(1_750m);
        entry.PhilHealthEmployee.Should().Be(912.50m);
        entry.PagIbigEmployee.Should().Be(200m);
        // No regular run was paid for March, so the final pay takes March's full month, the
        // employer's too: SSS 3,530 (3,500 + 30 EC), PhilHealth 912.50, Pag-IBIG 200.
        entry.SSSEmployer.Should().Be(3_530m);
        entry.PhilHealthEmployer.Should().Be(912.50m);
        entry.PagIbigEmployer.Should().Be(200m);

        // Tax, settled through the 2316:
        //   First compute (no override): withholding base = 15,600 + 0 taxable final pay
        //     - 1,750 - 912.50 - 200 = 12,737.50 a month -> 152,850 a year -> 0 tax; the 13th
        //     month (4,341.67) is inside the 90,000 exemption -> 0. So this entry withholds 0.
        //   2316 over February + this draft:
        //     Item 39 basic, net of contributions = 36,500 + (15,600 - 2,862.50) = 49,237.50;
        //     Item 48 taxable 13th month = 0;
        //     Item 51B taxable final pay = 6,000 + 182,500 - 188,500 = 0.
        //     Item 52 = Item 23 = 49,237.50 -> Item 24 tax due = 0 (under 250,000).
        //     Item 25A = 2,000 (February) + 0 (this entry); Item 25B = 0; Item 27 = 0.
        //   Settled = Item24 - (Item25A - this entry's 0) - Item25B - Item27
        //           = 0 - (2,000 - 0) - 0 - 0 = -2,000.00, a refund of February's over-withholding.
        entry.WithholdingTax.Should().Be(-2_000m);

        // Loan: statutory = 1,750 + 912.50 + 200 + (-2,000) = 862.50. The budget for loans is
        //   15,600 + 4,341.67 + 6,000 + 182,500 - 862.50 = 207,579.17, which covers the whole
        //   3,000 balance.
        entry.LoanDeductions.Should().Be(3_000m);
        entry.OtherDeductions.Should().Be(0m);

        // Gross = 15,600 + 4,341.67 + 6,000 + 182,500 = 208,441.67
        // Deductions = 1,750 + 912.50 + 200 + (-2,000) + 3,000 + 0 = 3,862.50
        // Net = 208,441.67 - 3,862.50 = 204,579.17
        entry.GrossPay.Should().Be(208_441.67m);
        entry.NetPay.Should().Be(204_579.17m);

        summary.GrossPay.Should().Be(208_441.67m);
        summary.WithholdingTax.Should().Be(-2_000m);
        summary.NetPay.Should().Be(204_579.17m);
        summary.LeaveConversionPay.Should().Be(6_000m);
        summary.LeaveConversionNonTaxable.Should().Be(6_000m);
        summary.DailyRate.Should().Be(1_200m);   // the rate the leave (and the salary days) were paid at
        summary.SeparationPay.Should().Be(182_500m);
        summary.ComputedSeparationOrRetirementPay.Should().Be(182_500m);
    }

    [Fact]
    public async Task CreateAsync_SettledTax_MakesTheYearsCertificateBalance()
    {
        await _sut.CreateAsync(_separation.Id, Request());

        // With the settled entry in, the 2316's tax due equals everything withheld.
        var bir2316 = new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object);
        var cert = await bir2316.BuildWithDraftEntryAsync(_employee.Id, 2026, _savedRun!, SavedEntry);

        cert!.Item24_TaxDue.Should().Be(cert.Item26_TotalTaxWithheld + cert.Item27_PeraTaxCredit);
    }

    [Fact]
    public async Task CreateAsync_SettledTax_TakesOffThePreviousEmployersTaxAndThePeraCredit()
    {
        // The saved 2316 inputs carry a previous employer's 100,000 taxable compensation with 500
        // withheld from it, and a 100 PERA credit. Taxable for the year is 49,237.50 + 100,000 =
        // 149,237.50, still under 250,000, so tax due stays 0 and the settlement is
        // 0 - (2,000 - 0) - 500 - 100 = -2,600.00.
        _bir2316Inputs.Setup(r => r.GetAsync(_employee.Id, 2026, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new Bir2316Inputs
                      {
                          EmployeeId = _employee.Id, Year = 2026,
                          Item22_PrevTaxableCompensation = 100_000m,
                          Item25B_PrevTaxWithheld = 500m, Item27_PeraTaxCredit = 100m,
                      });

        await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.WithholdingTax.Should().Be(-2_600m);
    }

    [Fact]
    public async Task CreateAsync_SettledTax_CollectsWhatTheYearStillOwes()
    {
        // A 150,000 monthly salary, and 300,000 paid earlier in the year with only 1,000 withheld.
        // Daily rate = 150,000 x 12 / 365 = 4,931.506... -> 4,931.51. The final period pays
        // 13 x 4,931.51 = 64,109.63 of basic, less the employee's contributions on a 150,000
        // salary: SSS 1,750 (the top bracket), PhilHealth 2,500 (the ceiling) and Pag-IBIG 200 =
        // 4,450, which aren't taxable. Separation pay and the 5 leave days are non-taxable too, and
        // the 13th month ((300,000 + 64,109.63) / 12 = 30,342.47) is inside the 90,000 exemption,
        // so taxable for the year = 300,000 + 64,109.63 - 4,450 = 359,659.63 and tax due =
        // (359,659.63 - 250,000) x 15% = 16,448.9445 -> 16,448.94. The settlement collects
        // 16,448.94 - 1,000 = 15,448.94.
        _compensation.BasicSalary = 150_000m;
        _februaryRun.Employees.Single().RegularPay = 300_000m;
        _februaryRun.Employees.Single().WithholdingTax = 1_000m;

        await _sut.CreateAsync(_separation.Id, Request());

        var bir2316 = new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object);
        var cert = await bir2316.BuildWithDraftEntryAsync(_employee.Id, 2026, _savedRun!, SavedEntry);

        (SavedEntry.SSSEmployee + SavedEntry.PhilHealthEmployee + SavedEntry.PagIbigEmployee).Should().Be(4_450m);
        cert!.Item23_GrossTaxable.Should().Be(359_659.63m);
        SavedEntry.WithholdingTax.Should().Be(15_448.94m);
        cert.Item24_TaxDue.Should().Be(16_448.94m);
        cert.Item24_TaxDue.Should().Be(cert.Item26_TotalTaxWithheld);
    }

    // ------------------------------------------------------------------
    // Create: the checks
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Throws_WhenTheSeparationDoesNotExist()
    {
        var act = () => _sut.CreateAsync(Guid.NewGuid(), Request());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_Refuses_WhenTheSeparationAlreadyHasAFinalPayRun()
    {
        var existing = new PayrollRun { RunNumber = "FP-2026-001", RunType = PayrollRunType.FinalPay };
        _separation.FinalPayRunId = existing.Id;
        _separation.FinalPayRun = existing;

        var act = () => _sut.CreateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos already has a final-pay run (FP-2026-001).");
        _runs.Verify(r => r.AddFinalPayRunAsync(It.IsAny<PayrollRun>(), It.IsAny<Separation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_Refuses_WhenTheEmployeeHasNoCompensationRecord()
    {
        _compensations.Setup(r => r.GetByEmployeeIdAsync(_employee.Id, It.IsAny<CancellationToken>()))
                      .ReturnsAsync((EmployeeCompensation?)null);

        var act = () => _sut.CreateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no compensation record.");
    }

    [Fact]
    public async Task CreateAsync_Refuses_WithoutAPayDate()
    {
        var act = () => _sut.CreateAsync(_separation.Id, Request(payDate: default(DateOnly)));

        await act.Should().ThrowAsync<DomainException>().WithMessage("The pay date is required.");
    }

    [Fact]
    public async Task CreateAsync_Refuses_APeriodStartAfterTheLastWorkingDay()
    {
        var act = () => _sut.CreateAsync(_separation.Id, Request(periodStart: LastDay.AddDays(1)));

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("The final pay period can't start after the last working day.");
    }

    [Theory]
    [InlineData(2026, 2, 20)]
    [InlineData(2026, 2, 28)]   // the paid run's last day
    [InlineData(2025, 12, 1)]   // long before it
    public async Task CreateAsync_Refuses_AStartHrPutsInsideAPaidRegularRun(int year, int month, int day)
    {
        // February (PAY-2026-002, Feb 1-28) is Paid: those days were paid already.
        var act = () => _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(year, month, day)));

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-002 already paid up to Feb 28, 2026; start final pay after that.");
    }

    [Fact]
    public async Task CreateAsync_TakesAStartTheDayAfterAPaidRegularRun()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 1)));

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 1));
        summary.NoSalaryDays.Should().BeFalse();
    }

    // PeriodStartIsDefault is inferred, not stored: the stored start against today's default
    // (the day after the last Paid regular run - Mar 1 here).

    [Fact]
    public async Task TheSummary_SaysTheStartIsTheDefault_WhenHrGaveNone()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.PeriodStartIsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task TheSummary_SaysTheStartIsNotTheDefault_WhenHrChoseAnother()
    {
        await _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 9)));

        var summary = await _sut.GetAsync(_separation.Id);

        summary!.PeriodStartIsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task TheSummary_TakesAStartHrTypedThatEqualsTheDefault_AsTheDefault()
    {
        // Indistinguishable once stored, and harmless: sending no start gives the same period.
        var summary = await _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 1)));

        summary.PeriodStartIsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task TheSummary_TakesANoSalaryStart_AsTheDefault()
    {
        // Stored as the last working day, which is not the default start (Apr 1) - but it is
        // what the default produces on the no-salary path.
        PaidThroughMarch();

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.NoSalaryDays.Should().BeTrue();
        summary.PeriodStartIsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_UsesTheStartHrGives()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 9)));

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 9));
        summary.WorkingDays.Should().Be(5m);   // Mar 9-13
        SavedEntry.RegularPay.Should().Be(6_000m);
    }

    [Fact]
    public async Task CreateAsync_DefaultPeriod_StartsTheDayAfterTheLastPaidRegularRun_IgnoringFinalPayRuns()
    {
        // A paid run of another type must not move the start. (An unpaid regular run can't be
        // there at all: any one the employee is in refuses the final pay.)
        var otherFinalPay = new PayrollRun
        {
            RunType = PayrollRunType.FinalPay,
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 10),
            PayDate = new DateOnly(2026, 3, 10), Status = PayrollRunStatus.Paid,
        };
        var january = new PayrollRun
        {
            PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31),
            PayDate = new DateOnly(2026, 1, 31), Status = PayrollRunStatus.Paid,
        };
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([otherFinalPay, _februaryRun, january]);

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public async Task CreateAsync_DefaultPeriod_StartsOnTheFirstOfTheLastWorkingDaysMonth_WhenNothingWasPaid()
    {
        _paidRuns.Clear();
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 1));
    }

    // ------------------------------------------------------------------
    // Nothing left to pay as salary, and unpaid regular runs
    // ------------------------------------------------------------------

    /// <summary>March paid in full as a regular run, through the 31st - past the last working day.</summary>
    private PayrollRun PaidThroughMarch()
    {
        var march = new PayrollRun
        {
            RunNumber = "PAY-2026-003",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PayDate = new DateOnly(2026, 3, 31),
            Frequency = PayFrequency.Monthly,
            Status = PayrollRunStatus.Paid,
        };
        march.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = march.Id, EmployeeId = _employee.Id, RegularPay = 36_500m,
            SSSEmployee = 1_750m, PhilHealthEmployee = 912.50m, PagIbigEmployee = 200m,
            SSSEmployer = 3_530m, PhilHealthEmployer = 912.50m, PagIbigEmployer = 200m,
        });
        _paidRuns.Add(march);
        return march;
    }

    [Fact]
    public async Task CreateAsync_WhenRegularPayrollAlreadyPaidPastTheLastWorkingDay_PaysNoSalary_AndTheRest()
    {
        // February and all of March are Paid regular runs, so the default start (Apr 1) falls
        // after the last working day (Mar 13). Nothing is left to pay as salary, but the final
        // pay still carries the 13th month, leave, separation pay, loans and the tax settle.
        PaidThroughMarch();

        var summary = await _sut.CreateAsync(_separation.Id, Request());
        var entry = SavedEntry;

        // Stored as the last working day alone, with no salary days - and no attendance, as
        // there's no salary for absences to come off.
        summary.PeriodStart.Should().Be(LastDay);
        summary.PeriodEnd.Should().Be(LastDay);
        summary.WorkingDays.Should().Be(0m);
        summary.NoSalaryDays.Should().BeTrue();
        _attendance.Verify(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                             It.IsAny<CancellationToken>()), Times.Never);
        entry.RegularPay.Should().Be(0m);

        // 13th month = (36,500 Feb + 36,500 Mar + 0) / 12 = 73,000 / 12 = 6,083.33.
        entry.ThirteenthMonth.Should().Be(6_083.33m);
        entry.LeaveConversionPay.Should().Be(6_000m);       // 5 x 1,200
        entry.SeparationPay.Should().Be(182_500m);          // 36,500 x 5 years
        entry.LoanDeductions.Should().Be(3_000m);

        // Contributions: March's run already took the full month (employee 2,862.50, employer
        // 4,642.50), so the final pay tops it up by nothing.
        entry.SSSEmployee.Should().Be(0m);
        entry.PhilHealthEmployee.Should().Be(0m);
        entry.PagIbigEmployee.Should().Be(0m);
        entry.SSSEmployer.Should().Be(0m);
        entry.PhilHealthEmployer.Should().Be(0m);
        entry.PagIbigEmployer.Should().Be(0m);

        // Tax: 2316 Item 23 = 36,500 + (36,500 - 2,862.50) + 0 = 70,137.50 -> 0 due.
        // Withheld elsewhere: February's 2,000. Settled = 0 - 2,000 = -2,000.
        entry.WithholdingTax.Should().Be(-2_000m);

        // Gross = 0 + 6,083.33 + 6,000 + 182,500 = 194,583.33
        // Deductions = 0 - 2,000 + 3,000 = 1,000; net = 193,583.33.
        entry.GrossPay.Should().Be(194_583.33m);
        entry.NetPay.Should().Be(193_583.33m);

        // A recompute reproduces it from what was stored.
        var recomputed = (await _sut.RecomputeAsync(_savedRun!)).Single();
        recomputed.Should().BeEquivalentTo(entry, o => o
            .Excluding(e => e.Id).Excluding(e => e.CreatedAt).Excluding(e => e.UpdatedAt)
            .Excluding(e => e.PayrollRun).Excluding(e => e.LoanDeductionLines).Excluding(e => e.PremiumDays));
    }

    [Fact]
    public async Task UpdateAsync_WithNoStart_KeepsTheNoSalaryPeriod()
    {
        PaidThroughMarch();
        await _sut.CreateAsync(_separation.Id, Request());
        _separation.FinalPayRun = _savedRun;

        var summary = await _sut.UpdateAsync(_separation.Id, Request(deductions: [new FinalPayDeductionDto("Cash advance", 500m)]));

        summary.PeriodStart.Should().Be(LastDay);
        summary.WorkingDays.Should().Be(0m);
        summary.NoSalaryDays.Should().BeTrue();
        _savedRun!.FinalPayInputs!.WorkingDays.Should().Be(0m);
    }

    [Fact]
    public async Task UpdateAsync_TheOverridesOfANoSalaryFinalPay_WithANullStart_StaysAtNoSalaryDays()
    {
        // The page sends a null start back for a final pay whose summary says NoSalaryDays.
        PaidThroughMarch();
        var created = await _sut.CreateAsync(_separation.Id, Request());
        created.NoSalaryDays.Should().BeTrue();
        _separation.FinalPayRun = _savedRun;

        var summary = await _sut.UpdateAsync(_separation.Id,
            Request(periodStart: null, separationPayOverride: 200_000m, overrideNote: "Per CBA"));

        summary.WorkingDays.Should().Be(0m);
        summary.NoSalaryDays.Should().BeTrue();
        summary.PeriodStart.Should().Be(LastDay);
        summary.SeparationPay.Should().Be(200_000m);
        _savedRun!.FinalPayInputs!.WorkingDays.Should().Be(0m);
    }

    /// <summary>A semi-monthly March cutoff, Paid, with its half of the month's contributions.</summary>
    private PayrollRun PaidMarchCutoff(string runNumber, DateOnly start, DateOnly end)
    {
        // Half of March's month: employee SSS 1,750 / 2 = 875, PhilHealth 912.50 / 2 = 456.25,
        // Pag-IBIG 200 / 2 = 100 (1,431.25); employer 3,530 / 2 = 1,765, 456.25, 100 (2,321.25).
        var cutoff = new PayrollRun
        {
            RunNumber = runNumber, PeriodStart = start, PeriodEnd = end, PayDate = end.AddDays(5),
            Frequency = PayFrequency.SemiMonthly, Status = PayrollRunStatus.Paid,
        };
        cutoff.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = cutoff.Id, EmployeeId = _employee.Id, Employee = _employee, RegularPay = 18_250m,
            SSSEmployee = 875m, PhilHealthEmployee = 456.25m, PagIbigEmployee = 100m,
            SSSEmployer = 1_765m, PhilHealthEmployer = 456.25m, PagIbigEmployer = 100m,
        });
        _paidRuns.Add(cutoff);
        return cutoff;
    }

    [Fact]
    public async Task CreateAsync_ASemiMonthlyEmployeeLeavingOnThe15th_WithTheFirstCutoffPaid_TakesTheMonthsOtherHalf()
    {
        // Leaves on Sunday 2026-03-15 with the Mar 1-15 cutoff Paid: no salary left, and the
        // cutoff took half of March's contributions.
        var lastDay = new DateOnly(2026, 3, 15);
        _separation.LastWorkingDay = lastDay;
        _compensation.PayFrequency = PayFrequency.SemiMonthly;
        PaidMarchCutoff("PAY-2026-005", new DateOnly(2026, 3, 1), lastDay);

        var summary = await _sut.CreateAsync(_separation.Id, Request());
        var entry = SavedEntry;

        summary.WorkingDays.Should().Be(0m);
        entry.RegularPay.Should().Be(0m);

        // The final pay tops March up to one month: employee 2,862.50 - 1,431.25 = 1,431.25
        // (SSS 1,750 - 875, PhilHealth 912.50 - 456.25, Pag-IBIG 200 - 100); employer
        // 4,642.50 - 2,321.25 = 2,321.25 (3,530 - 1,765, 912.50 - 456.25, 200 - 100).
        entry.SSSEmployee.Should().Be(875m);
        entry.PhilHealthEmployee.Should().Be(456.25m);
        entry.PagIbigEmployee.Should().Be(100m);
        entry.SSSEmployer.Should().Be(1_765m);
        entry.PhilHealthEmployer.Should().Be(456.25m);
        entry.PagIbigEmployer.Should().Be(100m);

        // A recompute reproduces it.
        var recomputed = (await _sut.RecomputeAsync(_savedRun!)).Single();
        (recomputed.SSSEmployee, recomputed.PhilHealthEmployee, recomputed.PagIbigEmployee,
         recomputed.SSSEmployer, recomputed.PhilHealthEmployer, recomputed.PagIbigEmployer)
            .Should().Be((875m, 456.25m, 100m, 1_765m, 456.25m, 100m));

        // The 2316 over February (no contributions on it here), the cutoff and the final pay:
        // Item 36 = 1,431.25 + 1,431.25 = 2,862.50, one month; Item 39 = 36,500 + (18,250 -
        // 1,431.25) + (0 - 1,431.25) = 51,887.50.
        var bir2316 = new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object);
        var cert = (await bir2316.BuildWithDraftEntryAsync(_employee.Id, 2026, _savedRun!, entry))!;
        cert.Item36_SssPhicPagibigContributions.Should().Be(2_862.50m);
        cert.Item39_BasicSalary.Should().Be(51_887.50m);

        // Once paid, March's remittance reports show exactly one month.
        _savedRun!.Status = PayrollRunStatus.Paid;
        entry.Employee = _employee;
        _paidRuns.Add(_savedRun);
        var reports = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, new AprilClock(),
                                                  Mock.Of<IBir2316Service>(), _employees.Object);

        var sss = await reports.BuildAsync("sss", 2026, 3);
        var sssRow = sss.Rows.Should().ContainSingle().Subject;
        sssRow.Cells[sss.Columns.ToList().IndexOf("MSC")].Should().Be("35000.00");
        sssRow.Cells[sss.Columns.ToList().IndexOf("Employee share")].Should().Be("1750.00");
        sssRow.Cells[sss.Columns.ToList().IndexOf("Employer total")].Should().Be("3530.00");

        var philHealth = await reports.BuildAsync("philhealth", 2026, 3);
        philHealth.Rows.Single().Cells.Skip(2).Should().Equal("912.50", "912.50", "1825.00");

        var pagIbig = await reports.BuildAsync("pagibig", 2026, 3);
        pagIbig.Rows.Single().Cells.Skip(5).Should().Equal("200.00", "200.00", "400.00");
    }

    [Fact]
    public async Task CreateAsync_ASemiMonthlyEmployee_WithBothMarchCutoffsPaid_TakesNoContributions()
    {
        // Leaves on Friday 2026-03-27; both March cutoffs are Paid, together a full month.
        _separation.LastWorkingDay = new DateOnly(2026, 3, 27);
        _compensation.PayFrequency = PayFrequency.SemiMonthly;
        PaidMarchCutoff("PAY-2026-005", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15));
        PaidMarchCutoff("PAY-2026-006", new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 31));

        await _sut.CreateAsync(_separation.Id, Request());
        var entry = SavedEntry;

        _savedRun!.FinalPayInputs!.WorkingDays.Should().Be(0m);
        // 2,862.50 - (1,431.25 x 2) = 0 for the employee; 4,642.50 - (2,321.25 x 2) = 0 for the employer.
        (entry.SSSEmployee, entry.PhilHealthEmployee, entry.PagIbigEmployee,
         entry.SSSEmployer, entry.PhilHealthEmployer, entry.PagIbigEmployer)
            .Should().Be((0m, 0m, 0m, 0m, 0m, 0m));
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.Processing)]
    [InlineData(PayrollRunStatus.ForApproval)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task CreateAsync_Refuses_WhileAnUnpaidRegularRunCoversTheFinalPeriod(PayrollRunStatus status)
    {
        // Mar 1-15 includes the employee and overlaps the final period Mar 1-13: paying both would
        // pay those days twice.
        var cutoff = new PayrollRun
        {
            RunNumber = "PAY-2026-005",
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 15),
            PayDate = new DateOnly(2026, 3, 20), Status = status,
        };
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([cutoff, _februaryRun]);

        var act = () => _sut.CreateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-005 covers Mar 1 – Mar 15, 2026 and isn't paid yet; pay it before creating final pay.");
        _runs.Verify(r => r.AddFinalPayRunAsync(It.IsAny<PayrollRun>(), It.IsAny<Separation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_Refuses_APeriodAnUnpaidRegularRunCovers()
    {
        await _sut.CreateAsync(_separation.Id, Request());
        _separation.FinalPayRun = _savedRun;
        var cutoff = new PayrollRun
        {
            RunNumber = "PAY-2026-005",
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 15),
            PayDate = new DateOnly(2026, 3, 20), Status = PayrollRunStatus.Draft,
        };
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([cutoff, _februaryRun, _savedRun!]);

        var act = () => _sut.UpdateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 9)));

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-005 covers Mar 1 – Mar 15, 2026 and isn't paid yet; pay it before creating final pay.");
    }

    [Fact]
    public async Task CreateAsync_Refuses_WhileAnUnpaidRegularRunAfterTheLastWorkingDayIncludesTheEmployee()
    {
        // Mar 14-31 lies after the last working day, but paying it would pay salary after the
        // separation and take March's contributions a second time. The employee belongs off it,
        // not paid through it.
        var later = new PayrollRun
        {
            RunNumber = "PAY-2026-006",
            PeriodStart = new DateOnly(2026, 3, 14), PeriodEnd = new DateOnly(2026, 3, 31),
            PayDate = new DateOnly(2026, 3, 31), Status = PayrollRunStatus.Draft,
        };
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([later, _februaryRun]);

        var act = () => _sut.CreateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-006 covers Mar 14 – Mar 31, 2026 after Maria Santos's last working day; take them off it before creating final pay.");
    }

    [Fact]
    public async Task CreateAsync_Refuses_AStartHrPutsAfterAnUnpaidRunInTheMonth()
    {
        // Leaves Mar 27. HR starts the final pay on Mar 16, after a Draft Mar 1-15 cutoff: paying
        // both would take March's contributions one and a half times.
        _separation.LastWorkingDay = new DateOnly(2026, 3, 27);
        var cutoff = new PayrollRun
        {
            RunNumber = "PAY-2026-005",
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 15),
            PayDate = new DateOnly(2026, 3, 20), Status = PayrollRunStatus.Draft,
        };
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([cutoff, _februaryRun]);

        var act = () => _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 16)));

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-005 covers Mar 1 – Mar 15, 2026 and isn't paid yet; pay it before creating final pay.");
    }

    /// <summary>
    /// Scenario A: the last working day is Apr 10. Mar 1-15 is Paid and Mar 16-31 Approved but not
    /// paid; HR starts the final pay on Apr 1. The unpaid run ends before both the start and the
    /// separation month, but paid after the final pay it would fall outside the final pay's 13th
    /// month and tax settle, which take the final pay as the employee's last pay.
    /// </summary>
    private void AnApprovedMarchCutoffBeforeAnAprilSeparation()
    {
        _separation.LastWorkingDay = new DateOnly(2026, 4, 10);
        var firstHalf = new PayrollRun
        {
            RunNumber = "PAY-2026-005",
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 15),
            PayDate = new DateOnly(2026, 3, 15), Status = PayrollRunStatus.Paid,
        };
        var secondHalf = new PayrollRun
        {
            RunNumber = "PAY-2026-006",
            PeriodStart = new DateOnly(2026, 3, 16), PeriodEnd = new DateOnly(2026, 3, 31),
            PayDate = new DateOnly(2026, 3, 31), Status = PayrollRunStatus.Approved,
        };
        _paidRuns.AddRange([firstHalf, secondHalf]);
    }

    [Fact]
    public async Task CreateAsync_Refuses_WhileAnUnpaidRegularRunBeforeTheStartAndTheSeparationMonthIncludesTheEmployee()
    {
        AnApprovedMarchCutoffBeforeAnAprilSeparation();

        var act = () => _sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2026, 4, 15),
                                                                 periodStart: new DateOnly(2026, 4, 1)));

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-006 covers Mar 16 – Mar 31, 2026 and isn't paid yet; pay it before creating final pay.");
        _runs.Verify(r => r.AddFinalPayRunAsync(It.IsAny<PayrollRun>(), It.IsAny<Separation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_Refuses_WhileAnUnpaidRegularRunBeforeTheStartAndTheSeparationMonthIncludesTheEmployee()
    {
        _separation.LastWorkingDay = new DateOnly(2026, 4, 10);
        await _sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2026, 4, 15)));
        _separation.FinalPayRun = _savedRun;
        _paidRuns.Add(_savedRun!);
        AnApprovedMarchCutoffBeforeAnAprilSeparation();

        var act = () => _sut.UpdateAsync(_separation.Id, Request(payDate: new DateOnly(2026, 4, 15),
                                                                 periodStart: new DateOnly(2026, 4, 1)));

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-006 covers Mar 16 – Mar 31, 2026 and isn't paid yet; pay it before creating final pay.");
    }

    [Fact]
    public async Task UpdateAsync_Refuses_WhileAnUnpaidRegularRunAfterTheLastWorkingDayIncludesTheEmployee()
    {
        await _sut.CreateAsync(_separation.Id, Request());
        _separation.FinalPayRun = _savedRun;
        var later = new PayrollRun
        {
            RunNumber = "PAY-2026-006",
            PeriodStart = new DateOnly(2026, 3, 14), PeriodEnd = new DateOnly(2026, 3, 31),
            PayDate = new DateOnly(2026, 3, 31), Status = PayrollRunStatus.ForApproval,
        };
        _runs.Setup(r => r.GetRunsForEmployeeAsync(_employee.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync([later, _februaryRun, _savedRun!]);

        var act = () => _sut.UpdateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Payroll PAY-2026-006 covers Mar 14 – Mar 31, 2026 after Maria Santos's last working day; take them off it before creating final pay.");
    }

    [Fact]
    public async Task CreateAsync_Refuses_AStartHrPutsBeforeTheHireDate()
    {
        // Hired Thursday 2026-03-05 and never paid: a start of Mar 2 would pay days before they
        // were employed.
        _employee.HireDate = new DateOnly(2026, 3, 5);
        _paidRuns.Clear();

        var act = () => _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 2)));

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos was hired on Mar 5, 2026; start final pay on or after that.");
    }

    [Fact]
    public async Task CreateAsync_TakesAStartHrPutsOnTheHireDate()
    {
        _employee.HireDate = new DateOnly(2026, 3, 5);
        _paidRuns.Clear();

        var summary = await _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 5)));

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 5));
    }

    [Fact]
    public async Task CreateAsync_DefaultPeriod_StartsOnTheHireDate_ForSomeoneHiredThatMonthAndNeverPaid()
    {
        // Hired Thursday 2026-03-05, never paid, last working day Mar 13: the period can't start
        // on Mar 1, before they were employed. Mar 5-13 = 9 calendar days x 1,200 = 10,800.
        _employee.HireDate = new DateOnly(2026, 3, 5);
        _paidRuns.Clear();

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 5));
        summary.WorkingDays.Should().Be(9m);
        SavedEntry.RegularPay.Should().Be(10_800m);
    }

    [Fact]
    public async Task CreateAsync_Refuses_AnOverrideWithoutANote()
    {
        var act = () => _sut.CreateAsync(_separation.Id, Request(separationPayOverride: 200_000m, overrideNote: "  "));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Explain the separation or retirement pay override.");
    }

    [Fact]
    public async Task CreateAsync_Refuses_ARetirementOverrideWithoutANote()
    {
        var act = () => _sut.CreateAsync(_separation.Id, Request(retirementPayOverride: 50_000m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Explain the separation or retirement pay override.");
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("   ", 100)]
    [InlineData("Unreturned laptop", 0)]
    [InlineData("Unreturned laptop", -5)]
    public async Task CreateAsync_Refuses_ADeductionWithoutALabelOrAPositiveAmount(string label, decimal amount)
    {
        var act = () => _sut.CreateAsync(_separation.Id, Request(deductions: [new FinalPayDeductionDto(label, amount)]));

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Each deduction needs a label and an amount above zero.");
    }

    // ------------------------------------------------------------------
    // The run
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SavesOneFinalPayRunForTheEmployee_LinkedToTheSeparation()
    {
        _compensation.PayFrequency = PayFrequency.SemiMonthly;
        _runs.Setup(r => r.GetLastFinalPaySequenceAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        _runs.Verify(r => r.AddFinalPayRunAsync(It.IsAny<PayrollRun>(), _separation, It.IsAny<CancellationToken>()), Times.Once);
        var run = _savedRun!;
        run.RunType.Should().Be(PayrollRunType.FinalPay);
        run.RunNumber.Should().Be("FP-2026-004");
        run.Frequency.Should().Be(PayFrequency.SemiMonthly);
        run.Status.Should().Be(PayrollRunStatus.Draft);
        run.PayDate.Should().Be(PayDate);
        run.PeriodEnd.Should().Be(LastDay);
        run.Employees.Should().ContainSingle().Which.EmployeeId.Should().Be(_employee.Id);
        SavedEntry.IncludeThirteenthMonth.Should().BeTrue();
        run.FinalPayInputs!.PayrollRunId.Should().Be(run.Id);
        run.FinalPayInputs.SeparationId.Should().Be(_separation.Id);
        _separation.FinalPayRunId.Should().Be(run.Id);

        summary.RunId.Should().Be(run.Id);
        summary.RunNumber.Should().Be("FP-2026-004");
        summary.Status.Should().Be(PayrollRunStatus.Draft);
        summary.PayDate.Should().Be(PayDate);
    }

    [Fact]
    public async Task CreateAsync_NumbersTheRunByThePayDatesYear()
    {
        _runs.Setup(r => r.GetLastFinalPaySequenceAsync(2027, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await _sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2027, 1, 5)));

        _savedRun!.RunNumber.Should().Be("FP-2027-001");
    }

    [Fact]
    public async Task CreateAsync_NoThirteenthMonth_ForAnEmployeeMarkedNotEligible()
    {
        _employee.Is13thMonthEligible = false;

        await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.ThirteenthMonth.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_StoresTheHrDeductions_AndTakesThemAfterTheLoans()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request(deductions:
        [
            new FinalPayDeductionDto(" Unreturned laptop ", 25_000m),
            new FinalPayDeductionDto("Cash advance", 1_500m),
        ]));

        _savedRun!.FinalPayInputs!.Deductions.Select(d => (d.Label, d.Amount))
            .Should().Equal(("Unreturned laptop", 25_000m), ("Cash advance", 1_500m));
        _savedRun.FinalPayInputs.Deductions.Should().OnlyContain(d => d.FinalPayInputsId == _savedRun.FinalPayInputs.Id);
        SavedEntry.LoanDeductions.Should().Be(3_000m);
        SavedEntry.OtherDeductions.Should().Be(26_500m);
        summary.Deductions.Should().Equal(
            new FinalPayDeductionDto("Unreturned laptop", 25_000m), new FinalPayDeductionDto("Cash advance", 1_500m));
        summary.DeductionLines.Should().Equal(
            new FinalPayDeductionLineDto("Unreturned laptop", 25_000m, 25_000m, 0m),
            new FinalPayDeductionLineDto("Cash advance", 1_500m, 1_500m, 0m));
    }

    [Fact]
    public async Task TheSummary_ShowsWhatEachHrDeductionActuallyTook_WhenNetPayCantCoverThemAll()
    {
        // The worked example leaves 207,579.17 for loans and HR's deductions (gross 208,441.67
        // less statutory 862.50). The 3,000 loan comes first: 204,579.17 left. HR's deductions
        // are taken in the order HR listed them: the 200,000 laptop in full, then 4,579.17 of the
        // 10,000 cash advance - 5,420.83 of it uncovered.
        var summary = await _sut.CreateAsync(_separation.Id, Request(deductions:
        [
            new FinalPayDeductionDto("Unreturned laptop", 200_000m),
            new FinalPayDeductionDto("Cash advance", 10_000m),
        ]));

        SavedEntry.LoanDeductions.Should().Be(3_000m);
        SavedEntry.OtherDeductions.Should().Be(204_579.17m);
        SavedEntry.NetPay.Should().Be(0m);
        // What HR asked for is kept as asked, for the edit form.
        summary.Deductions.Should().Equal(
            new FinalPayDeductionDto("Unreturned laptop", 200_000m), new FinalPayDeductionDto("Cash advance", 10_000m));
        summary.DeductionLines.Should().Equal(
            new FinalPayDeductionLineDto("Unreturned laptop", 200_000m, 200_000m, 0m),
            new FinalPayDeductionLineDto("Cash advance", 10_000m, 4_579.17m, 5_420.83m));
    }

    // ------------------------------------------------------------------
    // Working days
    // ------------------------------------------------------------------

    private void SixDayShiftForTheFirstWeek()
        => _shifts.Setup(s => s.ResolveShiftForDayAsync(_employee.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((Guid _, DateOnly date, CancellationToken _) => date <= new DateOnly(2026, 3, 7)
                      ? new DailyScheduleDto(date, "Six-day", new TimeOnly(8, 0), new TimeOnly(17, 0),
                                             IsRestDay: date.DayOfWeek == DayOfWeek.Sunday, IsNightShift: false)
                      : null);

    [Theory]
    [InlineData(313)]
    [InlineData(261)]
    public async Task CreateAsync_SalaryDays_OnAFactorThatLeavesRestDaysUnpaid_AreTheDaysTheShiftSchedules(int factor)
    {
        // Under 313 or 261 a rest day is unpaid, so only scheduled days count.
        // First week (Mar 1-7) on a six-day shift, Sunday off: Mar 2-7 = 6 days.
        // Second week (Mar 8-13) unassigned: weekdays Mar 9-13 = 5 days. Total 11.
        _settings.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PayrollSettings { DailyRateFactor = factor });
        SixDayShiftForTheFirstWeek();

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.WorkingDays.Should().Be(11m);
        _savedRun!.FinalPayInputs!.WorkingDays.Should().Be(11m);
        // Daily rate = 36,500 x 12 / factor: 438,000 / 313 = 1,399.36 (x 11 = 15,392.96);
        // 438,000 / 261 = 1,678.16 (x 11 = 18,459.76).
        SavedEntry.RegularPay.Should().Be(factor == 313 ? 15_392.96m : 18_459.76m);
    }

    [Fact]
    public async Task CreateAsync_SalaryDays_OnThe365Factor_AreEveryCalendarDay_WhateverTheShift()
    {
        // The 365 factor pays rest days, so the six-day shift's Sunday off and the unassigned
        // weekend still count: Mar 1-13 = 13 days.
        SixDayShiftForTheFirstWeek();

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.WorkingDays.Should().Be(13m);
        SavedEntry.RegularPay.Should().Be(15_600m);
    }

    [Fact]
    public async Task CreateAsync_AbsencesComeOffOnceThroughTheAttendanceBridge()
    {
        // Salary days count the calendar (13), not attendance; the bridge's 2 absent scheduled
        // days then come off the base once: 1,200 x 13 - 1,200 x 2 = 13,200.
        _attendance.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), new DateOnly(2026, 3, 1), LastDay,
                                            It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new AttendanceBridgeResult(
                       new Dictionary<Guid, PayrollAttendanceInput> { [_employee.Id] = new() { AbsenceDays = 2m } }, []));

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.WorkingDays.Should().Be(13m);
        SavedEntry.AbsenceDeduction.Should().Be(2_400m);
        SavedEntry.AbsenceDays.Should().Be(2m);   // snapshotted for a recompute
        SavedEntry.RegularPay.Should().Be(13_200m);
    }

    // ------------------------------------------------------------------
    // Allowances
    // ------------------------------------------------------------------

    private sealed class AprilClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
    }

    private void AllowancesOf(decimal taxable, decimal nonTaxable)
    {
        _allowanceList.Add(new EmployeeAllowance
            { EmployeeId = _employee.Id, Type = AllowanceType.Transportation, Amount = taxable, IsTaxable = true });
        _allowanceList.Add(new EmployeeAllowance
            { EmployeeId = _employee.Id, Type = AllowanceType.Meal, Amount = nonTaxable, IsTaxable = false });
    }

    [Fact]
    public async Task CreateAsync_PaysEachAllowanceForTheSalaryDays_AndThe2316And1601CStillReconcile()
    {
        // 3,650 a month taxable, 1,825 non-taxable, over the worked example's 13 salary days:
        //   3,650 x 12 / 365 x 13 = 1,560.00 taxable; 1,825 x 12 / 365 x 13 = 780.00 non-taxable.
        AllowancesOf(taxable: 3_650m, nonTaxable: 1_825m);

        var summary = await _sut.CreateAsync(_separation.Id, Request());
        var entry = SavedEntry;

        entry.TaxableAllowances.Should().Be(1_560m);
        entry.NonTaxableAllowances.Should().Be(780m);
        entry.ThirteenthMonth.Should().Be(4_341.67m);   // basic only, as before
        // Tax: Item 23 = 36,500 + (15,600 - 2,862.50) + 1,560 = 50,797.50 -> 0 due; settled -2,000.
        entry.WithholdingTax.Should().Be(-2_000m);
        // Gross = 208,441.67 (the worked example) + 1,560 + 780 = 210,781.67; net = gross - 3,862.50.
        entry.GrossPay.Should().Be(210_781.67m);
        summary.NetPay.Should().Be(206_919.17m);

        // The 2316 over February and this entry: the taxable allowance in 51A, the other in 37,
        // and Item 19 what was paid - 36,500 + 210,781.67 = 247,281.67.
        var bir2316 = new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object);
        var cert = (await bir2316.BuildWithDraftEntryAsync(_employee.Id, 2026, _savedRun!, entry))!;
        cert.Item51A_OtherAmount.Should().Be(1_560m);
        cert.Item37_SalariesOtherForms.Should().Be(780m + 182_500m);   // + the non-taxable separation pay
        cert.Item19_GrossCompensation.Should().Be(247_281.67m);
        cert.Item52_TotalTaxableCompensation.Should().Be(50_797.50m);

        // March's 1601-C (this run alone) taxes what the 2316 adds for it: 50,797.50 - 36,500 =
        // 14,297.50 = 210,781.67 - 4,341.67 13th month - 2,862.50 shares - 6,000 de minimis
        // - (780 + 182,500) other non-taxable.
        entry.Employee = _employee;
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([_savedRun!]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([_februaryRun]);
        var reports = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, new AprilClock(),
                                                  Mock.Of<IBir2316Service>(), _employees.Object);
        var march = await reports.BuildAsync("1601c", 2026, 3);
        march.Summary.Single(l => l.Label == "Total amount of compensation").Amount.Should().Be(210_781.67m);
        march.Summary.Single(l => l.Label == "Total taxable compensation").Amount.Should().Be(14_297.50m);
    }

    [Fact]
    public async Task CreateAsync_WithNoSalaryDays_PaysNoAllowances()
    {
        AllowancesOf(taxable: 3_650m, nonTaxable: 1_825m);
        PaidThroughMarch();

        await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.TaxableAllowances.Should().Be(0m);
        SavedEntry.NonTaxableAllowances.Should().Be(0m);
    }

    // ------------------------------------------------------------------
    // Separation and retirement pay
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_AnOverride_ReplacesTheComputedSeparationPay_AndStaysNonTaxable()
    {
        var summary = await _sut.CreateAsync(_separation.Id,
            Request(separationPayOverride: 200_000m, overrideNote: "Per CBA"));

        SavedEntry.SeparationPay.Should().Be(200_000m);
        SavedEntry.FinalPayNonTaxable.Should().Be(206_000m);   // 6,000 leave + 200,000
        summary.SeparationPay.Should().Be(200_000m);
        summary.ComputedSeparationOrRetirementPay.Should().Be(182_500m);
        summary.OverrideNote.Should().Be("Per CBA");
        _savedRun!.FinalPayInputs!.SeparationPayOverride.Should().Be(200_000m);
        _savedRun.FinalPayInputs.OverrideNote.Should().Be("Per CBA");
    }

    [Fact]
    public async Task TheSummary_CarriesTheStoredOverrides_EvenOneEqualToTheComputedFigure()
    {
        // An edit form starts from these, so an override that happens to match the computed
        // figure must still read as an override - the figures alone can't tell.
        var summary = await _sut.CreateAsync(_separation.Id,
            Request(separationPayOverride: 182_500m, overrideNote: "Agreed in the exit interview"));

        summary.SeparationPayOverride.Should().Be(182_500m);
        summary.ComputedSeparationOrRetirementPay.Should().Be(182_500m);
        summary.RetirementPayOverride.Should().BeNull();
    }

    [Fact]
    public async Task TheSummary_HasNoOverrides_WhenHrSetNone()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.SeparationPayOverride.Should().BeNull();
        summary.RetirementPayOverride.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_RetirementPay_ForAnEligibleRetiree_IsNonTaxable()
    {
        // 61 on the last day, hired 2006-01-02: 20 years of service.
        // 22.5 x 1,200 x 20 = 540,000.00.
        _separation.Type = SeparationType.Retirement;
        _separation.AuthorizedCause = null;
        _employee.DateOfBirth = new DateOnly(1965, 1, 10);
        _employee.HireDate = new DateOnly(2006, 1, 2);

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.RetirementPay.Should().Be(540_000m);
        SavedEntry.SeparationPay.Should().Be(0m);
        SavedEntry.FinalPayNonTaxable.Should().Be(546_000m);
        summary.ServiceYears.Should().Be(20);
        summary.ComputedSeparationOrRetirementPay.Should().Be(540_000m);
    }

    [Fact]
    public async Task CreateAsync_RetirementPay_IsZero_WhenNotEligible_AndAnOverrideIsTaxable()
    {
        // 40 years old: an early retirement under a company plan.
        _separation.Type = SeparationType.Retirement;
        _separation.AuthorizedCause = null;
        _employee.DateOfBirth = new DateOnly(1986, 1, 10);

        var computed = await _sut.CreateAsync(_separation.Id, Request());
        SavedEntry.RetirementPay.Should().Be(0m);
        computed.ComputedSeparationOrRetirementPay.Should().Be(0m);

        _separation.FinalPayRunId = null;
        _separation.FinalPayRun = null;
        var overridden = await _sut.CreateAsync(_separation.Id,
            Request(retirementPayOverride: 100_000m, overrideNote: "Company plan"));

        SavedEntry.RetirementPay.Should().Be(100_000m);
        SavedEntry.FinalPayNonTaxable.Should().Be(6_000m, "only the de minimis leave; the override is taxable");
        SavedEntry.FinalPayTaxable.Should().Be(100_000m);
        overridden.ComputedSeparationOrRetirementPay.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_Resignation_ComputesNoSeparationPay_AndAnOverrideIsTaxable()
    {
        _separation.Type = SeparationType.Resignation;
        _separation.AuthorizedCause = null;

        var summary = await _sut.CreateAsync(_separation.Id,
            Request(separationPayOverride: 50_000m, overrideNote: "Goodwill"));

        summary.ComputedSeparationOrRetirementPay.Should().BeNull();
        SavedEntry.SeparationPay.Should().Be(50_000m);
        SavedEntry.FinalPayTaxable.Should().Be(50_000m);
    }

    // ------------------------------------------------------------------
    // Across a year end
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PaidInTheNextYear_TakesThe13thMonthFromTheLastWorkingDaysYear_AndSettlesThePayYear()
    {
        // Last working day Friday 2026-12-11, paid 2027-01-15. 2026's Paid runs: Jan-Oct (basic
        // 365,000, with a 10,000 13th-month advance and 5,000 withheld) and November (36,500).
        _separation.LastWorkingDay = new DateOnly(2026, 12, 11);
        _paidRuns.Clear();
        var janToOct = new PayrollRun
        {
            RunNumber = "PAY-2026-010", PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            PayDate = new DateOnly(2026, 10, 31), Frequency = PayFrequency.Monthly, Status = PayrollRunStatus.Paid,
        };
        janToOct.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = janToOct.Id, EmployeeId = _employee.Id, RegularPay = 365_000m, ThirteenthMonth = 10_000m,
            WithholdingTax = 5_000m,
        });
        var november = new PayrollRun
        {
            RunNumber = "PAY-2026-011", PeriodStart = new DateOnly(2026, 11, 1), PeriodEnd = new DateOnly(2026, 11, 30),
            PayDate = new DateOnly(2026, 11, 30), Frequency = PayFrequency.Monthly, Status = PayrollRunStatus.Paid,
        };
        november.Employees.Add(new PayrollRunEmployee { PayrollRunId = november.Id, EmployeeId = _employee.Id, RegularPay = 36_500m });
        _paidRuns.AddRange([janToOct, november]);

        await _sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2027, 1, 15)));
        var entry = SavedEntry;

        // Period Dec 1-11: 11 calendar days x 1,200 = 13,200.
        entry.RegularPay.Should().Be(13_200m);

        // 13th month on 2026's basic: (365,000 + 36,500 + 13,200) / 12 = 414,700 / 12 = 34,558.33,
        // less the 10,000 already paid in 2026 = 24,558.33. (Taken from 2027's runs - none - it
        // would have been 13,200 / 12 = 1,100.)
        entry.ThirteenthMonth.Should().Be(24_558.33m);

        // The settle builds 2027's certificate: this entry alone, taxable 13,200 - 2,862.50 =
        // 10,337.50 (the 13th month is inside 2027's 90,000 exemption) -> 0 due, nothing withheld
        // in 2027 -> 0. 2026's 5,000 stays on 2026's certificate; settling 2026 instead would have
        // collected (22,500 + 20% x 11,837.50) - 5,000 = 19,867.50.
        entry.WithholdingTax.Should().Be(0m);
        _runs.Verify(r => r.GetPaidRunsForEmployeeInYearAsync(_employee.Id, 2027, It.IsAny<CancellationToken>()));
        _savedRun!.RunNumber.Should().Be("FP-2027-001");
    }

    // ------------------------------------------------------------------
    // Leave
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Leave_ConvertsRemainingDaysOfConvertibleTypes_ForTheLastWorkingDaysYear()
    {
        _balances.Clear();
        _balances.Add(Balance("Vacation Leave", totalDays: 15m, usedDays: 3m, convertible: true, countsAsVacation: true));
        _balances.Add(Balance("Service Incentive Leave", totalDays: 5m, convertible: true, countsAsVacation: false));
        _balances.Add(Balance("Sick Leave", totalDays: 10m, convertible: false, countsAsVacation: false));

        // Paid in the next year: the balances are still the last working day's year's.
        var summary = await _sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2027, 1, 5)));

        _leaveBalances.Verify(r => r.GetByEmployeeAsync(_employee.Id, 2026, It.IsAny<CancellationToken>()));
        _leaveBalances.Verify(r => r.GetByEmployeeAsync(_employee.Id, 2027, It.IsAny<CancellationToken>()), Times.Never);
        // VL 12 remaining (10 de minimis, 2 taxable) + SIL 5 (taxable): 17 x 1,200 = 20,400;
        // non-taxable 10 x 1,200 = 12,000.
        SavedEntry.LeaveConversionPay.Should().Be(20_400m);
        SavedEntry.LeaveConversionNonTaxable.Should().Be(12_000m);
        summary.LeaveLines.Should().Equal(
            new FinalPayLeaveLineDto("Vacation Leave", 12m, true),
            new FinalPayLeaveLineDto("Service Incentive Leave", 5m, false));
    }

    [Fact]
    public async Task CreateAsync_LeaveBeyondDeMinimis_SharesThe90000WithThe13thMonth_AndOnlyTheRestIsTaxed()
    {
        // The settle case above (150,000 a month, 300,000 paid in February with 1,000 withheld),
        // with 30 days of vacation leave instead of 5.
        _compensation.BasicSalary = 150_000m;
        _februaryRun.Employees.Single().RegularPay = 300_000m;
        _februaryRun.Employees.Single().WithholdingTax = 1_000m;
        _balances.Clear();
        _balances.Add(Balance("Vacation Leave", totalDays: 30m, convertible: true, countsAsVacation: true));

        var summary = await _sut.CreateAsync(_separation.Id, Request());
        var entry = SavedEntry;

        // Daily rate 4,931.51. 13th month = (300,000 + 13 x 4,931.51) / 12
        //   = (300,000 + 64,109.63) / 12 = 30,342.469... -> 30,342.47.
        entry.ThirteenthMonth.Should().Be(30_342.47m);
        // Leave: 10 de minimis days = 49,315.10; the other 20 = 98,630.20 of other benefits;
        // 147,945.30 in all.
        entry.LeaveConversionPay.Should().Be(147_945.30m);
        entry.LeaveConversionNonTaxable.Should().Be(49_315.10m);
        // The 90,000 exemption, none of it used earlier in 2026: the 13th month takes 30,342.47,
        // leaving 59,657.53 for the leave. The other 98,630.20 - 59,657.53 = 38,972.67 is taxable.
        summary.LeaveConversionNonTaxable.Should().Be(108_972.63m);   // 49,315.10 + 59,657.53

        var bir2316 = new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object);
        var cert = (await bir2316.BuildWithDraftEntryAsync(_employee.Id, 2026, _savedRun!, entry))!;
        cert.Item34_ThirteenthMonthAndBenefits.Should().Be(90_000m);
        cert.Item48_TaxableThirteenthMonth.Should().Be(38_972.67m);   // 30,342.47 + 98,630.20 - 90,000
        cert.Item35_DeMinimis.Should().Be(49_315.10m);
        cert.Item51B_OtherAmount.Should().Be(0m);
        // Taxable: 359,659.63 (the settle case's basic, net of contributions) + 38,972.67 =
        // 398,632.30. Tax due = (398,632.30 - 250,000) x 15% = 22,294.845 -> 22,294.84 (half to
        // even). Settled = 22,294.84 - 1,000 withheld in February = 21,294.84. Before the leave
        // beyond de minimis shared the exemption, all 98,630.20 of it was taxed: 458,289.83 ->
        // 22,500 + 20% x 58,289.83 = 34,157.97 due.
        cert.Item23_GrossTaxable.Should().Be(398_632.30m);
        cert.Item24_TaxDue.Should().Be(22_294.84m);
        entry.WithholdingTax.Should().Be(21_294.84m);
        cert.Item24_TaxDue.Should().Be(cert.Item26_TotalTaxWithheld);

        // March's 1601-C taxes what the 2316 adds for this run: 398,632.30 - 300,000 = 98,632.30
        //   = gross 992,397.40 (64,109.63 + 30,342.47 + 147,945.30 + 750,000 separation pay)
        //     - 90,000 13th month and other benefits - 4,450 shares - 49,315.10 de minimis
        //     - 750,000 other non-taxable.
        entry.Employee = _employee;
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([_savedRun!]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([_februaryRun]);
        var reports = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, new AprilClock(),
                                                  Mock.Of<IBir2316Service>(), _employees.Object);
        var march = await reports.BuildAsync("1601c", 2026, 3);
        march.Summary.Single(l => l.Label == "Total amount of compensation").Amount.Should().Be(992_397.40m);
        march.Summary.Single(l => l.Label == "13th month pay and other benefits").Amount.Should().Be(90_000m);
        march.Summary.Single(l => l.Label == "Total taxable compensation").Amount.Should().Be(98_632.30m);
    }

    [Fact]
    public async Task TheSummary_CountsLeaveBeyondDeMinimisAsNonTaxable_WithinWhatTheYearLeftOfThe90000()
    {
        // 88,000 of 13th month already paid in 2026 (a mid-year advance in February): the final
        // pay's 13th month = (36,500 + 15,600) / 12 = 4,341.67 due for the year, less 88,000
        // paid -> 0. Of the exemption, 90,000 - 88,000 = 2,000 is left for the leave.
        // 15 vacation days: 10 de minimis = 12,000; 5 beyond = 6,000, of which 2,000 is exempt.
        _februaryRun.Employees.Single().ThirteenthMonth = 88_000m;
        _balances.Clear();
        _balances.Add(Balance("Vacation Leave", totalDays: 15m, convertible: true, countsAsVacation: true));

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.ThirteenthMonth.Should().Be(0m);
        summary.LeaveConversionPay.Should().Be(18_000m);
        summary.LeaveConversionNonTaxable.Should().Be(14_000m);   // 12,000 + 2,000
        (await _sut.GetAsync(_separation.Id))!.LeaveConversionNonTaxable.Should().Be(14_000m);
    }

    [Fact]
    public async Task TheSummary_SaysALeaveTypeConvertsToCash_EvenWithNoDaysLeftToPay()
    {
        // Vacation leave converts, but all 5 days were taken.
        _balances.Clear();
        _balances.Add(Balance("Vacation Leave", totalDays: 5m, usedDays: 5m, convertible: true, countsAsVacation: true));

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.LeaveLines.Should().BeEmpty();
        summary.HasConvertibleLeaveType.Should().BeTrue();
    }

    [Fact]
    public async Task TheSummary_SaysALeaveTypeConvertsToCash_EvenOneTheEmployeeHasNoBalanceFor()
    {
        _balances.Clear();
        _otherLeaveTypes.Add(new LeaveType { Name = "Vacation Leave", Code = "VL", IsConvertibleToCash = true });

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.LeaveLines.Should().BeEmpty();
        summary.HasConvertibleLeaveType.Should().BeTrue();
    }

    [Fact]
    public async Task TheSummary_SaysNoLeaveTypeConvertsToCash_WhenNoneIsMarked()
    {
        _balances.Clear();
        _balances.Add(Balance("Sick Leave", totalDays: 7m, convertible: false, countsAsVacation: false));

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.HasConvertibleLeaveType.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Paying the leave out
    // ------------------------------------------------------------------

    [Fact]
    public async Task LeavePaidOutAsync_GivesEachConvertedBalanceAndTheDaysItPaidOut()
    {
        await _sut.CreateAsync(_separation.Id, Request());

        var paidOut = await _sut.LeavePaidOutAsync(_savedRun!);

        // Vacation leave's 5 days; sick leave doesn't convert.
        paidOut.Should().ContainSingle();
        paidOut[0].Balance.Should().BeSameAs(_balances[0]);
        paidOut[0].Days.Should().Be(5m);
    }

    [Fact]
    public async Task LeavePaidOutAsync_Refuses_WhenTheBalancesNoLongerPriceToWhatTheFinalPayPaid()
    {
        // The final pay converted 5 days (6,000); two of them were then taken as leave.
        await _sut.CreateAsync(_separation.Id, Request());
        _balances[0].UsedDays = 2m;

        var act = () => _sut.LeavePaidOutAsync(_savedRun!);

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's convertible leave has changed since the final pay was computed; recompute it before paying.");
    }

    [Fact]
    public async Task RecordLeavePaidOutAsync_MarksTheDaysUsed_AndSavesEachBalance()
    {
        await _sut.CreateAsync(_separation.Id, Request());
        var paidOut = await _sut.LeavePaidOutAsync(_savedRun!);

        await _sut.RecordLeavePaidOutAsync(paidOut);

        _balances[0].UsedDays.Should().Be(5m);
        _balances[0].RemainingDays.Should().Be(0m);
        _balances[1].UsedDays.Should().Be(0m, "sick leave wasn't paid out");
        _leaveBalances.Verify(r => r.UpdateAsync(_balances[0], It.IsAny<CancellationToken>()), Times.Once);
        _leaveBalances.Verify(r => r.UpdateAsync(_balances[1], It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Summary
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Summary_ShowsEachLoansBalanceDeductionAndWhatIsLeftUncovered()
    {
        // A 500,000 balance can't be covered: the budget after statutory deductions is
        // 208,441.67 - 862.50 = 207,579.17, so 500,000 - 207,579.17 = 292,420.83 stays on the loan.
        _activeLoans[0].RemainingBalance = 500_000m;

        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.Loans.Should().Equal(new FinalPayLoanLineDto("SSSLoan", 500_000m, 207_579.17m, 292_420.83m));
    }

    [Fact]
    public async Task CreateAsync_Summary_ShowsTheOutstandingClearance()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request());

        summary.ClearanceComplete.Should().BeFalse();
        summary.OutstandingClearance.Should().Equal("Finance", "HR exit interview");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTheSeparationHasNoFinalPayRun()
    {
        (await _sut.GetAsync(_separation.Id)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsTheSummaryOfTheSavedRun()
    {
        var created = await _sut.CreateAsync(_separation.Id, Request());

        var loaded = await _sut.GetAsync(_separation.Id);

        loaded.Should().NotBeNull();
        loaded!.RunNumber.Should().Be(created.RunNumber);
        loaded.NetPay.Should().Be(204_579.17m);
        loaded.WorkingDays.Should().Be(13m);
        loaded.Loans.Should().Equal(new FinalPayLoanLineDto("SSSLoan", 3_000m, 3_000m, 0m));
    }

    // ------------------------------------------------------------------
    // Recompute and update
    // ------------------------------------------------------------------

    [Fact]
    public async Task RecomputeAsync_UsesTheStoredWorkingDays_AndResettlesTheTax()
    {
        await _sut.CreateAsync(_separation.Id, Request());
        var run = _savedRun!;

        // The schedule changes after the run was made: a recompute must not pick it up.
        _shifts.Setup(s => s.ResolveShiftForDayAsync(_employee.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Guid _, DateOnly date, CancellationToken _) =>
                   new DailyScheduleDto(date, "Off", null, null, IsRestDay: true, IsNightShift: false));

        // Another run was paid since, withholding 1,000 more: the settlement moves with it.
        var march = new PayrollRun
        {
            RunNumber = "PAY-2026-003", PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 1),
            PayDate = new DateOnly(2026, 3, 2), Status = PayrollRunStatus.Paid,
        };
        march.Employees.Add(new PayrollRunEmployee { PayrollRunId = march.Id, EmployeeId = _employee.Id, WithholdingTax = 1_000m });
        _paidRuns.Add(march);

        var entries = await _sut.RecomputeAsync(run);

        var entry = entries.Should().ContainSingle().Subject;
        entry.RegularPay.Should().Be(15_600m);                 // still the 13 stored salary days
        entry.WithholdingTax.Should().Be(-3_000m);             // 0 - (2,000 + 1,000)
        entry.SeparationPay.Should().Be(182_500m);
        entry.LoanDeductions.Should().Be(3_000m);
        run.FinalPayInputs!.WorkingDays.Should().Be(13m);
    }

    [Fact]
    public async Task RecomputeAsync_ReproducesTheCreatedFigures()
    {
        await _sut.CreateAsync(_separation.Id, Request(
            separationPayOverride: 190_000m, overrideNote: "Per CBA",
            deductions: [new FinalPayDeductionDto("Unreturned laptop", 25_000m)]));
        var created = SavedEntry;

        var entry = (await _sut.RecomputeAsync(_savedRun!)).Single();

        entry.Should().BeEquivalentTo(created, o => o
            .Excluding(e => e.Id).Excluding(e => e.CreatedAt).Excluding(e => e.UpdatedAt)
            .Excluding(e => e.PayrollRun).Excluding(e => e.LoanDeductionLines).Excluding(e => e.PremiumDays));
        entry.WithholdingTax.Should().Be(-2_000m);
        entry.OtherDeductions.Should().Be(25_000m);
        entry.SeparationPay.Should().Be(190_000m);
        entry.LoanDeductionLines.Select(l => (l.EmployeeLoanId, l.Amount))
            .Should().Equal(created.LoanDeductionLines.Select(l => (l.EmployeeLoanId, l.Amount)));
    }

    [Fact]
    public async Task RecomputeAsync_UsesTheAttendanceSnapshot_NotTheBridge()
    {
        _attendance.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                            It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new AttendanceBridgeResult(
                       new Dictionary<Guid, PayrollAttendanceInput> { [_employee.Id] = new() { AbsenceDays = 1m } }, []));
        await _sut.CreateAsync(_separation.Id, Request());

        // A punch edited afterwards would now show 3 absences - the recompute must keep 1.
        _attendance.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                            It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new AttendanceBridgeResult(
                       new Dictionary<Guid, PayrollAttendanceInput> { [_employee.Id] = new() { AbsenceDays = 3m } }, []));

        var entry = (await _sut.RecomputeAsync(_savedRun!)).Single();

        entry.AbsenceDays.Should().Be(1m);
        entry.RegularPay.Should().Be(14_400m);   // 1,200 x 13 - 1,200 x 1
    }

    [Fact]
    public async Task UpdateAsync_ChangesTheInputs_AndRecomputes()
    {
        await _sut.CreateAsync(_separation.Id, Request());
        var run = _savedRun!;
        _separation.FinalPayRun = run;
        IReadOnlyList<PayrollRunEmployee>? replaced = null;
        _runs.Setup(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
             .Callback((PayrollRun _, IReadOnlyList<PayrollRunEmployee> entries, CancellationToken _) => replaced = entries)
             .Returns(Task.CompletedTask);
        run.Status = PayrollRunStatus.ForApproval;

        var summary = await _sut.UpdateAsync(_separation.Id, Request(
            periodStart: new DateOnly(2026, 3, 9),
            deductions: [new FinalPayDeductionDto("Cash advance", 1_500m)]));

        run.Status.Should().Be(PayrollRunStatus.Draft);
        run.PeriodStart.Should().Be(new DateOnly(2026, 3, 9));
        run.FinalPayInputs!.WorkingDays.Should().Be(5m);
        run.FinalPayInputs.Deductions.Select(d => (d.Label, d.Amount)).Should().Equal(("Cash advance", 1_500m));
        run.FinalPayInputs.Deductions.Should().OnlyContain(d => d.FinalPayInputsId == run.FinalPayInputs.Id);
        replaced.Should().ContainSingle();
        replaced![0].RegularPay.Should().Be(6_000m);
        replaced[0].OtherDeductions.Should().Be(1_500m);
        summary.WorkingDays.Should().Be(5m);
        summary.Deductions.Should().Equal(new FinalPayDeductionDto("Cash advance", 1_500m));
    }

    [Fact]
    public async Task UpdateAsync_OnAnApprovedFinalPay_ChangesIt_AndSendsItBackForApproval()
    {
        // Approved before clearance was complete; clearance then turns up an unreturned laptop.
        await _sut.CreateAsync(_separation.Id, Request());
        var run = _savedRun!;
        _separation.FinalPayRun = run;
        IReadOnlyList<PayrollRunEmployee>? replaced = null;
        _runs.Setup(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
             .Callback((PayrollRun _, IReadOnlyList<PayrollRunEmployee> entries, CancellationToken _) => replaced = entries)
             .Returns(Task.CompletedTask);
        run.Status = PayrollRunStatus.Approved;

        var summary = await _sut.UpdateAsync(_separation.Id, Request(
            deductions: [new FinalPayDeductionDto("Unreturned laptop", 2_500m)]));

        run.Status.Should().Be(PayrollRunStatus.Draft);
        summary.Status.Should().Be(PayrollRunStatus.Draft);
        replaced.Should().ContainSingle().Which.OtherDeductions.Should().Be(2_500m);
    }

    [Fact]
    public async Task UpdateAsync_Refuses_OnceTheRunIsPaid()
    {
        await _sut.CreateAsync(_separation.Id, Request());
        _savedRun!.Status = PayrollRunStatus.Paid;

        var act = () => _sut.UpdateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage("A paid final pay can't be changed.");
        _runs.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                It.IsAny<CancellationToken>()), Times.Never);
        _savedRun.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task UpdateAsync_Refuses_WhenThereIsNoFinalPayRunYet()
    {
        var act = () => _sut.UpdateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no final-pay run yet.");
    }
}
