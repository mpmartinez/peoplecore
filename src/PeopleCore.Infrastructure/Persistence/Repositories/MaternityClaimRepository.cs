using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class MaternityClaimRepository : Repository<MaternityClaim>, IMaternityClaimRepository
{
    /// <summary>Postgres SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    public MaternityClaimRepository(AppDbContext context) : base(context) { }

    public async Task<MaternityClaim> AddNewAsync(MaternityClaim claim, CancellationToken ct = default)
    {
        Context.MaternityClaims.Add(claim);
        try
        {
            await Context.SaveChangesAsync(ct);
            return claim;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Detached so a later save on this context doesn't try the insert again.
            Context.Entry(claim).State = EntityState.Detached;
            var employee = claim.Employee ?? await Context.Employees.FindAsync([claim.EmployeeId], ct);
            throw new DomainException($"{employee?.FullName ?? "This employee"} already has a maternity claim for this leave.");
        }
    }

    public async Task SaveRelinkAsync(MaternityClaim claim, CancellationToken ct = default)
    {
        try
        {
            await UpdateAsync(claim, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Detached so a later save on this context doesn't try the move again.
            Context.Entry(claim).State = EntityState.Detached;
            throw new DomainException("Choose an approved maternity leave of the same employee that has no claim.");
        }
    }

    /// <summary>The claim with its employee, leave request and advance run loaded.</summary>
    public override async Task<MaternityClaim?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Context.MaternityClaims
            .Include(c => c.Employee)
            .Include(c => c.LeaveRequest)
            .Include(c => c.AdvanceRun)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<MaternityClaim?> GetByLeaveRequestAsync(Guid leaveRequestId, CancellationToken ct = default)
        => await Context.MaternityClaims.FirstOrDefaultAsync(c => c.LeaveRequestId == leaveRequestId, ct);

    public async Task<IReadOnlyList<MaternityClaim>> GetForEmployeesAsync(
        IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await Context.MaternityClaims
            .Where(c => ids.Contains(c.EmployeeId))
            .Include(c => c.Employee)
            .Include(c => c.LeaveRequest)
            .Include(c => c.AdvanceRun)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

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
