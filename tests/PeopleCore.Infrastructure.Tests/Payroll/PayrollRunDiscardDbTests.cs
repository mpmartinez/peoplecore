using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Discarding a regular run that was never paid, through the real repositories and Postgres: the
/// run goes with its entries and everything hanging off them, nothing else changes, and the next
/// run's number doesn't collide with one still in use.
/// <para>
/// Maria Santos earns 36,500 a month, paid monthly.
/// </para>
/// </summary>
public class PayrollRunDiscardDbTests : DatabaseTestBase
{
    public PayrollRunDiscardDbTests(PostgresFixture fixture) : base(fixture) { }

    private static PayrollRunService PayrollRuns(AppDbContext context) => new(
        new PayrollRunRepository(context), new EmployeeCompensationRepository(context),
        new EmployeeAllowanceRepository(context), new EmployeeLoanRepository(context),
        new PayrollSettingsRepository(context), new PayrollComputationService(), NoAttendance(),
        new EmployeeRepository(context), new SeparationRepository(context),
        NullLogger<PayrollRunService>.Instance);

    private static IPayrollAttendanceBridge NoAttendance()
    {
        var bridge = new Mock<IPayrollAttendanceBridge>();
        bridge.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                       It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AttendanceBridgeResult(new Dictionary<Guid, PayrollAttendanceInput>(), []));
        return bridge.Object;
    }

    private async Task<Employee> SeedMariaAsync()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        Context.Companies.Add(ACompany());
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 36_500m, PayFrequency = PayFrequency.Monthly,
        });
        await Context.SaveChangesAsync();
        return maria;
    }

    private static CreatePayrollRunRequest Run(Guid employeeId, DateOnly start, DateOnly end, DateOnly payDate,
        bool thirteenthMonth = false) => new(
        start, end, payDate, PayFrequency.Monthly,
        [new PayrollRunEmployeeInput(employeeId, IncludeThirteenthMonth: thirteenthMonth)]);

    private async Task<T> InRequestAsync<T>(Func<PayrollRunService, Task<T>> step)
    {
        await using var context = NewContext();
        return await step(PayrollRuns(context));
    }

    private async Task InRequestAsync(Func<PayrollRunService, Task> step)
    {
        await using var context = NewContext();
        await step(PayrollRuns(context));
    }

    [Fact]
    public async Task DiscardingAStaleCutoff_DeletesItWithItsEntriesAndChildRows_AndFreesThe13thMonth()
    {
        var maria = await SeedMariaAsync();
        var stale = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 12, 1), new(2026, 12, 15), new(2026, 12, 15))));
        var december = Run(maria.Id, new(2026, 12, 16), new(2026, 12, 31), new(2026, 12, 29), thirteenthMonth: true);

        // Dec 1-15 is unpaid, so Dec 16-31 can't carry the 13th month.
        await InRequestAsync(async s =>
        {
            var act = () => s.CreateAsync(december);
            (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
                $"Maria Santos is on {stale.RunNumber}, which isn't paid yet; pay it before computing the 13th month.");
        });

        // The stale run's entry has a loan deduction line and a premium day hanging off it.
        var loan = new EmployeeLoan
        {
            EmployeeId = maria.Id, LoanType = LoanType.SSSLoan, TotalAmount = 12_000m, MonthlyDeduction = 1_000m,
            RemainingBalance = 9_000m, StartDate = new DateOnly(2026, 1, 1),
        };
        Context.EmployeeLoans.Add(loan);
        var entryId = stale.Employees.Single().Id;
        Context.Set<PayrollLoanDeduction>().Add(new()
        {
            PayrollRunEmployeeId = entryId, EmployeeLoanId = loan.Id, LoanType = "SSSLoan", Amount = 1_000m,
        });
        Context.PayrollRunPremiumDays.Add(new PayrollRunPremiumDay
        {
            PayrollRunEmployeeId = entryId, DayType = WorkDayType.RestDay, Hours = 8m,
        });
        await Context.SaveChangesAsync();

        await InRequestAsync(s => s.DiscardAsync(stale.Id));

        await using (var reader = NewContext())
        {
            (await reader.PayrollRuns.AnyAsync(r => r.Id == stale.Id)).Should().BeFalse();
            (await reader.PayrollRunEmployees.AnyAsync(e => e.PayrollRunId == stale.Id)).Should().BeFalse();
            (await reader.Set<PayrollLoanDeduction>().CountAsync()).Should().Be(0);
            (await reader.PayrollRunPremiumDays.CountAsync()).Should().Be(0);
            // Loans only change at Mark Paid, and this run was never paid.
            var storedLoan = await reader.EmployeeLoans.SingleAsync();
            storedLoan.RemainingBalance.Should().Be(9_000m);
            storedLoan.IsActive.Should().BeTrue();
        }

        // With the stale cutoff gone, Dec 16-31 carries the 13th month: 36,500 / 12 = 3,041.67.
        var created = await InRequestAsync(s => s.CreateAsync(december));

        created.Employees.Single().ThirteenthMonth.Should().Be(3_041.67m);
    }

    [Fact]
    public async Task DiscardingAPaidRun_IsRefused_AndKeepsIt()
    {
        var maria = await SeedMariaAsync();
        var run = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 1, 1), new(2026, 1, 31), new(2026, 1, 31))));
        await InRequestAsync(s => s.ApproveAsync(run.Id));
        await InRequestAsync(s => s.MarkPaidAsync(run.Id));

        await InRequestAsync(async s =>
        {
            var act = () => s.DiscardAsync(run.Id);
            (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
                "A paid payroll run can't be discarded.");
        });

        await using var reader = NewContext();
        (await reader.PayrollRunEmployees.CountAsync(e => e.PayrollRunId == run.Id)).Should().Be(1);
    }

    [Fact]
    public async Task TheNextRunAfterADiscard_IsNumberedPastTheYearsHighest_SoItDoesntCollide()
    {
        var maria = await SeedMariaAsync();
        var january = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 1, 1), new(2026, 1, 31), new(2026, 1, 31))));
        var february = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 2, 1), new(2026, 2, 28), new(2026, 2, 28))));
        january.RunNumber.Should().Be("PAY-2026-001");
        february.RunNumber.Should().Be("PAY-2026-002");

        await InRequestAsync(s => s.DiscardAsync(january.Id));

        // One run is left, but a count would reissue PAY-2026-002, which February still holds.
        var march = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 3, 1), new(2026, 3, 31), new(2026, 3, 31))));

        march.RunNumber.Should().Be("PAY-2026-003");
    }
}
