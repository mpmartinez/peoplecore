# Phase 8: Payroll Integration Export

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Dedicated API endpoints consumed by the payroll system — employee master data, attendance summaries, approved leaves, approved overtime, and status changes.

**Prereq:** Phase 7 complete.

---

### Task 20: Payroll Export — DTOs, Interface, Service, Tests, Controller

**Files:**
- Create: `src/PeopleCore.Application/PayrollIntegration/DTOs/PayrollExportDtos.cs`
- Create: `src/PeopleCore.Application/PayrollIntegration/Interfaces/IPayrollExportService.cs`
- Create: `src/PeopleCore.Application/PayrollIntegration/Services/PayrollExportService.cs`
- Create: `tests/PeopleCore.Application.Tests/PayrollIntegration/PayrollExportServiceTests.cs`
- Create: `src/PeopleCore.API/Controllers/PayrollExport/PayrollExportController.cs`

**Step 1: Create export DTOs**

```csharp
// src/PeopleCore.Application/PayrollIntegration/DTOs/PayrollExportDtos.cs
namespace PeopleCore.Application.PayrollIntegration.DTOs;

/// <summary>Employee master data for payroll system consumption.</summary>
public record PayrollEmployeeDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    string WorkEmail,
    string DepartmentName,
    string PositionTitle,
    string EmploymentStatus,
    string EmploymentType,
    DateOnly HireDate,
    DateOnly? RegularizationDate,
    bool Is13thMonthEligible,
    bool IsActive,
    string? SssNumber,
    string? PhilHealthNumber,
    string? PagIbigNumber,
    string? TinNumber);

/// <summary>Attendance summary per employee per cutoff period.</summary>
public record PayrollAttendanceSummaryDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string FullName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int DaysPresent,
    int TotalLateMinutes,
    int TotalUndertimeMinutes,
    int TotalApprovedOvertimeMinutes,
    int RegularHolidaysWorked,
    int SpecialHolidaysWorked);

/// <summary>Approved leave deductions for payroll processing.</summary>
public record PayrollLeaveDeductionDto(
    Guid LeaveRequestId,
    Guid EmployeeId,
    string EmployeeNumber,
    string FullName,
    string LeaveTypeCode,
    string LeaveTypeName,
    bool IsPaid,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    DateTime ApprovedAt);

/// <summary>Approved overtime for payroll computation.</summary>
public record PayrollOvertimeDto(
    Guid OvertimeRequestId,
    Guid EmployeeId,
    string EmployeeNumber,
    string FullName,
    DateOnly OvertimeDate,
    int TotalMinutes,
    DateTime ApprovedAt);

/// <summary>Employment status changes within a date range (for payroll adjustments).</summary>
public record PayrollStatusChangeDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string FullName,
    string OldStatus,
    string NewStatus,
    DateOnly EffectiveDate);
```

**Step 2: Create service interface**

```csharp
// src/PeopleCore.Application/PayrollIntegration/Interfaces/IPayrollExportService.cs
using PeopleCore.Application.PayrollIntegration.DTOs;

namespace PeopleCore.Application.PayrollIntegration.Interfaces;

public interface IPayrollExportService
{
    Task<IReadOnlyList<PayrollEmployeeDto>> GetEmployeeMasterDataAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PayrollAttendanceSummaryDto>> GetAttendanceSummaryAsync(DateOnly from, DateOnly to, Guid? employeeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollLeaveDeductionDto>> GetApprovedLeavesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollOvertimeDto>> GetApprovedOvertimeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollStatusChangeDto>> GetStatusChangesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

**Step 3: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/PayrollIntegration/PayrollExportServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.PayrollIntegration.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.PayrollIntegration;

public class PayrollExportServiceTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepo = new();
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<IOvertimeRepository> _overtimeRepo = new();
    private readonly PayrollExportService _sut;

    public PayrollExportServiceTests()
    {
        _sut = new PayrollExportService(
            _employeeRepo.Object,
            _attendanceRepo.Object,
            _leaveRepo.Object,
            _overtimeRepo.Object);
    }

    [Fact]
    public async Task GetEmployeeMasterDataAsync_ReturnsOnlyActiveEmployees()
    {
        var employees = new List<Employee>
        {
            new() {
                Id = Guid.NewGuid(), EmployeeNumber = "EMP-001",
                FirstName = "Juan", LastName = "dela Cruz",
                DateOfBirth = new DateOnly(1990,1,1), Gender = "Male",
                WorkEmail = "juan@test.com", EmploymentType = "FullTime",
                HireDate = new DateOnly(2020,1,1),
                EmploymentStatus = EmploymentStatus.Regular, IsActive = true,
                GovernmentIds = []
            },
            new() {
                Id = Guid.NewGuid(), EmployeeNumber = "EMP-002",
                FirstName = "Maria", LastName = "Santos",
                DateOfBirth = new DateOnly(1988,5,15), Gender = "Female",
                WorkEmail = "maria@test.com", EmploymentType = "FullTime",
                HireDate = new DateOnly(2019,6,1),
                EmploymentStatus = EmploymentStatus.Regular, IsActive = false,
                GovernmentIds = []
            }
        };

        _employeeRepo.Setup(r => r.GetAllAsync(default)).ReturnsAsync(employees);

        var result = await _sut.GetEmployeeMasterDataAsync();

        result.Should().HaveCount(1);
        result[0].EmployeeNumber.Should().Be("EMP-001");
    }

    [Fact]
    public async Task GetAttendanceSummaryAsync_CalculatesTotalsCorrectly()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2025, 3, 1);
        var to = new DateOnly(2025, 3, 15);

        var employee = new Employee
        {
            Id = employeeId, EmployeeNumber = "EMP-001",
            FirstName = "Juan", LastName = "dela Cruz",
            DateOfBirth = new DateOnly(1990,1,1), Gender = "Male",
            WorkEmail = "juan@test.com", EmploymentType = "FullTime",
            HireDate = new DateOnly(2020,1,1), IsActive = true
        };

        var records = new List<AttendanceRecord>
        {
            new() { EmployeeId = employeeId, AttendanceDate = new DateOnly(2025,3,3), IsPresent = true, LateMinutes = 15, UndertimeMinutes = 0 },
            new() { EmployeeId = employeeId, AttendanceDate = new DateOnly(2025,3,4), IsPresent = true, LateMinutes = 0, UndertimeMinutes = 30 },
            new() { EmployeeId = employeeId, AttendanceDate = new DateOnly(2025,3,5), IsPresent = true, LateMinutes = 0, UndertimeMinutes = 0 },
        };

        _employeeRepo.Setup(r => r.GetByIdAsync(employeeId, default)).ReturnsAsync(employee);
        _attendanceRepo.Setup(r => r.GetByEmployeeAndPeriodAsync(employeeId, from, to, default)).ReturnsAsync(records);

        var result = await _sut.GetAttendanceSummaryAsync(from, to, employeeId);

        result.Should().HaveCount(1);
        result[0].DaysPresent.Should().Be(3);
        result[0].TotalLateMinutes.Should().Be(15);
        result[0].TotalUndertimeMinutes.Should().Be(30);
    }

    [Fact]
    public async Task GetApprovedLeavesAsync_ReturnsOnlyUnpaidLeavesForDeduction()
    {
        var from = new DateOnly(2025, 3, 1);
        var to = new DateOnly(2025, 3, 31);

        var leaveType = new LeaveType { Code = "SL", Name = "Sick Leave", IsPaid = false, MaxDaysPerYear = 15 };
        var requests = new List<LeaveRequest>
        {
            new() {
                Id = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                LeaveType = leaveType,
                StartDate = new DateOnly(2025,3,5),
                EndDate = new DateOnly(2025,3,5),
                TotalDays = 1,
                Status = LeaveStatus.Approved,
                ApprovedAt = DateTime.UtcNow
            }
        };

        _leaveRepo.Setup(r => r.GetApprovedByPeriodAsync(from, to, default)).ReturnsAsync(requests);

        var result = await _sut.GetApprovedLeavesAsync(from, to);

        result.Should().HaveCount(1);
        result[0].LeaveTypeCode.Should().Be("SL");
        result[0].IsPaid.Should().BeFalse();
    }
}
```

**Step 4: Run failing tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "PayrollExportServiceTests"
```
Expected: FAIL.

**Step 5: Implement PayrollExportService**

```csharp
// src/PeopleCore.Application/PayrollIntegration/Services/PayrollExportService.cs
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.PayrollIntegration.DTOs;
using PeopleCore.Application.PayrollIntegration.Interfaces;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.PayrollIntegration.Services;

public class PayrollExportService : IPayrollExportService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILeaveRequestRepository _leaveRepo;
    private readonly IOvertimeRepository _overtimeRepo;

    public PayrollExportService(
        IEmployeeRepository employeeRepo,
        IAttendanceRepository attendanceRepo,
        ILeaveRequestRepository leaveRepo,
        IOvertimeRepository overtimeRepo)
    {
        _employeeRepo = employeeRepo;
        _attendanceRepo = attendanceRepo;
        _leaveRepo = leaveRepo;
        _overtimeRepo = overtimeRepo;
    }

    public async Task<IReadOnlyList<PayrollEmployeeDto>> GetEmployeeMasterDataAsync(CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        return employees
            .Where(e => e.IsActive)
            .Select(e => new PayrollEmployeeDto(
                e.Id,
                e.EmployeeNumber,
                e.FirstName,
                e.MiddleName,
                e.LastName,
                e.WorkEmail,
                e.Department?.Name ?? string.Empty,
                e.Position?.Title ?? string.Empty,
                e.EmploymentStatus.ToString(),
                e.EmploymentType,
                e.HireDate,
                e.RegularizationDate,
                e.Is13thMonthEligible,
                e.IsActive,
                e.GovernmentIds.FirstOrDefault(g => g.IdType == GovernmentIdType.SSS)?.IdNumber,
                e.GovernmentIds.FirstOrDefault(g => g.IdType == GovernmentIdType.PhilHealth)?.IdNumber,
                e.GovernmentIds.FirstOrDefault(g => g.IdType == GovernmentIdType.PagIbig)?.IdNumber,
                e.GovernmentIds.FirstOrDefault(g => g.IdType == GovernmentIdType.TIN)?.IdNumber))
            .ToList();
    }

    public async Task<IReadOnlyList<PayrollAttendanceSummaryDto>> GetAttendanceSummaryAsync(
        DateOnly from, DateOnly to, Guid? employeeId = null, CancellationToken ct = default)
    {
        var result = new List<PayrollAttendanceSummaryDto>();

        if (employeeId.HasValue)
        {
            var employee = await _employeeRepo.GetByIdAsync(employeeId.Value, ct);
            if (employee is null) return result;

            var records = await _attendanceRepo.GetByEmployeeAndPeriodAsync(employeeId.Value, from, to, ct);
            result.Add(BuildSummary(employee.EmployeeNumber, employee.FullName, employeeId.Value, from, to, records));
        }
        else
        {
            var employees = await _employeeRepo.GetAllAsync(ct);
            foreach (var emp in employees.Where(e => e.IsActive))
            {
                var records = await _attendanceRepo.GetByEmployeeAndPeriodAsync(emp.Id, from, to, ct);
                result.Add(BuildSummary(emp.EmployeeNumber, emp.FullName, emp.Id, from, to, records));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<PayrollLeaveDeductionDto>> GetApprovedLeavesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var requests = await _leaveRepo.GetApprovedByPeriodAsync(from, to, ct);
        return requests.Select(r => new PayrollLeaveDeductionDto(
            r.Id,
            r.EmployeeId,
            r.Employee?.EmployeeNumber ?? string.Empty,
            r.Employee?.FullName ?? string.Empty,
            r.LeaveType?.Code ?? string.Empty,
            r.LeaveType?.Name ?? string.Empty,
            r.LeaveType?.IsPaid ?? true,
            r.StartDate,
            r.EndDate,
            r.TotalDays,
            r.ApprovedAt ?? DateTime.UtcNow)).ToList();
    }

    public async Task<IReadOnlyList<PayrollOvertimeDto>> GetApprovedOvertimeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var requests = await _overtimeRepo.GetApprovedByPeriodAsync(from, to, ct);
        return requests.Select(r => new PayrollOvertimeDto(
            r.Id,
            r.EmployeeId,
            r.Employee?.EmployeeNumber ?? string.Empty,
            r.Employee?.FullName ?? string.Empty,
            r.OvertimeDate,
            r.TotalMinutes,
            r.ApprovedAt ?? DateTime.UtcNow)).ToList();
    }

    public async Task<IReadOnlyList<PayrollStatusChangeDto>> GetStatusChangesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Return employees whose regularization date falls in the period
        // (Probationary → Regular is the most common status change)
        var employees = await _employeeRepo.GetAllAsync(ct);
        return employees
            .Where(e => e.RegularizationDate.HasValue
                        && e.RegularizationDate.Value >= from
                        && e.RegularizationDate.Value <= to)
            .Select(e => new PayrollStatusChangeDto(
                e.Id,
                e.EmployeeNumber,
                e.FullName,
                "Probationary",
                "Regular",
                e.RegularizationDate!.Value))
            .ToList();
    }

    private static PayrollAttendanceSummaryDto BuildSummary(
        string employeeNumber, string fullName, Guid employeeId,
        DateOnly from, DateOnly to,
        IReadOnlyList<Domain.Entities.Attendance.AttendanceRecord> records)
        => new(
            employeeId,
            employeeNumber,
            fullName,
            from,
            to,
            records.Count(r => r.IsPresent),
            records.Sum(r => r.LateMinutes),
            records.Sum(r => r.UndertimeMinutes),
            records.Sum(r => r.OvertimeMinutes),
            records.Count(r => r.IsHoliday && r.HolidayType == Domain.Enums.HolidayType.RegularHoliday && r.IsPresent),
            records.Count(r => r.IsHoliday && r.HolidayType == Domain.Enums.HolidayType.SpecialNonWorking && r.IsPresent));
}
```

**Step 6: Run tests — verify pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "PayrollExportServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 7: Create PayrollExportController**

```csharp
// src/PeopleCore.API/Controllers/PayrollExport/PayrollExportController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.PayrollIntegration.Interfaces;

namespace PeopleCore.API.Controllers.PayrollExport;

/// <summary>
/// Endpoints consumed by the external payroll system.
/// Authenticate with a dedicated service account JWT (Role: PayrollService).
/// </summary>
[ApiController]
[Route("api/payroll-export")]
[Authorize(Roles = "Admin,HRManager,PayrollService")]
public class PayrollExportController : ControllerBase
{
    private readonly IPayrollExportService _service;
    public PayrollExportController(IPayrollExportService service) => _service = service;

    /// <summary>Active employee master data including government IDs.</summary>
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken ct)
        => Ok(await _service.GetEmployeeMasterDataAsync(ct));

    /// <summary>Attendance summary per employee for a payroll period.</summary>
    [HttpGet("attendance-summary")]
    public async Task<IActionResult> GetAttendanceSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId,
        CancellationToken ct)
        => Ok(await _service.GetAttendanceSummaryAsync(from, to, employeeId, ct));

    /// <summary>Approved leave requests — include IsPaid flag for deduction logic.</summary>
    [HttpGet("approved-leaves")]
    public async Task<IActionResult> GetApprovedLeaves(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
        => Ok(await _service.GetApprovedLeavesAsync(from, to, ct));

    /// <summary>Approved overtime requests for payroll computation.</summary>
    [HttpGet("approved-overtime")]
    public async Task<IActionResult> GetApprovedOvertime(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
        => Ok(await _service.GetApprovedOvertimeAsync(from, to, ct));

    /// <summary>Employment status changes (e.g. regularization) within the period.</summary>
    [HttpGet("status-changes")]
    public async Task<IActionResult> GetStatusChanges(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
        => Ok(await _service.GetStatusChangesAsync(from, to, ct));
}
```

**Step 8: Add PayrollService role to Identity seed data** (add alongside Admin/HRManager/Manager/Employee roles in startup/seed)

**Step 9: Run all tests**

```bash
dotnet test PeopleCore.sln
```
Expected: All tests pass.

**Step 10: Build**

```bash
dotnet build PeopleCore.sln
```
Expected: `Build succeeded. 0 Error(s)`

**Step 11: Commit**

```bash
git add -A
git commit -m "feat: implement payroll export service and controller with unit tests"
```

---

**Phase 8 complete.** Continue with [Phase 9 — Blazor WASM Web](2026-03-10-hrms-phase-9-blazor-web.md).
