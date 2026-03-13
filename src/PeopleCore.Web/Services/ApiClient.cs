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

    // Companies
    public async Task<IReadOnlyList<CompanyDto>?> GetCompaniesAsync()
        => await _http.GetFromJsonAsync<IReadOnlyList<CompanyDto>>("api/companies", JsonOptions);

    // Departments
    public async Task<PagedResult<DepartmentDto>?> GetDepartmentsAsync(int page = 1, int pageSize = 50)
        => await _http.GetFromJsonAsync<PagedResult<DepartmentDto>>($"api/departments?page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<DepartmentDto?> CreateDepartmentAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/departments", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DepartmentDto>(JsonOptions);
    }

    public async Task DeleteDepartmentAsync(Guid id)
        => (await _http.DeleteAsync($"api/departments/{id}")).EnsureSuccessStatusCode();

    // Positions
    public async Task<PagedResult<PositionDto>?> GetPositionsAsync(Guid? departmentId = null, int page = 1, int pageSize = 50)
    {
        var url = $"api/positions?page={page}&pageSize={pageSize}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await _http.GetFromJsonAsync<PagedResult<PositionDto>>(url, JsonOptions);
    }

    public async Task<PositionDto?> CreatePositionAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/positions", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PositionDto>(JsonOptions);
    }

    // Job Postings
    public async Task<PagedResult<JobPostingDto>?> GetJobPostingsAsync(string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/job-postings?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await _http.GetFromJsonAsync<PagedResult<JobPostingDto>>(url, JsonOptions);
    }

    public async Task<JobPostingDto?> CreateJobPostingAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/job-postings", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobPostingDto>(JsonOptions);
    }

    public async Task<JobPostingDto?> PublishJobPostingAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/job-postings/{id}/publish", new { });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobPostingDto>(JsonOptions);
    }

    public async Task<JobPostingDto?> CloseJobPostingAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/job-postings/{id}/close", new { });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobPostingDto>(JsonOptions);
    }

    // Applicants
    public async Task<PagedResult<ApplicantDto>?> GetApplicantsAsync(Guid? jobPostingId = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/applicants?page={page}&pageSize={pageSize}";
        if (jobPostingId.HasValue) url += $"&jobPostingId={jobPostingId}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await _http.GetFromJsonAsync<PagedResult<ApplicantDto>>(url, JsonOptions);
    }

    // Overtime
    public async Task<PagedResult<OvertimeRequestDto>?> GetOvertimeRequestsAsync(Guid? employeeId = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/overtime-requests?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) url += $"&employeeId={employeeId}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await _http.GetFromJsonAsync<PagedResult<OvertimeRequestDto>>(url, JsonOptions);
    }

    public async Task ApproveOvertimeAsync(Guid id, Guid approverId)
    {
        var response = await _http.PutAsJsonAsync($"api/overtime-requests/{id}/approve", new { approverId });
        response.EnsureSuccessStatusCode();
    }

    public async Task RejectOvertimeAsync(Guid id, string reason)
    {
        var response = await _http.PutAsJsonAsync($"api/overtime-requests/{id}/reject", new { rejectionReason = reason });
        response.EnsureSuccessStatusCode();
    }

    // Performance
    public async Task<PagedResult<ReviewCycleDto>?> GetReviewCyclesAsync(int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<ReviewCycleDto>>($"api/review-cycles?page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<PagedResult<PerformanceReviewDto>?> GetPerformanceReviewsAsync(Guid? employeeId = null, Guid? cycleId = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/performance-reviews?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) url += $"&employeeId={employeeId}";
        if (cycleId.HasValue) url += $"&cycleId={cycleId}";
        return await _http.GetFromJsonAsync<PagedResult<PerformanceReviewDto>>(url, JsonOptions);
    }

    public async Task<ReviewCycleDto?> CreateReviewCycleAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/review-cycles", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ReviewCycleDto>(JsonOptions);
    }
}

// Client-side DTO copies
public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles);
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);
public record EmployeeListDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, string FullName, string WorkEmail, string? DepartmentName, string? PositionTitle, string EmploymentStatus, bool IsActive);
public record LeaveBalanceDto(Guid Id, string LeaveTypeName, int Year, decimal TotalDays, decimal UsedDays, decimal RemainingDays);
public record LeaveRequestDto(Guid Id, Guid EmployeeId, string LeaveTypeName, string StartDate, string EndDate, decimal TotalDays, string Status, string? Reason);
public record AttendanceRecordDto(Guid Id, string AttendanceDate, string? TimeIn, string? TimeOut, int LateMinutes, int UndertimeMinutes, bool IsPresent);
public record CompanyDto(Guid Id, string Name);
public record DepartmentDto(Guid Id, Guid CompanyId, Guid? ParentDepartmentId, string? ParentDepartmentName, string Name, string? Code, int SubDepartmentCount);
public record PositionDto(Guid Id, Guid DepartmentId, string DepartmentName, string Title, string? Level);
public record JobPostingDto(Guid Id, string Title, Guid? DepartmentId, string? DepartmentName, Guid? PositionId, string? PositionTitle, string? Description, string? Requirements, int Vacancies, string Status, DateTime? PostedAt, DateTime? ClosedAt);
public record ApplicantDto(Guid Id, Guid JobPostingId, string JobPostingTitle, string FirstName, string LastName, string Email, string? Phone, string Status, Guid? ConvertedEmployeeId, DateTime AppliedAt);
public record OvertimeRequestDto(Guid Id, Guid EmployeeId, string EmployeeName, string OvertimeDate, string StartTime, string EndTime, int TotalMinutes, string Reason, string Status, string? RejectionReason);
public record ReviewCycleDto(Guid Id, string Name, int Year, int? Quarter, string StartDate, string EndDate, string Status);
public record PerformanceReviewDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid ReviewCycleId, string ReviewCycleName, decimal? FinalScore, string Status);
