using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Leave;

/// <summary>
/// A leave type that has been used - any request or any balance row - can no longer be deleted.
/// And the statutory set, written through the real repositories, lands in the tables as it should.
/// </summary>
public class LeaveTypeRepositoryTests : DatabaseTestBase
{
    public LeaveTypeRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<(Guid EmployeeId, LeaveType Type, LeaveType Other)> SeedAsync()
    {
        var employee = AnEmployee();
        var type = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15 };
        var other = new LeaveType { Name = "Sick Leave", Code = "SL", MaxDaysPerYear = 15 };
        Context.Employees.Add(employee);
        Context.LeaveTypes.AddRange(type, other);
        await Context.SaveChangesAsync();
        return (employee.Id, type, other);
    }

    [Fact]
    public async Task IsUsedAsync_IsFalse_ForATypeWithNoRequestOrBalance()
    {
        var (employeeId, type, other) = await SeedAsync();
        // Another type's rows don't count.
        Context.LeaveBalances.Add(new LeaveBalance { EmployeeId = employeeId, LeaveTypeId = other.Id, Year = 2026, TotalDays = 5 });
        Context.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = employeeId, LeaveTypeId = other.Id, Status = LeaveStatus.Cancelled, TotalDays = 1, DaysInStartYear = 1,
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 5),
        });
        await Context.SaveChangesAsync();

        (await new LeaveTypeRepository(NewContext()).IsUsedAsync(type.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task IsUsedAsync_IsTrue_OnceTheTypeHasABalanceRow()
    {
        var (employeeId, type, _) = await SeedAsync();
        Context.LeaveBalances.Add(new LeaveBalance { EmployeeId = employeeId, LeaveTypeId = type.Id, Year = 2026 });
        await Context.SaveChangesAsync();

        (await new LeaveTypeRepository(NewContext()).IsUsedAsync(type.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task IsUsedAsync_IsTrue_OnceTheTypeHasARequest_WhateverItsStatus()
    {
        var (employeeId, type, _) = await SeedAsync();
        Context.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = employeeId, LeaveTypeId = type.Id, Status = LeaveStatus.Rejected, TotalDays = 1, DaysInStartYear = 1,
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 5),
        });
        await Context.SaveChangesAsync();

        (await new LeaveTypeRepository(NewContext()).IsUsedAsync(type.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task TheStatutorySet_IsSaved_WithSilsPolicy_AndAnUnusedSilCanBeDeletedAgain()
    {
        var context = NewContext();
        var sut = new LeaveTypeService(new LeaveTypeRepository(context), new LeaveAccrualRepository(context));

        var first = await sut.AddStatutoryAsync();
        var second = await sut.AddStatutoryAsync();

        first.Added.Should().HaveCount(7);
        second.Added.Should().BeEmpty();
        var read = NewContext();
        (await read.LeaveTypes.CountAsync()).Should().Be(7);
        var sil = await read.LeaveTypes.SingleAsync(t => t.Code == "SIL");
        var policy = await read.LeaveAccrualPolicies.SingleAsync();
        policy.LeaveTypeId.Should().Be(sil.Id);
        policy.TenureMonthsMin.Should().Be(12);
        policy.TenureMonthsMax.Should().BeNull();
        policy.DaysPerYear.Should().Be(5m);
        policy.AccrualFrequency.Should().Be(AccrualFrequency.Monthly);

        // The policy's foreign key restricts deletes; deleting the unused type takes its policy with it.
        await sut.DeleteAsync(sil.Id);

        var after = NewContext();
        (await after.LeaveTypes.AnyAsync(t => t.Code == "SIL")).Should().BeFalse();
        (await after.LeaveAccrualPolicies.AnyAsync()).Should().BeFalse();
    }
}
