using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class MaternityClaimRepository : Repository<MaternityClaim>, IMaternityClaimRepository
{
    public MaternityClaimRepository(AppDbContext context) : base(context) { }

    /// <summary>The claim with its employee, leave request and advance run loaded.</summary>
    public override async Task<MaternityClaim?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Context.MaternityClaims
            .Include(c => c.Employee)
            .Include(c => c.LeaveRequest)
            .Include(c => c.AdvanceRun)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<MaternityClaim?> GetByLeaveRequestAsync(Guid leaveRequestId, CancellationToken ct = default)
        => await Context.MaternityClaims.FirstOrDefaultAsync(c => c.LeaveRequestId == leaveRequestId, ct);

    public async Task<IReadOnlyList<MaternityClaim>> GetForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await Context.MaternityClaims
            .Where(c => c.EmployeeId == employeeId)
            .Include(c => c.LeaveRequest)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    /// <summary>Every claim, newest first, with its employee, leave request and advance run loaded.</summary>
    public override async Task<IReadOnlyList<MaternityClaim>> GetAllAsync(CancellationToken ct = default)
        => await Context.MaternityClaims
            .Include(c => c.Employee)
            .Include(c => c.LeaveRequest)
            .Include(c => c.AdvanceRun)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeaveRequest>> GetUnclaimedApprovedRequestsAsync(CancellationToken ct = default)
        => await Context.LeaveRequests
            .Where(r => r.Status == LeaveStatus.Approved && r.LeaveType.IsMaternity
                        && !Context.MaternityClaims.Any(c => c.LeaveRequestId == r.Id))
            .Include(r => r.Employee)
            .OrderBy(r => r.StartDate)
            .ToListAsync(ct);
}
