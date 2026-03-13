using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Performance.DTOs;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IPerformanceReviewService
{
    Task<PagedResult<PerformanceReviewDto>> GetAllAsync(Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default);
    Task<PerformanceReviewDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PerformanceReviewDto> CreateAsync(CreatePerformanceReviewDto dto, CancellationToken ct = default);
    Task<PerformanceReviewDto> SubmitSelfEvaluationAsync(Guid id, Guid employeeId, SubmitSelfEvaluationDto dto, CancellationToken ct = default);
    Task<PerformanceReviewDto> SubmitManagerReviewAsync(Guid id, Guid reviewerId, SubmitManagerReviewDto dto, CancellationToken ct = default);
}
