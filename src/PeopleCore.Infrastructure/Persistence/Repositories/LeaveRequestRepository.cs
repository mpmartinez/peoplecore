using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class LeaveRequestRepository : Repository<LeaveRequest>, ILeaveRequestRepository
{
    public LeaveRequestRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<LeaveRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.LeaveRequests
            .Include(r => r.Employee)
            .Include(r => r.LeaveType)
            .AsQueryable();
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<LeaveStatus>(status, true, out var statusEnum))
            query = query.Where(r => r.Status == statusEnum);
        query = query.OrderByDescending(r => r.StartDate);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<bool> HasOverlapAsync(
        Guid employeeId, DateOnly startDate, DateOnly endDate,
        Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = Context.LeaveRequests
            .Where(r => r.EmployeeId == employeeId &&
                        r.Status != LeaveStatus.Rejected &&
                        r.Status != LeaveStatus.Cancelled &&
                        r.StartDate <= endDate && r.EndDate >= startDate);
        if (excludeId.HasValue) query = query.Where(r => r.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveRequest>> GetApprovedByPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
        => await Context.LeaveRequests
            .Include(r => r.Employee)
            .Include(r => r.LeaveType)
            .Where(r => r.Status == LeaveStatus.Approved && r.StartDate >= from && r.EndDate <= to)
            .ToListAsync(ct);
}
