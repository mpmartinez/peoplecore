using System.Net.Http.Json;
using System.Text.Json;

namespace PeopleCore.Web.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http) => _http = http;

    // Auth
    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    // Employees
    public async Task<PagedResult<EmployeeListDto>?> GetEmployeesAsync(int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<EmployeeListDto>>($"api/employees?page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<EmployeeListDto?> GetEmployeeAsync(Guid id)
        => await _http.GetFromJsonAsync<EmployeeListDto>($"api/employees/{id}", JsonOptions);

    // Leave
    public async Task<IReadOnlyList<LeaveBalanceDto>?> GetLeaveBalancesAsync(Guid employeeId)
        => await _http.GetFromJsonAsync<IReadOnlyList<LeaveBalanceDto>>($"api/leave-balances/{employeeId}", JsonOptions);

    public async Task<PagedResult<LeaveRequestDto>?> GetLeaveRequestsAsync(Guid? employeeId = null, int page = 1, int pageSize = 20)
    {
        var query = $"api/leave-requests?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) query += $"&employeeId={employeeId}";
        return await _http.GetFromJsonAsync<PagedResult<LeaveRequestDto>>(query, JsonOptions);
    }

    public async Task<LeaveRequestDto?> CreateLeaveRequestAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/leave-requests", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }

    // Attendance
    public async Task<PagedResult<AttendanceRecordDto>?> GetAttendanceAsync(Guid? employeeId = null, int page = 1, int pageSize = 20)
    {
        var query = $"api/attendance?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) query += $"&employeeId={employeeId}";
        return await _http.GetFromJsonAsync<PagedResult<AttendanceRecordDto>>(query, JsonOptions);
    }

    public async Task<AttendanceRecordDto?> TimeInAsync(Guid employeeId)
    {
        var response = await _http.PostAsJsonAsync("api/attendance/time-in", new { employeeId, timeIn = DateTime.UtcNow });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>(JsonOptions);
    }

    public async Task<AttendanceRecordDto?> TimeOutAsync(Guid employeeId)
    {
        var response = await _http.PostAsJsonAsync("api/attendance/time-out", new { employeeId, timeOut = DateTime.UtcNow });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>(JsonOptions);
    }

    // Leave approvals (HR)
    public async Task<LeaveRequestDto?> ApproveLeaveAsync(Guid requestId)
    {
        var response = await _http.PutAsJsonAsync($"api/leave-requests/{requestId}/approve", new { });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }

    public async Task<LeaveRequestDto?> RejectLeaveAsync(Guid requestId, string reason)
    {
        var response = await _http.PutAsJsonAsync($"api/leave-requests/{requestId}/reject", new { reason });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }
}

// Client-side DTO copies
public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles);
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);
public record EmployeeListDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, string FullName, string WorkEmail, string? DepartmentName, string? PositionTitle, string EmploymentStatus, bool IsActive);
public record LeaveBalanceDto(Guid Id, string LeaveTypeName, int Year, decimal TotalDays, decimal UsedDays, decimal RemainingDays);
public record LeaveRequestDto(Guid Id, Guid EmployeeId, string LeaveTypeName, string StartDate, string EndDate, decimal TotalDays, string Status, string? Reason);
public record AttendanceRecordDto(Guid Id, string AttendanceDate, string? TimeIn, string? TimeOut, int LateMinutes, int UndertimeMinutes, bool IsPresent);
