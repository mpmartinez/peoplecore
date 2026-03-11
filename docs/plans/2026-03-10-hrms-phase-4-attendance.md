# Phase 4: Attendance Module

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Holidays, daily time records (time-in/out), late/undertime calculation, overtime request workflow.

**Prereq:** Phase 3 complete.

---

### Task 14: Attendance — DTOs, Interfaces, Unit Tests

**Files:**
- Create: `src/PeopleCore.Application/Attendance/DTOs/AttendanceDtos.cs`
- Create: `src/PeopleCore.Application/Attendance/Interfaces/IAttendanceRepository.cs`
- Create: `src/PeopleCore.Application/Attendance/Interfaces/IAttendanceService.cs`
- Create: `src/PeopleCore.Application/Attendance/Interfaces/IOvertimeService.cs`
- Create: `src/PeopleCore.Application/Attendance/Interfaces/IHolidayService.cs`
- Create: `tests/PeopleCore.Application.Tests/Attendance/AttendanceServiceTests.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Attendance/DTOs/AttendanceDtos.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Attendance.DTOs;

public record AttendanceRecordDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly AttendanceDate,
    DateTime? TimeIn,
    DateTime? TimeOut,
    int LateMinutes,
    int UndertimeMinutes,
    int OvertimeMinutes,
    bool IsPresent,
    bool IsHoliday,
    HolidayType? HolidayType,
    string? Remarks);

public record TimeInRequest(Guid EmployeeId, DateTime TimeIn);
public record TimeOutRequest(Guid EmployeeId, DateTime TimeOut);

public record AttendanceSummaryDto(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int TotalWorkingDays,
    int DaysPresent,
    int TotalLateMinutes,
    int TotalUndertimeMinutes,
    int TotalOvertimeMinutes,
    int RegularHolidaysWorked,
    int SpecialHolidaysWorked);

public record OvertimeRequestDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly OvertimeDate,
    DateTime StartTime,
    DateTime EndTime,
    int TotalMinutes,
    string Reason,
    string Status,
    Guid? ApprovedBy,
    DateTime? ApprovedAt,
    string? RejectionReason);

public record CreateOvertimeRequestDto(
    Guid EmployeeId,
    DateOnly OvertimeDate,
    DateTime StartTime,
    DateTime EndTime,
    string Reason);

public record ApproveOvertimeDto(Guid ApproverId);
public record RejectOvertimeDto(string RejectionReason);

public record HolidayDto(Guid Id, string Name, DateOnly HolidayDate, HolidayType HolidayType, bool IsRecurring);
public record CreateHolidayDto(string Name, DateOnly HolidayDate, HolidayType HolidayType, bool IsRecurring);
```

**Step 2: Create service interfaces**

```csharp
// src/PeopleCore.Application/Attendance/Interfaces/IAttendanceService.cs
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IAttendanceService
{
    Task<AttendanceRecordDto> TimeInAsync(TimeInRequest request, CancellationToken ct = default);
    Task<AttendanceRecordDto> TimeOutAsync(TimeOutRequest request, CancellationToken ct = default);
    Task<PagedResult<AttendanceRecordDto>> GetAllAsync(Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);
    Task<AttendanceSummaryDto> GetSummaryAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

// src/PeopleCore.Application/Attendance/Interfaces/IOvertimeService.cs
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IOvertimeService
{
    Task<PagedResult<OvertimeRequestDto>> GetAllAsync(Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<OvertimeRequestDto> CreateAsync(CreateOvertimeRequestDto dto, CancellationToken ct = default);
    Task<OvertimeRequestDto> ApproveAsync(Guid id, ApproveOvertimeDto dto, CancellationToken ct = default);
    Task<OvertimeRequestDto> RejectAsync(Guid id, RejectOvertimeDto dto, CancellationToken ct = default);
}

// src/PeopleCore.Application/Attendance/Interfaces/IHolidayService.cs
using PeopleCore.Application.Attendance.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IHolidayService
{
    Task<IReadOnlyList<HolidayDto>> GetByYearAsync(int year, CancellationToken ct = default);
    Task<HolidayDto> CreateAsync(CreateHolidayDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default);
}

// src/PeopleCore.Application/Attendance/Interfaces/IAttendanceRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IAttendanceRepository : IRepository<AttendanceRecord>
{
    Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)> GetPagedAsync(Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceRecord>> GetByEmployeeAndPeriodAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

**Step 3: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/Attendance/AttendanceServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

public class AttendanceServiceTests
{
    private readonly Mock<IAttendanceRepository> _repo = new();
    private readonly Mock<IHolidayService> _holidayService = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly AttendanceService _sut;

    public AttendanceServiceTests()
    {
        _sut = new AttendanceService(_repo.Object, _holidayService.Object, _employeeRepo.Object);
    }

    private static Employee MakeEmployee(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = "Male",
        WorkEmail = "juan@test.com",
        EmploymentType = "FullTime",
        HireDate = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    [Fact]
    public async Task TimeInAsync_WhenEmployeeNotFound_ThrowsKeyNotFoundException()
    {
        _employeeRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
                     .ReturnsAsync((Employee?)null);

        var act = () => _sut.TimeInAsync(new TimeInRequest(Guid.NewGuid(), DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TimeInAsync_WhenAlreadyClockedIn_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var existing = new AttendanceRecord
        {
            EmployeeId = emp.Id,
            AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TimeIn = DateTime.UtcNow.AddHours(-2)
        };
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(emp.Id, It.IsAny<DateOnly>(), default))
             .ReturnsAsync(existing);

        var act = () => _sut.TimeInAsync(new TimeInRequest(emp.Id, DateTime.UtcNow));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already clocked in*");
    }

    [Fact]
    public async Task TimeOutAsync_WhenNotClockedIn_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(emp.Id, It.IsAny<DateOnly>(), default))
             .ReturnsAsync((AttendanceRecord?)null);

        var act = () => _sut.TimeOutAsync(new TimeOutRequest(emp.Id, DateTime.UtcNow));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*not clocked in*");
    }

    [Fact]
    public async Task TimeInAsync_At9AM_CalculatesLateMinutesCorrectly()
    {
        // Standard shift: 8:00 AM. Employee comes in at 9:05 AM = 65 minutes late.
        var emp = MakeEmployee();
        var timeIn = new DateTime(2025, 1, 6, 9, 5, 0, DateTimeKind.Utc);
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(emp.Id, DateOnly.FromDateTime(timeIn), default))
             .ReturnsAsync((AttendanceRecord?)null);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(false);
        _repo.Setup(r => r.AddAsync(It.IsAny<AttendanceRecord>(), default))
             .ReturnsAsync((AttendanceRecord a, CancellationToken _) => a);

        var result = await _sut.TimeInAsync(new TimeInRequest(emp.Id, timeIn));

        result.LateMinutes.Should().Be(65);
    }
}
```

**Step 4: Run failing tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "AttendanceServiceTests"
```
Expected: FAIL — `AttendanceService` does not exist.

**Step 5: Implement AttendanceService**

```csharp
// src/PeopleCore.Application/Attendance/Services/AttendanceService.cs
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Attendance.Services;

public class AttendanceService : IAttendanceService
{
    // Standard shift: 08:00 - 17:00 (configurable in future)
    private static readonly TimeOnly ShiftStart = new(8, 0);
    private static readonly TimeOnly ShiftEnd = new(17, 0);

    private readonly IAttendanceRepository _repo;
    private readonly IHolidayService _holidayService;
    private readonly IEmployeeRepository _employeeRepo;

    public AttendanceService(
        IAttendanceRepository repo,
        IHolidayService holidayService,
        IEmployeeRepository employeeRepo)
    {
        _repo = repo;
        _holidayService = holidayService;
        _employeeRepo = employeeRepo;
    }

    public async Task<AttendanceRecordDto> TimeInAsync(TimeInRequest request, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");

        var today = DateOnly.FromDateTime(request.TimeIn);
        var existing = await _repo.GetByEmployeeAndDateAsync(request.EmployeeId, today, ct);

        if (existing?.TimeIn is not null)
            throw new DomainException("Employee has already clocked in today.");

        var isHoliday = await _holidayService.IsHolidayAsync(today, ct);
        var lateMinutes = CalculateLateMinutes(TimeOnly.FromDateTime(request.TimeIn));

        var record = existing ?? new AttendanceRecord
        {
            EmployeeId = request.EmployeeId,
            AttendanceDate = today
        };

        record.TimeIn = request.TimeIn;
        record.IsPresent = true;
        record.LateMinutes = lateMinutes;
        record.IsHoliday = isHoliday;

        var saved = existing is null
            ? await _repo.AddAsync(record, ct)
            : await UpdateAndReturn(record, ct);

        return ToDto(saved, employee.FullName);
    }

    public async Task<AttendanceRecordDto> TimeOutAsync(TimeOutRequest request, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");

        var today = DateOnly.FromDateTime(request.TimeOut);
        var record = await _repo.GetByEmployeeAndDateAsync(request.EmployeeId, today, ct);

        if (record?.TimeIn is null)
            throw new DomainException("Employee is not clocked in today.");

        record.TimeOut = request.TimeOut;
        record.UndertimeMinutes = CalculateUndertimeMinutes(TimeOnly.FromDateTime(request.TimeOut));
        record.OvertimeMinutes = CalculateOvertimeMinutes(TimeOnly.FromDateTime(request.TimeOut));
        record.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(record, ct);
        return ToDto(record, employee.FullName);
    }

    public async Task<PagedResult<AttendanceRecordDto>> GetAllAsync(
        Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(employeeId, from, to, page, pageSize, ct);
        var dtos = items.Select(r => ToDto(r, r.Employee?.FullName ?? string.Empty)).ToList();
        return PagedResult<AttendanceRecordDto>.Create(dtos, total, page, pageSize);
    }

    public async Task<AttendanceSummaryDto> GetSummaryAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var records = await _repo.GetByEmployeeAndPeriodAsync(employeeId, from, to, ct);

        return new AttendanceSummaryDto(
            employeeId,
            employee.FullName,
            from,
            to,
            TotalWorkingDays(from, to),
            records.Count(r => r.IsPresent),
            records.Sum(r => r.LateMinutes),
            records.Sum(r => r.UndertimeMinutes),
            records.Sum(r => r.OvertimeMinutes),
            records.Count(r => r.IsHoliday && r.HolidayType == Domain.Enums.HolidayType.RegularHoliday && r.IsPresent),
            records.Count(r => r.IsHoliday && r.HolidayType == Domain.Enums.HolidayType.SpecialNonWorking && r.IsPresent));
    }

    // Late if time-in is after 08:00
    private static int CalculateLateMinutes(TimeOnly timeIn)
    {
        if (timeIn <= ShiftStart) return 0;
        return (int)(timeIn - ShiftStart).TotalMinutes;
    }

    // Undertime if time-out is before 17:00
    private static int CalculateUndertimeMinutes(TimeOnly timeOut)
    {
        if (timeOut >= ShiftEnd) return 0;
        return (int)(ShiftEnd - timeOut).TotalMinutes;
    }

    // Overtime if time-out is after 17:00
    private static int CalculateOvertimeMinutes(TimeOnly timeOut)
    {
        if (timeOut <= ShiftEnd) return 0;
        return (int)(timeOut - ShiftEnd).TotalMinutes;
    }

    private static int TotalWorkingDays(DateOnly from, DateOnly to)
    {
        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                count++;
        return count;
    }

    private async Task<AttendanceRecord> UpdateAndReturn(AttendanceRecord record, CancellationToken ct)
    {
        await _repo.UpdateAsync(record, ct);
        return record;
    }

    private static AttendanceRecordDto ToDto(AttendanceRecord r, string employeeName) => new(
        r.Id, r.EmployeeId, employeeName, r.AttendanceDate,
        r.TimeIn, r.TimeOut, r.LateMinutes, r.UndertimeMinutes, r.OvertimeMinutes,
        r.IsPresent, r.IsHoliday, r.HolidayType, r.Remarks);
}
```

**Step 6: Run tests — verify pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "AttendanceServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 7: Implement OvertimeService**

```csharp
// src/PeopleCore.Application/Attendance/Services/OvertimeService.cs
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Attendance.Services;

public class OvertimeService : IOvertimeService
{
    private readonly IOvertimeRepository _repo;
    private readonly IEmployeeRepository _employeeRepo;

    public OvertimeService(IOvertimeRepository repo, IEmployeeRepository employeeRepo)
    {
        _repo = repo;
        _employeeRepo = employeeRepo;
    }

    public async Task<PagedResult<OvertimeRequestDto>> GetAllAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(employeeId, status, page, pageSize, ct);
        return PagedResult<OvertimeRequestDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<OvertimeRequestDto> CreateAsync(CreateOvertimeRequestDto dto, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(dto.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {dto.EmployeeId} not found.");

        if (dto.EndTime <= dto.StartTime)
            throw new DomainException("Overtime end time must be after start time.");

        var totalMinutes = (int)(dto.EndTime - dto.StartTime).TotalMinutes;

        var request = new OvertimeRequest
        {
            EmployeeId = dto.EmployeeId,
            OvertimeDate = dto.OvertimeDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            TotalMinutes = totalMinutes,
            Reason = dto.Reason,
            Status = OvertimeStatus.Pending
        };

        var created = await _repo.AddAsync(request, ct);
        return ToDto(created);
    }

    public async Task<OvertimeRequestDto> ApproveAsync(Guid id, ApproveOvertimeDto dto, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Overtime request {id} not found.");

        if (request.Status != OvertimeStatus.Pending)
            throw new DomainException("Only pending overtime requests can be approved.");

        // Verify approver is the direct manager
        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)!;
        if (employee?.ReportingManagerId != dto.ApproverId)
            throw new DomainException("Only the direct reporting manager can approve overtime requests.");

        request.Status = OvertimeStatus.Approved;
        request.ApprovedBy = dto.ApproverId;
        request.ApprovedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(request, ct);
        return ToDto(request);
    }

    public async Task<OvertimeRequestDto> RejectAsync(Guid id, RejectOvertimeDto dto, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Overtime request {id} not found.");

        if (request.Status != OvertimeStatus.Pending)
            throw new DomainException("Only pending overtime requests can be rejected.");

        request.Status = OvertimeStatus.Rejected;
        request.RejectionReason = dto.RejectionReason;
        request.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(request, ct);
        return ToDto(request);
    }

    private static OvertimeRequestDto ToDto(OvertimeRequest r) => new(
        r.Id, r.EmployeeId, r.Employee?.FullName ?? string.Empty,
        r.OvertimeDate, r.StartTime, r.EndTime, r.TotalMinutes,
        r.Reason, r.Status.ToString(), r.ApprovedBy, r.ApprovedAt, r.RejectionReason);
}
```

**Step 8: Add IOvertimeRepository interface**

```csharp
// src/PeopleCore.Application/Attendance/Interfaces/IOvertimeRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IOvertimeRepository : IRepository<OvertimeRequest>
{
    Task<(IReadOnlyList<OvertimeRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<OvertimeRequest>> GetApprovedByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

**Step 9: Build**

```bash
dotnet build PeopleCore.sln
```

**Step 10: Commit**

```bash
git add -A
git commit -m "feat: implement attendance and overtime services with unit tests"
```

---

### Task 15: Attendance — Controller

**Files:**
- Create: `src/PeopleCore.API/Controllers/Attendance/AttendanceController.cs`
- Create: `src/PeopleCore.API/Controllers/Attendance/OvertimeController.cs`
- Create: `src/PeopleCore.API/Controllers/Attendance/HolidaysController.cs`

```csharp
// src/PeopleCore.API/Controllers/Attendance/AttendanceController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    public AttendanceController(IAttendanceService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(employeeId, from, to, page, pageSize, ct));

    [HttpPost("time-in")]
    public async Task<IActionResult> TimeIn([FromBody] TimeInRequest request, CancellationToken ct)
        => Ok(await _service.TimeInAsync(request, ct));

    [HttpPost("time-out")]
    public async Task<IActionResult> TimeOut([FromBody] TimeOutRequest request, CancellationToken ct)
        => Ok(await _service.TimeOutAsync(request, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
        => Ok(await _service.GetSummaryAsync(employeeId, from, to, ct));
}

// src/PeopleCore.API/Controllers/Attendance/OvertimeController.cs
[ApiController]
[Route("api/overtime-requests")]
[Authorize]
public class OvertimeController : ControllerBase
{
    private readonly IOvertimeService _service;
    public OvertimeController(IOvertimeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(employeeId, status, page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOvertimeRequestDto dto, CancellationToken ct)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}/approve")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveOvertimeDto dto, CancellationToken ct)
        => Ok(await _service.ApproveAsync(id, dto, ct));

    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectOvertimeDto dto, CancellationToken ct)
        => Ok(await _service.RejectAsync(id, dto, ct));
}
```

**Step 2: Commit**

```bash
git add -A
git commit -m "feat: add attendance and overtime controllers"
```

---

---

### Task 16 (Extension): Biometric Device Sync + CSV Import

> These are optional tasks to implement after Task 15. They reuse the same late/undertime calculation logic already in `AttendanceService`.

#### 16A — AttendanceDevice entity + migration

**Add to Domain:**

```csharp
// src/PeopleCore.Domain/Entities/Attendance/AttendanceDevice.cs
namespace PeopleCore.Domain.Entities.Attendance;

public class AttendanceDevice : AuditableEntity
{
    public string Name { get; set; } = string.Empty;        // e.g. "Main Entrance"
    public string IpAddress { get; set; } = string.Empty;   // e.g. "192.168.1.201"
    public int Port { get; set; } = 4370;                   // ZKTeco default
    public string Protocol { get; set; } = "ZKTeco";        // ZKTeco | HTTP | ADMS
    public bool IsActive { get; set; } = true;
    public DateTime? LastSyncAt { get; set; }
    public string? Location { get; set; }
}
```

**EF Config:**

```csharp
// table: attendance_devices
builder.ToTable("attendance_devices");
builder.HasKey(x => x.Id);
builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
builder.Property(x => x.IpAddress).HasMaxLength(50).IsRequired();
builder.Property(x => x.Protocol).HasMaxLength(20).IsRequired();
```

**Add migration:**

```bash
dotnet ef migrations add AddAttendanceDevice \
  --project src/PeopleCore.Infrastructure \
  --startup-project src/PeopleCore.API
```

---

#### 16B — Bulk sync endpoint (for biometric devices + CSV)

Both biometric sync workers and CSV imports use the same endpoint. The server doesn't care about the source.

**Add to DTOs:**

```csharp
// src/PeopleCore.Application/Attendance/DTOs/AttendanceDtos.cs (append)

// One punch record from any source (biometric device, CSV, manual)
public record AttendancePunchDto(
    string EmployeeNumber,   // mapped to Employee.EmployeeNumber
    DateTime PunchTime,
    string? DeviceId = null  // optional, for audit trail
);

public record AttendanceImportResultDto(
    int Imported,
    int Skipped,
    IReadOnlyList<string> Errors
);
```

**Add to IAttendanceService:**

```csharp
Task<AttendanceImportResultDto> SyncPunchesAsync(
    IReadOnlyList<AttendancePunchDto> punches,
    CancellationToken ct = default);
```

**Implement in AttendanceService:**

```csharp
public async Task<AttendanceImportResultDto> SyncPunchesAsync(
    IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default)
{
    int imported = 0, skipped = 0;
    var errors = new List<string>();

    foreach (var punch in punches)
    {
        try
        {
            var employee = await _employeeRepo.GetByNumberAsync(punch.EmployeeNumber, ct);
            if (employee is null)
            {
                errors.Add($"Employee '{punch.EmployeeNumber}' not found.");
                skipped++;
                continue;
            }

            var date = DateOnly.FromDateTime(punch.PunchTime);
            var existing = await _repo.GetByEmployeeAndDateAsync(employee.Id, date, ct);

            if (existing is null)
            {
                // First punch of day = time-in
                await TimeInAsync(new TimeInRequest(employee.Id, punch.PunchTime), ct);
            }
            else if (existing.TimeIn is not null && existing.TimeOut is null &&
                     punch.PunchTime > existing.TimeIn)
            {
                // Later punch = time-out
                await TimeOutAsync(new TimeOutRequest(employee.Id, punch.PunchTime), ct);
            }
            else
            {
                skipped++; // duplicate or out-of-order punch
                continue;
            }
            imported++;
        }
        catch (Exception ex)
        {
            errors.Add($"Row for '{punch.EmployeeNumber}': {ex.Message}");
            skipped++;
        }
    }

    return new AttendanceImportResultDto(imported, skipped, errors);
}
```

**Add endpoint to AttendanceController:**

```csharp
// POST /api/attendance/sync  — accepts JSON (from biometric worker) or parsed CSV
[HttpPost("sync")]
[Authorize(Roles = "Admin,HRManager,Service")]
public async Task<IActionResult> Sync(
    [FromBody] IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct)
    => Ok(await _service.SyncPunchesAsync(punches, ct));

// POST /api/attendance/import  — accepts CSV/Excel file upload
[HttpPost("import")]
[Authorize(Roles = "Admin,HRManager")]
public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
{
    using var stream = file.OpenReadStream();
    var punches = ParseCsv(stream);   // see below
    return Ok(await _service.SyncPunchesAsync(punches, ct));
}

private static List<AttendancePunchDto> ParseCsv(Stream stream)
{
    // CSV format: employee_number,date,time_in,time_out
    // Uses CsvHelper package (add: dotnet add package CsvHelper)
    using var reader = new StreamReader(stream);
    using var csv = new CsvHelper.CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);
    var punches = new List<AttendancePunchDto>();
    csv.Read(); csv.ReadHeader();
    while (csv.Read())
    {
        var empNum = csv.GetField("employee_number")!;
        var date = csv.GetField("date")!;
        var timeIn = csv.GetField("time_in");
        var timeOut = csv.GetField("time_out");

        if (!string.IsNullOrEmpty(timeIn))
            punches.Add(new(empNum, DateTime.Parse($"{date} {timeIn}")));
        if (!string.IsNullOrEmpty(timeOut))
            punches.Add(new(empNum, DateTime.Parse($"{date} {timeOut}")));
    }
    return punches;
}
```

**Install CsvHelper:**

```bash
dotnet add src/PeopleCore.API/PeopleCore.API.csproj package CsvHelper
```

---

#### 16C — AttendanceSync worker (separate project, implement post-Phase 9)

This is a standalone `Worker Service` that polls biometric devices over LAN and calls `POST /api/attendance/sync`.

```
PeopleCore.AttendanceSync/
├── Worker.cs                  — PeriodicTimer, calls DevicePoller
├── DevicePoller.cs            — ZKTeco TCP or HTTP polling
├── AttendanceSyncClient.cs    — HTTP client → POST /api/attendance/sync
└── appsettings.json           — API base URL, service token, device IPs
```

> ZKLib.NET NuGet package provides .NET bindings for ZKTeco TCP protocol (port 4370).
> One worker can manage multiple devices; each device is loaded from `attendance_devices` table via the API.

**Defer this to post-Phase 9.** The sync endpoint (`POST /api/attendance/sync`) is ready in Phase 4, so the worker can be added at any time without touching the main API.

---

**Phase 4 complete.** Continue with [Phase 5 — Leave](2026-03-10-hrms-phase-5-leave.md).
