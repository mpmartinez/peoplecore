using FluentAssertions;
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
    private readonly PayrollRunService _sut;

    public PayrollRunServiceTests()
    {
        _sut = new PayrollRunService(
            _runRepo.Object,
            _compensationRepo.Object,
            _allowanceRepo.Object,
            _loanRepo.Object,
            _settingsRepo.Object,
            new PayrollComputationService());
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

        var request = new CreatePayrollRunRequest(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 20),
            PayFrequency.SemiMonthly, [employeeId]);

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

    [Fact]
    public async Task GetAsync_WhenRunNotFound_ReturnsNull()
    {
        _runRepo.Setup(r => r.GetWithEntriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PayrollRun?)null);

        var result = await _sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
