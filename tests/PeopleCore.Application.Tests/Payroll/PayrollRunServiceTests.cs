using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollRunServiceTests
{
    private readonly Mock<IPayrollRunRepository> _runRepo = new();
    private readonly Mock<IEmployeeCompensationRepository> _compensationRepo = new();
    private readonly Mock<IEmployeeAllowanceRepository> _allowanceRepo = new();
    private readonly Mock<IEmployeeLoanRepository> _loanRepo = new();
    private readonly Mock<IPayrollSettingsRepository> _settingsRepo = new();
    private readonly Mock<IPayrollAttendanceBridge> _attendanceBridge = new();
    private readonly PayrollRunService _sut;

    public PayrollRunServiceTests()
    {
        // Default: the bridge derives nothing, so every employee accumulates zeros and the
        // Phase 1 expectations below are unaffected. Tests that care set it up themselves.
        _attendanceBridge
            .Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                     It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, DateOnly _, DateOnly _, CancellationToken _) =>
                new AttendanceBridgeResult(
                    ids.ToDictionary(id => id, _ => new PayrollAttendanceInput()), []));

        _sut = new PayrollRunService(
            _runRepo.Object,
            _compensationRepo.Object,
            _allowanceRepo.Object,
            _loanRepo.Object,
            _settingsRepo.Object,
            new PayrollComputationService(),
            _attendanceBridge.Object,
            NullLogger<PayrollRunService>.Instance);
    }

    // ------------------------------------------------------------------
    // MarkPaidAsync - retiring loan balances from the deduction line, never the loan's schedule
    // ------------------------------------------------------------------

    [Fact]
    public async Task MarkPaidAsync_RetiresLoanBalanceFromTheDeductionLine()
    {
        var loan = new EmployeeLoan
        {
            EmployeeId = Guid.NewGuid(),
            LoanType = LoanType.SSSLoan,
            TotalAmount = 10_000m,
            MonthlyDeduction = 2_000m,   // deliberately different from the line below
            RemainingBalance = 5_000m,
            IsActive = true
        };

        var entry = new PayrollRunEmployee { EmployeeId = loan.EmployeeId, LoanDeductions = 1_200m };
        entry.LoanDeductionLines.Add(new PayrollLoanDeduction
        {
            EmployeeLoanId = loan.Id,
            LoanType = loan.LoanType.ToString(),
            Amount = 1_200m
        });

        var run = new PayrollRun { RunNumber = "PR-0001", Status = PayrollRunStatus.Approved };
        run.Employees.Add(entry);

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _loanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([loan]);

        await _sut.MarkPaidAsync(run.Id, CancellationToken.None);

        // 5,000 - 1,200 taken from the line, not 5,000 - 2,000 re-derived from the schedule.
        loan.RemainingBalance.Should().Be(3_800m);
        run.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task MarkPaidAsync_RetiresMultipleLoansIndependently()
    {
        var sss = new EmployeeLoan
        {
            EmployeeId = Guid.NewGuid(), LoanType = LoanType.SSSLoan,
            TotalAmount = 16_000m, MonthlyDeduction = 1_000m, RemainingBalance = 16_000m, IsActive = true
        };
        var company = new EmployeeLoan
        {
            EmployeeId = sss.EmployeeId, LoanType = LoanType.CompanyLoan,
            TotalAmount = 600m, MonthlyDeduction = 600m, RemainingBalance = 150m, IsActive = true
        };

        // The company loan had only 150 left, so the run withheld 150, not the 300 instalment.
        var entry = new PayrollRunEmployee { EmployeeId = sss.EmployeeId, LoanDeductions = 650m };
        entry.LoanDeductionLines.Add(new PayrollLoanDeduction { EmployeeLoanId = sss.Id, Amount = 500m });
        entry.LoanDeductionLines.Add(new PayrollLoanDeduction { EmployeeLoanId = company.Id, Amount = 150m });

        var run = new PayrollRun { RunNumber = "PR-0003", Status = PayrollRunStatus.Approved };
        run.Employees.Add(entry);

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _loanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([sss, company]);

        await _sut.MarkPaidAsync(run.Id, CancellationToken.None);

        sss.RemainingBalance.Should().Be(15_500m);
        company.RemainingBalance.Should().Be(0m);
    }

    [Fact]
    public async Task MarkPaidAsync_DeactivatesLoanOnceFullyRepaid()
    {
        var loan = new EmployeeLoan
        {
            EmployeeId = Guid.NewGuid(), LoanType = LoanType.CompanyLoan,
            TotalAmount = 600m, MonthlyDeduction = 600m, RemainingBalance = 150m, IsActive = true
        };

        var entry = new PayrollRunEmployee { EmployeeId = loan.EmployeeId, LoanDeductions = 150m };
        entry.LoanDeductionLines.Add(new PayrollLoanDeduction { EmployeeLoanId = loan.Id, Amount = 150m });

        var run = new PayrollRun { RunNumber = "PR-0004", Status = PayrollRunStatus.Approved };
        run.Employees.Add(entry);

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _loanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([loan]);

        await _sut.MarkPaidAsync(run.Id, CancellationToken.None);

        loan.RemainingBalance.Should().Be(0m);
        loan.IsActive.Should().BeFalse("a cleared loan must stop deducting");
    }

    [Fact]
    public async Task MarkPaidAsync_LeavesLoanUntouchedWhenRunWithheldNothingFromIt()
    {
        var deducted = new EmployeeLoan
        {
            EmployeeId = Guid.NewGuid(), LoanType = LoanType.SSSLoan,
            TotalAmount = 16_000m, MonthlyDeduction = 1_000m, RemainingBalance = 16_000m, IsActive = true
        };
        // Added after the run was computed, so it has no line and must not be retired.
        var addedLater = new EmployeeLoan
        {
            EmployeeId = deducted.EmployeeId, LoanType = LoanType.CompanyLoan,
            TotalAmount = 4_000m, MonthlyDeduction = 800m, RemainingBalance = 4_000m, IsActive = true
        };

        var entry = new PayrollRunEmployee { EmployeeId = deducted.EmployeeId, LoanDeductions = 500m };
        entry.LoanDeductionLines.Add(new PayrollLoanDeduction { EmployeeLoanId = deducted.Id, Amount = 500m });

        var run = new PayrollRun { RunNumber = "PR-0005", Status = PayrollRunStatus.Approved };
        run.Employees.Add(entry);

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        // Only the loan referenced by a deduction line is ever looked up.
        _loanRepo.Setup(r => r.GetByIdsAsync(It.Is<IEnumerable<Guid>>(ids => ids.Single() == deducted.Id),
                 It.IsAny<CancellationToken>())).ReturnsAsync([deducted]);

        await _sut.MarkPaidAsync(run.Id, CancellationToken.None);

        deducted.RemainingBalance.Should().Be(15_500m);
        addedLater.RemainingBalance.Should().Be(4_000m);
        addedLater.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task MarkPaidAsync_WhenRunIsNotApproved_ThrowsDomainException()
    {
        var run = new PayrollRun { RunNumber = "PR-0006", Status = PayrollRunStatus.Draft };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.MarkPaidAsync(run.Id, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ------------------------------------------------------------------
    // ComputeAsync - recomputing a run in place
    // ------------------------------------------------------------------

    [Fact]
    public async Task ComputeAsync_WhenRunIsAlreadyPaid_ThrowsDomainException()
    {
        var run = new PayrollRun { RunNumber = "PR-0002", Status = PayrollRunStatus.Paid };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.ComputeAsync(run.Id, CancellationToken.None);

        // Recomputing a paid run would silently change what an employee was already paid.
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task ComputeAsync_WhenRunIsApproved_ThrowsDomainException()
    {
        // An approver has signed off on these figures; recomputing would change what they saw.
        var employeeId = Guid.NewGuid();
        var run = new PayrollRun { RunNumber = "PR-0007", Status = PayrollRunStatus.Approved };
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = employeeId });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.ComputeAsync(run.Id, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task ComputeAsync_RecomputesAgainstCurrentRatesAndSendsRunBackToDraft()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 20_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "ME"
        };

        // Figures a previous, wrong engine stored: the old /22 daily rate and a stale SSS amount.
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-001",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 15),
            PayDate = new DateOnly(2026, 1, 20),
            Frequency = PayFrequency.SemiMonthly,
            Status = PayrollRunStatus.ForApproval
        };
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = employeeId, RegularPay = 9_090.91m, SSSEmployee = 461.25m });

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PayrollSettings?)null);
        _compensationRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync([compensation]);
        _allowanceRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([]);
        _loanRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        List<PayrollRunEmployee>? capturedEntries = null;
        _runRepo.Setup(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>((_, entries, _) => capturedEntries = entries.ToList())
                .Returns(Task.CompletedTask);

        await _sut.ComputeAsync(run.Id, CancellationToken.None);

        capturedEntries.Should().ContainSingle();
        capturedEntries!.Single().RegularPay.Should().Be(10_000.00m, "a full period pays the full salary slice");
        capturedEntries!.Single().SSSEmployee.Should().Be(500.00m, "5% of the 20,000 MSC, halved for semi-monthly");
        run.Status.Should().Be(PayrollRunStatus.Draft, "the figures an approver was asked to sign off have changed");
    }

    [Fact]
    public async Task ComputeAsync_ReplacesTheOldEntriesRatherThanAddingToThem()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 20_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "ME"
        };

        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-002",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 15),
            PayDate = new DateOnly(2026, 1, 20),
            Frequency = PayFrequency.SemiMonthly,
            Status = PayrollRunStatus.Draft
        };
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = employeeId });

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PayrollSettings?)null);
        _compensationRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync([compensation]);
        _allowanceRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([]);
        _loanRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        await _sut.ComputeAsync(run.Id, CancellationToken.None);

        _runRepo.Verify(r => r.ReplaceEntriesAsync(
            run,
            It.Is<IReadOnlyList<PayrollRunEmployee>>(entries => entries.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once,
            "recomputing replaces the entries, it does not duplicate them");
    }

    [Fact]
    public async Task ComputeAsync_PreservesTheStoredPerEmployeeInputsFromTheExistingEntry()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 20_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "ME"
        };

        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-009",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 15),
            PayDate = new DateOnly(2026, 1, 20),
            Frequency = PayFrequency.SemiMonthly,
            Status = PayrollRunStatus.Draft
        };
        // Non-default inputs already persisted on the entry from when the run was created -
        // ComputeAsync must read these back rather than re-defaulting them.
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = employeeId, OvertimeHours = 5m });

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PayrollSettings?)null);
        _compensationRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync([compensation]);
        _allowanceRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([]);
        _loanRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        List<PayrollRunEmployee>? capturedEntries = null;
        _runRepo.Setup(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>((_, entries, _) => capturedEntries = entries.ToList())
                .Returns(Task.CompletedTask);

        await _sut.ComputeAsync(run.Id, CancellationToken.None);

        capturedEntries.Should().ContainSingle();
        capturedEntries!.Single().OvertimeHours.Should().Be(5m,
            "a recompute must preserve the OvertimeHours already stored on the entry, not re-default it to zero");
    }

    [Fact]
    public async Task ComputeAsync_WhenRunHasNoEmployees_ThrowsDomainException()
    {
        var run = new PayrollRun { RunNumber = "PR-0008", Status = PayrollRunStatus.Draft };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.ComputeAsync(run.Id, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ------------------------------------------------------------------
    // CreateAsync / GetAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ComputesEntriesForRequestedEmployeesAndPersistsTheRun()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 20_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "ME"
        };

        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PayrollSettings?)null);
        _compensationRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync([compensation]);
        _allowanceRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([]);
        _loanRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        _runRepo.Setup(r => r.CountForYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        _runRepo.Setup(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        PayrollRun? capturedRun = null;
        _runRepo.Setup(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, CancellationToken>((run, _) => capturedRun = run)
                .Returns(Task.CompletedTask);
        // CreateAsync reloads via GetWithEntriesAsync after saving, so its response's employee
        // names come from the same lookup GET uses (see PayrollRunEmployee.Employee's remarks).
        _runRepo.Setup(r => r.GetWithEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => capturedRun);

        var request = new CreatePayrollRunRequest(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 20),
            PayFrequency.SemiMonthly, [new PayrollRunEmployeeInput(employeeId)]);

        var result = await _sut.CreateAsync(request, CancellationToken.None);

        result.RunNumber.Should().Be("PAY-2026-005");
        result.EmployeeCount.Should().Be(1);
        result.Status.Should().Be(PayrollRunStatus.Draft);
        result.Employees.Single().EmployeeId.Should().Be(employeeId);
        _runRepo.Verify(r => r.AddWithEntriesAsync(
            It.Is<PayrollRun>(run => run.Employees.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenNoEmployeesRequested_ThrowsDomainException()
    {
        var request = new CreatePayrollRunRequest(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 20),
            PayFrequency.SemiMonthly, []);

        var act = () => _sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ------------------------------------------------------------------
    // The attendance bridge - deriving on create, snapshotting, and reproducing on recompute
    // ------------------------------------------------------------------

    /// <summary>
    /// Attendance that exercises both halves of every column the entry collapses: ordinary AND
    /// rest-day overtime, regular AND special holidays.
    /// </summary>
    private static PayrollAttendanceInput SplitAttendance() => new()
    {
        OvertimeHours      = 4m,   // ordinary overtime, priced at 1.25x
        RestDayOTHours     = 3m,   // rest-day overtime, priced at 1.69x
        HolidayRegularDays = 1m,
        HolidaySpecialDays = 2m,
        NightDiffHours     = 5m,
        AbsenceDays        = 1m,
        LateMinutes        = 30m,
        UndertimeMinutes   = 15m
    };

    private void SetupBridge(Guid employeeId, PayrollAttendanceInput attendance,
                             IReadOnlyList<Guid>? withoutSchedule = null) =>
        _attendanceBridge
            .Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                     It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceBridgeResult(
                new Dictionary<Guid, PayrollAttendanceInput> { [employeeId] = attendance },
                withoutSchedule ?? []));

    /// <summary>
    /// Wires the repositories a create-then-recompute round trip needs and returns the run that
    /// CreateAsync saved, so ComputeAsync can be pointed at it.
    /// </summary>
    private Func<PayrollRun?> SetupRoundTripRepositories(EmployeeCompensation compensation)
    {
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PayrollSettings?)null);
        _compensationRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync([compensation]);
        _allowanceRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([]);
        _loanRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        _runRepo.Setup(r => r.CountForYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        PayrollRun? saved = null;
        _runRepo.Setup(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, CancellationToken>((run, _) => saved = run)
                .Returns(Task.CompletedTask);
        _runRepo.Setup(r => r.GetWithEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => saved);

        return () => saved;
    }

    private static CreatePayrollRunRequest RoundTripRequest(Guid employeeId) => new(
        new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 20),
        PayFrequency.SemiMonthly, [new PayrollRunEmployeeInput(employeeId)]);

    [Fact]
    public async Task ComputeAsync_AfterARecompute_ReproducesEveryMonetaryFigure()
    {
        // A run whose entries include BOTH rest-day and ordinary overtime, and BOTH regular and
        // special holidays - the four values the collapsed columns cannot represent.
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };

        SetupBridge(employeeId, SplitAttendance());
        var savedRun = SetupRoundTripRepositories(compensation);

        List<PayrollRunEmployee>? recomputed = null;
        _runRepo.Setup(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(),
                    It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>(
                    (_, entries, _) => recomputed = entries.ToList())
                .Returns(Task.CompletedTask);

        await _sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);

        var run = savedRun()!;
        var created = run.Employees.Single();

        // Every figure the run was created with, captured before anything is recomputed.
        decimal regularPay         = created.RegularPay;
        decimal overtimePay        = created.OvertimePay;
        decimal holidayPay         = created.HolidayPay;
        decimal nightDiffPay       = created.NightDiffPay;
        decimal grossPay           = created.GrossPay;
        decimal sssEmployee        = created.SSSEmployee;
        decimal philHealthEmployee = created.PhilHealthEmployee;
        decimal pagIbigEmployee    = created.PagIbigEmployee;
        decimal withholdingTax     = created.WithholdingTax;
        decimal netPay             = created.NetPay;

        // The premiums the split attendance earned have to actually be in play, or the round
        // trip below would prove nothing.
        overtimePay.Should().BeGreaterThan(0m);
        holidayPay.Should().BeGreaterThan(0m);
        nightDiffPay.Should().BeGreaterThan(0m);

        await _sut.ComputeAsync(run.Id, CancellationToken.None);

        recomputed.Should().ContainSingle();
        var after = recomputed!.Single();

        // If this fails on OvertimePay or HolidayPay, FromSnapshot is mapping OvertimeHours
        // straight across instead of subtracting RestDayOTHours.
        after.RegularPay.Should().Be(regularPay);
        after.OvertimePay.Should().Be(overtimePay,
            "rest-day overtime must still be paid at 1.69x after a recompute, not repriced at 1.25x");
        after.HolidayPay.Should().Be(holidayPay,
            "the regular/special holiday split must survive the roll-up into HolidayDays");
        after.NightDiffPay.Should().Be(nightDiffPay);
        after.GrossPay.Should().Be(grossPay);
        after.SSSEmployee.Should().Be(sssEmployee);
        after.PhilHealthEmployee.Should().Be(philHealthEmployee);
        after.PagIbigEmployee.Should().Be(pagIbigEmployee);
        after.WithholdingTax.Should().Be(withholdingTax);
        after.NetPay.Should().Be(netPay);
    }

    [Fact]
    public async Task ComputeAsync_DoesNotReDeriveAttendanceFromTheBridge()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };

        SetupBridge(employeeId, SplitAttendance());
        var savedRun = SetupRoundTripRepositories(compensation);
        _runRepo.Setup(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(),
                    It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        await _sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);
        await _sut.ComputeAsync(savedRun()!.Id, CancellationToken.None);

        _attendanceBridge.Verify(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                 It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once,
            "a recompute reads the snapshot; re-deriving would let an edited punch change what someone was paid");
    }

    [Fact]
    public async Task CreateAsync_SnapshotsTheDerivedAttendanceOntoTheEntry()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };

        SetupBridge(employeeId, SplitAttendance());
        var savedRun = SetupRoundTripRepositories(compensation);

        await _sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);

        var entry = savedRun()!.Employees.Single();
        entry.AbsenceDays.Should().Be(1m);
        entry.LateMinutes.Should().Be(30m);
        entry.UndertimeMinutes.Should().Be(15m);
        entry.NightDiffHours.Should().Be(5m);
        entry.RestDayOTHours.Should().Be(3m);
        entry.HolidayRegularDays.Should().Be(1m);
        entry.HolidaySpecialDays.Should().Be(2m);

        // The roll-ups Compute writes are the totals, which is exactly why the parts above have
        // to be stored alongside them.
        entry.OvertimeHours.Should().Be(7m, "4 ordinary + 3 rest-day hours");
        entry.HolidayDays.Should().Be(3m, "1 regular + 2 special holidays");
    }

    [Fact]
    public async Task CreateAsync_RecordsHowManyEmployeesCouldNotBeScheduled()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };

        SetupBridge(employeeId, new PayrollAttendanceInput(), withoutSchedule: [employeeId]);
        var savedRun = SetupRoundTripRepositories(compensation);

        await _sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);

        var run = savedRun()!;
        run.EmployeesMissingAttendance.Should().Be(1,
            "an employee with no shift schedule is treated as fully present, which operations has to see");
        run.Employees.Single().AbsenceDays.Should().Be(0m, "an unscheduled employee is never deducted an absence");
    }

    [Fact]
    public async Task CreateAsync_WhenTheCallerSuppliesOvertimeHours_TheOverrideBeatsTheDerivedValue()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };

        SetupBridge(employeeId, SplitAttendance());
        var savedRun = SetupRoundTripRepositories(compensation);

        var request = new CreatePayrollRunRequest(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 20),
            PayFrequency.SemiMonthly, [new PayrollRunEmployeeInput(employeeId, OvertimeHours: 2m)]);

        await _sut.CreateAsync(request, CancellationToken.None);

        var entry = savedRun()!.Employees.Single();
        entry.OvertimeHours.Should().Be(2m, "a stated correction is the whole overtime figure, not an addition to it");
        entry.RestDayOTHours.Should().Be(0m);
        entry.HolidaySpecialDays.Should().Be(2m, "overriding overtime must not disturb the derived holidays");
    }

    [Fact]
    public async Task GetAsync_WhenRunNotFound_ReturnsNull()
    {
        _runRepo.Setup(r => r.GetWithEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PayrollRun?)null);

        var result = await _sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
