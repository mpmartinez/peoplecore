using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Maternity;

public interface IMaternityClaimRepository : IRepository<MaternityClaim>
{
    /// <summary>The claim for a leave request, or null when it has none.</summary>
    Task<MaternityClaim?> GetByLeaveRequestAsync(Guid leaveRequestId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new claim. Two creates for the same leave request at once both pass the service's
    /// "already has a claim" check, so the unique index on <see cref="MaternityClaim.LeaveRequestId"/>
    /// is the real guard: the loser gets the check's own <see cref="Domain.Exceptions.DomainException"/>
    /// ("{name} already has a maternity claim for this leave."), not a constraint-violation 500.
    /// </summary>
    Task<MaternityClaim> AddNewAsync(MaternityClaim claim, CancellationToken ct = default);

    /// <summary>All of an employee's claims, newest first, each with its <see cref="MaternityClaim.LeaveRequest"/> loaded.</summary>
    Task<IReadOnlyList<MaternityClaim>> GetForEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// Approved requests of a maternity leave type (<see cref="LeaveType.IsMaternity"/>) that have
    /// no claim yet, earliest start first, each with its <see cref="LeaveRequest.Employee"/> loaded.
    /// </summary>
    Task<IReadOnlyList<LeaveRequest>> GetUnclaimedApprovedRequestsAsync(CancellationToken ct = default);
}
