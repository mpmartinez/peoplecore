using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
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
    private readonly Mock<ILeaveBalanceRepository> _leaveBalances = new();
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
        _leaveBalances.Setup(r => r.GetByEmployeeAsync(_employee.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(() => _balances);

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
            _separations.Object, _runs.Object, _compensations.Object, _loans.Object, _leaveBalances.Object,
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

    [Fact]
    public async Task CreateAsync_UsesTheStartHrGives()
    {
        var summary = await _sut.CreateAsync(_separation.Id, Request(periodStart: new DateOnly(2026, 3, 9)));

        summary.PeriodStart.Should().Be(new DateOnly(2026, 3, 9));
        summary.WorkingDays.Should().Be(5m);   // Mar 9-13
        SavedEntry.RegularPay.Should().Be(6_000m);
    }

    [Fact]
    public async Task CreateAsync_DefaultPeriod_StartsTheDayAfterTheLastPaidRegularRun_IgnoringUnpaidAndFinalPayRuns()
    {
        // A later draft regular run and a paid run of another type must not move the start.
        var draft = new PayrollRun
        {
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 15),
            PayDate = new DateOnly(2026, 3, 15), Status = PayrollRunStatus.Draft,
        };
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
             .ReturnsAsync([draft, otherFinalPay, _februaryRun, january]);

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

    [Theory]
    [InlineData(PayrollRunStatus.Approved)]
    [InlineData(PayrollRunStatus.Paid)]
    public async Task UpdateAsync_Refuses_OnceTheRunIsApprovedOrPaid(PayrollRunStatus status)
    {
        await _sut.CreateAsync(_separation.Id, Request());
        _savedRun!.Status = status;

        var act = () => _sut.UpdateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only draft or for-approval final pay can be changed.");
    }

    [Fact]
    public async Task UpdateAsync_Refuses_WhenThereIsNoFinalPayRunYet()
    {
        var act = () => _sut.UpdateAsync(_separation.Id, Request());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no final-pay run yet.");
    }
}
