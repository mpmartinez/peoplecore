using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Performance.DTOs;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IReviewCycleService
{
    Task<PagedResult<ReviewCycleDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ReviewCycleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ReviewCycleDto> CreateAsync(CreateReviewCycleDto dto, CancellationToken ct = default);
    Task<ReviewCycleDto> CloseAsync(Guid id, CancellationToken ct = default);
}
