using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

public class OvertimeServiceTests
{
    private readonly Mock<IOvertimeRepository> _repo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly OvertimeService _sut;

    public OvertimeServiceTests()
    {
        _sut = new OvertimeService(_repo.Object, _employeeRepo.Object);
    }

    [Fact]
    public async Task CreateAsync_WhenEndBeforeStart_ThrowsDomainException()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = employeeId,
            EmployeeNumber = "EMP-001", FirstName = "Juan", LastName = "Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1), Gender = "Male",
            WorkEmail = "j@test.com", EmploymentType = "FT",
            HireDate = new DateOnly(2020, 1, 1), IsActive = true
        };
        _employeeRepo.Setup(r => r.GetByIdAsync(employeeId, default)).ReturnsAsync(employee);

        var dto = new CreateOvertimeRequestDto(
            employeeId,
            new DateOnly(2025, 1, 6),
            new DateTime(2025, 1, 6, 18, 0, 0),
            new DateTime(2025, 1, 6, 17, 0, 0),  // end before start
            "Reason");

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*after start time*");
    }

    [Fact]
    public async Task ApproveAsync_WhenNotManager_ThrowsDomainException()
    {
        var managerId = Guid.NewGuid();
        var wrongApproverId = Guid.NewGuid();
        var request = new OvertimeRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Status = OvertimeStatus.Pending
        };
        var employee = new Employee
        {
            Id = request.EmployeeId,
            ReportingManagerId = managerId,
            EmployeeNumber = "EMP-001", FirstName = "Juan", LastName = "Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1), Gender = "Male",
            WorkEmail = "j@test.com", EmploymentType = "FT",
            HireDate = new DateOnly(2020, 1, 1), IsActive = true
        };

        _repo.Setup(r => r.GetByIdAsync(request.Id, default)).ReturnsAsync(request);
        _employeeRepo.Setup(r => r.GetByIdAsync(request.EmployeeId, default)).ReturnsAsync(employee);

        var act = () => _sut.ApproveAsync(request.Id, new ApproveOvertimeDto(wrongApproverId));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*reporting manager*");
    }

    [Fact]
    public async Task RejectAsync_WhenAlreadyApproved_ThrowsDomainException()
    {
        var request = new OvertimeRequest
        {
            Id = Guid.NewGuid(),
            Status = OvertimeStatus.Approved
        };
        _repo.Setup(r => r.GetByIdAsync(request.Id, default)).ReturnsAsync(request);

        var act = () => _sut.RejectAsync(request.Id, new RejectOvertimeDto("reason"));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*pending*");
    }
}
