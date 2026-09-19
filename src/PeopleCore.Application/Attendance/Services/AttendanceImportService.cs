using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
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

    public AttendanceImportService(IEmployeeRepository employees, IAttendanceService attendance)
    {
        _employees = employees;
        _attendance = attendance;
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
        IReadOnlyList<AttendancePunchDto> punches, IReadOnlyList<string> fileErrors, CancellationToken ct = default)
    {
        var match = Matcher(await _employees.GetAttendanceImportKeysAsync(ct));

        var resolved = new List<AttendancePunchDto>();
        var unmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var punch in punches)
        {
            if (match(punch.EmployeeNumber) is { } employee)
                resolved.Add(punch with { EmployeeNumber = employee.EmployeeNumber });
            else
                unmatched[punch.EmployeeNumber] = unmatched.GetValueOrDefault(punch.EmployeeNumber) + 1;
        }

        var synced = await _attendance.SyncPunchesAsync(resolved, ct);
        var unmatchedErrors = unmatched.OrderBy(u => u.Key, StringComparer.Ordinal)
            .Select(u => $"'{u.Key}' matches no employee, so its {u.Value} punch{(u.Value == 1 ? "" : "es")} were not imported.");

        return new AttendanceImportResultDto(
            synced.Imported,
            synced.Skipped + unmatched.Values.Sum() + fileErrors.Count,
            [.. fileErrors, .. unmatchedErrors, .. synced.Errors]);
    }

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
