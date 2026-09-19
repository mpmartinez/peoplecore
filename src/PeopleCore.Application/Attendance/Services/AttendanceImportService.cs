using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Attendance.Services;

/// <summary>
/// Turns the people in an attendance file into employees. A time clock knows people by the number
/// they were enrolled under, which is rarely the employee number, so each number is matched against
/// the employee's <c>BiometricId</c> first, then their employee number. A numeric device id also
/// matches with its leading zeros dropped (<c>00123</c> is <c>123</c>), since device software pads
/// them inconsistently.
/// </summary>
public class AttendanceImportService : IAttendanceImportService
{
    private readonly IEmployeeRepository _employees;
    private readonly IAttendanceService _attendance;
    private readonly IAttendanceRepository _records;
    private readonly IAttendanceCorrectionService _corrections;

    public AttendanceImportService(
        IEmployeeRepository employees, IAttendanceService attendance,
        IAttendanceRepository records, IAttendanceCorrectionService corrections)
    {
        _employees = employees;
        _attendance = attendance;
        _records = records;
        _corrections = corrections;
    }

    public async Task<AttendanceImportPreviewDto> PreviewAsync(
        string layout, IReadOnlyList<AttendancePunchDto> punches, IReadOnlyList<string> fileErrors, CancellationToken ct = default)
    {
        var employees = await _employees.GetAttendanceImportKeysAsync(ct);
        var match = Matcher(employees);

        var matched = punches.Select(p => match(p.EmployeeNumber)).OfType<AttendanceImportEmployeeDto>().Select(e => e.Id).Distinct().Count();
        var unmatched = punches
            .Where(p => match(p.EmployeeNumber) is null)
            .GroupBy(p => p.EmployeeNumber, StringComparer.Ordinal)
            .Select(g => new UnmatchedDeviceIdDto(g.Key, g.Count()))
            .OrderBy(u => u.DeviceId, StringComparer.Ordinal)
            .ToList();
        var days = punches.Select(p => DateOnly.FromDateTime(p.PunchTime)).ToList();

        return new AttendanceImportPreviewDto(
            layout, punches.Count, matched,
            days.Count == 0 ? null : days.Min(), days.Count == 0 ? null : days.Max(),
            unmatched, fileErrors, employees);
    }

    public async Task<AttendanceImportResultDto> ImportAsync(
        IReadOnlyList<AttendancePunchDto> punches, IReadOnlyList<string> fileErrors,
        AttendanceImportOptions? options = null, CancellationToken ct = default)
    {
        var match = Matcher(await _employees.GetAttendanceImportKeysAsync(ct));

        var resolved = new List<(AttendanceImportEmployeeDto Employee, AttendancePunchDto Punch)>();
        var unmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var punch in punches)
        {
            if (match(punch.EmployeeNumber) is { } employee)
                resolved.Add((employee, punch with { EmployeeNumber = employee.EmployeeNumber }));
            else
                unmatched[punch.EmployeeNumber] = unmatched.GetValueOrDefault(punch.EmployeeNumber) + 1;
        }

        int replaced = 0, notReplaced = 0;
        var replaceErrors = new List<string>();
        var toSync = new List<AttendancePunchDto>();

        if (options is { ReplaceExisting: true })
        {
            // A day already recorded is replaced by the file's first and last punch for it, through a
            // correction - so it is logged, and refused on a day already paid.
            foreach (var day in resolved.GroupBy(r => (r.Employee.Id, Day: DateOnly.FromDateTime(r.Punch.PunchTime))))
            {
                var employee = day.First().Employee;
                var times = day.Select(r => TimeOnly.FromDateTime(r.Punch.PunchTime)).ToList();
                var (timeIn, last) = (times.Min(), times.Max());
                TimeOnly? timeOut = last > timeIn ? last : null;

                var existing = await _records.GetByEmployeeAndDateAsync(employee.Id, day.Key.Day, ct);
                if (existing is null)
                {
                    toSync.AddRange(day.Select(r => r.Punch));
                    continue;
                }
                if (Same(existing.TimeIn, timeIn) && Same(existing.TimeOut, timeOut))
                {
                    notReplaced += day.Count(); // already exactly this
                    continue;
                }

                try
                {
                    await _corrections.CorrectAsync(
                        new CorrectAttendanceDto(employee.Id, day.Key.Day, timeIn, timeOut, $"Replaced by importing {options.FileName}"),
                        options.Actor, AttendanceCorrectionSource.Import, ct);
                    replaced += day.Count();
                }
                catch (DomainException ex)
                {
                    replaceErrors.Add($"{employee.EmployeeNumber} on {day.Key.Day:MMM d, yyyy}: {ex.Message}");
                    notReplaced += day.Count();
                }
            }
        }
        else
        {
            toSync.AddRange(resolved.Select(r => r.Punch));
        }

        var synced = await _attendance.SyncPunchesAsync(toSync, ct);
        var unmatchedErrors = unmatched.OrderBy(u => u.Key, StringComparer.Ordinal)
            .Select(u => $"'{u.Key}' matches no employee, so its {u.Value} punch{(u.Value == 1 ? "" : "es")} were not imported.");

        return new AttendanceImportResultDto(
            synced.Imported + replaced,
            synced.Skipped + notReplaced + unmatched.Values.Sum() + fileErrors.Count,
            [.. fileErrors, .. unmatchedErrors, .. replaceErrors, .. synced.Errors]);
    }

    private static bool Same(DateTime? recorded, TimeOnly? time) =>
        recorded is null ? time is null : time is { } t && TimeOnly.FromDateTime(recorded.Value) == t;

    public async Task<AttendanceImportEmployeeDto> SetBiometricIdAsync(Guid employeeId, string? biometricId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var value = string.IsNullOrWhiteSpace(biometricId) ? null : biometricId.Trim();
        if (value is { Length: > 50 })
            throw new DomainException("A biometric ID can be at most 50 characters.");

        if (value is not null && await _employees.GetByBiometricIdAsync(value, ct) is { } holder && holder.Id != employeeId)
            throw new DomainException($"Biometric ID '{value}' already belongs to {holder.FirstName} {holder.LastName} ({holder.EmployeeNumber}).");

        employee.BiometricId = value;
        employee.UpdatedAt = DateTime.UtcNow;
        await _employees.UpdateAsync(employee, ct);

        return new AttendanceImportEmployeeDto(employee.Id, employee.EmployeeNumber, $"{employee.FirstName} {employee.LastName}", value, employee.IsActive);
    }

    /// <summary>Biometric id first, then employee number, then a numeric id without its leading zeros.</summary>
    private static Func<string, AttendanceImportEmployeeDto?> Matcher(IReadOnlyList<AttendanceImportEmployeeDto> employees)
    {
        var byBiometric = employees.Where(e => e.BiometricId is not null)
            .GroupBy(e => e.BiometricId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byNumber = employees
            .GroupBy(e => e.EmployeeNumber, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byUnpadded = employees.Where(e => e.BiometricId is { } b && b.All(char.IsAsciiDigit))
            .GroupBy(e => Unpad(e.BiometricId!), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return key =>
        {
            var trimmed = key.Trim();
            if (byBiometric.TryGetValue(trimmed, out var e)) return e;
            if (byNumber.TryGetValue(trimmed, out e)) return e;
            if (trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit) && byUnpadded.TryGetValue(Unpad(trimmed), out e)) return e;
            return null;
        };
    }

    private static string Unpad(string digits) => digits.TrimStart('0') is { Length: > 0 } s ? s : "0";
}
