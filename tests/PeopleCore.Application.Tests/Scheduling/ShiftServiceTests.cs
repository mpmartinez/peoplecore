using FluentAssertions;
using Moq;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Scheduling;

public class ShiftServiceTests
{
    private readonly Mock<IShiftTemplateRepository> _shiftTemplateRepo = new();
    private readonly Mock<IRotatingPatternRepository> _rotatingPatternRepo = new();
    private readonly Mock<IShiftAssignmentRepository> _assignmentRepo = new();
    private readonly ShiftService _sut;

    public ShiftServiceTests()
    {
        _sut = new ShiftService(_shiftTemplateRepo.Object, _rotatingPatternRepo.Object, _assignmentRepo.Object);
    }

    [Fact]
    public async Task ResolveShiftForDay_FixedShift_ReturnsShiftDetails()
    {
        // Arrange
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 11); // Wednesday - a fixed shift's default work week is Mon-Fri
        var shiftTemplate = new ShiftTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Morning",
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(15, 0),
            IsNightShift = false
        };
        var assignment = new EmployeeShiftAssignment
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ShiftTemplateId = shiftTemplate.Id,
            ShiftTemplate = shiftTemplate,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        _assignmentRepo
            .Setup(r => r.GetActiveAssignmentAsync(employeeId, date, default))
            .ReturnsAsync(assignment);

        // Act
        var result = await _sut.ResolveShiftForDayAsync(employeeId, date);

        // Assert
        result.Should().NotBeNull();
        result!.ShiftName.Should().Be("Morning");
        result.StartTime.Should().Be(new TimeOnly(6, 0));
        result.IsRestDay.Should().BeFalse();
        result.IsNightShift.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveShiftForDay_RotatingPattern_RestDay_ReturnsIsRestDayTrue()
    {
        // Arrange
        var employeeId = Guid.NewGuid();
        var patternStartDate = new DateOnly(2026, 3, 9); // Monday
        var date = new DateOnly(2026, 3, 15);            // Sunday — day offset 6 in a 7-day cycle

        var rotatingPattern = new RotatingPattern
        {
            Id = Guid.NewGuid(),
            Name = "Standard Week",
            CycleLengthDays = 7,
            Slots =
            [
                new RotatingPatternSlot
                {
                    Id = Guid.NewGuid(),
                    DayOffset = 6,
                    ShiftTemplateId = null,
                    ShiftTemplate = null
                }
            ]
        };

        var assignment = new EmployeeShiftAssignment
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            RotatingPatternId = rotatingPattern.Id,
            RotatingPattern = rotatingPattern,
            PatternStartDate = patternStartDate,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        _assignmentRepo
            .Setup(r => r.GetActiveAssignmentAsync(employeeId, date, default))
            .ReturnsAsync(assignment);

        // Act
        var result = await _sut.ResolveShiftForDayAsync(employeeId, date);

        // Assert
        result.Should().NotBeNull();
        result!.IsRestDay.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveShiftForDay_NoAssignment_ReturnsNull()
    {
        // Arrange
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 14);

        _assignmentRepo
            .Setup(r => r.GetActiveAssignmentAsync(employeeId, date, default))
            .ReturnsAsync((EmployeeShiftAssignment?)null);

        // Act
        var result = await _sut.ResolveShiftForDayAsync(employeeId, date);

        // Assert
        result.Should().BeNull();
    }
}
