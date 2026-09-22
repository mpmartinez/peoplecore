using FluentAssertions;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollRunRepositoryMonthTests : DatabaseTestBase
{
    public PayrollRunRepositoryMonthTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(NewContext());

    private async Task<Guid> SeedAsync()
    {
        var employee = AnEmployee();
        employee.GovernmentIds.Add(new EmployeeGovernmentId { IdType = GovernmentIdType.SSS, IdNumber = "34-1234567-8" });
        Context.Employees.Add(employee);

        // December's second cutoff paid in January, a March run paid in March, a draft March run.
        var decJan = ARun("PAY-2025-024", new(2025, 12, 16), new(2025, 12, 31), new(2026, 1, 5));
        var march = ARun("PAY-2026-005", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20));
        var draft = ARun("PAY-2026-006", new(2026, 3, 16), new(2026, 3, 31), new(2026, 4, 5), PayrollRunStatus.Draft);
        foreach (var run in new[] { decJan, march, draft })
        {
            Context.PayrollRuns.Add(run);
            Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));
        }
        await Context.SaveChangesAsync();
        return employee.Id;
    }

    [Fact]
    public async Task ByPeriodEndMonth_CountsTheMonthThePayWasEarned_PaidOnly()
    {
        await SeedAsync();

        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2025, 12)).Select(r => r.RunNumber).Should().Equal("PAY-2025-024");
        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 1)).Should().BeEmpty();
        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 3)).Select(r => r.RunNumber).Should().Equal("PAY-2026-005");
    }

    [Fact]
    public async Task ByPayMonth_CountsTheMonthItWasPaid_PaidOnly()
    {
        await SeedAsync();

        (await Sut.GetPaidRunsByPayMonthAsync(2026, 1)).Select(r => r.RunNumber).Should().Equal("PAY-2025-024");
        (await Sut.GetPaidRunsByPayMonthAsync(2025, 12)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheRuns_ComeWithEachEmployeesGovernmentIds()
    {
        await SeedAsync();

        var run = (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 3)).Single();

        run.Employees.Single().Employee!.GovernmentIds.Single().IdNumber.Should().Be("34-1234567-8");
    }

    [Fact]
    public async Task CountUnpaidRuns_CountsRunsNotYetPaid_OnTheReportsBasis()
    {
        await SeedAsync();

        (await Sut.CountUnpaidRunsAsync(2026, 3, byPayDate: false)).Should().Be(1);
        (await Sut.CountUnpaidRunsAsync(2026, 4, byPayDate: true)).Should().Be(1);
        (await Sut.CountUnpaidRunsAsync(2026, 3, byPayDate: true)).Should().Be(0);
    }

    [Fact]
    public async Task ByPeriodEndMonth_DoesNotMatchTheSameMonthInAnotherYear()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        var march2025 = ARun("PAY-2025-005", new(2025, 3, 1), new(2025, 3, 15), new(2025, 3, 20));
        var march2026 = ARun("PAY-2026-005", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20));
        foreach (var run in new[] { march2025, march2026 })
        {
            Context.PayrollRuns.Add(run);
            Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));
        }
        await Context.SaveChangesAsync();

        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 3)).Select(r => r.RunNumber).Should().Equal("PAY-2026-005");
    }
}
