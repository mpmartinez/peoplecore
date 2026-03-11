using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class OvertimeRepository : Repository<OvertimeRequest>, IOvertimeRepository
{
    public OvertimeRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<OvertimeRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.OvertimeRequests.Include(r => r.Employee).AsQueryable();
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OvertimeStatus>(status, true, out var statusEnum))
            query = query.Where(r => r.Status == statusEnum);
        query = query.OrderByDescending(r => r.OvertimeDate);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<OvertimeRequest>> GetApprovedByPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
        => await Context.OvertimeRequests
            .Include(r => r.Employee)
            .Where(r => r.Status == OvertimeStatus.Approved && r.OvertimeDate >= from && r.OvertimeDate <= to)
            .ToListAsync(ct);
}
