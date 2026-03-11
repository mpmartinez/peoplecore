using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class AttendanceRepository : Repository<AttendanceRecord>, IAttendanceRepository
{
    public AttendanceRepository(AppDbContext context) : base(context) { }

    public async Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
        => await Context.AttendanceRecords
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.AttendanceDate == date, ct);

    public async Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.AttendanceRecords.Include(r => r.Employee).AsQueryable();
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (from.HasValue) query = query.Where(r => r.AttendanceDate >= from.Value);
        if (to.HasValue) query = query.Where(r => r.AttendanceDate <= to.Value);
        query = query.OrderByDescending(r => r.AttendanceDate);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByEmployeeAndPeriodAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await Context.AttendanceRecords
            .Include(r => r.Employee)
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate >= from && r.AttendanceDate <= to)
            .ToListAsync(ct);
}
