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

        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");
        if (employee.ReportingManagerId != dto.ApproverId)
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
