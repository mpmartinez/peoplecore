using FluentAssertions;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Leave;

/// <summary>
/// The facts the leave rules need from SQL: an employee's pending requests of one type (the
/// pending holds) and how many of that type were approved (the event count).
/// </summary>
public class LeaveRequestRepositoryTests : DatabaseTestBase
{
    public LeaveRequestRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private static LeaveRequest ARequest(Employee employee, LeaveType type, LeaveStatus status, int day = 5)
        => new()
        {
            EmployeeId = employee.Id, LeaveTypeId = type.Id, Status = status, TotalDays = 1, DaysInStartYear = 1,
            StartDate = new DateOnly(2026, 10, day), EndDate = new DateOnly(2026, 10, day)
        };

    private async Task<(Employee Employee, Employee Other, LeaveType Type, LeaveType OtherType)> SeedAsync()
    {
        var employee = AnEmployee();
        var other = AnEmployee("Santos", "Maria");
        var type = new LeaveType { Name = "Paternity Leave", Code = "PL", EntitlementKind = LeaveEntitlementKind.PerEvent };
        var otherType = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15 };
        Context.Employees.AddRange(employee, other);
        Context.LeaveTypes.AddRange(type, otherType);
        await Context.SaveChangesAsync();
        return (employee, other, type, otherType);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyThatEmployeesPendingRequestsOfThatType()
    {
        var (employee, other, type, otherType) = await SeedAsync();
        var pending1 = ARequest(employee, type, LeaveStatus.Pending, 5);
        var pending2 = ARequest(employee, type, LeaveStatus.Pending, 6);
        Context.LeaveRequests.AddRange(
            pending1, pending2,
            ARequest(employee, type, LeaveStatus.Approved, 7),
            ARequest(employee, type, LeaveStatus.Rejected, 8),
            ARequest(employee, type, LeaveStatus.Cancelled, 9),
            ARequest(employee, otherType, LeaveStatus.Pending, 12),
            ARequest(other, type, LeaveStatus.Pending, 5));
        await Context.SaveChangesAsync();
        var sut = new LeaveRequestRepository(NewContext());

        var all = await sut.GetPendingAsync(employee.Id, type.Id, null);
        var excludingOne = await sut.GetPendingAsync(employee.Id, type.Id, pending1.Id);

        all.Select(r => r.Id).Should().BeEquivalentTo([pending1.Id, pending2.Id]);
        excludingOne.Select(r => r.Id).Should().Equal(pending2.Id);
    }

    [Fact]
    public async Task CountApprovedAsync_CountsOnlyThatEmployeesApprovedRequestsOfThatType()
    {
        var (employee, other, type, otherType) = await SeedAsync();
        Context.LeaveRequests.AddRange(
            ARequest(employee, type, LeaveStatus.Approved, 5),
            ARequest(employee, type, LeaveStatus.Approved, 6),
            ARequest(employee, type, LeaveStatus.Pending, 7),
            ARequest(employee, type, LeaveStatus.Rejected, 8),
            ARequest(employee, type, LeaveStatus.Cancelled, 9),
            ARequest(employee, otherType, LeaveStatus.Approved, 12),
            ARequest(other, type, LeaveStatus.Approved, 5));
        await Context.SaveChangesAsync();
        var sut = new LeaveRequestRepository(NewContext());

        var count = await sut.CountApprovedAsync(employee.Id, type.Id);

        count.Should().Be(2);
    }

    [Fact]
    public async Task GetPagedAsync_ExcludingConfidential_LeavesOutConfidentialTypes_AndCountsWithoutThem()
    {
        var (employee, _, type, otherType) = await SeedAsync();
        var vawc = new LeaveType { Name = "VAWC Leave", Code = "VAWC", IsConfidential = true, EntitlementKind = LeaveEntitlementKind.YearlyAllowance };
        Context.LeaveTypes.Add(vawc);
        Context.LeaveRequests.AddRange(
            ARequest(employee, type, LeaveStatus.Pending, 5),
            ARequest(employee, otherType, LeaveStatus.Pending, 6),
            ARequest(employee, vawc, LeaveStatus.Pending, 7));
        await Context.SaveChangesAsync();
        var sut = new LeaveRequestRepository(NewContext());

        var (kept, keptTotal) = await sut.GetPagedAsync(null, null, null, 1, 20, excludeConfidential: true);
        var (all, allTotal) = await sut.GetPagedAsync(null, null, null, 1, 20, excludeConfidential: false);

        kept.Should().OnlyContain(r => r.LeaveTypeId != vawc.Id);
        keptTotal.Should().Be(2, "the count drives the pager, so it must leave them out too");
        all.Should().Contain(r => r.LeaveTypeId == vawc.Id);
        allTotal.Should().Be(3);
    }

    [Fact]
    public async Task GetByIdAsync_LoadsTheTypeAndTheEmployee()
    {
        // The controller reads the type's IsConfidential from the loaded request to decide who may
        // see, decide or open it; an unloaded type would read as "not confidential".
        var (employee, _, type, _) = await SeedAsync();
        var request = ARequest(employee, type, LeaveStatus.Pending);
        Context.LeaveRequests.Add(request);
        await Context.SaveChangesAsync();
        var sut = new LeaveRequestRepository(NewContext());

        var loaded = await sut.GetByIdAsync(request.Id);

        loaded!.LeaveType.Should().NotBeNull();
        loaded.LeaveType.Name.Should().Be("Paternity Leave");
        loaded.Employee.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPagedAsync_AndGetApprovedByPeriodAsync_LoadTheType()
    {
        // A request whose type did not load is treated as confidential (fail closed), so every
        // read that ends in a DTO or an export row has to include it.
        var (employee, _, type, _) = await SeedAsync();
        Context.LeaveRequests.Add(ARequest(employee, type, LeaveStatus.Approved));
        await Context.SaveChangesAsync();
        var sut = new LeaveRequestRepository(NewContext());

        var (items, _) = await sut.GetPagedAsync(null, null, null, 1, 20, excludeConfidential: false);
        var approved = await sut.GetApprovedByPeriodAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        items.Should().ContainSingle().Which.LeaveType.Should().NotBeNull();
        approved.Should().ContainSingle().Which.LeaveType.Should().NotBeNull();
    }
}
