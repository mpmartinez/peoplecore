using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Payroll;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// <see cref="PayrollRunPremiumDay.UnworkedDays"/> through Postgres: the column round-trips, and
/// recomputing an unpaid run reprices the unworked days from the stored rows.
/// </summary>
public class PremiumDayUnworkedDaysDbTests : DatabaseTestBase
{
    public PremiumDayUnworkedDaysDbTests(PostgresFixture fixture) : base(fixture) { }

    private static PayrollRunService PayrollRuns(AppDbContext context, IPayrollAttendanceBridge bridge) => new(
        new PayrollRunRepository(context), new EmployeeCompensationRepository(context),
        new EmployeeAllowanceRepository(context), new EmployeeLoanRepository(context),
        new PayrollSettingsRepository(context), new PayrollComputationService(), bridge,
        new EmployeeRepository(context), new SeparationRepository(context),
        NullLogger<PayrollRunService>.Instance);

    private static IPayrollAttendanceBridge Bridge(Guid? employeeId = null, PayrollAttendanceInput? attendance = null)
    {
        var attendances = new Dictionary<Guid, PayrollAttendanceInput>();
        if (employeeId is { } id && attendance is not null) attendances[id] = attendance;
        var bridge = new Mock<IPayrollAttendanceBridge>();
        bridge.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                       It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AttendanceBridgeResult(attendances, []));
        return bridge.Object;
    }

    private async Task<PayrollRunEmployee> EntryAsync(Guid runId)
    {
        await using var reader = NewContext();
        return (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Employees.Single();
    }

    [Fact]
    public async Task TheColumn_RoundTripsAFractionOfADay_AndDefaultsToZero()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var entry = AnEntry(run.Id, employee.Id);
        entry.PremiumDays =
        [
            new PayrollRunPremiumDay { DayType = WorkDayType.DoubleRegularHoliday, UnworkedDays = 2.5m },
            new PayrollRunPremiumDay { DayType = WorkDayType.RestDay, Hours = 8m }
        ];
        run.Employees = [entry];
        await new PayrollRunRepository(Context).AddWithEntriesAsync(run);

        await using var reader = NewContext();
        var loaded = await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id);
        loaded!.Employees.Single().PremiumDays
            .Select(d => (d.DayType, d.Hours, d.UnworkedDays))
            .Should().BeEquivalentTo([
                (WorkDayType.DoubleRegularHoliday, 0m, 2.5m),
                (WorkDayType.RestDay, 8m, 0m)
            ]);
    }

    [Fact]
    public async Task Recompute_OfAnUnpaidRun_RepricesTheUnworkedDaysFromTheStoredRows()
    {
        // Maria earns 36,500 a month, paid monthly: 1,200 a day under the 365 factor.
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        Context.Companies.Add(ACompany());
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 36_500m, PayFrequency = PayFrequency.Monthly,
        });
        await Context.SaveChangesAsync();

        var attendance = new PayrollAttendanceInput
        {
            PremiumDays = [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, UnworkedDays: 1m)]
        };
        PayrollRunDto created;
        await using (var context = NewContext())
            created = await PayrollRuns(context, Bridge(maria.Id, attendance)).CreateAsync(new CreatePayrollRunRequest(
                new(2026, 1, 1), new(2026, 1, 31), new(2026, 1, 31), PayFrequency.Monthly,
                [new PayrollRunEmployeeInput(maria.Id)]));

        // One unworked double regular holiday: 1,200 x (2.00 - 1.00).
        var stored = await EntryAsync(created.Id);
        stored.HolidayPay.Should().Be(1_200.00m);
        stored.PremiumDays.Single().UnworkedDays.Should().Be(1m);

        // The stored row now says three days; a recompute with nothing from the bridge reads it, not
        // today's punches: 1,200 x 3 = 3,600.
        await Context.Database.ExecuteSqlAsync(
            $"update payroll_run_premium_days set unworked_days = 3 where payroll_run_employee_id = {stored.Id}");
        await using (var context = NewContext())
            await PayrollRuns(context, Bridge()).ComputeAsync(created.Id);

        var recomputed = await EntryAsync(created.Id);
        recomputed.HolidayPay.Should().Be(3_600.00m);
        recomputed.PremiumDays.Single().UnworkedDays.Should().Be(3m);
    }
}
