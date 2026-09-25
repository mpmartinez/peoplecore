using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;
using Employee = PeopleCore.Domain.Entities.Employees.Employee;
using Separation = PeopleCore.Domain.Entities.Employees.Separation;
using SeparationClearanceItem = PeopleCore.Domain.Entities.Employees.SeparationClearanceItem;
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
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<ISeparationRepository> _separations = new();
    // Pays no leave out unless a test says otherwise.
    private readonly Mock<PeopleCore.Application.Payroll.FinalPay.IFinalPayService> _finalPay = new();
    private readonly Mock<IYearEndLeaveConversion> _yearEnd = new();
    private readonly PeopleCore.Application.Tests.Common.FixedClock _clock = new("2026-12-30T02:15:00Z");
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

        // Default: nobody in any run has left. The separated-employee tests set up their own.
        _separations
            .Setup(s => s.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _sut = new PayrollRunService(
            _runRepo.Object,
            _compensationRepo.Object,
            _allowanceRepo.Object,
            _loanRepo.Object,
            _settingsRepo.Object,
            new PayrollComputationService(),
            _attendanceBridge.Object,
            _employeeRepo.Object,
            _separations.Object,
            NullLogger<PayrollRunService>.Instance,
            _finalPay.Object,
            _yearEnd.Object,
            _clock);
        _finalPay.Setup(f => f.LeavePaidOutAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
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
    // ApproveAsync - the gate between computing and paying
    // ------------------------------------------------------------------

    [Fact]
    public async Task ApproveAsync_MovesADraftRunToApproved()
    {
        var run = new PayrollRun { RunNumber = "PR-0009", Status = PayrollRunStatus.Draft };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _runRepo.Setup(r => r.UpdateAsync(run, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ApproveAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(PayrollRunStatus.Approved);
        _runRepo.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_WhenRunIsAlreadyPaid_ThrowsDomainException()
    {
        var run = new PayrollRun { RunNumber = "PR-0010", Status = PayrollRunStatus.Paid };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.ApproveAsync(run.Id, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task FullSequence_CreateComputeApproveMarkPaid_EndsWithTheRunPaid()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 20_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "ME"
        };

        var savedRun = SetupRoundTripRepositories(compensation);
        _runRepo.Setup(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(),
                    It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>(
                    (run, entries, _) => run.Employees = entries.ToList())
                .Returns(Task.CompletedTask);
        _runRepo.Setup(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        _loanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        await _sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);
        var run = savedRun()!;
        run.Status.Should().Be(PayrollRunStatus.Draft);

        await _sut.ComputeAsync(run.Id, CancellationToken.None);
        run.Status.Should().Be(PayrollRunStatus.Draft);

        await _sut.ApproveAsync(run.Id, CancellationToken.None);
        run.Status.Should().Be(PayrollRunStatus.Approved);

        await _sut.MarkPaidAsync(run.Id, CancellationToken.None);

        // This is the regression test: before ApproveAsync existed, nothing could ever move a
        // run into Approved, so MarkPaidAsync would always throw and the run could never be paid.
        run.Status.Should().Be(PayrollRunStatus.Paid);
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

        // Only a final pay's approval gives way to a recompute; a regular run's holds.
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Only draft or for-approval payroll runs can be recomputed.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
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
    public async Task CreateAsync_Pays13thMonthFromTheYearsBasicLessWhatWasAlreadyPaid()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 120_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };
        var savedRun = SetupRoundTripRepositories(compensation);

        // Paid runs earlier in the same pay year: 1,380,000 of basic and a 60,000 13th month
        // advance for this employee. Another employee's figures in the same run must not count.
        var earlier = new PayrollRun
        {
            RunNumber = "PAY-2026-000", Status = PayrollRunStatus.Paid, PayDate = new DateOnly(2026, 1, 5)
        };
        earlier.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = employeeId, RegularPay = 1_380_000m, ThirteenthMonth = 60_000m
        });
        earlier.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = Guid.NewGuid(), RegularPay = 9_000_000m, ThirteenthMonth = 500_000m
        });
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([earlier]);

        await _sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // (1,380,000 + 60,000) / 12 = 120,000 due, 60,000 already paid. Tax: 10,381.25 on the
        // regular half-month plus 7,500 on the 30,000 above what is left of the exemption (see
        // PayrollComputationServiceTests for the arithmetic).
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(60_000m);
        entry.WithholdingTax.Should().Be(17_881.25m);
    }

    [Fact]
    public async Task CreateAsync_TaxesThe13thMonthPastTheExemptionTheYearsOtherBenefitsAlsoUsed()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 120_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };
        var savedRun = SetupRoundTripRepositories(compensation);

        // As above, 1,380,000 of basic and a 60,000 13th month advance earlier in the year - and
        // this time 30,000 of leave converted past de minimis (other benefits) with it, so the
        // year's earlier Paid runs have used 60,000 + 30,000 = 90,000: all of the exemption.
        var earlier = new PayrollRun
        {
            RunNumber = "PAY-2026-000", Status = PayrollRunStatus.Paid, PayDate = new DateOnly(2026, 1, 5)
        };
        earlier.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = employeeId, RegularPay = 1_380_000m, ThirteenthMonth = 60_000m,
            LeaveConversionPay = 40_000m, LeaveConversionNonTaxable = 10_000m
        });
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([earlier]);

        await _sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // The 13th month due is still (1,380,000 + 60,000) / 12 = 120,000 less the 60,000 paid:
        // 60,000. None of the exemption is left, so all 60,000 is taxed at the margin. Half-month
        // base 60,000 - 875 SSS - 1,250 PhilHealth - 100 Pag-IBIG = 57,775, 1,386,600 a year;
        // + 60,000 = 1,446,600, still in the 25% bracket: 60,000 x 25% = 15,000.
        // 10,381.25 on the regular half-month + 15,000 = 25,381.25.
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(60_000m);
        entry.WithholdingTax.Should().Be(25_381.25m);
    }

    [Fact]
    public async Task CreateAsync_PaysNo13thMonthToAnEmployeeMarkedIneligible()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };
        var savedRun = SetupRoundTripRepositories(compensation);
        _employeeRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync([new Employee { Id = employeeId, Is13thMonthEligible = false }]);

        await _sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        savedRun()!.Employees.Single().ThirteenthMonth.Should().Be(0m);
    }

    private static CreatePayrollRunRequest With13thMonth(CreatePayrollRunRequest request) => request with
    {
        Employees = request.Employees.Select(e => e with { IncludeThirteenthMonth = true }).ToList()
    };

    [Fact]
    public async Task ComputeAsync_RepricesFromTheStoredBreakdown_NotTheCollapsedTotals()
    {
        // A double holiday, a rest day and holiday overtime: none of which the collapsed totals
        // can tell apart from a single holiday or ordinary overtime.
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 36_500m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };
        SetupBridge(employeeId, new PayrollAttendanceInput
        {
            PremiumDays =
            [
                new PremiumDayInput(WorkDayType.DoubleRegularHoliday, Days: 1m, OvertimeHours: 2m, NightDiffHours: 3m),
                new PremiumDayInput(WorkDayType.RestDay, Hours: 8m, OvertimeHours: 1m)
            ]
        });
        var savedRun = SetupRoundTripRepositories(compensation);

        List<PayrollRunEmployee>? recomputed = null;
        _runRepo.Setup(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(),
                    It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>(
                    (_, entries, _) => recomputed = entries.ToList())
                .Returns(Task.CompletedTask);

        await _sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);
        var created = savedRun()!.Employees.Single();

        await _sut.ComputeAsync(savedRun()!.Id, CancellationToken.None);
        var entry = recomputed!.Single();

        // 1,200 a day, 150 an hour: 2,400 for the double holiday + 360 for the rest day's eight hours;
        // 2 x 150 x 3.90 + 1 x 150 x 1.69 overtime; 3 x 150 x 0.30 night differential.
        created.HolidayPay.Should().Be(2_760.00m);
        created.OvertimePay.Should().Be(1_423.50m);
        created.NightDiffPay.Should().Be(135.00m);

        entry.HolidayPay.Should().Be(created.HolidayPay);
        entry.OvertimePay.Should().Be(created.OvertimePay);
        entry.NightDiffPay.Should().Be(created.NightDiffPay);
    }

    [Fact]
    public async Task CreateAsync_AnOvertimeOverride_ReplacesTheBreakdownsOvertime()
    {
        var employeeId = Guid.NewGuid();
        var compensation = new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 36_500m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };
        SetupBridge(employeeId, new PayrollAttendanceInput
        {
            PremiumDays = [new PremiumDayInput(WorkDayType.RegularHoliday, Days: 1m, OvertimeHours: 2m)]
        });
        var savedRun = SetupRoundTripRepositories(compensation);

        var request = RoundTripRequest(employeeId) with
        {
            Employees = [new PayrollRunEmployeeInput(employeeId, OvertimeHours: 5m)]
        };
        await _sut.CreateAsync(request, CancellationToken.None);

        // The five overridden hours are ordinary overtime at 125%; the holiday day itself stays.
        var entry = savedRun()!.Employees.Single();
        entry.OvertimePay.Should().Be(937.50m);
        entry.HolidayPay.Should().Be(1_200.00m);
    }

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

    // ------------------------------------------------------------------
    // GetPagedAsync - the runs list, summarised without the Employees collection
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_ProjectsTotalsWithoutCarryingEntries()
    {
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-010",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 15),
            PayDate = new DateOnly(2026, 1, 20),
            Frequency = PayFrequency.SemiMonthly,
            Status = PayrollRunStatus.Draft
        };
        // GrossPay/NetPay are computed from these; RegularPay and SSSEmployee alone are enough
        // to give each entry a distinct, known Gross/Net.
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = Guid.NewGuid(), RegularPay = 20_000m, SSSEmployee = 2_000m });
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = Guid.NewGuid(), RegularPay = 15_000m, SSSEmployee = 1_500m });

        _runRepo.Setup(r => r.GetPagedAsync(1, 20, It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<PayrollRun> { run }, 1));

        var result = await _sut.GetPagedAsync(1, 20, CancellationToken.None);

        var summary = result.Items.Single();
        summary.EmployeeCount.Should().Be(2);
        summary.TotalGrossPay.Should().Be(35_000m);
        summary.TotalNetPay.Should().Be(31_500m);

        // PayrollRunSummaryDto must have no per-employee collection at all.
        typeof(PayrollRunSummaryDto).GetProperties()
            .Should().NotContain(p => typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)
                                       && p.PropertyType != typeof(string));
    }

    [Fact]
    public async Task GetPagedAsync_PassesPagingThroughAndReportsTheTotalCount()
    {
        _runRepo.Setup(r => r.GetPagedAsync(2, 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<PayrollRun>(), 12));

        var result = await _sut.GetPagedAsync(2, 5, CancellationToken.None);

        _runRepo.Verify(r => r.GetPagedAsync(2, 5, It.IsAny<CancellationToken>()), Times.Once);
        result.TotalCount.Should().Be(12);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(5);
    }

    // ------------------------------------------------------------------
    // Final-pay runs: ComputeAsync hands them to FinalPayService
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(PayrollRunStatus.ForApproval)]
    // Approval can come before clearance is complete, and clearance is where deductions such as an
    // unreturned laptop come up - so an approved final pay can still change, and goes back for
    // approval when it does.
    [InlineData(PayrollRunStatus.Approved)]
    public async Task ComputeAsync_OnAFinalPayRun_RecomputesThroughFinalPayService_AndSendsItBackToDraft(PayrollRunStatus status)
    {
        var employeeId = Guid.NewGuid();
        var run = new PayrollRun
        {
            RunNumber = "FP-2026-001",
            RunType = PayrollRunType.FinalPay,
            Status = status,
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 13),
            PayDate = new DateOnly(2026, 3, 31),
        };
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = employeeId, RegularPay = 1m });
        var recomputed = new List<PayrollRunEmployee>
        {
            new() { PayrollRunId = run.Id, EmployeeId = employeeId, RegularPay = 12_000m }
        };

        var finalPay = new Mock<PeopleCore.Application.Payroll.FinalPay.IFinalPayService>();
        finalPay.Setup(f => f.RecomputeAsync(run, It.IsAny<CancellationToken>())).ReturnsAsync(recomputed);
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object,
            _settingsRepo.Object, new PayrollComputationService(), _attendanceBridge.Object,
            _employeeRepo.Object, _separations.Object, NullLogger<PayrollRunService>.Instance, finalPay.Object);

        await sut.ComputeAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Draft);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(run, recomputed, It.IsAny<CancellationToken>()), Times.Once);
        // The regular path (compensation lookups, the attendance bridge) is never taken.
        _compensationRepo.Verify(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComputeAsync_OnAPaidFinalPayRun_IsRefused()
    {
        var run = new PayrollRun
        {
            RunNumber = "FP-2026-001",
            RunType = PayrollRunType.FinalPay,
            Status = PayrollRunStatus.Paid,
        };
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid() });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        var finalPay = new Mock<PeopleCore.Application.Payroll.FinalPay.IFinalPayService>();
        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object,
            _settingsRepo.Object, new PayrollComputationService(), _attendanceBridge.Object,
            _employeeRepo.Object, _separations.Object, NullLogger<PayrollRunService>.Instance, finalPay.Object);

        var act = () => sut.ComputeAsync(run.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("A paid final pay can't be recomputed.");
        run.Status.Should().Be(PayrollRunStatus.Paid);
        finalPay.Verify(f => f.RecomputeAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComputeAsync_OnAFinalPayRun_WithoutAFinalPayService_RefusesRatherThanComputeItAsRegular()
    {
        // _sut is built without an IFinalPayService. Falling through to the regular path would
        // recompute the final pay as an ordinary period - dropping its separation pay, leave
        // conversion and settled tax - so it must refuse and leave the entries alone.
        var run = new PayrollRun
        {
            RunNumber = "FP-2026-001",
            RunType = PayrollRunType.FinalPay,
            Status = PayrollRunStatus.Draft,
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 13),
            PayDate = new DateOnly(2026, 3, 31),
        };
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), RegularPay = 12_000m });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object,
            _settingsRepo.Object, new PayrollComputationService(), _attendanceBridge.Object,
            _employeeRepo.Object, _separations.Object, NullLogger<PayrollRunService>.Instance);

        var act = () => sut.ComputeAsync(run.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*without an IFinalPayService*");
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
        _compensationRepo.Verify(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_CarriesTheRunType()
    {
        var run = new PayrollRun { RunNumber = "FP-2026-001", RunType = PayrollRunType.FinalPay };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var dto = await _sut.GetAsync(run.Id);

        dto!.RunType.Should().Be(PayrollRunType.FinalPay);
    }

    [Fact]
    public async Task GetAsync_CarriesEachEntrysFinalPayEarnings()
    {
        // The run page shows a final pay's leave conversion, separation and retirement pay and
        // their non-taxable part per employee; without them it shows a gross that doesn't foot.
        var entry = new PayrollRunEmployee
        {
            EmployeeId = Guid.NewGuid(),
            LeaveConversionPay = 5_000m,
            LeaveConversionNonTaxable = 3_000m,
            SeparationPay = 40_000m,
            RetirementPay = 1_000m,
            FinalPayNonTaxable = 43_000m,
        };
        var run = new PayrollRun { RunNumber = "FP-2026-001", RunType = PayrollRunType.FinalPay, Employees = [entry] };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var dto = await _sut.GetAsync(run.Id);

        var line = dto!.Employees.Single();
        line.LeaveConversionPay.Should().Be(5_000m);
        line.LeaveConversionNonTaxable.Should().Be(3_000m);
        line.SeparationPay.Should().Be(40_000m);
        line.RetirementPay.Should().Be(1_000m);
        line.FinalPayNonTaxable.Should().Be(43_000m);
    }

    [Fact]
    public async Task GetPagedAsync_CarriesTheRunType()
    {
        var run = new PayrollRun { RunNumber = "FP-2026-001", RunType = PayrollRunType.FinalPay };
        _runRepo.Setup(r => r.GetPagedAsync(1, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(((IReadOnlyList<PayrollRun>)[run], 1));

        var page = await _sut.GetPagedAsync(1, 10);

        page.Items.Single().RunType.Should().Be(PayrollRunType.FinalPay);
    }

    // ------------------------------------------------------------------
    // Final-pay runs: Mark Paid waits for the separation's clearance
    // ------------------------------------------------------------------

    /// <summary>An approved final-pay run for Maria Santos, and her separation with the given clearance items.</summary>
    private (PayrollRun Run, Separation Separation) ApprovedFinalPay(params SeparationClearanceItem[] items)
    {
        var employee = new Employee { FirstName = "Maria", LastName = "Santos" };
        var run = new PayrollRun
        {
            RunNumber = "FP-2026-001",
            RunType = PayrollRunType.FinalPay,
            Status = PayrollRunStatus.Approved,
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 13),
            PayDate = new DateOnly(2026, 3, 31),
        };
        var separation = new Separation
        {
            EmployeeId = employee.Id,
            Employee = employee,
            LastWorkingDay = new DateOnly(2026, 3, 13),
            Status = SeparationStatus.Separated,
            FinalPayRunId = run.Id,
            FinalPayRun = run,
            ClearanceItems = items.ToList(),
        };
        run.FinalPayInputs = new FinalPayInputs { PayrollRunId = run.Id, SeparationId = separation.Id };
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = employee.Id, RegularPay = 15_600m });

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _separations.Setup(s => s.GetAsync(separation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(separation);
        return (run, separation);
    }

    private static SeparationClearanceItem Item(string name, int sortOrder, bool cleared) => new()
    {
        Name = name,
        SortOrder = sortOrder,
        ClearedBy = cleared ? "hr@company.test" : null,
        ClearedAt = cleared ? new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc) : null,
    };

    [Fact]
    public async Task MarkPaidAsync_OnAFinalPayRun_RecordsTheConvertedLeaveAsUsed()
    {
        // So a year-end carry-over doesn't carry the paid-out days forward.
        var (run, separation) = ApprovedFinalPay(Item("Finance", 1, cleared: true));
        var balance = new PeopleCore.Domain.Entities.Leave.LeaveBalance { EmployeeId = separation.EmployeeId, Year = 2026, TotalDays = 5m };
        IReadOnlyList<LeavePaidOut> paidOut = [new(balance, 5m)];
        _finalPay.Setup(f => f.LeavePaidOutAsync(run, It.IsAny<CancellationToken>())).ReturnsAsync(paidOut);

        await _sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
        balance.UsedDays.Should().Be(5m);
        _runRepo.Verify(r => r.SavePaidAsync(run, It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
            It.Is<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(b => b.Single() == balance),
            It.IsAny<CancellationToken>()), Times.Once);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFinalPayRun_WhoseLeaveChanged_IsRefused_AndChangesNothing()
    {
        var (run, _) = ApprovedFinalPay(Item("Finance", 1, cleared: true));
        _finalPay.Setup(f => f.LeavePaidOutAsync(run, It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new DomainException("Maria Santos's convertible leave has changed since the final pay was computed; recompute it before paying."));

        var act = () => _sut.MarkPaidAsync(run.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*convertible leave has changed*");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAsync_OnARegularRun_PaysNoLeaveOut()
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Approved);

        await _sut.MarkPaidAsync(run.Id);

        _finalPay.Verify(f => f.LeavePaidOutAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFinalPayRun_WhileClearanceIsOutstanding_NamesTheItemsInOrder()
    {
        var (run, _) = ApprovedFinalPay(
            Item("HR exit interview", 3, cleared: false),
            Item("IT equipment", 1, cleared: true),
            Item("Finance", 2, cleared: false));

        var act = () => _sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("Clear Finance, HR exit interview before paying final pay.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFinalPayRun_WhenEveryClearanceItemIsCleared_MarksItPaid()
    {
        var (run, _) = ApprovedFinalPay(Item("IT equipment", 1, cleared: true), Item("Finance", 2, cleared: true));

        await _sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
        _runRepo.Verify(r => r.SavePaidAsync(run, It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
            It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFinalPayRun_WithNoClearanceItemsAtAll_IsRefused()
    {
        // Every item was removed: nothing has been cleared, so clearance isn't complete.
        var (run, _) = ApprovedFinalPay();

        var act = () => _sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("Add the separation's clearance items and clear them before paying final pay.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFinalPayRun_IsNotHeldToTheRegularRunRule()
    {
        // The final pay's own employee has left, and its period may well start after the last
        // working day (no salary left); only clearance gates paying it.
        var (run, separation) = ApprovedFinalPay(Item("Finance", 1, cleared: true));
        run.PeriodStart = run.PeriodEnd = separation.LastWorkingDay;
        separation.LastWorkingDay = separation.LastWorkingDay.AddDays(-1);
        _separations
            .Setup(s => s.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([separation]);

        await _sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
    }

    // ------------------------------------------------------------------
    // Regular runs keep separated employees off them
    // ------------------------------------------------------------------

    private static readonly DateOnly SecondHalfStart = new(2026, 3, 16);
    private static readonly DateOnly SecondHalfEnd = new(2026, 3, 31);

    /// <summary>A regular Mar 16-31 run holding Maria Santos and one other employee.</summary>
    private (PayrollRun Run, Employee Maria) RegularRunWithMaria(PayrollRunStatus status)
    {
        var maria = new Employee { FirstName = "Maria", LastName = "Santos" };
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-006",
            PeriodStart = SecondHalfStart,
            PeriodEnd = SecondHalfEnd,
            PayDate = new DateOnly(2026, 3, 31),
            Frequency = PayFrequency.SemiMonthly,
            Status = status,
        };
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), RegularPay = 10_000m });
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = maria.Id, RegularPay = 10_000m });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        return (run, maria);
    }

    private void Separated(Employee employee, DateOnly lastWorkingDay, PayrollRun? finalPay = null,
                           SeparationStatus status = SeparationStatus.Separated) =>
        _separations
            .Setup(s => s.GetForEmployeesAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(employee.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Separation
            {
                EmployeeId = employee.Id,
                Employee = employee,
                LastWorkingDay = lastWorkingDay,
                Status = status,
                FinalPayRunId = finalPay?.Id,
                FinalPayRun = finalPay,
            }]);

    private static PayrollRun FinalPayRun(DateOnly start, DateOnly end) => new()
    {
        RunNumber = "FP-2026-001",
        RunType = PayrollRunType.FinalPay,
        PeriodStart = start,
        PeriodEnd = end,
        PayDate = new DateOnly(2026, 4, 10),
        Status = PayrollRunStatus.Draft,
    };

    /// <summary>
    /// Creates, computes, approves or marks paid a regular Mar 16-31 run holding Maria - each step
    /// the separated-employee rule is checked at.
    /// </summary>
    private (Func<Task> Act, PayrollRun Run, Employee Maria) StepOnMariasRun(string step)
    {
        var status = step == "MarkPaid" ? PayrollRunStatus.Approved : PayrollRunStatus.Draft;
        var (run, maria) = RegularRunWithMaria(status);
        Func<Task> act = step switch
        {
            "Create" => () => _sut.CreateAsync(new CreatePayrollRunRequest(
                SecondHalfStart, SecondHalfEnd, new DateOnly(2026, 3, 31), PayFrequency.SemiMonthly,
                run.Employees.Select(e => new PayrollRunEmployeeInput(e.EmployeeId)).ToList())),
            "Compute" => () => _sut.ComputeAsync(run.Id),
            "Approve" => () => _sut.ApproveAsync(run.Id),
            "MarkPaid" => () => _sut.MarkPaidAsync(run.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
        return (act, run, maria);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Compute")]
    [InlineData("Approve")]
    [InlineData("MarkPaid")]
    public async Task ARegularRun_HoldingSomeoneWhoLeftBeforeItsPeriod_IsRefused(string step)
    {
        var (act, run, maria) = StepOnMariasRun(step);
        Separated(maria, lastWorkingDay: new DateOnly(2026, 3, 13));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos left on Mar 13, 2026; take them off this payroll - their pay goes in final pay.");
        run.Status.Should().Be(step == "MarkPaid" ? PayrollRunStatus.Approved : PayrollRunStatus.Draft);
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Compute")]
    [InlineData("Approve")]
    [InlineData("MarkPaid")]
    public async Task ARegularRun_OverlappingSomeonesFinalPay_IsRefused(string step)
    {
        // Leaves Mar 20, inside the run; their final pay already pays Mar 16-20.
        var (act, _, maria) = StepOnMariasRun(step);
        Separated(maria, lastWorkingDay: new DateOnly(2026, 3, 20),
                  finalPay: FinalPayRun(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 20)));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's final pay has been started; the rest of their pay goes there. Take them off this payroll.");
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARegularRun_HoldingSomeoneWhoseNoticeGivesALastDayBeforeIt_IsRefused()
    {
        // Not marked separated yet, but their last working day is already before the period.
        var (act, _, maria) = StepOnMariasRun("Approve");
        Separated(maria, lastWorkingDay: new DateOnly(2026, 3, 13), status: SeparationStatus.NoticeGiven);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos left on Mar 13, 2026; take them off this payroll - their pay goes in final pay.");
    }

    /// <summary>
    /// A final pay whose own separation comes back from the lookup: its final-pay run is the run
    /// itself, which overlaps its own period - so only the exemption lets it through.
    /// </summary>
    private (PayrollRun Run, Separation Separation) DraftFinalPayItsOwnSeparationOverlaps()
    {
        var (run, separation) = ApprovedFinalPay(Item("Finance", 1, cleared: true));
        run.Status = PayrollRunStatus.Draft;
        _separations
            .Setup(s => s.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([separation]);
        return (run, separation);
    }

    [Fact]
    public async Task ApproveAsync_OnAFinalPayRun_IsNotHeldToTheRegularRunRule()
    {
        var (run, _) = DraftFinalPayItsOwnSeparationOverlaps();

        await _sut.ApproveAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task ComputeAsync_OnAFinalPayRun_IsNotHeldToTheRegularRunRule()
    {
        var (run, _) = DraftFinalPayItsOwnSeparationOverlaps();
        var recomputed = new List<PayrollRunEmployee> { new() { PayrollRunId = run.Id, EmployeeId = run.Employees[0].EmployeeId } };
        var finalPay = new Mock<PeopleCore.Application.Payroll.FinalPay.IFinalPayService>();
        finalPay.Setup(f => f.RecomputeAsync(run, It.IsAny<CancellationToken>())).ReturnsAsync(recomputed);
        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object,
            _settingsRepo.Object, new PayrollComputationService(), _attendanceBridge.Object,
            _employeeRepo.Object, _separations.Object, NullLogger<PayrollRunService>.Instance, finalPay.Object);

        await sut.ComputeAsync(run.Id);

        _runRepo.Verify(r => r.ReplaceEntriesAsync(run, recomputed, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARegularRun_HoldingSomeoneLeavingDuringItsPeriod_WithoutFinalPayYet_CanBeApproved()
    {
        // Their last working day is inside the run, so the run still pays them for Mar 16-20.
        var (act, run, maria) = StepOnMariasRun("Approve");
        Separated(maria, lastWorkingDay: new DateOnly(2026, 3, 20));

        await act();

        run.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task ARegularRun_HoldingSomeoneWhoLeftTheDayBeforeIt_IsRefused()
    {
        var (act, _, maria) = StepOnMariasRun("Approve");
        Separated(maria, lastWorkingDay: new DateOnly(2026, 3, 15),
                  finalPay: FinalPayRun(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15)));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos left on Mar 15, 2026; take them off this payroll - their pay goes in final pay.");
    }

    [Theory]
    [InlineData("Create", PayrollRunStatus.Draft)]
    [InlineData("Compute", PayrollRunStatus.ForApproval)]
    [InlineData("Approve", PayrollRunStatus.Approved)]
    [InlineData("MarkPaid", PayrollRunStatus.Paid)]
    public async Task ARegularRun_BeforeSomeonesFinalPay_IsRefused(string step, PayrollRunStatus finalPayStatus)
    {
        // Leaves Apr 10, after the run, and their final pay for Apr 1-10 has been started. The
        // final pay's 13th month, tax settle and contributions all take it as their last pay, so
        // a Mar 16-31 run paid after it would fall outside all three: the rest of their pay goes
        // in the final pay, whatever the dates and whatever the final pay's status.
        var (act, run, maria) = StepOnMariasRun(step);
        var finalPay = FinalPayRun(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 10));
        finalPay.Status = finalPayStatus;
        Separated(maria, lastWorkingDay: new DateOnly(2026, 4, 10), finalPay: finalPay);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's final pay has been started; the rest of their pay goes there. Take them off this payroll.");
        run.Status.Should().Be(step == "MarkPaid" ? PayrollRunStatus.Approved : PayrollRunStatus.Draft);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARegularRun_CreatedAfterSomeonesFinalPayStarted_IsRefused_EvenBeforeTheFinalPeriod()
    {
        // Scenario B: the last working day is Dec 31 and the final pay (Dec 16-31, with the 13th
        // month) has been started. A Dec 1-15 run with the 13th month, created afterwards, lies
        // wholly before the final period - but the final pay's 13th month already counts every
        // Paid run of the year, so paying this one too would pay the 13th month twice.
        var maria = new Employee { FirstName = "Maria", LastName = "Santos" };
        Separated(maria, lastWorkingDay: new DateOnly(2026, 12, 31),
                  finalPay: FinalPayRun(new DateOnly(2026, 12, 16), new DateOnly(2026, 12, 31)));

        var act = () => _sut.CreateAsync(new CreatePayrollRunRequest(
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 15), new DateOnly(2026, 12, 15), PayFrequency.SemiMonthly,
            [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: true)]));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's final pay has been started; the rest of their pay goes there. Take them off this payroll.");
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // RemoveEmployeeAsync - taking someone off a run
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.Processing)]
    [InlineData(PayrollRunStatus.ForApproval)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task RemoveEmployeeAsync_TakesThemOffTheRun_AndSendsItBackToDraft(PayrollRunStatus status)
    {
        var (run, maria) = RegularRunWithMaria(status);
        var entry = RemovingActuallyRemoves(run, maria);

        var dto = await _sut.RemoveEmployeeAsync(run.Id, maria.Id);

        _runRepo.Verify(r => r.RemoveEntryAsync(run, entry, It.IsAny<CancellationToken>()), Times.Once);
        run.Status.Should().Be(PayrollRunStatus.Draft);
        dto.Status.Should().Be(PayrollRunStatus.Draft);
        dto.Employees.Should().ContainSingle().Which.EmployeeId.Should().NotBe(maria.Id);
    }

    /// <summary>Makes the mocked repository drop the entry from the run, as the real one does.</summary>
    private PayrollRunEmployee RemovingActuallyRemoves(PayrollRun run, Employee employee)
    {
        var entry = run.Employees.Single(e => e.EmployeeId == employee.Id);
        _runRepo.Setup(r => r.RemoveEntryAsync(run, entry, It.IsAny<CancellationToken>()))
                .Callback(() => run.Employees.Remove(entry))
                .Returns(Task.CompletedTask);
        return entry;
    }

    [Fact]
    public async Task AnApprovedRun_HoldingSomeoneSeparatedAfterApproval_IsFreedByTakingThemOff()
    {
        // PAY-2026-006 (Mar 16-31) is approved; HR then records Maria's separation with a last
        // working day of Mar 13. Paying the run is refused, and taking her off must still be
        // possible, or the run could never be paid.
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Approved);
        RemovingActuallyRemoves(run, maria);
        Separated(maria, lastWorkingDay: new DateOnly(2026, 3, 13));

        var pay = () => _sut.MarkPaidAsync(run.Id);
        (await pay.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos left on Mar 13, 2026; take them off this payroll - their pay goes in final pay.");

        await _sut.RemoveEmployeeAsync(run.Id, maria.Id);
        run.Status.Should().Be(PayrollRunStatus.Draft);

        await _sut.ApproveAsync(run.Id);
        await _sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
        run.Employees.Should().ContainSingle().Which.EmployeeId.Should().NotBe(maria.Id);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_OnAPaidRun_IsRefused()
    {
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Paid);

        var act = () => _sut.RemoveEmployeeAsync(run.Id, maria.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("A paid payroll run can't be changed.");
        _runRepo.Verify(r => r.RemoveEntryAsync(It.IsAny<PayrollRun>(), It.IsAny<PayrollRunEmployee>(),
                                                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_TheLastEmployee_IsRefused()
    {
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Draft);
        run.Employees.RemoveAll(e => e.EmployeeId != maria.Id);

        var act = () => _sut.RemoveEmployeeAsync(run.Id, maria.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("A payroll run needs at least one employee.");
        _runRepo.Verify(r => r.RemoveEntryAsync(It.IsAny<PayrollRun>(), It.IsAny<PayrollRunEmployee>(),
                                                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_OnAFinalPayRun_IsRefused()
    {
        var (run, separation) = ApprovedFinalPay(Item("Finance", 1, cleared: false));
        run.Status = PayrollRunStatus.Draft;

        var act = () => _sut.RemoveEmployeeAsync(run.Id, separation.EmployeeId);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("A final-pay run's employee can't be removed.");
        _runRepo.Verify(r => r.RemoveEntryAsync(It.IsAny<PayrollRun>(), It.IsAny<PayrollRunEmployee>(),
                                                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_SomeoneWithNoShiftSchedule_NoLongerCountsAsMissingAttendance()
    {
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Draft);
        run.EmployeesMissingAttendance = 2;
        RemovingActuallyRemoves(run, maria);
        SetupBridge(maria.Id, new PayrollAttendanceInput(), withoutSchedule: [maria.Id]);

        var dto = await _sut.RemoveEmployeeAsync(run.Id, maria.Id);

        run.EmployeesMissingAttendance.Should().Be(1);
        dto.EmployeesMissingAttendance.Should().Be(1);
        _attendanceBridge.Verify(b => b.BuildAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { maria.Id })),
            SecondHalfStart, SecondHalfEnd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_SomeoneWithAShiftSchedule_LeavesTheMissingAttendanceCount()
    {
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Draft);
        run.EmployeesMissingAttendance = 1;
        RemovingActuallyRemoves(run, maria);
        SetupBridge(maria.Id, new PayrollAttendanceInput());

        await _sut.RemoveEmployeeAsync(run.Id, maria.Id);

        run.EmployeesMissingAttendance.Should().Be(1);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_WhenNobodyWasMissingAttendance_DoesNotAskTheBridge()
    {
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Draft);
        RemovingActuallyRemoves(run, maria);

        await _sut.RemoveEmployeeAsync(run.Id, maria.Id);

        run.EmployeesMissingAttendance.Should().Be(0);
        _attendanceBridge.Verify(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                                   It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveEmployeeAsync_SomeoneNotOnTheRun_ThrowsKeyNotFound()
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Draft);

        var act = () => _sut.RemoveEmployeeAsync(run.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RemoveEmployeeAsync_WhenTheRunDoesNotExist_ThrowsKeyNotFound()
    {
        var act = () => _sut.RemoveEmployeeAsync(Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------
    // Year-end leave conversion: a December regular run pays out unused convertible leave
    // ------------------------------------------------------------------

    private static readonly DateOnly DecemberStart = new(2026, 12, 1);
    private static readonly DateOnly DecemberEnd = new(2026, 12, 31);

    /// <summary>
    /// Maria Santos at 36,500 a month, paid monthly: 36,500 x 12 / 365 = 1,200.00 a day, the
    /// engine's daily rate under the default 365 factor (no payroll settings on file).
    /// </summary>
    private (Employee Maria, Func<PayrollRun?> SavedRun) MariaAt36500()
    {
        var maria = new Employee { FirstName = "Maria", LastName = "Santos", Is13thMonthEligible = true };
        var savedRun = SetupRoundTripRepositories(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 36_500m, PayFrequency = PayFrequency.Monthly, TaxCode = "S"
        });
        _employeeRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync([maria]);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        _runRepo.Setup(r => r.GetLeaveConversionsInYearAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        return (maria, savedRun);
    }

    /// <summary>A Service Incentive Leave balance for the employee, counting towards the de minimis ten days.</summary>
    private static PeopleCore.Domain.Entities.Leave.LeaveBalance SilBalance(Guid employeeId, decimal totalDays)
    {
        var sil = new PeopleCore.Domain.Entities.Leave.LeaveType
        {
            Name = "Service Incentive Leave", Code = "SIL", ConvertsAtYearEnd = true, CountsAsVacationForDeMinimis = true
        };
        return new PeopleCore.Domain.Entities.Leave.LeaveBalance
        {
            EmployeeId = employeeId, LeaveType = sil, LeaveTypeId = sil.Id, Year = 2026, TotalDays = totalDays
        };
    }

    private void ConvertibleDays(Guid employeeId, params LeavePaidOut[] days)
        => _yearEnd.Setup(y => y.DaysAsync(employeeId, 2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(days);

    private static CreatePayrollRunRequest DecemberRequest(Guid employeeId, bool includeThirteenthMonth = false) => new(
        DecemberStart, DecemberEnd, new DateOnly(2026, 12, 29), PayFrequency.Monthly,
        [new PayrollRunEmployeeInput(employeeId, IncludeThirteenthMonth: includeThirteenthMonth)],
        IncludeLeaveConversion: true);

    [Fact]
    public async Task CreateAsync_ADecemberRunWithTheFlag_PaysOutTheUnusedSilAtTheEntrysDailyRate()
    {
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));

        var dto = await _sut.CreateAsync(DecemberRequest(maria.Id));

        // 3 SIL days x 1,200.00 = 3,600.00, all inside the ten de minimis days.
        var run = savedRun()!;
        run.IncludesLeaveConversion.Should().BeTrue();
        dto.IncludesLeaveConversion.Should().BeTrue();
        var entry = run.Employees.Single();
        entry.DailyRate.Should().Be(1_200m);
        entry.LeaveConversionPay.Should().Be(3_600m);
        entry.LeaveConversionNonTaxable.Should().Be(3_600m);
        entry.LeaveConversionOtherBenefits.Should().Be(0m);
        entry.FinalPayNonTaxable.Should().Be(3_600m);
        entry.FinalPayTaxable.Should().Be(0m);
        dto.Employees.Single().LeaveConversionPay.Should().Be(3_600m);
    }

    [Fact]
    public async Task CreateAsync_WithoutTheFlag_ConvertsNothing()
    {
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));

        await _sut.CreateAsync(DecemberRequest(maria.Id) with { IncludeLeaveConversion = false });

        savedRun()!.IncludesLeaveConversion.Should().BeFalse();
        savedRun()!.Employees.Single().LeaveConversionPay.Should().Be(0m);
        _yearEnd.Verify(y => y.DaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ANovemberRunWithTheFlag_IsRefused()
    {
        var (maria, _) = MariaAt36500();
        var november = DecemberRequest(maria.Id) with
        {
            PeriodStart = new DateOnly(2026, 11, 1), PeriodEnd = new DateOnly(2026, 11, 30),
            PayDate = new DateOnly(2026, 11, 30)
        };

        var act = () => _sut.CreateAsync(november);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("Year-end leave conversion goes on a December payroll.");
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ASecondDecemberRunWithTheFlag_ForSomeoneAlreadyConverted_IsRefused()
    {
        var (maria, _) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        _runRepo.Setup(r => r.GetLeaveConversionsInYearAsync(2026,
                    It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(maria.Id)), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LeaveConvertedInRun(maria.Id, "PAY-2026-023")]);

        var act = () => _sut.CreateAsync(DecemberRequest(maria.Id));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("Maria Santos's leave for 2026 was already converted in PAY-2026-023.");
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComputeAsync_OnAFlaggedRun_ReproducesTheConversion_AndLooksOnlyAtOtherRuns()
    {
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        List<PayrollRunEmployee>? recomputed = null;
        _runRepo.Setup(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(),
                    It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>(
                    (_, entries, _) => recomputed = entries.ToList())
                .Returns(Task.CompletedTask);

        await _sut.CreateAsync(DecemberRequest(maria.Id));
        var created = savedRun()!.Employees.Single();

        await _sut.ComputeAsync(savedRun()!.Id);

        var entry = recomputed!.Single();
        entry.LeaveConversionPay.Should().Be(created.LeaveConversionPay).And.Be(3_600m);
        entry.LeaveConversionNonTaxable.Should().Be(created.LeaveConversionNonTaxable);
        entry.FinalPayNonTaxable.Should().Be(created.FinalPayNonTaxable);
        entry.WithholdingTax.Should().Be(created.WithholdingTax);
        entry.NetPay.Should().Be(created.NetPay);
        // Both computes ask about the year's other runs only - never the run itself.
        _runRepo.Verify(r => r.GetLeaveConversionsInYearAsync(2026, It.IsAny<IReadOnlyCollection<Guid>>(),
            savedRun()!.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateAsync_ADecemberConversion_WithNo13thMonth_TaxesTheOtherBenefitsOnceTheExemptionIsUsed()
    {
        var (maria, savedRun) = MariaAt36500();
        // 12 SIL days: the first 10 are de minimis (12,000.00), the other 2 other benefits (2,400.00).
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 12m), 12m));
        // Earlier in the year: a 60,000 13th month advance and 30,000 of leave past de minimis -
        // 90,000, the whole exemption.
        var earlier = new PayrollRun
        {
            RunNumber = "PAY-2026-010", Status = PayrollRunStatus.Paid, PayDate = new DateOnly(2026, 6, 30)
        };
        earlier.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = maria.Id, RegularPay = 36_500m, ThirteenthMonth = 60_000m,
            LeaveConversionPay = 42_000m, LeaveConversionNonTaxable = 12_000m
        });
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([earlier]);

        await _sut.CreateAsync(DecemberRequest(maria.Id, includeThirteenthMonth: false));

        // Contributions on 36,500: SSS 1,750 + PhilHealth 912.50 + Pag-IBIG 200 = 2,862.50.
        // Base 36,500 - 2,862.50 = 33,637.50 a month, 403,650 a year: 22,500 + 20% x 3,650 = 23,230
        // a year, 1,935.83 a month. None of the exemption is left, so the 2,400 of other benefits
        // is taxed at the margin: 406,050 is still in the 20% bracket, 2,400 x 20% = 480.00.
        // 1,935.83 + 480.00 = 2,415.83. (With the exemption unused it would stay 1,935.83.)
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(0m);
        entry.LeaveConversionPay.Should().Be(14_400m);
        entry.LeaveConversionNonTaxable.Should().Be(12_000m);
        entry.LeaveConversionOtherBenefits.Should().Be(2_400m);
        entry.WithholdingTax.Should().Be(2_415.83m);
    }

    /// <summary>An approved, flagged December run whose entry for Maria converted 3 SIL days at 1,200.</summary>
    private (PayrollRun Run, Employee Maria, EmployeeLoan Loan) ApprovedDecemberConversion()
    {
        var maria = new Employee { FirstName = "Maria", LastName = "Santos" };
        var loan = new EmployeeLoan
        {
            EmployeeId = maria.Id, LoanType = LoanType.SSSLoan, TotalAmount = 10_000m,
            MonthlyDeduction = 1_000m, RemainingBalance = 5_000m, IsActive = true
        };
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-024", PeriodStart = DecemberStart, PeriodEnd = DecemberEnd,
            PayDate = new DateOnly(2026, 12, 29), Frequency = PayFrequency.Monthly,
            Status = PayrollRunStatus.Approved, IncludesLeaveConversion = true
        };
        var entry = new PayrollRunEmployee
        {
            PayrollRunId = run.Id, EmployeeId = maria.Id, Employee = maria, RegularPay = 36_500m, DailyRate = 1_200m,
            LeaveConversionPay = 3_600m, LeaveConversionNonTaxable = 3_600m, FinalPayNonTaxable = 3_600m,
            LoanDeductions = 1_000m
        };
        entry.LoanDeductionLines.Add(new PayrollLoanDeduction { EmployeeLoanId = loan.Id, Amount = 1_000m });
        run.Employees.Add(entry);
        // Someone with nothing to convert: never looked up.
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), RegularPay = 20_000m });

        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _loanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([loan]);
        return (run, maria, loan);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFlaggedRun_RecordsTheConvertedDaysAsUsed()
    {
        var (run, maria, loan) = ApprovedDecemberConversion();
        var paidOut = new LeavePaidOut(SilBalance(maria.Id, 3m), 3m);
        ConvertibleDays(maria.Id, paidOut);

        await _sut.MarkPaidAsync(run.Id);

        // The status, the loan and the balance's 3 more used days go out in one save.
        run.Status.Should().Be(PayrollRunStatus.Paid);
        loan.RemainingBalance.Should().Be(4_000m);
        paidOut.Balance.UsedDays.Should().Be(3m);
        _runRepo.Verify(r => r.SavePaidAsync(run,
            It.Is<IReadOnlyCollection<EmployeeLoan>>(l => l.Single() == loan),
            It.Is<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(b => b.Single() == paidOut.Balance),
            It.IsAny<CancellationToken>()), Times.Once);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _loanRepo.Verify(r => r.UpdateRangeAsync(It.IsAny<IEnumerable<EmployeeLoan>>(), It.IsAny<CancellationToken>()), Times.Never);
        _yearEnd.Verify(y => y.DaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaidAsync_StampsTheRunAndTheLeaveBalances_FromTheInjectedClock()
    {
        var (run, maria, _) = ApprovedDecemberConversion();
        var paidOut = new LeavePaidOut(SilBalance(maria.Id, 3m), 3m);
        ConvertibleDays(maria.Id, paidOut);

        await _sut.MarkPaidAsync(run.Id);

        var paidAt = new DateTime(2026, 12, 30, 2, 15, 0, DateTimeKind.Utc);
        paidOut.Balance.UpdatedAt.Should().Be(paidAt);
        run.UpdatedAt.Should().Be(paidAt);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFlaggedRun_WhoseLeaveChangedSinceCompute_IsRefused_AndChangesNothing()
    {
        // A leave request filed after compute holds one of the three days: 2 x 1,200 = 2,400, not 3,600.
        var (run, maria, loan) = ApprovedDecemberConversion();
        var balance = SilBalance(maria.Id, 3m);
        ConvertibleDays(maria.Id, new LeavePaidOut(balance, 2m));

        var act = () => _sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's convertible leave has changed since this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        loan.RemainingBalance.Should().Be(5_000m);
        loan.IsActive.Should().BeTrue();
        balance.UsedDays.Should().Be(0m);
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAnUnflaggedRun_LooksUpNoYearEndLeave()
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Approved);

        await _sut.MarkPaidAsync(run.Id);

        _yearEnd.Verify(y => y.DaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComputeAsync_OnAFinalPayRun_NeverConvertsOnTheRegularPath()
    {
        // Final pay converts its own leave; the year-end conversion is never applied to it.
        var run = FinalPayRun(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 15));
        run.IncludesLeaveConversion = true;
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid() });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _finalPay.Setup(f => f.RecomputeAsync(run, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await _sut.ComputeAsync(run.Id);

        _yearEnd.Verify(y => y.DaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_AndGetPagedAsync_CarryTheLeaveConversionFlag()
    {
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-024", PeriodStart = DecemberStart, PeriodEnd = DecemberEnd,
            PayDate = new DateOnly(2026, 12, 29), IncludesLeaveConversion = true
        };
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _runRepo.Setup(r => r.GetPagedAsync(1, 20, It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<PayrollRun> { run }, 1));

        (await _sut.GetAsync(run.Id))!.IncludesLeaveConversion.Should().BeTrue();
        (await _sut.GetPagedAsync(1, 20)).Items.Single().IncludesLeaveConversion.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Year-end leave conversion: recovering a run whose leave changed, and turning it on or off
    // ------------------------------------------------------------------

    private void ConvertibleDaysIn(int year, Guid employeeId, params LeavePaidOut[] days)
        => _yearEnd.Setup(y => y.DaysAsync(employeeId, year, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(days);

    /// <summary>A recompute's entries become the run's, as the repository leaves them.</summary>
    private void RecomputesInPlace()
        => _runRepo.Setup(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(),
                    It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>(
                    (run, entries, _) => run.Employees = entries.ToList())
                .Returns(Task.CompletedTask);

    /// <summary>An earlier Paid run of the pay year that paid Maria some leave as de minimis.</summary>
    private static PayrollRun PaidConversion(Guid employeeId, DateOnly periodEnd, DateOnly payDate, decimal deMinimisDays)
    {
        var run = new PayrollRun
        {
            RunNumber = "PAY-EARLIER", PeriodStart = periodEnd.AddDays(-15), PeriodEnd = periodEnd, PayDate = payDate,
            Status = PayrollRunStatus.Paid, IncludesLeaveConversion = true
        };
        run.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = run.Id, EmployeeId = employeeId, DailyRate = 1_200m,
            LeaveConversionPay = deMinimisDays * 1_200m, LeaveConversionNonTaxable = deMinimisDays * 1_200m
        });
        return run;
    }

    [Fact]
    public async Task ComputeAsync_OnAnApprovedFlaggedRun_RecomputesIt_AndSendsItBackToDraft()
    {
        // The way back from a Mark Paid refused because the leave changed: recompute, approve again.
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        RecomputesInPlace();
        await _sut.CreateAsync(DecemberRequest(maria.Id));
        var run = savedRun()!;
        run.Status = PayrollRunStatus.Approved;
        // Since then, a request was filed for one of the three days.
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 2m));

        await _sut.ComputeAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Draft);
        run.Employees.Single().LeaveConversionPay.Should().Be(2_400m);   // 2 x 1,200
    }

    [Fact]
    public async Task ApproveAsync_OnAFlaggedRun_WhoseLeaveChangedSinceCompute_IsRefused()
    {
        var (run, maria, _) = ApprovedDecemberConversion();
        run.Status = PayrollRunStatus.ForApproval;
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 2m));

        var act = () => _sut.ApproveAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's convertible leave has changed since this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.ForApproval);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_OnAFlaggedRun_WhoseLeaveStillPrices_ApprovesIt()
    {
        var (run, maria, _) = ApprovedDecemberConversion();
        run.Status = PayrollRunStatus.ForApproval;
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));

        await _sut.ApproveAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAFlaggedRun_WhoseDeMinimisSplitChanged_IsRefused_EvenAtTheSameTotal()
    {
        // The entry paid 3 days as 3,600 of de minimis. Since then another 2026 run was paid that
        // used 8 of the year's 10 de minimis days, so the same 3 days now split 2 x 1,200 = 2,400
        // de minimis and 1,200 other benefits - the same 3,600 in all, taxed differently.
        var (run, maria, loan) = ApprovedDecemberConversion();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([PaidConversion(maria.Id, new DateOnly(2026, 12, 15), new DateOnly(2026, 12, 20), deMinimisDays: 8m)]);

        var act = () => _sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's convertible leave has changed since this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        loan.RemainingBalance.Should().Be(5_000m);
    }

    [Fact]
    public async Task CreateAsync_TheTenDeMinimisDaysArePerTaxYear_SoASecondConversionPaidInTheSameYearGetsTheRest()
    {
        // The 2026 conversion was paid on 2027-01-05 (a Dec 16-31 run) and paid 6 days as de
        // minimis - 7,200 at 1,200. December 2027's conversion is paid in the same tax year, 2027.
        var (maria, savedRun) = MariaAt36500();
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2027, It.IsAny<CancellationToken>()))
                .ReturnsAsync([PaidConversion(maria.Id, new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 5), deMinimisDays: 6m)]);
        ConvertibleDaysIn(2027, maria.Id, new LeavePaidOut(SilBalance(maria.Id, 5m), 5m));

        await _sut.CreateAsync(DecemberRequest(maria.Id) with
        {
            PeriodStart = new DateOnly(2027, 12, 1), PeriodEnd = new DateOnly(2027, 12, 31), PayDate = new DateOnly(2027, 12, 29)
        });

        // 5 SIL days x 1,200 = 6,000. Only 10 - 6 = 4 days are left as de minimis: 4,800;
        // the fifth day is other benefits: 1,200.
        var entry = savedRun()!.Employees.Single();
        entry.LeaveConversionPay.Should().Be(6_000m);
        entry.LeaveConversionNonTaxable.Should().Be(4_800m);
        entry.LeaveConversionOtherBenefits.Should().Be(1_200m);
    }

    [Fact]
    public async Task CreateAsync_ADec16To31RunPaidInJanuary_ConvertsThePeriodsYear_ButReadsThePayYearsExemption()
    {
        // Period Dec 16-31, 2026, paid Jan 5, 2027: the leave converted is 2026's, and 2026's
        // conversions are what the once-a-year check looks at; the 90,000 exemption and the ten
        // de minimis days are the tax year's - the pay date's, 2027.
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));

        await _sut.CreateAsync(new CreatePayrollRunRequest(
            new DateOnly(2026, 12, 16), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 5), PayFrequency.SemiMonthly,
            [new PayrollRunEmployeeInput(maria.Id)], IncludeLeaveConversion: true));

        savedRun()!.Employees.Single().LeaveConversionPay.Should().Be(3_600m);
        _yearEnd.Verify(y => y.DaysAsync(maria.Id, 2026, It.IsAny<CancellationToken>()), Times.Once);
        _runRepo.Verify(r => r.GetLeaveConversionsInYearAsync(2026, It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _runRepo.Verify(r => r.GetPaidRunsInYearAsync(2027, It.IsAny<CancellationToken>()), Times.Once);
        _runRepo.Verify(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLeaveConversionAsync_TurningItOn_ForADecemberRun_RecomputesWithTheConversion()
    {
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        RecomputesInPlace();
        await _sut.CreateAsync(DecemberRequest(maria.Id) with { IncludeLeaveConversion = false });
        var run = savedRun()!;

        var dto = await _sut.SetLeaveConversionAsync(run.Id, include: true);

        run.IncludesLeaveConversion.Should().BeTrue();
        run.Employees.Single().LeaveConversionPay.Should().Be(3_600m);
        dto.IncludesLeaveConversion.Should().BeTrue();
        dto.Employees.Single().LeaveConversionPay.Should().Be(3_600m);
        dto.Status.Should().Be(PayrollRunStatus.Draft);
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.ForApproval)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task SetLeaveConversionAsync_TurningItOff_RecomputesWithout_AndSendsTheRunBackToDraft(PayrollRunStatus status)
    {
        // An abandoned flagged run no longer holds its employees' conversion for the year.
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        RecomputesInPlace();
        await _sut.CreateAsync(DecemberRequest(maria.Id));
        var run = savedRun()!;
        run.Status = status;

        var dto = await _sut.SetLeaveConversionAsync(run.Id, include: false);

        run.IncludesLeaveConversion.Should().BeFalse();
        run.Status.Should().Be(PayrollRunStatus.Draft);
        run.Employees.Single().LeaveConversionPay.Should().Be(0m);
        dto.IncludesLeaveConversion.Should().BeFalse();
        _yearEnd.Verify(y => y.DaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLeaveConversionAsync_TurningItOn_ForANovemberRun_IsRefused_AndChangesNothing()
    {
        var (maria, savedRun) = MariaAt36500();
        await _sut.CreateAsync(DecemberRequest(maria.Id) with
        {
            PeriodStart = new DateOnly(2026, 11, 1), PeriodEnd = new DateOnly(2026, 11, 30),
            PayDate = new DateOnly(2026, 11, 30), IncludeLeaveConversion = false
        });
        var run = savedRun()!;

        var act = () => _sut.SetLeaveConversionAsync(run.Id, include: true);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("Year-end leave conversion goes on a December payroll.");
        run.IncludesLeaveConversion.Should().BeFalse();
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetLeaveConversionAsync_OnAPaidRun_IsRefused(bool include)
    {
        var (run, _, _) = ApprovedDecemberConversion();
        run.Status = PayrollRunStatus.Paid;

        var act = () => _sut.SetLeaveConversionAsync(run.Id, include);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("A paid payroll run can't be changed.");
        run.IncludesLeaveConversion.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, "Year-end leave conversion goes on a December payroll.")]
    [InlineData(false, "A final pay's leave conversion can't be changed here.")]
    public async Task SetLeaveConversionAsync_OnAFinalPayRun_IsRefused(bool include, string message)
    {
        // Final pay converts its own leave: turning the year-end conversion on is the December
        // rule's to refuse; turning it off isn't something this endpoint can do to a final pay.
        var run = FinalPayRun(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 15));
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid() });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.SetLeaveConversionAsync(run.Id, include);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(message);
        run.Status.Should().Be(PayrollRunStatus.Draft);
        _finalPay.Verify(f => f.RecomputeAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "This payroll already converts unused leave.")]
    [InlineData(false, "This payroll already doesn't convert unused leave.")]
    public async Task SetLeaveConversionAsync_ThatChangesNothing_IsRefused_SoItCantRecomputeAnApprovedRun(
        bool include, string message)
    {
        // Without this, "turning off" the conversion on an approved run that never had it would
        // recompute that run - which ComputeAsync refuses for an approved unflagged run.
        var (run, _, _) = ApprovedDecemberConversion();
        run.IncludesLeaveConversion = include;

        var act = () => _sut.SetLeaveConversionAsync(run.Id, include);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(message);
        run.Status.Should().Be(PayrollRunStatus.Approved);
        run.IncludesLeaveConversion.Should().Be(include);
        _yearEnd.Verify(y => y.DaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLeaveConversionAsync_WhenTheRunDoesNotExist_ThrowsKeyNotFound()
    {
        var act = () => _sut.SetLeaveConversionAsync(Guid.NewGuid(), include: true);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------
    // The 13th month, switched on or off after a regular run is created
    // ------------------------------------------------------------------

    /// <summary>
    /// Maria's regular September run at 36,500 a month, created without the 13th month: an
    /// advance is allowed in any month.
    /// </summary>
    private async Task<PayrollRun> SeptemberRunForMaria(bool includeThirteenthMonth)
    {
        var (maria, saved) = MariaAt36500();
        RecomputesInPlace();
        await _sut.CreateAsync(new CreatePayrollRunRequest(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), PayFrequency.Monthly,
            [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: includeThirteenthMonth)]));
        return saved()!;
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.ForApproval)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task SetThirteenthMonthAsync_TurningItOn_RecomputesWithIt_AndSendsTheRunBackToDraft(PayrollRunStatus status)
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: false);
        run.Employees.Single().ThirteenthMonth.Should().Be(0m);
        run.Status = status;

        var dto = await _sut.SetThirteenthMonthAsync(run.Id, include: true);

        // Nothing paid earlier in the year: 36,500 / 12 = 3,041.67.
        var entry = run.Employees.Single();
        entry.IncludeThirteenthMonth.Should().BeTrue();
        entry.ThirteenthMonth.Should().Be(3_041.67m);
        run.Status.Should().Be(PayrollRunStatus.Draft);
        dto.IncludesThirteenthMonth.Should().BeTrue();
        dto.Status.Should().Be(PayrollRunStatus.Draft);
        dto.Employees.Single().ThirteenthMonth.Should().Be(3_041.67m);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.ForApproval)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task SetThirteenthMonthAsync_TurningItOff_RecomputesWithout_AndSendsTheRunBackToDraft(PayrollRunStatus status)
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        run.Employees.Single().ThirteenthMonth.Should().Be(3_041.67m);
        run.Status = status;

        var dto = await _sut.SetThirteenthMonthAsync(run.Id, include: false);

        var entry = run.Employees.Single();
        entry.IncludeThirteenthMonth.Should().BeFalse();
        entry.ThirteenthMonth.Should().Be(0m);
        run.Status.Should().Be(PayrollRunStatus.Draft);
        dto.IncludesThirteenthMonth.Should().BeFalse();
        dto.Employees.Single().ThirteenthMonth.Should().Be(0m);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_OnARunWhereOnlySomeHaveIt_GivesItToEveryone()
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Draft);
        run.Employees[0].IncludeThirteenthMonth = true;
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PayrollSettings?)null);
        _compensationRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync(run.Employees.Select(e => new EmployeeCompensation
                         {
                             EmployeeId = e.EmployeeId, BasicSalary = 20_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
                         }).ToList());
        _allowanceRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _loanRepo.Setup(r => r.GetByEmployeeIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _employeeRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runRepo.Setup(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
                .Callback<PayrollRun, IReadOnlyList<PayrollRunEmployee>, CancellationToken>(
                    (r, entries, _) => r.Employees = entries.ToList())
                .Returns(Task.CompletedTask);

        await _sut.SetThirteenthMonthAsync(run.Id, include: true);

        run.Employees.Should().HaveCount(2).And.OnlyContain(e => e.IncludeThirteenthMonth);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_OnAFlaggedDecemberRun_KeepsTheLeaveConversion_AndBothShareTheExemption()
    {
        // 12 SIL days: 12,000 de minimis and 2,400 other benefits. The 13th month and those 2,400
        // share the 90,000 exemption, so neither is taxed.
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 12m), 12m));
        RecomputesInPlace();
        await _sut.CreateAsync(DecemberRequest(maria.Id, includeThirteenthMonth: false));
        var run = savedRun()!;

        var dto = await _sut.SetThirteenthMonthAsync(run.Id, include: true);

        run.IncludesLeaveConversion.Should().BeTrue();
        var entry = run.Employees.Single();
        entry.ThirteenthMonth.Should().Be(3_041.67m);
        entry.LeaveConversionPay.Should().Be(14_400m);
        entry.LeaveConversionNonTaxable.Should().Be(12_000m);
        entry.LeaveConversionOtherBenefits.Should().Be(2_400m);
        entry.ThirteenthMonthAndOtherBenefits.Should().Be(5_441.67m);
        // The regular month's 1,935.83 (see the conversion test above) and nothing more.
        entry.WithholdingTax.Should().Be(1_935.83m);
        dto.IncludesThirteenthMonth.Should().BeTrue();
        dto.IncludesLeaveConversion.Should().BeTrue();
        // The run's own conversion is never held against it.
        _runRepo.Verify(r => r.GetLeaveConversionsInYearAsync(2026, It.IsAny<IReadOnlyCollection<Guid>>(),
            run.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetThirteenthMonthAsync_OnAPaidRun_IsRefused(bool include)
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Paid);

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("A paid payroll run can't be changed.");
        run.Status.Should().Be(PayrollRunStatus.Paid);
        run.Employees.Should().OnlyContain(e => !e.IncludeThirteenthMonth);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetThirteenthMonthAsync_OnAFinalPayRun_IsRefused(bool include)
    {
        var run = FinalPayRun(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 15));
        run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), IncludeThirteenthMonth = !include });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("A final pay's 13th month can't be changed here.");
        _finalPay.Verify(f => f.RecomputeAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "This payroll already includes the 13th month.")]
    [InlineData(false, "This payroll already leaves out the 13th month.")]
    public async Task SetThirteenthMonthAsync_ThatChangesNothing_IsRefused_SoItCantRecomputeAnApprovedRun(
        bool include, string message)
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Approved);
        foreach (var e in run.Employees) e.IncludeThirteenthMonth = include;

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(message);
        run.Status.Should().Be(PayrollRunStatus.Approved);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_WhenTheRecomputeIsRefused_SavesNothing()
    {
        // Maria left before the period: the recompute refuses, and the approved run keeps its
        // entries and its approval.
        var (run, maria) = RegularRunWithMaria(PayrollRunStatus.Approved);
        Separated(maria, new DateOnly(2026, 3, 10));

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include: true);

        await act.Should().ThrowAsync<DomainException>();
        run.Status.Should().Be(PayrollRunStatus.Approved);
        run.Employees.Should().OnlyContain(e => !e.IncludeThirteenthMonth);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_WhenTheRunDoesNotExist_ThrowsKeyNotFound()
    {
        var act = () => _sut.SetThirteenthMonthAsync(Guid.NewGuid(), include: true);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetAsync_AndGetPagedAsync_SayWhetherAnyEntryIncludesThe13thMonth()
    {
        var (with, _) = RegularRunWithMaria(PayrollRunStatus.Draft);
        with.Employees[1].IncludeThirteenthMonth = true;
        var without = new PayrollRun { RunNumber = "PAY-2026-007", PeriodStart = SecondHalfStart, PeriodEnd = SecondHalfEnd };
        without.Employees.Add(new PayrollRunEmployee { PayrollRunId = without.Id, EmployeeId = Guid.NewGuid() });
        _runRepo.Setup(r => r.GetPagedAsync(1, 20, It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<PayrollRun> { with, without }, 2));

        (await _sut.GetAsync(with.Id))!.IncludesThirteenthMonth.Should().BeTrue();
        (await _sut.GetPagedAsync(1, 20)).Items.Select(r => r.IncludesThirteenthMonth).Should().Equal(true, false);
    }

    // ------------------------------------------------------------------
    // The 13th month is paid in its own year: never on a payroll paid in another
    // ------------------------------------------------------------------

    private const string PayItIn2026 =
        "The 2026 13th month must be paid by Dec 24, 2026; give this payroll a pay date in 2026.";

    /// <summary>Maria's Dec 16-31, 2026 run, paid Jan 5, 2027: a pay date in the next year.</summary>
    private static CreatePayrollRunRequest PaidInJanuaryRequest(Guid employeeId, bool includeThirteenthMonth) => new(
        new DateOnly(2026, 12, 16), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 5), PayFrequency.SemiMonthly,
        [new PayrollRunEmployeeInput(employeeId, IncludeThirteenthMonth: includeThirteenthMonth)]);

    /// <summary>
    /// Maria's Dec 16-31, 2026 run paid Jan 5, 2027, saved without the 13th month; and, when
    /// <paramref name="flagged"/>, marked as including it, as a run saved before the rule would be.
    /// </summary>
    private async Task<PayrollRun> PaidInJanuaryRunForMaria(bool flagged)
    {
        var (maria, savedRun) = MariaAt36500();
        RecomputesInPlace();
        await _sut.CreateAsync(PaidInJanuaryRequest(maria.Id, includeThirteenthMonth: false));
        var run = savedRun()!;
        foreach (var entry in run.Employees)
            entry.IncludeThirteenthMonth = flagged;
        _runRepo.Invocations.Clear();
        return run;
    }

    [Fact]
    public async Task CreateAsync_WithThe13thMonth_OnAPayrollPaidInTheNextYear_IsRefused_AndSavesNothing()
    {
        // Computed from 2027's basic, it would underpay, and count as 2027's 13th month.
        var (maria, _) = MariaAt36500();

        var act = () => _sut.CreateAsync(PaidInJanuaryRequest(maria.Id, includeThirteenthMonth: true));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(PayItIn2026);
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithoutThe13thMonth_OnAPayrollPaidInTheNextYear_IsAllowed()
    {
        var (maria, savedRun) = MariaAt36500();

        await _sut.CreateAsync(PaidInJanuaryRequest(maria.Id, includeThirteenthMonth: false));

        savedRun()!.PayDate.Should().Be(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public async Task ComputeAsync_OnAPayrollPaidInAnotherYear_ThatIncludesThe13thMonth_IsRefused_AndSavesNothing()
    {
        var run = await PaidInJanuaryRunForMaria(flagged: true);

        var act = () => _sut.ComputeAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(PayItIn2026);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_TurningItOn_OnAPayrollPaidInAnotherYear_IsRefused_AndChangesNothing()
    {
        var run = await PaidInJanuaryRunForMaria(flagged: false);
        run.Status = PayrollRunStatus.Approved;

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include: true);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(PayItIn2026);
        run.Status.Should().Be(PayrollRunStatus.Approved);
        run.Employees.Single().IncludeThirteenthMonth.Should().BeFalse();
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_TurningItOff_OnAPayrollPaidInAnotherYear_IsAllowed()
    {
        // A run saved with it before the rule can still be put right.
        var run = await PaidInJanuaryRunForMaria(flagged: true);

        var dto = await _sut.SetThirteenthMonthAsync(run.Id, include: false);

        dto.IncludesThirteenthMonth.Should().BeFalse();
        run.Employees.Single().ThirteenthMonth.Should().Be(0m);
    }

    // ------------------------------------------------------------------
    // The 13th month is paid once: never on two unpaid runs of a pay year, and never on a run
    // whose figures predate a 13th month paid since
    // ------------------------------------------------------------------

    private const string AlreadyOnPay2026023 =
        "Maria Santos's 13th month is already on PAY-2026-023, which isn't paid yet; pay it or leave it out there first.";

    private void ThirteenthMonthUnpaidOn(Guid employeeId, int payYear, string runNumber)
        => _runRepo.Setup(r => r.GetUnpaidThirteenthMonthsInYearAsync(payYear,
                    It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(employeeId)), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new ThirteenthMonthInRun(employeeId, runNumber)]);

    [Fact]
    public async Task CreateAsync_WithThe13thMonth_WhileAnotherUnpaidRunOfThePayYearHasIt_IsRefused()
    {
        // Dec 1-15 already carries it and isn't paid; Dec 16-31 would pay the full amount again.
        var (maria, _) = MariaAt36500();
        ThirteenthMonthUnpaidOn(maria.Id, 2026, "PAY-2026-023");

        var act = () => _sut.CreateAsync(DecemberRequest(maria.Id, includeThirteenthMonth: true) with { IncludeLeaveConversion = false });

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(AlreadyOnPay2026023);
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The13thMonthCheck_LooksAtThePayYear_AndNeverAtTheRunItself()
    {
        // Dec 21, 2025 - Jan 5, 2026 paid Jan 10, 2026 is in the 2026 pay year, though it starts in 2025.
        var (maria, savedRun) = MariaAt36500();
        RecomputesInPlace();
        await _sut.CreateAsync(new CreatePayrollRunRequest(
            new DateOnly(2025, 12, 21), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 10), PayFrequency.SemiMonthly,
            [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: true)]));
        var run = savedRun()!;

        await _sut.ComputeAsync(run.Id);

        _runRepo.Verify(r => r.GetUnpaidThirteenthMonthsInYearAsync(2026,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == maria.Id), run.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _runRepo.Verify(r => r.GetUnpaidThirteenthMonthsInYearAsync(2025, It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComputeAsync_WhileAnotherUnpaidRunHasThe13thMonth_IsRefused_AndSavesNothing()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        var maria = run.Employees.Single().EmployeeId;
        ThirteenthMonthUnpaidOn(maria, 2026, "PAY-2026-023");

        var act = () => _sut.ComputeAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(AlreadyOnPay2026023);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_TurningItOn_WhileAnotherUnpaidRunHasIt_IsRefused_AndChangesNothing()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: false);
        run.Status = PayrollRunStatus.Approved;
        ThirteenthMonthUnpaidOn(run.Employees.Single().EmployeeId, 2026, "PAY-2026-023");

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include: true);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(AlreadyOnPay2026023);
        run.Status.Should().Be(PayrollRunStatus.Approved);
        run.Employees.Single().IncludeThirteenthMonth.Should().BeFalse();
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_TurningItOff_DoesNotLookForOtherRuns()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        _runRepo.Invocations.Clear();

        await _sut.SetThirteenthMonthAsync(run.Id, include: false);

        _runRepo.Verify(r => r.GetUnpaidThirteenthMonthsInYearAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The13thMonthCheck_SkipsSomeoneIneligible_WhoIsPaidNoneOfIt()
    {
        var (maria, savedRun) = MariaAt36500();
        maria.Is13thMonthEligible = false;
        ThirteenthMonthUnpaidOn(maria.Id, 2026, "PAY-2026-023");

        await _sut.CreateAsync(DecemberRequest(maria.Id, includeThirteenthMonth: true) with { IncludeLeaveConversion = false });

        savedRun()!.Employees.Single().ThirteenthMonth.Should().Be(0m);
    }

    // The 13th month is worked out from the basic on the pay year's Paid runs, so an earlier
    // cutoff that isn't paid yet would silently drop out of it.

    private const string OnUnpaidPay2026021 =
        "Maria Santos is on PAY-2026-021, which isn't paid yet; pay it before computing the 13th month.";

    private void EarlierRunUnpaid(Guid employeeId, int payYear, string runNumber)
        => _runRepo.Setup(r => r.GetEarlierUnpaidRunsInYearAsync(payYear, It.IsAny<DateOnly>(),
                    It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(employeeId)), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new EarlierUnpaidRun(employeeId, runNumber)]);

    [Fact]
    public async Task CreateAsync_WithThe13thMonth_WhileAnEarlierRunOfThePayYearIsUnpaid_IsRefused()
    {
        // Dec 1-15 isn't paid yet; Dec 16-31's 13th month would leave its basic out.
        var (maria, _) = MariaAt36500();
        EarlierRunUnpaid(maria.Id, 2026, "PAY-2026-021");

        var act = () => _sut.CreateAsync(DecemberRequest(maria.Id, includeThirteenthMonth: true) with { IncludeLeaveConversion = false });

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(OnUnpaidPay2026021);
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheEarlierUnpaidRunCheck_LooksBeforeThisRunsPayDate_InItsPayYear_AndNeverAtTheRunItself()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        var maria = run.Employees.Single().EmployeeId;
        _runRepo.Invocations.Clear();

        await _sut.ComputeAsync(run.Id);

        _runRepo.Verify(r => r.GetEarlierUnpaidRunsInYearAsync(2026, new DateOnly(2026, 9, 30),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == maria), run.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComputeAsync_WithThe13thMonth_WhileAnEarlierRunIsUnpaid_IsRefused_AndSavesNothing()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        EarlierRunUnpaid(run.Employees.Single().EmployeeId, 2026, "PAY-2026-021");

        var act = () => _sut.ComputeAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(OnUnpaidPay2026021);
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetThirteenthMonthAsync_TurningItOn_WhileAnEarlierRunIsUnpaid_IsRefused_AndChangesNothing()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: false);
        run.Status = PayrollRunStatus.Approved;
        EarlierRunUnpaid(run.Employees.Single().EmployeeId, 2026, "PAY-2026-021");

        var act = () => _sut.SetThirteenthMonthAsync(run.Id, include: true);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(OnUnpaidPay2026021);
        run.Status.Should().Be(PayrollRunStatus.Approved);
        run.Employees.Single().IncludeThirteenthMonth.Should().BeFalse();
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
                                                   It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARunWithoutThe13thMonth_DoesNotLookForEarlierUnpaidRuns()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        _runRepo.Invocations.Clear();

        await _sut.SetThirteenthMonthAsync(run.Id, include: false);

        _runRepo.Verify(r => r.GetEarlierUnpaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<DateOnly>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheEarlierUnpaidRunCheck_SkipsSomeoneIneligible_WhoIsPaidNoneOfIt()
    {
        var (maria, savedRun) = MariaAt36500();
        maria.Is13thMonthEligible = false;
        EarlierRunUnpaid(maria.Id, 2026, "PAY-2026-021");

        await _sut.CreateAsync(DecemberRequest(maria.Id, includeThirteenthMonth: true) with { IncludeLeaveConversion = false });

        savedRun()!.Employees.Single().ThirteenthMonth.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_StoresThe13thMonthPaidEarlierInYear_ThatTheEntryWasComputedWith()
    {
        var (maria, savedRun) = MariaAt36500();
        var advance = PaidThirteenthMonth(maria.Id, "PAY-2026-010", 2_000m, paidAt: new DateTime(2026, 6, 30));
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([advance]);

        await _sut.CreateAsync(new CreatePayrollRunRequest(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), PayFrequency.Monthly,
            [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: true)]));

        savedRun()!.Employees.Single().ThirteenthMonthPaidEarlierInYear.Should().Be(2_000m);
    }

    [Fact]
    public async Task CreateAsync_WithoutThe13thMonth_StoresNoFigureForIt()
    {
        var run = await SeptemberRunForMaria(includeThirteenthMonth: false);

        run.Employees.Single().ThirteenthMonthPaidEarlierInYear.Should().BeNull();
    }

    /// <summary>A Paid regular run of 2026 that paid Maria some 13th month, marked Paid at <paramref name="paidAt"/>.</summary>
    private static PayrollRun PaidThirteenthMonth(Guid employeeId, string runNumber, decimal thirteenthMonth, DateTime paidAt)
    {
        var run = new PayrollRun
        {
            RunNumber = runNumber, PeriodStart = new DateOnly(2026, 6, 1), PeriodEnd = new DateOnly(2026, 6, 30),
            PayDate = new DateOnly(2026, 6, 30), Status = PayrollRunStatus.Paid, UpdatedAt = paidAt
        };
        run.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = run.Id, EmployeeId = employeeId, RegularPay = 36_500m, ThirteenthMonth = thirteenthMonth
        });
        return run;
    }

    /// <summary>
    /// Maria's approved December run: a 13th month computed when <paramref name="paidEarlier"/> of it
    /// had been paid in the year.
    /// </summary>
    private (PayrollRun Run, Employee Maria) ApprovedDecemberWithThe13thMonth(decimal? paidEarlier)
    {
        var maria = new Employee { FirstName = "Maria", LastName = "Santos" };
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-024", PeriodStart = DecemberStart, PeriodEnd = DecemberEnd,
            PayDate = new DateOnly(2026, 12, 29), Frequency = PayFrequency.Monthly, Status = PayrollRunStatus.Approved
        };
        run.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = run.Id, EmployeeId = maria.Id, Employee = maria, RegularPay = 36_500m,
            IncludeThirteenthMonth = true, ThirteenthMonth = 30_000m, ThirteenthMonthPaidEarlierInYear = paidEarlier
        });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        return (run, maria);
    }

    private void VerifyNothingSavedAsPaid() =>
        _runRepo.Verify(r => r.SavePaidAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
                                              It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
                                              It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task MarkPaidAsync_WhenA13thMonthWasPaidElsewhereSinceCompute_IsRefused_NamingTheLatestSuchRun()
    {
        // Computed with 6,000 paid earlier (June). Since then, PAY-2026-020 was paid with 3,000 more.
        var (run, maria) = ApprovedDecemberWithThe13thMonth(paidEarlier: 6_000m);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            PaidThirteenthMonth(maria.Id, "PAY-2026-012", 6_000m, paidAt: new DateTime(2026, 6, 30)),
            PaidThirteenthMonth(maria.Id, "PAY-2026-020", 3_000m, paidAt: new DateTime(2026, 11, 30)),
            PaidThirteenthMonth(maria.Id, "PAY-2026-021", 0m, paidAt: new DateTime(2026, 12, 1)),
        ]);

        var act = () => _sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's 13th month was paid on PAY-2026-020 after this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_WhenThe13thMonthPaidElsewhereIsWhatTheEntryWasComputedWith_PaysIt()
    {
        var (run, maria) = ApprovedDecemberWithThe13thMonth(paidEarlier: 6_000m);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            [PaidThirteenthMonth(maria.Id, "PAY-2026-012", 6_000m, paidAt: new DateTime(2026, 6, 30))]);

        await _sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAnEntryComputedBeforeTheFigureWasStored_DoesNotCheck()
    {
        var (run, maria) = ApprovedDecemberWithThe13thMonth(paidEarlier: null);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            [PaidThirteenthMonth(maria.Id, "PAY-2026-012", 6_000m, paidAt: new DateTime(2026, 6, 30))]);

        await _sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task MarkPaidAsync_OnARunWithoutThe13thMonth_DoesNotLookUpThePaidRuns()
    {
        var (run, _) = RegularRunWithMaria(PayrollRunStatus.Approved);

        await _sut.MarkPaidAsync(run.Id);

        _runRepo.Verify(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComputeAsync_OnAnApprovedRunWithThe13thMonth_RecomputesIt_AndSendsItBackToDraft()
    {
        // The way on from a Mark Paid refused because a 13th month was paid elsewhere since compute.
        var run = await SeptemberRunForMaria(includeThirteenthMonth: true);
        run.Status = PayrollRunStatus.Approved;
        var maria = run.Employees.Single().EmployeeId;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            [PaidThirteenthMonth(maria, "PAY-2026-020", 1_000m, paidAt: new DateTime(2026, 9, 29))]);

        await _sut.ComputeAsync(run.Id);

        // (36,500 June basic + 36,500) / 12 = 6,083.33, less the 1,000 paid: 5,083.33.
        run.Status.Should().Be(PayrollRunStatus.Draft);
        var entry = run.Employees.Single();
        entry.ThirteenthMonth.Should().Be(5_083.33m);
        entry.ThirteenthMonthPaidEarlierInYear.Should().Be(1_000m);
    }
}
