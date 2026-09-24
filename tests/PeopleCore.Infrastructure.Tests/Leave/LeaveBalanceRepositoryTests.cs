using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Leave;

/// <summary>
/// A YearlyAllowance year's balance row is added on first filing. Two filings at the same moment
/// (a double click) can both find no row; the unique index on (employee, type, year) lets only one
/// insert, and the other is refused with a readable message instead of a database error.
/// </summary>
public class LeaveBalanceRepositoryTests : DatabaseTestBase
{
    public LeaveBalanceRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<(Guid EmployeeId, Guid LeaveTypeId)> SeedAsync()
    {
        var employee = AnEmployee();
        var type = new LeaveType
        {
            Name = "Solo Parent Leave", Code = "SPL", MaxDaysPerYear = 7,
            EntitlementKind = LeaveEntitlementKind.YearlyAllowance
        };
        Context.Employees.Add(employee);
        Context.LeaveTypes.Add(type);
        await Context.SaveChangesAsync();
        return (employee.Id, type.Id);
    }

    private static LeaveBalance ARow(Guid employeeId, Guid leaveTypeId)
        => new() { EmployeeId = employeeId, LeaveTypeId = leaveTypeId, Year = 2026, TotalDays = 7 };

    [Fact]
    public async Task GetByEmployeeAsync_LoadsTheType_SoTheBalanceSaysWhetherItIsConfidential()
    {
        // The balances endpoint leaves confidential rows out for managers by this flag; a type not
        // loaded would read as "not confidential" and show VAWC to them.
        var employee = AnEmployee();
        var vawc = new LeaveType
        {
            Name = "VAWC Leave", Code = "VAWC", MaxDaysPerYear = 10, IsConfidential = true,
            EntitlementKind = LeaveEntitlementKind.YearlyAllowance
        };
        var vacation = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15 };
        Context.Employees.Add(employee);
        Context.LeaveTypes.AddRange(vawc, vacation);
        Context.LeaveBalances.AddRange(ARow(employee.Id, vawc.Id), ARow(employee.Id, vacation.Id));
        await Context.SaveChangesAsync();
        var sut = new PeopleCore.Application.Leave.Services.LeaveBalanceService(new LeaveBalanceRepository(NewContext()));

        var rows = await sut.GetByEmployeeAsync(employee.Id, 2026);

        rows.Should().ContainSingle(r => r.LeaveTypeId == vawc.Id).Which.IsConfidential.Should().BeTrue();
        rows.Should().ContainSingle(r => r.LeaveTypeId == vacation.Id).Which.IsConfidential.Should().BeFalse();
    }

    [Fact]
    public async Task AddNewAsync_InsertsTheRow()
    {
        var (employeeId, leaveTypeId) = await SeedAsync();
        var sut = new LeaveBalanceRepository(NewContext());

        await sut.AddNewAsync(ARow(employeeId, leaveTypeId));

        await using var read = NewContext();
        (await read.LeaveBalances.CountAsync(b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId && b.Year == 2026))
            .Should().Be(1);
    }

    [Fact]
    public async Task AddNewAsync_WhenAConcurrentFilingAddedTheRowFirst_IsRefusedReadably_AndDetachesTheRejectedRow()
    {
        var (employeeId, leaveTypeId) = await SeedAsync();

        // The other filing's insert, committed between this filing's "no row yet" read and its insert.
        await using (var other = NewContext())
        {
            other.LeaveBalances.Add(ARow(employeeId, leaveTypeId));
            await other.SaveChangesAsync();
        }

        await using var context = NewContext();
        var sut = new LeaveBalanceRepository(context);
        var rejected = ARow(employeeId, leaveTypeId);

        var act = () => sut.AddNewAsync(rejected);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Your leave is being filed already; try again in a moment.");
        context.Entry(rejected).State.Should().Be(EntityState.Detached,
            "a rejected row left Added would be inserted again by the next SaveChanges on this context");

        await using var read = NewContext();
        (await read.LeaveBalances.CountAsync(b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId && b.Year == 2026))
            .Should().Be(1);
    }
}
