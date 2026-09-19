using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class AttendanceCorrectionRepository : Repository<AttendanceCorrection>, IAttendanceCorrectionRepository
{
    public AttendanceCorrectionRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? reportingManagerId, AttendanceCorrectionStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.AttendanceCorrections.Include(c => c.Employee).AsQueryable();
        if (employeeId.HasValue) query = query.Where(c => c.EmployeeId == employeeId.Value);
        if (reportingManagerId.HasValue) query = query.Where(c => c.Employee.ReportingManagerId == reportingManagerId.Value);
        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        query = query.OrderByDescending(c => c.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<AttendanceCorrection>> GetForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
        => await Context.AttendanceCorrections
            .Where(c => c.EmployeeId == employeeId && c.AttendanceDate == date)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> HasPendingForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
        => await Context.AttendanceCorrections.AnyAsync(
            c => c.EmployeeId == employeeId && c.AttendanceDate == date && c.Status == AttendanceCorrectionStatus.Pending, ct);

    public async Task<AttendanceCorrection?> GetWithEmployeeAsync(Guid id, CancellationToken ct = default)
        => await Context.AttendanceCorrections.Include(c => c.Employee).FirstOrDefaultAsync(c => c.Id == id, ct);
}
