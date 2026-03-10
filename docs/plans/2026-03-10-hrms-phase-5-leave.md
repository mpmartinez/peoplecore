# Phase 5: Leave Module

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Leave types, balances, request workflow with approval, leave day counting (excludes weekends + PH holidays), monthly accrual hosted service.

**Prereq:** Phase 4 complete.

---

### Task 16: Leave — DTOs, Interfaces, Unit Tests

**Files:**
- Create: `src/PeopleCore.Application/Leave/DTOs/LeaveDtos.cs`
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveTypeService.cs`
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveBalanceService.cs`
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveRequestService.cs`
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveRequestRepository.cs`
- Create: `tests/PeopleCore.Application.Tests/Leave/LeaveRequestServiceTests.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Leave/DTOs/LeaveDtos.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Leave.DTOs;

public record LeaveTypeDto(
    Guid Id, string Name, string Code,
    decimal MaxDaysPerYear, bool IsPaid, bool IsCarryOver,
    decimal? CarryOverMaxDays, string? GenderRestriction,
    bool RequiresDocument, bool IsActive);

public record CreateLeaveTypeDto(
    string Name, string Code, decimal MaxDaysPerYear,
    bool IsPaid, bool IsCarryOver, decimal? CarryOverMaxDays,
    string? GenderRestriction, bool RequiresDocument);

public record LeaveBalanceDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid LeaveTypeId, string LeaveTypeName,
    int Year, decimal TotalDays, decimal UsedDays,
    decimal CarriedOverDays, decimal RemainingDays);

public record LeaveRequestDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid LeaveTypeId, string LeaveTypeName,
    DateOnly StartDate, DateOnly EndDate,
    decimal TotalDays, string? Reason,
    LeaveStatus Status, Guid? ApprovedBy, DateTime? ApprovedAt,
    string? RejectionReason, DateTime CreatedAt);

public record CreateLeaveRequestDto(
    Guid EmployeeId, Guid LeaveTypeId,
    DateOnly StartDate, DateOnly EndDate, string? Reason);

public record ApproveLeaveDto(Guid ApproverId);
public record RejectLeaveDto(string RejectionReason);
```

**Step 2: Create service interfaces**

```csharp
// src/PeopleCore.Application/Leave/Interfaces/ILeaveTypeService.cs
using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveTypeService
{
    Task<IReadOnlyList<LeaveTypeDto>> GetAllAsync(CancellationToken ct = default);
    Task<LeaveTypeDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveTypeDto> CreateAsync(CreateLeaveTypeDto dto, CancellationToken ct = default);
    Task<LeaveTypeDto> UpdateAsync(Guid id, CreateLeaveTypeDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// src/PeopleCore.Application/Leave/Interfaces/ILeaveBalanceService.cs
using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveBalanceService
{
    Task<IReadOnlyList<LeaveBalanceDto>> GetByEmployeeAsync(Guid employeeId, int? year = null, CancellationToken ct = default);
    Task AccrueMonthlyAsync(int year, int month, CancellationToken ct = default);
    Task CarryOverAsync(int fromYear, int toYear, CancellationToken ct = default);
}

// src/PeopleCore.Application/Leave/Interfaces/ILeaveRequestService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveRequestService
{
    Task<PagedResult<LeaveRequestDto>> GetAllAsync(Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<LeaveRequestDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto dto, CancellationToken ct = default);
    Task<LeaveRequestDto> ApproveAsync(Guid id, ApproveLeaveDto dto, CancellationToken ct = default);
    Task<LeaveRequestDto> RejectAsync(Guid id, RejectLeaveDto dto, CancellationToken ct = default);
    Task CancelAsync(Guid id, Guid requestingEmployeeId, CancellationToken ct = default);
}

// src/PeopleCore.Application/Leave/Interfaces/ILeaveRequestRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveRequestRepository : IRepository<LeaveRequest>
{
    Task<(IReadOnlyList<LeaveRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<bool> HasOverlapAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid? excludeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveRequest>> GetApprovedByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

**Step 3: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/Leave/LeaveRequestServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

public class LeaveRequestServiceTests
{
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<ILeaveBalanceRepository> _balanceRepo = new();
    private readonly Mock<IHolidayService> _holidayService = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly LeaveRequestService _sut;

    public LeaveRequestServiceTests()
    {
        _sut = new LeaveRequestService(_leaveRepo.Object, _balanceRepo.Object, _holidayService.Object, _employeeRepo.Object);
    }

    private static Employee MakeEmployee(string gender = "Male") => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = gender,
        WorkEmail = "juan@test.com",
        EmploymentType = "FullTime",
        HireDate = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    private static LeaveType MakeLeaveType(string? genderRestriction = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Vacation Leave",
        Code = "VL",
        MaxDaysPerYear = 15,
        IsPaid = true,
        GenderRestriction = genderRestriction
    };

    private static LeaveBalance MakeBalance(Guid employeeId, Guid leaveTypeId, decimal remaining = 10) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        Year = DateTime.UtcNow.Year,
        TotalDays = remaining,
        UsedDays = 0
    };

    [Fact]
    public async Task CreateAsync_WhenInsufficientBalance_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var balance = MakeBalance(emp.Id, lt.Id, remaining: 2);

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(false);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(false);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 14), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*insufficient leave balance*");
    }

    [Fact]
    public async Task CreateAsync_WhenDatesOverlap_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var balance = MakeBalance(emp.Id, lt.Id, remaining: 15);

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(false);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(true);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*overlapping leave request*");
    }

    [Fact]
    public async Task CreateAsync_WhenMaternityLeaveAndMaleEmployee_ThrowsDomainException()
    {
        var emp = MakeEmployee(gender: "Male");
        var lt = MakeLeaveType(genderRestriction: "Female");
        var balance = MakeBalance(emp.Id, lt.Id);

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*not eligible*");
    }

    [Fact]
    public async Task CreateAsync_ExcludesWeekendsFromTotalDays()
    {
        // Mon 2025-03-10 to Fri 2025-03-14 = 5 working days
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var balance = MakeBalance(emp.Id, lt.Id, remaining: 15);

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(false);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(false);
        _leaveRepo.Setup(r => r.AddAsync(It.IsAny<LeaveRequest>(), default))
                  .ReturnsAsync((LeaveRequest l, CancellationToken _) => l);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 14), null);

        var result = await _sut.CreateAsync(dto);

        result.TotalDays.Should().Be(5);
    }
}
```

**Step 4: Run failing tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "LeaveRequestServiceTests"
```
Expected: FAIL.

**Step 5: Add ILeaveBalanceRepository**

```csharp
// src/PeopleCore.Application/Leave/Interfaces/ILeaveBalanceRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveBalanceRepository : IRepository<LeaveBalance>
{
    Task<LeaveBalance?> GetByEmployeeAndTypeAsync(Guid employeeId, Guid leaveTypeId, int year, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalance>> GetByEmployeeAsync(Guid employeeId, int? year = null, CancellationToken ct = default);
}
```

**Step 6: Implement LeaveRequestService**

```csharp
// src/PeopleCore.Application/Leave/Services/LeaveRequestService.cs
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Leave.Services;

public class LeaveRequestService : ILeaveRequestService
{
    private readonly ILeaveRequestRepository _leaveRepo;
    private readonly ILeaveBalanceRepository _balanceRepo;
    private readonly IHolidayService _holidayService;
    private readonly IEmployeeRepository _employeeRepo;

    public LeaveRequestService(
        ILeaveRequestRepository leaveRepo,
        ILeaveBalanceRepository balanceRepo,
        IHolidayService holidayService,
        IEmployeeRepository employeeRepo)
    {
        _leaveRepo = leaveRepo;
        _balanceRepo = balanceRepo;
        _holidayService = holidayService;
        _employeeRepo = employeeRepo;
    }

    public async Task<PagedResult<LeaveRequestDto>> GetAllAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _leaveRepo.GetPagedAsync(employeeId, status, page, pageSize, ct);
        return PagedResult<LeaveRequestDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<LeaveRequestDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");
        return ToDto(request);
    }

    public async Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto dto, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(dto.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {dto.EmployeeId} not found.");

        // Load leave type from balance (includes leave type navigation)
        var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(
            dto.EmployeeId, dto.LeaveTypeId, dto.StartDate.Year, ct);

        // Gender restriction check
        var leaveType = balance?.LeaveType;
        if (leaveType?.GenderRestriction is not null && leaveType.GenderRestriction != employee.Gender)
            throw new DomainException($"Employee is not eligible for this leave type.");

        // Overlap check
        if (await _leaveRepo.HasOverlapAsync(dto.EmployeeId, dto.StartDate, dto.EndDate, null, ct))
            throw new DomainException("Employee has an overlapping leave request for these dates.");

        // Calculate working days (exclude weekends + holidays)
        var totalDays = await CountWorkingDaysAsync(dto.StartDate, dto.EndDate, ct);

        // Balance check
        if (balance is null || balance.RemainingDays < totalDays)
            throw new DomainException($"Employee has insufficient leave balance. Requested: {totalDays}, Available: {balance?.RemainingDays ?? 0}");

        var request = new LeaveRequest
        {
            EmployeeId = dto.EmployeeId,
            LeaveTypeId = dto.LeaveTypeId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            TotalDays = totalDays,
            Reason = dto.Reason,
            Status = LeaveStatus.Pending
        };

        var created = await _leaveRepo.AddAsync(request, ct);
        return ToDto(created);
    }

    public async Task<LeaveRequestDto> ApproveAsync(Guid id, ApproveLeaveDto dto, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");

        if (request.Status != LeaveStatus.Pending)
            throw new DomainException("Only pending leave requests can be approved.");

        var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(
            request.EmployeeId, request.LeaveTypeId, request.StartDate.Year, ct)
            ?? throw new DomainException("Leave balance not found.");

        balance.UsedDays += request.TotalDays;
        balance.UpdatedAt = DateTime.UtcNow;

        request.Status = LeaveStatus.Approved;
        request.ApprovedBy = dto.ApproverId;
        request.ApprovedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _balanceRepo.UpdateAsync(balance, ct);
        await _leaveRepo.UpdateAsync(request, ct);
        return ToDto(request);
    }

    public async Task<LeaveRequestDto> RejectAsync(Guid id, RejectLeaveDto dto, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");

        if (request.Status != LeaveStatus.Pending)
            throw new DomainException("Only pending leave requests can be rejected.");

        request.Status = LeaveStatus.Rejected;
        request.RejectionReason = dto.RejectionReason;
        request.UpdatedAt = DateTime.UtcNow;

        await _leaveRepo.UpdateAsync(request, ct);
        return ToDto(request);
    }

    public async Task CancelAsync(Guid id, Guid requestingEmployeeId, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");

        if (request.EmployeeId != requestingEmployeeId)
            throw new DomainException("You can only cancel your own leave requests.");

        if (request.Status == LeaveStatus.Approved)
        {
            // Restore balance
            var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(
                request.EmployeeId, request.LeaveTypeId, request.StartDate.Year, ct);
            if (balance is not null)
            {
                balance.UsedDays -= request.TotalDays;
                balance.UpdatedAt = DateTime.UtcNow;
                await _balanceRepo.UpdateAsync(balance, ct);
            }
        }
        else if (request.Status != LeaveStatus.Pending)
        {
            throw new DomainException("Only pending or approved leave requests can be cancelled.");
        }

        request.Status = LeaveStatus.Cancelled;
        request.UpdatedAt = DateTime.UtcNow;
        await _leaveRepo.UpdateAsync(request, ct);
    }

    private async Task<decimal> CountWorkingDaysAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        decimal count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (await _holidayService.IsHolidayAsync(d, ct)) continue;
            count++;
        }
        return count;
    }

    private static LeaveRequestDto ToDto(LeaveRequest r) => new(
        r.Id, r.EmployeeId, r.Employee?.FullName ?? string.Empty,
        r.LeaveTypeId, r.LeaveType?.Name ?? string.Empty,
        r.StartDate, r.EndDate, r.TotalDays, r.Reason,
        r.Status, r.ApprovedBy, r.ApprovedAt, r.RejectionReason, r.CreatedAt);
}
```

**Step 7: Run tests — verify pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "LeaveRequestServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 8: Implement LeaveAccrualHostedService**

```csharp
// src/PeopleCore.Infrastructure/Jobs/LeaveAccrualHostedService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Jobs;

public class LeaveAccrualHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LeaveAccrualHostedService> _logger;

    public LeaveAccrualHostedService(IServiceProvider services, ILogger<LeaveAccrualHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run on startup and then check every hour
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            // Run accrual on the 1st of each month
            if (now.Day == 1 && now.Hour == 0)
            {
                await RunAccrualAsync(stoppingToken);
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunAccrualAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running monthly leave accrual for {Month}/{Year}",
            DateTime.UtcNow.Month, DateTime.UtcNow.Year);

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var leaveTypes = await context.LeaveTypes.Where(lt => lt.IsActive).ToListAsync(ct);
        var employees = await context.Employees.Where(e => e.IsActive).ToListAsync(ct);
        var year = DateTime.UtcNow.Year;
        var monthlyAccrual = 1m / 12m; // simplified: credit 1/12 of annual entitlement per month

        foreach (var employee in employees)
        {
            foreach (var leaveType in leaveTypes)
            {
                var balance = await context.LeaveBalances
                    .FirstOrDefaultAsync(b => b.EmployeeId == employee.Id &&
                                              b.LeaveTypeId == leaveType.Id &&
                                              b.Year == year, ct);
                if (balance is null)
                {
                    balance = new Domain.Entities.Leave.LeaveBalance
                    {
                        EmployeeId = employee.Id,
                        LeaveTypeId = leaveType.Id,
                        Year = year,
                        TotalDays = leaveType.MaxDaysPerYear * monthlyAccrual,
                        UsedDays = 0
                    };
                    await context.LeaveBalances.AddAsync(balance, ct);
                }
                else
                {
                    balance.TotalDays += leaveType.MaxDaysPerYear * monthlyAccrual;
                    // Cap at max
                    if (balance.TotalDays > leaveType.MaxDaysPerYear)
                        balance.TotalDays = leaveType.MaxDaysPerYear;
                    balance.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("Leave accrual complete.");
    }
}
```

**Step 9: Register hosted service in ServiceExtensions**

```csharp
// Add to AddInfrastructure:
services.AddHostedService<LeaveAccrualHostedService>();
```

**Step 10: Build and run all tests**

```bash
dotnet test PeopleCore.sln
```
Expected: All tests pass.

**Step 11: Commit**

```bash
git add -A
git commit -m "feat: implement leave module with request workflow, balance, and monthly accrual"
```

---

### Task 17: Leave — Controllers

**Files:**
- Create: `src/PeopleCore.API/Controllers/Leave/LeaveController.cs`

```csharp
// src/PeopleCore.API/Controllers/Leave/LeaveController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

[ApiController]
[Route("api")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveTypeService _typeService;
    private readonly ILeaveRequestService _requestService;
    private readonly ILeaveBalanceService _balanceService;

    public LeaveController(
        ILeaveTypeService typeService,
        ILeaveRequestService requestService,
        ILeaveBalanceService balanceService)
    {
        _typeService = typeService;
        _requestService = requestService;
        _balanceService = balanceService;
    }

    // Leave Types
    [HttpGet("leave-types")]
    public async Task<IActionResult> GetLeaveTypes(CancellationToken ct)
        => Ok(await _typeService.GetAllAsync(ct));

    [HttpPost("leave-types")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateLeaveType([FromBody] CreateLeaveTypeDto dto, CancellationToken ct)
        => Ok(await _typeService.CreateAsync(dto, ct));

    // Leave Requests
    [HttpGet("leave-requests")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _requestService.GetAllAsync(employeeId, status, page, pageSize, ct));

    [HttpPost("leave-requests")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto, CancellationToken ct)
        => Ok(await _requestService.CreateAsync(dto, ct));

    [HttpPut("leave-requests/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveLeaveDto dto, CancellationToken ct)
        => Ok(await _requestService.ApproveAsync(id, dto, ct));

    [HttpPut("leave-requests/{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeaveDto dto, CancellationToken ct)
        => Ok(await _requestService.RejectAsync(id, dto, ct));

    [HttpPut("leave-requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] Guid employeeId, CancellationToken ct)
    {
        await _requestService.CancelAsync(id, employeeId, ct);
        return NoContent();
    }

    // Leave Balances
    [HttpGet("leave-balances/{employeeId:guid}")]
    public async Task<IActionResult> GetBalances(Guid employeeId, [FromQuery] int? year, CancellationToken ct)
        => Ok(await _balanceService.GetByEmployeeAsync(employeeId, year, ct));
}
```

**Step 2: Commit**

```bash
git add -A
git commit -m "feat: add leave controllers"
```

---

**Phase 5 complete.** Continue with [Phase 6 — Recruitment](2026-03-10-hrms-phase-6-recruitment.md).
