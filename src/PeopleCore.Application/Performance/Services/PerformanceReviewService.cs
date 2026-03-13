using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Performance.Services;

public class PerformanceReviewService : IPerformanceReviewService
{
    private readonly IPerformanceReviewRepository _reviewRepo;
    private readonly IEmployeeRepository _employeeRepo;

    public PerformanceReviewService(
        IPerformanceReviewRepository reviewRepo,
        IEmployeeRepository employeeRepo)
    {
        _reviewRepo = reviewRepo;
        _employeeRepo = employeeRepo;
    }

    public async Task<PagedResult<PerformanceReviewDto>> GetAllAsync(
        Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _reviewRepo.GetPagedAsync(employeeId, cycleId, page, pageSize, ct);
        return PagedResult<PerformanceReviewDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<PerformanceReviewDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var review = await _reviewRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Performance review {id} not found.");
        return ToDto(review);
    }

    public async Task<PerformanceReviewDto> CreateAsync(CreatePerformanceReviewDto dto, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(dto.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {dto.EmployeeId} not found.");

        var reviewer = await _employeeRepo.GetByIdAsync(dto.ReviewerId, ct)
            ?? throw new KeyNotFoundException($"Reviewer employee {dto.ReviewerId} not found.");

        var review = new PerformanceReview
        {
            EmployeeId = dto.EmployeeId,
            Employee = employee,
            ReviewCycleId = dto.ReviewCycleId,
            ReviewerId = dto.ReviewerId,
            Reviewer = reviewer,
            Status = ReviewStatus.Draft,
            KpiItems = dto.KpiItems.Select(k => new KpiItem
            {
                Description = k.Description,
                Target = k.Target,
                Weight = k.Weight
            }).ToList()
        };

        var created = await _reviewRepo.AddAsync(review, ct);
        return ToDto(created);
    }

    public async Task<PerformanceReviewDto> SubmitSelfEvaluationAsync(
        Guid id, Guid employeeId, SubmitSelfEvaluationDto dto, CancellationToken ct = default)
    {
        var review = await _reviewRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Performance review {id} not found.");

        if (review.EmployeeId != employeeId)
            throw new DomainException("You can only submit your own self-evaluation.");

        if (review.Status != ReviewStatus.Draft)
            throw new DomainException("Self-evaluation can only be submitted when the review is in Draft status.");

        review.SelfEvaluationScore = dto.Score;
        review.SelfEvaluationComments = dto.Comments;
        review.Status = ReviewStatus.Submitted;
        review.SubmittedAt = DateTime.UtcNow;

        foreach (var kpiUpdate in dto.KpiItems)
        {
            var kpi = review.KpiItems.FirstOrDefault(k => k.Id == kpiUpdate.Id);
            if (kpi is not null)
            {
                kpi.Actual = kpiUpdate.Actual;
                kpi.Score = kpiUpdate.Score;
            }
        }

        await _reviewRepo.UpdateAsync(review, ct);
        return ToDto(review);
    }

    public async Task<PerformanceReviewDto> SubmitManagerReviewAsync(
        Guid id, Guid reviewerId, SubmitManagerReviewDto dto, CancellationToken ct = default)
    {
        var review = await _reviewRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Performance review {id} not found.");

        if (review.ReviewerId != reviewerId)
            throw new DomainException("Only the assigned reviewer can submit a manager review.");

        if (review.Status != ReviewStatus.Submitted)
            throw new DomainException("Manager review requires a completed self-evaluation to be submitted first.");

        review.ManagerScore = dto.Score;
        review.ManagerComments = dto.Comments;
        review.Status = ReviewStatus.Completed;
        review.CompletedAt = DateTime.UtcNow;
        review.FinalScore = (review.SelfEvaluationScore + dto.Score) / 2;

        foreach (var kpiUpdate in dto.KpiItems)
        {
            var kpi = review.KpiItems.FirstOrDefault(k => k.Id == kpiUpdate.Id);
            if (kpi is not null)
            {
                kpi.Actual = kpiUpdate.Actual;
                kpi.Score = kpiUpdate.Score;
            }
        }

        await _reviewRepo.UpdateAsync(review, ct);
        return ToDto(review);
    }

    private static PerformanceReviewDto ToDto(PerformanceReview r) => new(
        r.Id,
        r.EmployeeId,
        r.Employee?.FullName ?? string.Empty,
        r.ReviewCycleId,
        r.ReviewCycle?.Name ?? string.Empty,
        r.ReviewerId,
        r.Reviewer?.FullName ?? string.Empty,
        r.SelfEvaluationScore,
        r.ManagerScore,
        r.FinalScore,
        r.SelfEvaluationComments,
        r.ManagerComments,
        r.Status,
        r.SubmittedAt,
        r.CompletedAt,
        r.KpiItems.Select(k => new KpiItemDto(k.Id, k.Description, k.Target, k.Actual, k.Weight, k.Score)).ToList());
}
