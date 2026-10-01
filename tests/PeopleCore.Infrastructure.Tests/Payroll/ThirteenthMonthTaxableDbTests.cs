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
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// <see cref="PayrollRunEmployee.ThirteenthMonthTaxable"/> through Postgres: the column keeps its
/// centavos and its null, and recomputing a run stores the figure on an entry that had none.
/// </summary>
public class ThirteenthMonthTaxableDbTests : DatabaseTestBase
{
    public ThirteenthMonthTaxableDbTests(PostgresFixture fixture) : base(fixture) { }

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

    private async Task<PayrollRunEmployee> EntryAsync(Guid runId)
    {
        await using var reader = NewContext();
        return (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Employees.Single();
    }

    [Fact]
    public async Task TheColumn_RoundTripsAFigureAndANull()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Reyes", "Ana");
        Context.Employees.AddRange(maria, ana);
        var run = ARun("PAY-2026-024", new(2026, 12, 1), new(2026, 12, 15), new(2026, 12, 15), PayrollRunStatus.Draft);
        Context.PayrollRuns.Add(run);
        var withFigure = AnEntry(run.Id, maria.Id);
        withFigure.ThirteenthMonth = 50_000.10m;
        withFigure.ThirteenthMonthTaxable = 12_345.67m;
        var withoutFigure = AnEntry(run.Id, ana.Id);
        withoutFigure.ThirteenthMonth = 40_000m;
        Context.PayrollRunEmployees.AddRange(withFigure, withoutFigure);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var entries = await reader.PayrollRunEmployees.AsNoTracking().Where(e => e.PayrollRunId == run.Id).ToListAsync();

        var stored = entries.Single(e => e.EmployeeId == maria.Id);
        stored.ThirteenthMonthTaxable.Should().Be(12_345.67m);
        stored.ThirteenthMonthExempt.Should().Be(37_654.43m, "50,000.10 - 12,345.67");
        var old = entries.Single(e => e.EmployeeId == ana.Id);
        old.ThirteenthMonthTaxable.Should().BeNull("an entry computed before the figure was stored has none");
        old.ThirteenthMonthExempt.Should().Be(40_000m);
    }

    [Fact]
    public async Task Recompute_StoresTheFigure_OnAnEntryThatHadNone()
    {
        // Maria earns 120,000 a month, paid monthly. A Paid run earlier in 2026 paid her 1,000,000 of
        // basic and an 80,000 13th month advance.
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        Context.Companies.Add(ACompany());
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 120_000m, PayFrequency = PayFrequency.Monthly,
        });
        var advance = ARun("PAY-2026-020", new(2026, 1, 1), new(2026, 11, 30), new(2026, 11, 30));
        Context.PayrollRuns.Add(advance);
        var line = AnEntry(advance.Id, maria.Id, regularPay: 1_000_000m);
        line.IncludeThirteenthMonth = true;
        line.ThirteenthMonth = 80_000m;
        Context.PayrollRunEmployees.Add(line);
        await Context.SaveChangesAsync();

        PayrollRunDto december;
        await using (var context = NewContext())
            december = await PayrollRuns(context).CreateAsync(new CreatePayrollRunRequest(
                new(2026, 12, 1), new(2026, 12, 31), new(2026, 12, 29), PayFrequency.Monthly,
                [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: true)]));

        // 13th month: (1,000,000 + December's 120,000) / 12 = 93,333.33, less the 80,000 paid =
        // 13,333.33. The advance used 80,000 of the exemption, so 10,000 is left: 10,000 exempt,
        // 13,333.33 - 10,000 = 3,333.33 taxable.
        var created = await EntryAsync(december.Id);
        created.ThirteenthMonth.Should().Be(13_333.33m);
        created.ThirteenthMonthTaxable.Should().Be(3_333.33m);
        var taxBefore = created.WithholdingTax;
        var netBefore = created.NetPay;

        // As if computed before the figure was stored.
        await Context.Database.ExecuteSqlAsync(
            $"update payroll_run_employees set thirteenth_month_taxable = null where payroll_run_id = {december.Id}");
        (await EntryAsync(december.Id)).ThirteenthMonthTaxable.Should().BeNull();

        await using (var context = NewContext())
            await PayrollRuns(context).ComputeAsync(december.Id);

        var recomputed = await EntryAsync(december.Id);
        recomputed.ThirteenthMonth.Should().Be(13_333.33m);
        recomputed.ThirteenthMonthTaxable.Should().Be(3_333.33m);
        recomputed.ThirteenthMonthExempt.Should().Be(10_000m);
        recomputed.WithholdingTax.Should().Be(taxBefore);
        recomputed.NetPay.Should().Be(netBefore);
    }
}
