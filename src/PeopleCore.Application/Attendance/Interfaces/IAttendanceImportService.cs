using PeopleCore.Application.Attendance.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

/// <summary>
/// Imports a time clock's punches. Each punch's <see cref="AttendancePunchDto.EmployeeNumber"/> is the
/// person's number as the file wrote it: a biometric id or an employee number.
/// </summary>
public interface IAttendanceImportService
{
    /// <summary>What <see cref="ImportAsync"/> would do with these punches, without writing anything.</summary>
    Task<AttendanceImportPreviewDto> PreviewAsync(string layout, IReadOnlyList<AttendancePunchDto> punches, IReadOnlyList<string> fileErrors, CancellationToken ct = default);

    /// <summary>
    /// Saves the punches of every person matched to an employee; the rest are reported, not saved. A day
    /// already recorded is left alone unless <see cref="AttendanceImportOptions.ReplaceExisting"/>.
    /// </summary>
    Task<AttendanceImportResultDto> ImportAsync(IReadOnlyList<AttendancePunchDto> punches, IReadOnlyList<string> fileErrors, AttendanceImportOptions? options = null, CancellationToken ct = default);

    /// <summary>Links an employee to the number they are enrolled under on the time clock, or unlinks them with null.</summary>
    Task<AttendanceImportEmployeeDto> SetBiometricIdAsync(Guid employeeId, string? biometricId, CancellationToken ct = default);
}

/// <summary>
/// How an import treats days already recorded. With <see cref="ReplaceExisting"/> each is replaced by
/// the file's times as a correction made by <see cref="Actor"/>, citing <see cref="FileName"/>.
/// </summary>
public record AttendanceImportOptions(bool ReplaceExisting, string FileName, string Actor);
