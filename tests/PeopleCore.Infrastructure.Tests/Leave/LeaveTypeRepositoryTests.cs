using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Leave;

/// <summary>
/// A leave type that has been used - any request, balance row or accrual transaction - can no
/// longer be deleted; an unused one goes with its accrual policies in one save, and a new one comes
/// with its policies in one save.
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
    public async Task IsUsedAsync_IsTrue_OnceTheTypeHasAnAccrualTransaction_EvenWithNoBalance()
    {
        // The transaction's foreign key restricts deletes just as a request's does.
        var (employeeId, type, _) = await SeedAsync();
        Context.LeaveAccrualTransactions.Add(ATransaction(employeeId, type.Id));
        await Context.SaveChangesAsync();

        (await new LeaveTypeRepository(NewContext()).IsUsedAsync(type.Id)).Should().BeTrue();
    }

    private static LeaveAccrualTransaction ATransaction(Guid employeeId, Guid leaveTypeId) => new()
    {
        EmployeeId = employeeId, LeaveTypeId = leaveTypeId, AccrualDate = new DateOnly(2026, 3, 1),
        DaysAccrued = 0.42m, PolicySnapshot = "{}", PeriodYear = 2026, PeriodMonth = 3,
    };

    private static LeaveAccrualPolicy APolicy(Guid leaveTypeId, decimal daysPerYear = 5m) => new()
    {
        LeaveTypeId = leaveTypeId, TenureMonthsMin = 12, DaysPerYear = daysPerYear, AccrualFrequency = AccrualFrequency.Monthly,
    };

    // ---- DeleteWithPoliciesAsync -----------------------------------------------------------

    [Fact]
    public async Task DeleteWithPoliciesAsync_RemovesTheType_AndOnlyItsPolicies()
    {
        var (_, type, other) = await SeedAsync();
        Context.LeaveAccrualPolicies.AddRange(APolicy(type.Id), APolicy(type.Id, 7m), APolicy(other.Id));
        await Context.SaveChangesAsync();
        var context = NewContext();
        var sut = new LeaveTypeRepository(context);

        await sut.DeleteWithPoliciesAsync((await sut.GetByIdAsync(type.Id))!);

        await using var read = NewContext();
        (await read.LeaveTypes.AnyAsync(t => t.Id == type.Id)).Should().BeFalse();
        (await read.LeaveAccrualPolicies.Select(p => p.LeaveTypeId).ToListAsync()).Should().Equal(other.Id);
    }

    [Fact]
    public async Task DeleteWithPoliciesAsync_WhenTheTypeCannotBeDeleted_KeepsItsPolicies()
    {
        // One save: a type still referenced (here by an accrual transaction, whose foreign key
        // restricts deletes) takes nothing with it when its own delete fails.
        var (employeeId, type, _) = await SeedAsync();
        Context.LeaveAccrualPolicies.Add(APolicy(type.Id));
        Context.LeaveAccrualTransactions.Add(ATransaction(employeeId, type.Id));
        await Context.SaveChangesAsync();
        var sut = new LeaveTypeRepository(NewContext());

        var act = async () => await sut.DeleteWithPoliciesAsync((await sut.GetByIdAsync(type.Id))!);

        await act.Should().ThrowAsync<DbUpdateException>();
        await using var read = NewContext();
        (await read.LeaveTypes.AnyAsync(t => t.Id == type.Id)).Should().BeTrue();
        (await read.LeaveAccrualPolicies.CountAsync(p => p.LeaveTypeId == type.Id)).Should().Be(1);
    }

    // ---- AddWithPoliciesAsync --------------------------------------------------------------

    [Fact]
    public async Task AddWithPoliciesAsync_SavesTheTypeAndItsPolicies()
    {
        var type = new LeaveType { Name = "Service Incentive Leave", Code = "SIL", MaxDaysPerYear = 5 };

        await new LeaveTypeRepository(NewContext()).AddWithPoliciesAsync(type, [APolicy(type.Id)]);

        await using var read = NewContext();
        (await read.LeaveTypes.AnyAsync(t => t.Id == type.Id)).Should().BeTrue();
        (await read.LeaveAccrualPolicies.SingleAsync()).LeaveTypeId.Should().Be(type.Id);
    }

    [Fact]
    public async Task AddWithPoliciesAsync_WhenAPolicyCannotBeSaved_SavesNeitherIt_NorTheType()
    {
        // 10,000 days overflows the policy's numeric(5,2) column. With one save, the type goes too.
        var type = new LeaveType { Name = "Service Incentive Leave", Code = "SIL", MaxDaysPerYear = 5 };

        var act = () => new LeaveTypeRepository(NewContext()).AddWithPoliciesAsync(type, [APolicy(type.Id, 10_000m)]);

        await act.Should().ThrowAsync<DbUpdateException>();
        await using var read = NewContext();
        (await read.LeaveTypes.AnyAsync()).Should().BeFalse();
        (await read.LeaveAccrualPolicies.AnyAsync()).Should().BeFalse();
    }

    // ---- the statutory set, end to end -----------------------------------------------------

    /// <summary>What each statutory type must read back as, per the plan's table.</summary>
    public record Expected(
        string Code, string Name, string Kind, decimal MaxDaysPerYear, decimal? DaysPerEvent, int? MaxEvents,
        bool CountsCalendarDays, string? Gender, bool RequiresDocument, bool RequiresMarried,
        bool RequiresSoloParentId, int? MinServiceMonths, bool IsConfidential, bool IsMaternity,
        bool IsConvertibleToCash, bool CountsAsVacationForDeMinimis);

    public static TheoryData<Expected> Table => new()
    {
        new Expected("SIL", "Service Incentive Leave", "Accrued", 5m, null, null, false, null, false, false, false, null, false, false, true, true),
        new Expected("ML", "Maternity Leave", "PerEvent", 0m, 105m, null, true, "Female", true, false, false, null, false, true, false, false),
        new Expected("AML", "Maternity Leave Allocated to Father", "PerEvent", 0m, 7m, null, true, "Male", true, false, false, null, false, false, false, false),
        new Expected("PL", "Paternity Leave", "PerEvent", 0m, 7m, 4, false, "Male", true, true, false, null, false, false, false, false),
        new Expected("SPL", "Solo Parent Leave", "YearlyAllowance", 7m, null, null, false, null, false, false, true, 6, false, false, false, false),
        new Expected("VAWC", "VAWC Leave", "YearlyAllowance", 10m, null, null, false, "Female", true, false, false, null, true, false, false, false),
        new Expected("SLW", "Special Leave for Women (Magna Carta)", "PerEvent", 0m, 60m, null, true, "Female", true, false, false, 6, false, false, false, false),
    };

    [Theory]
    [MemberData(nameof(Table))]
    public async Task TheStatutorySet_ReadsBackFromTheDatabase_AsTheTableSays(Expected row)
    {
        await new LeaveTypeService(new LeaveTypeRepository(NewContext())).AddStatutoryAsync();

        await using var read = NewContext();
        var t = await read.LeaveTypes.SingleAsync(x => x.Code == row.Code);
        var storedKind = await read.Database
            .SqlQuery<string>($"SELECT entitlement_kind AS \"Value\" FROM leave_types WHERE code = {row.Code}")
            .SingleAsync();

        storedKind.Should().Be(row.Kind, "the kind is stored as its name");
        t.EntitlementKind.ToString().Should().Be(row.Kind);
        t.Name.Should().Be(row.Name);
        t.MaxDaysPerYear.Should().Be(row.MaxDaysPerYear);
        t.DaysPerEvent.Should().Be(row.DaysPerEvent);
        t.MaxEvents.Should().Be(row.MaxEvents);
        t.CountsCalendarDays.Should().Be(row.CountsCalendarDays);
        t.GenderRestriction.Should().Be(row.Gender);
        t.RequiresDocument.Should().Be(row.RequiresDocument);
        t.RequiresMarried.Should().Be(row.RequiresMarried);
        t.RequiresSoloParentId.Should().Be(row.RequiresSoloParentId);
        t.MinServiceMonths.Should().Be(row.MinServiceMonths);
        t.IsConfidential.Should().Be(row.IsConfidential);
        t.IsMaternity.Should().Be(row.IsMaternity);
        t.IsConvertibleToCash.Should().Be(row.IsConvertibleToCash);
        // The entity's default is true, so a false here (ML included) proves the set wrote it.
        t.CountsAsVacationForDeMinimis.Should().Be(row.CountsAsVacationForDeMinimis);
        t.IsPaid.Should().BeTrue();
        t.IsCarryOver.Should().BeFalse();
        t.CarryOverMaxDays.Should().BeNull();
        t.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TheStatutorySet_IsSaved_WithSilsPolicy_AndAnUnusedSilCanBeDeletedAgain()
    {
        var context = NewContext();
        var sut = new LeaveTypeService(new LeaveTypeRepository(context));

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
        policy.IsActive.Should().BeTrue();

        // The policy's foreign key restricts deletes; deleting the unused type takes its policy with it.
        await sut.DeleteAsync(sil.Id);

        var after = NewContext();
        (await after.LeaveTypes.AnyAsync(t => t.Code == "SIL")).Should().BeFalse();
        (await after.LeaveAccrualPolicies.AnyAsync()).Should().BeFalse();
    }
}
