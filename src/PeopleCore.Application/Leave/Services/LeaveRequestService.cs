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
    private readonly ILeaveTypeRepository _leaveTypeRepo;

    public LeaveRequestService(
        ILeaveRequestRepository leaveRepo,
        ILeaveBalanceRepository balanceRepo,
        IHolidayService holidayService,
        IEmployeeRepository employeeRepo,
        ILeaveTypeRepository leaveTypeRepo)
    {
        _leaveRepo = leaveRepo;
        _balanceRepo = balanceRepo;
        _holidayService = holidayService;
        _employeeRepo = employeeRepo;
        _leaveTypeRepo = leaveTypeRepo;
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

        var leaveType = await _leaveTypeRepo.GetByIdAsync(dto.LeaveTypeId, ct)
            ?? throw new KeyNotFoundException($"Leave type {dto.LeaveTypeId} not found.");

        // Gender restriction check — always runs, even when no balance yet
        if (leaveType.GenderRestriction is not null && leaveType.GenderRestriction != employee.Gender)
            throw new DomainException($"Employee is not eligible for this leave type.");

        var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(
            dto.EmployeeId, dto.LeaveTypeId, dto.StartDate.Year, ct);

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
            if (await _holidayService.IsHolidayAsync(d, ct) is not null) continue;
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
