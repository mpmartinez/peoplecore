using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class LeaveBalanceRepository : Repository<LeaveBalance>, ILeaveBalanceRepository
{
    public LeaveBalanceRepository(AppDbContext context) : base(context) { }

    public async Task<LeaveBalance?> GetByEmployeeAndTypeAsync(
        Guid employeeId, Guid leaveTypeId, int year, CancellationToken ct = default)
        => await Context.LeaveBalances
            .Include(b => b.Employee)
            .Include(b => b.LeaveType)
            .FirstOrDefaultAsync(b => b.EmployeeId == employeeId &&
                                       b.LeaveTypeId == leaveTypeId &&
                                       b.Year == year, ct);

    public async Task<IReadOnlyList<LeaveBalance>> GetByEmployeeAsync(
        Guid employeeId, int? year = null, CancellationToken ct = default)
    {
        var query = Context.LeaveBalances
            .Include(b => b.Employee)
            .Include(b => b.LeaveType)
            .Where(b => b.EmployeeId == employeeId);
        if (year.HasValue)
            query = query.Where(b => b.Year == year.Value);
        return await query.OrderBy(b => b.Year).ToListAsync(ct);
    }

    // HRAnalyticsService.GetLeaveUtilizationAsync filters this list by
    // `b.Employee?.DepartmentId == departmentId.Value` - without this Include that navigation was
    // always null, so a department filter silently matched nothing, for every department.
    public async Task<IReadOnlyList<LeaveBalance>> GetByYearAsync(
        int year, CancellationToken ct = default)
        => await Context.LeaveBalances
            .Include(b => b.Employee)
            .Include(b => b.LeaveType)
            .Where(b => b.Year == year)
            .ToListAsync(ct);
}
