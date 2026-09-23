using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// A regular run holding someone who has left, through the real repositories and Postgres: the
/// separations it checks are read with their employee and final-pay run, and taking the person off
/// deletes their entry - with its loan deduction lines and premium days - and nothing else.
/// </summary>
public class SeparatedEmployeeRunDbTests : DatabaseTestBase
{
    public SeparatedEmployeeRunDbTests(PostgresFixture fixture) : base(fixture) { }

    private static PayrollRunService Service(AppDbContext context) => new(
        new PayrollRunRepository(context), new EmployeeCompensationRepository(context),
        new EmployeeAllowanceRepository(context), new EmployeeLoanRepository(context),
        new PayrollSettingsRepository(context), new PayrollComputationService(),
        Mock.Of<IPayrollAttendanceBridge>(), new EmployeeRepository(context),
        new SeparationRepository(context), NullLogger<PayrollRunService>.Instance);

    /// <summary>
    /// A Draft Mar 16-31 run holding Juan and Maria; Maria's entry has a loan deduction line and a
    /// premium day. Maria's separation has the given last working day.
    /// </summary>
    private async Task<(PayrollRun Run, Employee Maria, Separation Separation)> SeedAsync(DateOnly lastWorkingDay)
    {
        var juan = AnEmployee("Dela Cruz", "Juan");
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.AddRange(juan, maria);

        var run = ARun("PAY-2026-006", new(2026, 3, 16), new(2026, 3, 31), new(2026, 3, 31), PayrollRunStatus.Draft);
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, juan.Id));
        var mariasEntry = AnEntry(run.Id, maria.Id);
        Context.PayrollRunEmployees.Add(mariasEntry);
        Context.Set<PayrollLoanDeduction>().Add(new()
        {
            PayrollRunEmployeeId = mariasEntry.Id, EmployeeLoanId = Guid.NewGuid(), LoanType = "SSSLoan", Amount = 1_500m,
        });
        Context.PayrollRunPremiumDays.Add(new PayrollRunPremiumDay
        {
            PayrollRunEmployeeId = mariasEntry.Id, DayType = WorkDayType.RestDay, Hours = 8m,
        });

        var separation = new Separation
        {
            EmployeeId = maria.Id, Type = SeparationType.Resignation, NoticeDate = lastWorkingDay.AddDays(-30),
            LastWorkingDay = lastWorkingDay, Status = SeparationStatus.Separated, RecordedBy = "hr@company.test",
        };
        Context.Separations.Add(separation);
        await Context.SaveChangesAsync();
        return (run, maria, separation);
    }

    [Fact]
    public async Task SomeoneWhoLeftBeforeTheRun_BlocksApproval_UntilTheyAreTakenOff()
    {
        var (run, maria, _) = await SeedAsync(lastWorkingDay: new DateOnly(2026, 3, 13));

        await using (var context = NewContext())
        {
            var approve = () => Service(context).ApproveAsync(run.Id);
            (await approve.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
                "Maria Santos left on Mar 13, 2026; take them off this payroll - their pay goes in final pay.");
        }

        await using (var context = NewContext())
        {
            var dto = await Service(context).RemoveEmployeeAsync(run.Id, maria.Id);
            dto.Employees.Should().ContainSingle().Which.EmployeeName.Should().Be("Juan Dela Cruz");
        }

        await using (var context = NewContext())
            await Service(context).ApproveAsync(run.Id);

        await using var reader = NewContext();
        var saved = await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id);
        saved!.Status.Should().Be(PayrollRunStatus.Approved);
        saved.Employees.Should().ContainSingle().Which.EmployeeId.Should().NotBe(maria.Id);
        (await reader.Set<PayrollLoanDeduction>().CountAsync()).Should().Be(0);
        (await reader.PayrollRunPremiumDays.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SomeoneWhoseFinalPayOverlapsTheRun_BlocksApproval()
    {
        var (run, _, separation) = await SeedAsync(lastWorkingDay: new DateOnly(2026, 3, 20));
        var finalPay = ARun("FP-2026-001", new(2026, 3, 16), new(2026, 3, 20), new(2026, 4, 10), PayrollRunStatus.Draft);
        finalPay.RunType = PayrollRunType.FinalPay;
        Context.PayrollRuns.Add(finalPay);
        separation.FinalPayRunId = finalPay.Id;
        await Context.SaveChangesAsync();

        await using var context = NewContext();
        var approve = () => Service(context).ApproveAsync(run.Id);

        (await approve.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's final pay already covers Mar 16 – Mar 20, 2026; take them off this payroll.");
    }
}
