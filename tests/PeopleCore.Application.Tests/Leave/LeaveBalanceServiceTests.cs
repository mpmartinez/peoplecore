using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>A balance row says whether its type is confidential, read from the loaded type.</summary>
public class LeaveBalanceServiceTests
{
    private readonly Mock<ILeaveBalanceRepository> _repo = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetByEmployeeAsync_CarriesTheTypesConfidentialFlag(bool isConfidential)
    {
        var employeeId = Guid.NewGuid();
        var type = new LeaveType { Name = "VAWC Leave", Code = "VAWC", IsConfidential = isConfidential };
        _repo.Setup(r => r.GetByEmployeeAsync(employeeId, 2026, It.IsAny<CancellationToken>()))
             .ReturnsAsync([new LeaveBalance { EmployeeId = employeeId, LeaveTypeId = type.Id, LeaveType = type, Year = 2026, TotalDays = 10 }]);

        var rows = await new LeaveBalanceService(_repo.Object).GetByEmployeeAsync(employeeId, 2026);

        rows.Should().ContainSingle().Which.IsConfidential.Should().Be(isConfidential);
    }
}
