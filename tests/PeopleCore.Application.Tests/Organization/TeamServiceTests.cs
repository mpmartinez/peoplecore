using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Organization.Services;
using PeopleCore.Domain.Entities.Organization;
using Xunit;

namespace PeopleCore.Application.Tests.Organization;

public class TeamServiceTests
{
    private readonly Mock<ITeamRepository> _repo = new();
    private readonly TeamService _sut;

    public TeamServiceTests()
    {
        _sut = new TeamService(_repo.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((Team?)null);

        var act = () => _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsTeamDto()
    {
        var deptId = Guid.NewGuid();
        var dto = new CreateTeamDto(deptId, "Platform Team");
        var dept = new Department { Id = deptId, Name = "Engineering", CompanyId = Guid.NewGuid() };
        var created = new Team { Id = Guid.NewGuid(), DepartmentId = deptId, Department = dept, Name = dto.Name };

        _repo.Setup(r => r.AddAsync(It.IsAny<Team>(), default))
             .ReturnsAsync(created);

        var result = await _sut.CreateAsync(dto);

        result.Should().NotBeNull();
        result.Name.Should().Be("Platform Team");
    }
}
