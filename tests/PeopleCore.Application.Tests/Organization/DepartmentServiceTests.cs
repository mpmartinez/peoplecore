using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Organization.Services;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Organization;

public class DepartmentServiceTests
{
    private readonly Mock<IDepartmentRepository> _repo = new();
    private readonly DepartmentService _sut;

    public DepartmentServiceTests()
    {
        _sut = new DepartmentService(_repo.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenDepartmentNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Department?)null);

        var act = () => _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsDepartmentDto()
    {
        var dto = new CreateDepartmentDto(Guid.NewGuid(), null, "Engineering", "ENG");
        var created = new Department { Id = Guid.NewGuid(), CompanyId = dto.CompanyId, Name = dto.Name, Code = dto.Code };

        _repo.Setup(r => r.AddAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(created);

        var result = await _sut.CreateAsync(dto);

        result.Should().NotBeNull();
        result.Name.Should().Be("Engineering");
        result.Code.Should().Be("ENG");
    }

    [Fact]
    public async Task DeleteAsync_WhenDepartmentHasSubDepartments_ThrowsDomainException()
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Parent",
            CompanyId = Guid.NewGuid(),
            SubDepartments = [new Department { Name = "Child", CompanyId = Guid.NewGuid() }]
        };
        _repo.Setup(r => r.GetByIdAsync(dept.Id, It.IsAny<CancellationToken>())).ReturnsAsync(dept);

        var act = () => _sut.DeleteAsync(dept.Id);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*sub-departments*");
    }

    [Fact]
    public async Task DeleteAsync_WhenDepartmentHasNoSubDepartments_CallsRepositoryDelete()
    {
        var dept = new Department { Id = Guid.NewGuid(), Name = "Leaf", CompanyId = Guid.NewGuid(), SubDepartments = [] };
        _repo.Setup(r => r.GetByIdAsync(dept.Id, It.IsAny<CancellationToken>())).ReturnsAsync(dept);

        await _sut.DeleteAsync(dept.Id);

        _repo.Verify(r => r.DeleteAsync(dept, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenDepartmentNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Department?)null);

        var act = () => _sut.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }
}
