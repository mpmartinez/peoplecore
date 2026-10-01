using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// The 13th month is paid once in a pay year, through the real repositories and Postgres: two
/// unpaid runs can't both carry it, and a run whose figures predate a 13th month paid since
/// can't be paid until it is recomputed.
/// <para>
/// Maria Santos earns 36,500 a month, paid monthly, so each run's regular pay is 36,500 and its
/// 13th month is one twelfth of the year's basic so far, less what was already paid.
/// </para>
/// </summary>
public class ThirteenthMonthOnceDbTests : DatabaseTestBase
{
    public ThirteenthMonthOnceDbTests(PostgresFixture fixture) : base(fixture) { }

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
        bool thirteenthMonth) => new(
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

    private async Task<PayrollRunEmployee> EntryAsync(Guid runId)
    {
        await using var reader = NewContext();
        return (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Employees.Single();
    }

    [Fact]
    public async Task TwoUnpaidRunsOfAPayYear_CantBothCarryThe13thMonth_UntilTheFirstIsPaid()
    {
        var maria = await SeedMariaAsync();
        var first = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 12, 1), new(2026, 12, 15), new(2026, 12, 15), thirteenthMonth: true)));
        var alreadyOn =
            $"Maria Santos's 13th month is already on {first.RunNumber}, which isn't paid yet; pay it or leave it out there first.";

        // Created with it: refused.
        var secondRun = Run(maria.Id, new(2026, 12, 16), new(2026, 12, 31), new(2026, 12, 29), thirteenthMonth: true);
        await InRequestAsync(async s =>
        {
            var act = () => s.CreateAsync(secondRun);
            (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(alreadyOn);
        });

        // Created without it, then switched on: refused too, and nothing changes.
        var second = await InRequestAsync(s => s.CreateAsync(secondRun with
        {
            Employees = [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: false)]
        }));
        await InRequestAsync(async s =>
        {
            var act = () => s.SetThirteenthMonthAsync(second.Id, include: true);
            (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(alreadyOn);
        });
        (await EntryAsync(second.Id)).IncludeThirteenthMonth.Should().BeFalse();

        // Once the first is paid, the second may carry it - and nets out what the first paid:
        // (36,500 + 36,500) / 12 = 6,083.33, less the first's 36,500 / 12 = 3,041.67.
        await InRequestAsync(s => s.ApproveAsync(first.Id));
        await InRequestAsync(s => s.MarkPaidAsync(first.Id));
        (await EntryAsync(first.Id)).ThirteenthMonth.Should().Be(3_041.67m);

        var switched = await InRequestAsync(s => s.SetThirteenthMonthAsync(second.Id, include: true));

        switched.IncludesThirteenthMonth.Should().BeTrue();
        var entry = await EntryAsync(second.Id);
        entry.ThirteenthMonth.Should().Be(3_041.66m);
        entry.ThirteenthMonthPaidEarlierInYear.Should().Be(3_041.67m);
    }

    [Fact]
    public async Task The13thMonth_CantBeComputedWhileAnEarlierCutoffIsUnpaid_UntilItIsPaid()
    {
        // The 13th month is worked out from the basic on the pay year's Paid runs, so Dec 1-15,
        // unpaid, would drop out of Dec 16-31's.
        var maria = await SeedMariaAsync();
        var first = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 12, 1), new(2026, 12, 15), new(2026, 12, 15), thirteenthMonth: false)));
        var secondRun = Run(maria.Id, new(2026, 12, 16), new(2026, 12, 31), new(2026, 12, 29), thirteenthMonth: true);

        await InRequestAsync(async s =>
        {
            var act = () => s.CreateAsync(secondRun);
            (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
                $"Maria Santos is on {first.RunNumber}, which isn't paid yet; pay it before computing the 13th month.");
        });

        // Once it's paid, both cutoffs' basic counts: (36,500 + 36,500) / 12 = 6,083.33.
        await InRequestAsync(s => s.ApproveAsync(first.Id));
        await InRequestAsync(s => s.MarkPaidAsync(first.Id));
        var second = await InRequestAsync(s => s.CreateAsync(secondRun));

        (await EntryAsync(second.Id)).ThirteenthMonth.Should().Be(6_083.33m);
    }

    [Fact]
    public async Task ARunWhoseFiguresPredateA13thMonthPaidSince_IsRefusedAtMarkPaid_ThenRecomputedAndPaid()
    {
        var maria = await SeedMariaAsync();
        var december = await InRequestAsync(s => s.CreateAsync(
            Run(maria.Id, new(2026, 12, 1), new(2026, 12, 31), new(2026, 12, 29), thirteenthMonth: true)));
        await InRequestAsync(s => s.ApproveAsync(december.Id));
        (await EntryAsync(december.Id)).ThirteenthMonthPaidEarlierInYear.Should().Be(0m);

        // Since December was computed, a run paid 1,000 of Maria's 13th month - one computed
        // alongside it, or saved before the rule that keeps two unpaid runs from both carrying it.
        await using (var context = NewContext())
        {
            var advance = ARun("PAY-2026-020", new(2026, 11, 1), new(2026, 11, 30), new(2026, 11, 30));
            context.PayrollRuns.Add(advance);
            var line = AnEntry(advance.Id, maria.Id, regularPay: 0m);
            line.IncludeThirteenthMonth = true;
            line.ThirteenthMonth = 1_000m;
            context.PayrollRunEmployees.Add(line);
            await context.SaveChangesAsync();
        }

        await InRequestAsync(async s =>
        {
            var act = () => s.MarkPaidAsync(december.Id);
            (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
                "Maria Santos's 13th month was paid on PAY-2026-020 after this payroll was computed; recompute it before paying.");
        });
        await using (var reader = NewContext())
            (await new PayrollRunRepository(reader).GetWithEntriesAsync(december.Id))!.Status.Should().Be(PayrollRunStatus.Approved);

        // Recomputing the approved run sends it back to Draft with the 1,000 netted out:
        // 36,500 / 12 = 3,041.67, less 1,000 = 2,041.67. Approved again, it is paid.
        await InRequestAsync(s => s.ComputeAsync(december.Id));
        var entry = await EntryAsync(december.Id);
        entry.ThirteenthMonth.Should().Be(2_041.67m);
        entry.ThirteenthMonthPaidEarlierInYear.Should().Be(1_000m);

        await InRequestAsync(s => s.ApproveAsync(december.Id));
        await InRequestAsync(s => s.MarkPaidAsync(december.Id));

        await using var final = NewContext();
        (await new PayrollRunRepository(final).GetWithEntriesAsync(december.Id))!.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task GetUnpaidThirteenthMonthsInYear_FindsOnlyOtherUnpaidRegularRunsOfThatPayYear_ThatIncludeIt()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Reyes", "Ana");
        Context.Employees.AddRange(maria, ana);

        PayrollRun AddRun(string number, DateOnly payDate, PayrollRunStatus status = PayrollRunStatus.Approved,
                          PayrollRunType type = PayrollRunType.Regular)
        {
            var run = ARun(number, payDate.AddDays(-14), payDate, payDate, status);
            run.RunType = type;
            Context.PayrollRuns.Add(run);
            return run;
        }
        void AddEntry(PayrollRun run, Employee employee, bool includesIt)
        {
            var entry = AnEntry(run.Id, employee.Id);
            entry.IncludeThirteenthMonth = includesIt;
            Context.PayrollRunEmployees.Add(entry);
        }

        var counted = AddRun("PAY-2026-023", new(2026, 12, 15));
        AddEntry(counted, maria, includesIt: true);
        var draft = AddRun("PAY-2026-019", new(2026, 10, 15), PayrollRunStatus.Draft);
        AddEntry(draft, maria, includesIt: true);
        var thisRun = AddRun("PAY-2026-024", new(2026, 12, 29), PayrollRunStatus.Draft);
        AddEntry(thisRun, maria, includesIt: true);
        var paid = AddRun("PAY-2026-020", new(2026, 11, 30), PayrollRunStatus.Paid);
        AddEntry(paid, maria, includesIt: true);
        var nextPayYear = AddRun("PAY-2027-001", new(2027, 1, 5));
        AddEntry(nextPayYear, maria, includesIt: true);
        var finalPay = AddRun("FP-2026-001", new(2026, 12, 10), type: PayrollRunType.FinalPay);
        AddEntry(finalPay, maria, includesIt: true);
        var without = AddRun("PAY-2026-021", new(2026, 11, 15));
        AddEntry(without, maria, includesIt: false);
        AddEntry(counted, ana, includesIt: true);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var found = await new PayrollRunRepository(reader).GetUnpaidThirteenthMonthsInYearAsync(2026, [maria.Id], thisRun.Id);

        found.Should().Equal(new ThirteenthMonthInRun(maria.Id, "PAY-2026-019"), new ThirteenthMonthInRun(maria.Id, "PAY-2026-023"));
    }

    [Fact]
    public async Task GetEarlierUnpaidRunsInYear_FindsOnlyOtherUnpaidRegularRunsOfThatPayYear_PaidBeforeTheDate()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Reyes", "Ana");
        Context.Employees.AddRange(maria, ana);

        PayrollRun AddRun(string number, DateOnly payDate, PayrollRunStatus status = PayrollRunStatus.Approved,
                          PayrollRunType type = PayrollRunType.Regular)
        {
            var run = ARun(number, payDate.AddDays(-14), payDate, payDate, status);
            run.RunType = type;
            Context.PayrollRuns.Add(run);
            return run;
        }
        void AddEntry(PayrollRun run, Employee employee) =>
            Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));

        var approved = AddRun("PAY-2026-021", new(2026, 12, 15));
        AddEntry(approved, maria);
        var draft = AddRun("PAY-2026-019", new(2026, 10, 15), PayrollRunStatus.Draft);
        AddEntry(draft, maria);
        var thisRun = AddRun("PAY-2026-024", new(2026, 12, 29), PayrollRunStatus.Draft);
        AddEntry(thisRun, maria);
        var sameDay = AddRun("PAY-2026-025", new(2026, 12, 29), PayrollRunStatus.Draft);
        AddEntry(sameDay, maria);
        var later = AddRun("PAY-2026-026", new(2026, 12, 30), PayrollRunStatus.Draft);
        AddEntry(later, maria);
        var paid = AddRun("PAY-2026-020", new(2026, 11, 30), PayrollRunStatus.Paid);
        AddEntry(paid, maria);
        var lastPayYear = AddRun("PAY-2025-024", new(2025, 12, 29));
        AddEntry(lastPayYear, maria);
        var finalPay = AddRun("FP-2026-001", new(2026, 12, 10), type: PayrollRunType.FinalPay);
        AddEntry(finalPay, maria);
        AddEntry(approved, ana);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var found = await new PayrollRunRepository(reader).GetEarlierUnpaidRunsInYearAsync(
            2026, new DateOnly(2026, 12, 29), [maria.Id], thisRun.Id);

        found.Should().Equal(new EarlierUnpaidRun(maria.Id, "PAY-2026-019"), new EarlierUnpaidRun(maria.Id, "PAY-2026-021"));
    }

    [Fact]
    public async Task GetUnpaidRegularRunEntriesInYear_FindsEveryEntryOnARegularRunOfThatPayYearNotPaidYet()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Reyes", "Ana");
        Context.Employees.AddRange(maria, ana);

        PayrollRun AddRun(string number, DateOnly payDate, PayrollRunStatus status,
                          PayrollRunType type = PayrollRunType.Regular)
        {
            var run = ARun(number, payDate.AddDays(-14), payDate, payDate, status);
            run.RunType = type;
            Context.PayrollRuns.Add(run);
            return run;
        }
        void AddEntry(PayrollRun run, Employee employee) =>
            Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));

        AddEntry(AddRun("PAY-2026-004", new(2026, 2, 28), PayrollRunStatus.Approved), maria);
        var draft = AddRun("PAY-2026-002", new(2026, 1, 31), PayrollRunStatus.Draft);
        AddEntry(draft, maria);
        AddEntry(draft, ana);
        AddEntry(AddRun("PAY-2026-003", new(2026, 2, 15), PayrollRunStatus.ForApproval), ana);
        AddEntry(AddRun("PAY-2026-005", new(2026, 3, 15), PayrollRunStatus.Processing), maria);
        AddEntry(AddRun("PAY-2026-001", new(2026, 1, 15), PayrollRunStatus.Paid), maria);
        AddEntry(AddRun("FP-2026-001", new(2026, 2, 20), PayrollRunStatus.Approved, PayrollRunType.FinalPay), maria);
        AddEntry(AddRun("PAY-2025-024", new(2025, 12, 31), PayrollRunStatus.Draft), maria);
        AddEntry(AddRun("PAY-2027-001", new(2027, 1, 15), PayrollRunStatus.Draft), maria);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var found = await new PayrollRunRepository(reader).GetUnpaidRegularRunEntriesInYearAsync(2026);

        found.Should().BeEquivalentTo(
        [
            new UnpaidRunEntry(maria.Id, "PAY-2026-002", new(2026, 1, 31)),
            new UnpaidRunEntry(ana.Id, "PAY-2026-002", new(2026, 1, 31)),
            new UnpaidRunEntry(ana.Id, "PAY-2026-003", new(2026, 2, 15)),
            new UnpaidRunEntry(maria.Id, "PAY-2026-004", new(2026, 2, 28)),
            new UnpaidRunEntry(maria.Id, "PAY-2026-005", new(2026, 3, 15)),
        ]);
        found.Select(e => e.PayDate).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetUnpaidRegularRunEntriesInYear_ForTheLastYearADateCanHave_FindsItsRuns()
    {
        // An opening balance can be for 9999, the last year DateOnly holds: its year's end can't be
        // worked out as the first day of 10000. Dec 31, 9999 is DateOnly.MaxValue, which Npgsql
        // stores as 'infinity'.
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        var run = ARun("PAY-9999-024", new(9999, 12, 17), new(9999, 12, 31), new(9999, 12, 31), PayrollRunStatus.Draft);
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, maria.Id));
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var found = await new PayrollRunRepository(reader).GetUnpaidRegularRunEntriesInYearAsync(9999);

        found.Should().Equal(new UnpaidRunEntry(maria.Id, "PAY-9999-024", new(9999, 12, 31)));
    }
}
