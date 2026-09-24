using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Domain.Entities.Employees;
using M2NET.Core.Enums;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class EmployeeServiceTests
{
    private readonly Mock<IEmployeeRepository> _repo = new();
    private readonly Mock<ISeparationService> _separationService = new();
    private readonly EmployeeService _sut;

    public EmployeeServiceTests()
    {
        _sut = new EmployeeService(_repo.Object, _separationService.Object);
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
    public async Task GetByIdAsync_CarriesTheSeparationDate()
    {
        // The record-separation form lists employees who already left without a separation, and
        // pre-fills their last working day with this date.
        var employee = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "EMP-001", FirstName = "Juan", LastName = "dela Cruz",
            WorkEmail = "juan@company.com", IsActive = false, SeparationDate = new DateOnly(2026, 3, 3)
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var dto = await _sut.GetByIdAsync(employee.Id);

        dto.SeparationDate.Should().Be(new DateOnly(2026, 3, 3));
    }

    [Fact]
    public async Task CreateAsync_WhenEmployeeNumberAlreadyExists_ThrowsDomainException()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-001", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var dto = new CreateEmployeeDto("EMP-001", "Juan", null, "dela Cruz",
            new DateOnly(1990, 1, 1), Gender.Male, "juan@company.com", null,
            null, null, null, EmploymentStatus.Probationary, EmploymentType.Regular,
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
            Gender = Gender.Male,
            WorkEmail = "juan@company.com",
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = EmploymentType.Regular,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = false
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var act = () => _sut.DeactivateAsync(employee.Id, new DateOnly(2025, 1, 1));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already inactive*");
    }

    [Fact]
    public async Task DeactivateAsync_CallsSeparateNowAsync_WithTheIdDateTypeAndCause()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP-001",
            FirstName = "Juan",
            LastName = "dela Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            WorkEmail = "juan@company.com",
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = EmploymentType.Regular,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var separationDate = new DateOnly(2026, 9, 30);

        await _sut.DeactivateAsync(employee.Id, separationDate, SeparationType.AuthorizedCause, AuthorizedCause.Redundancy);

        _separationService.Verify(s => s.SeparateNowAsync(
            employee.Id, separationDate, SeparationType.AuthorizedCause, AuthorizedCause.Redundancy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsEmployeeDto()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-001", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var dto = new CreateEmployeeDto("EMP-001", "Juan", null, "dela Cruz",
            new DateOnly(1990, 1, 1), Gender.Male, "juan@company.com", null,
            null, null, null, EmploymentStatus.Probationary, EmploymentType.Regular,
            new DateOnly(2024, 1, 1));

        _repo.Setup(r => r.AddAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Employee e, CancellationToken _) => e);

        var result = await _sut.CreateAsync(dto);

        result.EmployeeNumber.Should().Be("EMP-001");
        result.FirstName.Should().Be("Juan");
        result.EmploymentStatus.Should().Be(EmploymentStatus.Probationary);
        result.IsActive.Should().BeTrue();
    }

    // ── Solo parent ID ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SavesAndReturnsTheSoloParentId_Trimmed()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-002", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Employee? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()))
             .Callback((Employee e, CancellationToken _) => saved = e)
             .ReturnsAsync((Employee e, CancellationToken _) => e);
        var dto = new CreateEmployeeDto("EMP-002", "Maria", null, "Santos",
            new DateOnly(1990, 1, 1), Gender.Female, "maria@company.com", null,
            null, null, null, EmploymentStatus.Regular, EmploymentType.Regular,
            new DateOnly(2024, 1, 1),
            SoloParentIdNumber: "  SP-12345  ", SoloParentIdValidUntil: new DateOnly(2027, 6, 30));

        var result = await _sut.CreateAsync(dto);

        saved!.SoloParentIdNumber.Should().Be("SP-12345");
        saved.SoloParentIdValidUntil.Should().Be(new DateOnly(2027, 6, 30));
        result.SoloParentIdNumber.Should().Be("SP-12345");
        result.SoloParentIdValidUntil.Should().Be(new DateOnly(2027, 6, 30));
    }

    [Fact]
    public async Task CreateAsync_BlankSoloParentId_IsStoredAsNull()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-003", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.AddAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Employee e, CancellationToken _) => e);
        var dto = new CreateEmployeeDto("EMP-003", "Maria", null, "Santos",
            new DateOnly(1990, 1, 1), Gender.Female, "maria@company.com", null,
            null, null, null, EmploymentStatus.Regular, EmploymentType.Regular,
            new DateOnly(2024, 1, 1), SoloParentIdNumber: "   ");

        var result = await _sut.CreateAsync(dto);

        result.SoloParentIdNumber.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_SavesAndReturnsTheSoloParentId()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "EMP-004", FirstName = "Maria", LastName = "Santos",
            WorkEmail = "maria@company.com", IsActive = true
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var result = await _sut.UpdateAsync(employee.Id, new UpdateEmployeeDto(
            "Maria", null, "Santos", null, null, null, null, null, null, null, null,
            EmploymentStatus.Regular, null, true,
            SoloParentIdNumber: " SP-777 ", SoloParentIdValidUntil: new DateOnly(2027, 1, 31)));

        employee.SoloParentIdNumber.Should().Be("SP-777");
        employee.SoloParentIdValidUntil.Should().Be(new DateOnly(2027, 1, 31));
        result.SoloParentIdNumber.Should().Be("SP-777");
        result.SoloParentIdValidUntil.Should().Be(new DateOnly(2027, 1, 31));
        _repo.Verify(r => r.UpdateAsync(employee, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_SoloParentIdLongerThanTheColumn_IsRefusedBeforeSaving()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "EMP-006", FirstName = "Maria", LastName = "Santos",
            WorkEmail = "maria@company.com", IsActive = true
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var act = () => _sut.UpdateAsync(employee.Id, new UpdateEmployeeDto(
            "Maria", null, "Santos", null, null, null, null, null, null, null, null,
            EmploymentStatus.Regular, null, true, SoloParentIdNumber: new string('9', 51)));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("The solo parent ID number can't be longer than 50 characters.");
        _repo.Verify(r => r.UpdateAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_EmptySoloParentId_ClearsIt()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "EMP-005", FirstName = "Maria", LastName = "Santos",
            WorkEmail = "maria@company.com", IsActive = true,
            SoloParentIdNumber = "SP-1", SoloParentIdValidUntil = new DateOnly(2027, 1, 1)
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var result = await _sut.UpdateAsync(employee.Id, new UpdateEmployeeDto(
            "Maria", null, "Santos", null, null, null, null, null, null, null, null,
            EmploymentStatus.Regular, null, true, SoloParentIdNumber: "", SoloParentIdValidUntil: null));

        employee.SoloParentIdNumber.Should().BeNull();
        employee.SoloParentIdValidUntil.Should().BeNull();
        result.SoloParentIdNumber.Should().BeNull();
    }
}
