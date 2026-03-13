using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Performance.Services;

public class ReviewCycleService : IReviewCycleService
{
    private readonly IReviewCycleRepository _cycleRepo;

    public ReviewCycleService(IReviewCycleRepository cycleRepo)
    {
        _cycleRepo = cycleRepo;
    }

    public async Task<PagedResult<ReviewCycleDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _cycleRepo.GetPagedAsync(page, pageSize, ct);
        return PagedResult<ReviewCycleDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<ReviewCycleDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cycle = await _cycleRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Review cycle {id} not found.");
        return ToDto(cycle);
    }

    public async Task<ReviewCycleDto> CreateAsync(CreateReviewCycleDto dto, CancellationToken ct = default)
    {
        var cycle = new ReviewCycle
        {
            Name = dto.Name,
            Year = dto.Year,
            Quarter = dto.Quarter,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Status = ReviewStatus.Draft
        };

        var created = await _cycleRepo.AddAsync(cycle, ct);
        return ToDto(created);
    }

    public async Task<ReviewCycleDto> CloseAsync(Guid id, CancellationToken ct = default)
    {
        var cycle = await _cycleRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Review cycle {id} not found.");

        if (cycle.Status != ReviewStatus.Draft && cycle.Status != ReviewStatus.Submitted)
            throw new DomainException("Only Draft or Active review cycles can be closed.");

        cycle.Status = ReviewStatus.Completed;

        await _cycleRepo.UpdateAsync(cycle, ct);
        return ToDto(cycle);
    }

    private static ReviewCycleDto ToDto(ReviewCycle c) => new(
        c.Id, c.Name, c.Year, c.Quarter, c.StartDate, c.EndDate, c.Status);
}
