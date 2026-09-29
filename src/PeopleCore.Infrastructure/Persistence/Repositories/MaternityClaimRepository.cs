using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class MaternityClaimRepository : Repository<MaternityClaim>, IMaternityClaimRepository
{
    public MaternityClaimRepository(AppDbContext context) : base(context) { }

    public async Task<MaternityClaim?> GetByLeaveRequestAsync(Guid leaveRequestId, CancellationToken ct = default)
        => await Context.MaternityClaims.FirstOrDefaultAsync(c => c.LeaveRequestId == leaveRequestId, ct);

    public async Task<IReadOnlyList<MaternityClaim>> GetForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await Context.MaternityClaims
            .Where(c => c.EmployeeId == employeeId)
            .Include(c => c.LeaveRequest)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    /// <summary>Every claim, newest first, with its employee and leave request loaded.</summary>
    public override async Task<IReadOnlyList<MaternityClaim>> GetAllAsync(CancellationToken ct = default)
        => await Context.MaternityClaims
            .Include(c => c.Employee)
            .Include(c => c.LeaveRequest)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
}
