using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

/// <summary>
/// A time clock knows people by the number they were enrolled under. The import matches it to the
/// employee's biometric id first, then their employee number, and never saves a punch for nobody.
/// </summary>
public class AttendanceImportServiceTests
{
    private static readonly Guid Juan = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IAttendanceService> _attendance = new();
    private readonly AttendanceImportService _sut;
    private List<AttendancePunchDto> _synced = [];

    public AttendanceImportServiceTests()
    {
        _employees.Setup(r => r.GetAttendanceImportKeysAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AttendanceImportEmployeeDto(Juan, "EMP-001", "Juan Cruz", "1023", true),
            new AttendanceImportEmployeeDto(Ana, "EMP-002", "Ana Reyes", null, true),
        ]);
        _attendance.Setup(a => a.SyncPunchesAsync(It.IsAny<IReadOnlyList<AttendancePunchDto>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<AttendancePunchDto> p, CancellationToken _) => _synced = [.. p])
            .ReturnsAsync((IReadOnlyList<AttendancePunchDto> p, CancellationToken _) => new AttendanceImportResultDto(p.Count, 0, []));
        _sut = new AttendanceImportService(_employees.Object, _attendance.Object);
    }

    private static AttendancePunchDto Punch(string who, int hour) => new(who, new DateTime(2026, 3, 10, hour, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData("1023")]     // the biometric id
    [InlineData("001023")]   // the same, padded by the device software
    [InlineData("EMP-001")]  // the employee number
    public async Task A_punch_is_saved_under_the_employee_its_number_points_at(string deviceId)
    {
        var result = await _sut.ImportAsync([Punch(deviceId, 8)], []);

        result.Imported.Should().Be(1);
        _synced.Should().ContainSingle().Which.EmployeeNumber.Should().Be("EMP-001");
    }

    [Fact]
    public async Task Punches_for_a_number_nobody_holds_are_reported_and_never_saved()
    {
        var result = await _sut.ImportAsync([Punch("77", 8), Punch("77", 17), Punch("1023", 8)], ["Row 9: bad"]);

        _synced.Should().ContainSingle().Which.EmployeeNumber.Should().Be("EMP-001");
        result.Skipped.Should().Be(3); // two punches for nobody, one bad row
        result.Errors.Should().Contain("Row 9: bad")
            .And.Contain(e => e.Contains("'77'") && e.Contains("2 punches"));
    }

    [Fact]
    public async Task The_preview_writes_nothing_and_lists_who_is_unmatched()
    {
        var preview = await _sut.PreviewAsync("Time clock scans",
            [Punch("1023", 8), Punch("1023", 17), Punch("EMP-002", 8), Punch("77", 8)], []);

        _attendance.Verify(a => a.SyncPunchesAsync(It.IsAny<IReadOnlyList<AttendancePunchDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        preview.MatchedPeople.Should().Be(2);
        preview.Punches.Should().Be(4);
        preview.Unmatched.Should().Equal(new UnmatchedDeviceIdDto("77", 1));
        preview.From.Should().Be(new DateOnly(2026, 3, 10));
        preview.Employees.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_biometric_id_already_held_by_someone_else_is_refused()
    {
        var holder = new Employee { Id = Juan, FirstName = "Juan", LastName = "Cruz", EmployeeNumber = "EMP-001" };
        _employees.Setup(r => r.GetByIdAsync(Ana, It.IsAny<CancellationToken>())).ReturnsAsync(new Employee { Id = Ana });
        _employees.Setup(r => r.GetByBiometricIdAsync("1023", It.IsAny<CancellationToken>())).ReturnsAsync(holder);

        var act = () => _sut.SetBiometricIdAsync(Ana, " 1023 ");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("Juan Cruz");
        _employees.Verify(r => r.UpdateAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Setting_a_biometric_id_trims_it_and_a_blank_one_unlinks()
    {
        var ana = new Employee { Id = Ana, FirstName = "Ana", LastName = "Reyes", EmployeeNumber = "EMP-002" };
        _employees.Setup(r => r.GetByIdAsync(Ana, It.IsAny<CancellationToken>())).ReturnsAsync(ana);

        (await _sut.SetBiometricIdAsync(Ana, " 88 ")).BiometricId.Should().Be("88");
        ana.BiometricId.Should().Be("88");

        await _sut.SetBiometricIdAsync(Ana, "  ");
        ana.BiometricId.Should().BeNull();
    }
}
