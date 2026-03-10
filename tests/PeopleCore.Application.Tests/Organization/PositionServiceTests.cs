using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Organization.Services;
using PeopleCore.Domain.Entities.Organization;
using Xunit;

namespace PeopleCore.Application.Tests.Organization;

public class PositionServiceTests
{
    private readonly Mock<IPositionRepository> _repo = new();
    private readonly PositionService _sut;

    public PositionServiceTests()
    {
        _sut = new PositionService(_repo.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Position?)null);

        var act = () => _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsPositionDto()
    {
        var deptId = Guid.NewGuid();
        var dto = new CreatePositionDto(deptId, "Software Engineer", "L3");
        var dept = new Department { Id = deptId, Name = "Engineering", CompanyId = Guid.NewGuid() };
        var created = new Position { Id = Guid.NewGuid(), DepartmentId = deptId, Department = dept, Title = dto.Title, Level = dto.Level };

        _repo.Setup(r => r.AddAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(created);

        var result = await _sut.CreateAsync(dto);

        result.Should().NotBeNull();
        result.Title.Should().Be("Software Engineer");
        result.Level.Should().Be("L3");
    }

    [Fact]
    public async Task UpdateAsync_WhenPositionNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Position?)null);

        var act = () => _sut.UpdateAsync(Guid.NewGuid(), new UpdatePositionDto("Title", null));

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task DeleteAsync_WhenPositionNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Position?)null);

        var act = () => _sut.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }
}
