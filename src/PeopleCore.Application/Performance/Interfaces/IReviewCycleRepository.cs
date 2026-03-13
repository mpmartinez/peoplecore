using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IReviewCycleRepository : IRepository<ReviewCycle>
{
    Task<(IReadOnlyList<ReviewCycle> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default);
}
