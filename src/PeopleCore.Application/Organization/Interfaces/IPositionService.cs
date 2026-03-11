using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;

namespace PeopleCore.Application.Organization.Interfaces;

public interface IPositionService
{
    Task<PagedResult<PositionDto>> GetAllAsync(int page, int pageSize, Guid? departmentId = null, CancellationToken ct = default);
    Task<PositionDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PositionDto> CreateAsync(CreatePositionDto dto, CancellationToken ct = default);
    Task<PositionDto> UpdateAsync(Guid id, UpdatePositionDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
