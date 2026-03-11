using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class EmployeeServiceTests
{
    private readonly Mock<IEmployeeRepository> _repo = new();
    private readonly EmployeeService _sut;

    public EmployeeServiceTests()
    {
        _sut = new EmployeeService(_repo.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenEmployeeNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Employee?)null);

        var act = () => _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WhenEmployeeNumberAlreadyExists_ThrowsDomainException()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-001", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var dto = new CreateEmployeeDto("EMP-001", "Juan", null, "dela Cruz",
            new DateOnly(1990, 1, 1), "Male", "juan@company.com", null,
            null, null, null, EmploymentStatus.Probationary, "FullTime",
            new DateOnly(2024, 1, 1));

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already exists*");
    }

    [Fact]
    public async Task DeactivateAsync_WhenEmployeeAlreadyInactive_ThrowsDomainException()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP-001",
            FirstName = "Juan",
            LastName = "dela Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "Male",
            WorkEmail = "juan@company.com",
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = "FullTime",
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = false
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var act = () => _sut.DeactivateAsync(employee.Id, new DateOnly(2025, 1, 1));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already inactive*");
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsEmployeeDto()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-001", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var dto = new CreateEmployeeDto("EMP-001", "Juan", null, "dela Cruz",
            new DateOnly(1990, 1, 1), "Male", "juan@company.com", null,
            null, null, null, EmploymentStatus.Probationary, "FullTime",
            new DateOnly(2024, 1, 1));

        _repo.Setup(r => r.AddAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Employee e, CancellationToken _) => e);

        var result = await _sut.CreateAsync(dto);

        result.EmployeeNumber.Should().Be("EMP-001");
        result.FirstName.Should().Be("Juan");
        result.EmploymentStatus.Should().Be(EmploymentStatus.Probationary);
        result.IsActive.Should().BeTrue();
    }
}
