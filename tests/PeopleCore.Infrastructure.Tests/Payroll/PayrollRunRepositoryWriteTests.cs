using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollRunRepositoryWriteTests : DatabaseTestBase
{
    public PayrollRunRepositoryWriteTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(Context);

    [Fact]
    public async Task AddWithEntries_PersistsTheRunAndItsEntries()
    {
        // Entries are added to both DbSets explicitly, because the navigation alone is not enough
        // once Compute has assigned each entry an Id. Whether that actually persists both rows is
        // a database question, and this is the first thing to ask it.
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        run.Employees = [AnEntry(run.Id, employee.Id, regularPay: 30_000m)];

        await Sut.AddWithEntriesAsync(run);

        await using var reader = NewContext();
        (await reader.PayrollRuns.CountAsync()).Should().Be(1);
        var entry = await reader.PayrollRunEmployees.SingleAsync();
        entry.PayrollRunId.Should().Be(run.Id);
        entry.RegularPay.Should().Be(30_000m);
    }

    [Fact]
    public async Task ReplaceEntries_SwapsTheEntriesAndLeavesTheRunIntact()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id, regularPay: 30_000m));
        await Context.SaveChangesAsync();

        var reloaded = await Context.PayrollRuns
            .Include(r => r.Employees)
            .SingleAsync(r => r.Id == run.Id);

        await Sut.ReplaceEntriesAsync(reloaded, [AnEntry(run.Id, employee.Id, regularPay: 45_000m)]);

        await using var reader = NewContext();
        (await reader.PayrollRuns.CountAsync()).Should().Be(1, "replacing entries must not delete the run");
        var entry = await reader.PayrollRunEmployees.SingleAsync();
        entry.RegularPay.Should().Be(45_000m);
    }

    [Fact]
    public async Task ReplaceEntries_CascadesToLoanDeductionLines()
    {
        // ExecuteDeleteAsync issues a DELETE the change tracker never sees, so the cascade has to
        // come from the database's own ON DELETE CASCADE rather than from EF. Orphaned deduction
        // lines would otherwise survive and be summed into a later payslip.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);

        var entry = AnEntry(run.Id, employee.Id);
        Context.PayrollRunEmployees.Add(entry);
        Context.Set<PayrollLoanDeduction>().Add(new()
        {
            PayrollRunEmployeeId = entry.Id,
            EmployeeLoanId = Guid.NewGuid(),
            LoanType = "SSSLoan",
            Amount = 1_500m,
        });
        await Context.SaveChangesAsync();

        var reloaded = await Context.PayrollRuns
            .Include(r => r.Employees)
            .SingleAsync(r => r.Id == run.Id);

        await Sut.ReplaceEntriesAsync(reloaded, [AnEntry(run.Id, employee.Id, regularPay: 45_000m)]);

        await using var reader = NewContext();
        (await reader.Set<PayrollLoanDeduction>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReplaceEntries_LeavesTheContextUsableAfterwards()
    {
        // The detach loop exists so EF does not issue updates against rows ExecuteDeleteAsync has
        // already removed. Without it the NEXT SaveChangesAsync throws a concurrency exception -
        // so saving again is exactly the assertion that proves the detach happened.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));
        await Context.SaveChangesAsync();

        var reloaded = await Context.PayrollRuns
            .Include(r => r.Employees)
            .SingleAsync(r => r.Id == run.Id);

        await Sut.ReplaceEntriesAsync(reloaded, [AnEntry(run.Id, employee.Id, regularPay: 45_000m)]);

        reloaded.Status = PayrollRunStatus.Approved;
        var saveAgain = async () => await Context.SaveChangesAsync();

        await saveAgain.Should().NotThrowAsync();
    }
}
