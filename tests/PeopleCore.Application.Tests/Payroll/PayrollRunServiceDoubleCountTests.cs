using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;
using Employee = PeopleCore.Domain.Entities.Employees.Employee;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A Paid run paid on or before an employee's opening-balance through date counts pay the balance
/// already covers: the run's page shows the double-count warning among its warnings.
/// </summary>
public partial class PayrollRunServiceTests
{
    private static readonly Employee DoubleCountMaria = new() { FirstName = "Maria", LastName = "Santos", EmployeeNumber = "E-001" };
    private static readonly Employee DoubleCountJose = new() { FirstName = "Jose", LastName = "Cruz", EmployeeNumber = "E-002" };

    /// <summary>The service as the app builds it, with <paramref name="balances"/> for the run's warnings.</summary>
    private (PayrollRunService Sut, Mock<IPayrollOpeningBalanceRepository> Balances) WithBalancesForWarnings(
        params PayrollOpeningBalance[] balances)
    {
        var repository = Holding(balances);
        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object, _settingsRepo.Object,
            new PayrollComputationService(), _attendanceBridge.Object, _employeeRepo.Object, _separations.Object,
            NullLogger<PayrollRunService>.Instance, _finalPay.Object, _yearEnd.Object, _clock,
            openingBalances: repository.Object);
        return (sut, repository);
    }

    /// <summary>A run paying <paramref name="employees"/>, paid on <paramref name="payDate"/>, that GetWithEntriesAsync loads.</summary>
    private PayrollRun RunPaying(PayrollRunStatus status, DateOnly payDate, params Employee[] employees)
    {
        var run = new PayrollRun { RunNumber = "PAY-2026-003", Status = status, PayDate = payDate };
        foreach (var employee in employees)
            run.Employees.Add(new PayrollRunEmployee { EmployeeId = employee.Id, Employee = employee });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        return run;
    }

    [Theory]
    [InlineData(15)]
    [InlineData(31)]
    public async Task GetAsync_APaidRunPaidOnOrBeforeHerThroughDate_WarnsOfTheDoubleCount(int payDay)
    {
        var run = RunPaying(PayrollRunStatus.Paid, new DateOnly(2026, 3, payDay), DoubleCountMaria);
        var (sut, _) = WithBalancesForWarnings(OpeningBalance(DoubleCountMaria.Id, throughDate: new DateOnly(2026, 3, 31)));

        var dto = await sut.GetAsync(run.Id);

        dto!.Warnings.Should().Equal(
            $"Maria Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-003 was paid on Mar {payDay}, 2026.");
    }

    [Fact]
    public async Task GetAsync_APaidRunPaidAfterHerThroughDate_HasNoWarning()
    {
        var run = RunPaying(PayrollRunStatus.Paid, new DateOnly(2026, 4, 15), DoubleCountMaria);
        var (sut, _) = WithBalancesForWarnings(OpeningBalance(DoubleCountMaria.Id, throughDate: new DateOnly(2026, 3, 31)));

        var dto = await sut.GetAsync(run.Id);

        dto!.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WarnsForEachEmployeeWhoseBalanceCoversTheRun_InTheRunsOrder()
    {
        var ana = new Employee { FirstName = "Ana", LastName = "Reyes", EmployeeNumber = "E-003" };
        var run = RunPaying(PayrollRunStatus.Paid, new DateOnly(2026, 3, 15), DoubleCountMaria, ana, DoubleCountJose);
        var (sut, _) = WithBalancesForWarnings(
            OpeningBalance(DoubleCountJose.Id, throughDate: new DateOnly(2026, 6, 30)),
            OpeningBalance(ana.Id, throughDate: new DateOnly(2026, 2, 28)),
            OpeningBalance(DoubleCountMaria.Id, throughDate: new DateOnly(2026, 3, 31)),
            // Another year's balance through a later date is not this run's.
            OpeningBalance(ana.Id, year: 2025, throughDate: new DateOnly(2025, 12, 31)));

        var dto = await sut.GetAsync(run.Id);

        dto!.Warnings.Should().Equal(
            "Maria Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-003 was paid on Mar 15, 2026.",
            "Jose Cruz's opening balance already covers pay through Jun 30, 2026; PAY-2026-003 was paid on Mar 15, 2026.");
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.Processing)]
    [InlineData(PayrollRunStatus.ForApproval)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task GetAsync_ARunNotYetPaid_HasNoDoubleCountWarning_AndReadsNoBalances(PayrollRunStatus status)
    {
        var run = RunPaying(status, new DateOnly(2026, 3, 15), DoubleCountMaria);
        var (sut, balances) = WithBalancesForWarnings(OpeningBalance(DoubleCountMaria.Id, throughDate: new DateOnly(2026, 3, 31)));

        var dto = await sut.GetAsync(run.Id);

        dto!.Warnings.Should().BeEmpty();
        balances.Verify(b => b.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_ReadsTheBalancesOnce_ForTheRunsEmployeesAndPayYear()
    {
        var run = RunPaying(PayrollRunStatus.Paid, new DateOnly(2026, 3, 15), DoubleCountMaria, DoubleCountJose);
        var (sut, balances) = WithBalancesForWarnings();

        await sut.GetAsync(run.Id);

        balances.Verify(b => b.GetForEmployeesAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(DoubleCountMaria.Id) && ids.Contains(DoubleCountJose.Id)),
            2026, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetForPayslipAsync_HasNoDoubleCountWarning_AndReadsNoBalances()
    {
        var run = RunPaying(PayrollRunStatus.Paid, new DateOnly(2026, 3, 15), DoubleCountMaria);
        var (sut, balances) = WithBalancesForWarnings(OpeningBalance(DoubleCountMaria.Id, throughDate: new DateOnly(2026, 3, 31)));

        var dto = await sut.GetForPayslipAsync(run.Id);

        dto!.Warnings.Should().BeEmpty();
        balances.Verify(b => b.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
