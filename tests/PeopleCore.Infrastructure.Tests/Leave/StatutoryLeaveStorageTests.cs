using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Infrastructure.Tests.Leave;

/// <summary>
/// Storage for the Philippine statutory leave settings: what a leave type says about itself, the
/// maternity and father-allocation details on a request, its supporting document, and an employee's
/// solo parent ID. Task 1 of the statutory leave plan - storage only, no behaviour yet.
/// </summary>
public class StatutoryLeaveStorageTests : DatabaseTestBase
{
    public StatutoryLeaveStorageTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task LeaveType_EverySetting_RoundTrips()
    {
        var type = new LeaveType
        {
            Name = "Solo Parent Leave",
            Code = "SPL-" + Guid.NewGuid().ToString("N")[..8],
            MaxDaysPerYear = 7m,
            EntitlementKind = LeaveEntitlementKind.YearlyAllowance,
            CountsCalendarDays = true,
            DaysPerEvent = 7.5m,
            MinServiceMonths = 6,
            RequiresMarried = true,
            RequiresSoloParentId = true,
            MaxEvents = 3,
            IsConfidential = true,
            IsMaternity = true,
        };
        Context.LeaveTypes.Add(type);
        await Context.SaveChangesAsync();

        await using var read = NewContext();
        var stored = await read.LeaveTypes.SingleAsync(t => t.Id == type.Id);

        stored.EntitlementKind.Should().Be(LeaveEntitlementKind.YearlyAllowance);
        stored.CountsCalendarDays.Should().BeTrue();
        stored.DaysPerEvent.Should().Be(7.5m);
        stored.MinServiceMonths.Should().Be(6);
        stored.RequiresMarried.Should().BeTrue();
        stored.RequiresSoloParentId.Should().BeTrue();
        stored.MaxEvents.Should().Be(3);
        stored.IsConfidential.Should().BeTrue();
        stored.IsMaternity.Should().BeTrue();
    }

    [Fact]
    public async Task LeaveRequest_MaternityAndDocumentFields_RoundTrip()
    {
        var employee = AnEmployee();
        var type = new LeaveType { Name = "Maternity Leave", Code = "ML-" + Guid.NewGuid().ToString("N")[..8], MaxDaysPerYear = 0m };
        Context.Employees.Add(employee);
        Context.LeaveTypes.Add(type);
        await Context.SaveChangesAsync();

        var uploadedBy = Guid.NewGuid();
        var uploadedAt = new DateTime(2026, 9, 24, 10, 30, 0, DateTimeKind.Utc);
        var request = new LeaveRequest
        {
            EmployeeId = employee.Id,
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 12, 14),
            TotalDays = 105m,
            MaternityCase = MaternityCase.LiveBirth,
            DaysAllocatedToFather = 7,
            DaysInStartYear = 90m,
            DocumentFileName = "birth-certificate.pdf",
            DocumentStorageKey = $"leave-requests/{Guid.NewGuid()}/{Guid.NewGuid()}.pdf",
            DocumentContentType = "application/pdf",
            DocumentSizeBytes = 123_456,
            DocumentUploadedBy = uploadedBy,
            DocumentUploadedAt = uploadedAt,
        };
        Context.LeaveRequests.Add(request);
        await Context.SaveChangesAsync();

        await using var read = NewContext();
        var stored = await read.LeaveRequests.SingleAsync(r => r.Id == request.Id);

        stored.MaternityCase.Should().Be(MaternityCase.LiveBirth);
        stored.DaysAllocatedToFather.Should().Be(7);
        stored.DaysInStartYear.Should().Be(90m);
        stored.DocumentFileName.Should().Be("birth-certificate.pdf");
        stored.DocumentStorageKey.Should().Be(request.DocumentStorageKey);
        stored.DocumentContentType.Should().Be("application/pdf");
        stored.DocumentSizeBytes.Should().Be(123_456);
        stored.DocumentUploadedBy.Should().Be(uploadedBy);
        stored.DocumentUploadedAt.Should().Be(uploadedAt);
    }

    [Fact]
    public async Task Employee_SoloParentIdFields_RoundTrip()
    {
        var employee = AnEmployee();
        employee.SoloParentIdNumber = "SP-0042";
        employee.SoloParentIdValidUntil = new DateOnly(2027, 3, 31);
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await using var read = NewContext();
        var stored = await read.Employees.SingleAsync(e => e.Id == employee.Id);

        stored.SoloParentIdNumber.Should().Be("SP-0042");
        stored.SoloParentIdValidUntil.Should().Be(new DateOnly(2027, 3, 31));
    }

    [Fact]
    public async Task LeaveType_InsertedWithoutNewColumns_GetsAccruedAndFalseDefaults()
    {
        var id = Guid.NewGuid();
        var code = "LEGACY-" + Guid.NewGuid().ToString("N")[..8];
        await Context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO leave_types
                (id, name, code, max_days_per_year, is_paid, is_carry_over, requires_document,
                 is_active, is_convertible_to_cash, counts_as_vacation_for_de_minimis,
                 created_at, updated_at)
            VALUES
                ({id}, 'Legacy Leave', {code}, 10, true, false, false,
                 true, false, true,
                 now(), now())");

        await using var read = NewContext();
        var stored = await read.LeaveTypes.SingleAsync(t => t.Id == id);

        stored.EntitlementKind.Should().Be(LeaveEntitlementKind.Accrued);
        stored.CountsCalendarDays.Should().BeFalse();
        stored.RequiresMarried.Should().BeFalse();
        stored.RequiresSoloParentId.Should().BeFalse();
        stored.IsConfidential.Should().BeFalse();
        stored.IsMaternity.Should().BeFalse();
        stored.DaysPerEvent.Should().BeNull();
        stored.MinServiceMonths.Should().BeNull();
        stored.MaxEvents.Should().BeNull();
    }
}
