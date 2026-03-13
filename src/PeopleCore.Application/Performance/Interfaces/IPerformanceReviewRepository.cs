using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IPerformanceReviewRepository : IRepository<PerformanceReview>
{
    Task<(IReadOnlyList<PerformanceReview> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default);
}
