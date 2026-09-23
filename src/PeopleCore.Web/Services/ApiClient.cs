using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeopleCore.Web.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    // Built on the Web defaults (camelCase property names, case-insensitive matching) rather than
    // JsonSerializerOptions.Default, because that is what JsonContent.Create/PostAsJsonAsync use
    // internally when no options are given - the plain "new JsonSerializerOptions()" this used to
    // be would have sent PascalCase property names once SendJsonAsync below started passing these
    // options to its request bodies too, not just its response reads. JsonStringEnumConverter
    // matches the API's own Program.cs setup: separation enums travel on the wire as strings
    // ("Resignation", not 0), in both directions.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiClient(HttpClient http) => _http = http;

    // Auth
    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    // The signed-in account's own profile. Not under api/auth: a 401 here is an expired session.
    public async Task<UserProfileDto?> GetMyProfileAsync()
        => await GetJsonAsync<UserProfileDto>("api/profile");

    public async Task<UserProfileDto?> UpdateMyProfileAsync(string firstName, string lastName)
    {
        var response = await _http.PutAsJsonAsync("api/profile", new { firstName, lastName });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonOptions);
    }

    /// <summary>
    /// Changes the signed-in account's own password. A wrong current password or one the password
    /// policy rejects comes back as a 400 whose message says which, ready to show as it is. On
    /// success the API returns a fresh token: changing a password revokes the one the request used.
    /// </summary>
    public async Task<LoginResponse?> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var response = await _http.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    /// <summary>
    /// Whether the login page should offer a password reset. A failure counts as "no": better a
    /// missing link than one that leads nowhere.
    /// </summary>
    public async Task<bool> IsPasswordResetAvailableAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/password-reset-available");
            if (!response.IsSuccessStatusCode) return false;
            return (await response.Content.ReadFromJsonAsync<ResetAvailability>(JsonOptions))?.Available ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Asks for a reset link. The answer is the same whether or not the address has an account.</summary>
    public async Task ForgotPasswordAsync(string email)
        => await EnsureSuccessAsync(await _http.PostAsJsonAsync("api/auth/forgot-password", new { email }));

    /// <summary>Sets a new password from a link. A stale link throws with the API's own sentence.</summary>
    public async Task ResetPasswordAsync(string email, string token, string newPassword)
        => await EnsureSuccessAsync(await _http.PostAsJsonAsync("api/auth/reset-password", new { email, token, newPassword }));

    // Email settings
    public async Task<EmailSettingsDto?> GetEmailSettingsAsync()
    {
        var response = await _http.GetAsync("api/email-settings");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<EmailSettingsDto>(JsonOptions);
    }

    public Task<EmailSettingsDto?> SaveEmailSettingsAsync(SaveEmailSettingsRequest request)
        => SendJsonAsync<EmailSettingsDto>(HttpMethod.Put, "api/email-settings", request);

    /// <summary>
    /// Sends a test message to the given address, or to the signed-in administrator when none is
    /// given, returning where it went.
    /// </summary>
    public async Task<string> SendTestEmailAsync(string? to = null)
    {
        var response = await _http.PostAsJsonAsync("api/email-settings/test", new { to });
        await EnsureSuccessAsync(response);
        var sent = await response.Content.ReadFromJsonAsync<TestEmailResult>(JsonOptions);
        return sent?.To ?? string.Empty;
    }

    // Accounts (Admin and HR)
    public async Task<PagedResult<UserAccountDto>?> GetUserAccountsAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        var url = $"api/users?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await GetJsonAsync<PagedResult<UserAccountDto>>(url);
    }

    /// <summary>Every role an account could be given, saying which the signed-in user may grant and, if not, why.</summary>
    public async Task<IReadOnlyList<AssignableRoleDto>?> GetAssignableRolesAsync()
        => await GetJsonAsync<List<AssignableRoleDto>>("api/users/assignable-roles");

    // Roles (anyone with Manage roles)
    public async Task<IReadOnlyList<RoleDto>?> GetRolesAsync()
        => await GetJsonAsync<List<RoleDto>>("api/roles");

    public async Task<IReadOnlyList<PermissionDto>?> GetPermissionCatalogueAsync()
        => await GetJsonAsync<List<PermissionDto>>("api/roles/permissions");

    public Task<RoleDto?> CreateRoleAsync(SaveRoleRequest request)
        => SendJsonAsync<RoleDto>(HttpMethod.Post, "api/roles", request);

    public Task<RoleDto?> UpdateRoleAsync(string roleId, SaveRoleRequest request)
        => SendJsonAsync<RoleDto>(HttpMethod.Put, $"api/roles/{Uri.EscapeDataString(roleId)}", request);

    // No body comes back from a delete, so there is nothing to read after the status check.
    public async Task DeleteRoleAsync(string roleId)
        => await EnsureSuccessAsync(await _http.DeleteAsync($"api/roles/{Uri.EscapeDataString(roleId)}"));

    public async Task<IReadOnlyList<EmployeeLinkDto>?> GetEmployeeLinksAsync()
        => await GetJsonAsync<List<EmployeeLinkDto>>("api/users/employee-links");

    public Task<CreatedUserAccountDto?> CreateUserAccountAsync(CreateUserAccountRequest request)
        => SendJsonAsync<CreatedUserAccountDto>(HttpMethod.Post, "api/users", request);

    public Task<UserAccountDto?> SetUserRolesAsync(string userId, IReadOnlyList<string> roles)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Put, $"{UserPath(userId)}/roles", new { roles });

    public Task<UserAccountDto?> LinkUserEmployeeAsync(string userId, Guid? employeeId)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Put, $"{UserPath(userId)}/employee", new { employeeId });

    public Task<UserAccountDto?> DeactivateUserAsync(string userId)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Post, $"{UserPath(userId)}/deactivate");

    public Task<UserAccountDto?> ReactivateUserAsync(string userId)
        => SendJsonAsync<UserAccountDto>(HttpMethod.Post, $"{UserPath(userId)}/reactivate");

    public async Task<string?> ResetUserPasswordAsync(string userId)
        => (await SendJsonAsync<TemporaryPasswordDto>(HttpMethod.Post, $"{UserPath(userId)}/reset-password"))?.TemporaryPassword;

    private static string UserPath(string userId) => $"api/users/{Uri.EscapeDataString(userId)}";

    // Employees
    public async Task<PagedResult<EmployeeListDto>?> GetEmployeesAsync(int page = 1, int pageSize = 20, string? search = null, bool? isActive = null)
    {
        var url = $"api/employees?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (isActive.HasValue) url += $"&isActive={(isActive.Value ? "true" : "false")}";
        return await GetJsonAsync<PagedResult<EmployeeListDto>>(url);
    }

    public async Task<EmployeeListDto?> GetEmployeeAsync(Guid id)
        => await GetJsonAsync<EmployeeListDto>($"api/employees/{id}");

    public async Task<EmployeeListDto?> CreateEmployeeAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/employees", dto);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<EmployeeListDto>(JsonOptions);
    }

    /// <summary>A Certificate of Employment for a current or former employee, as a ready-to-save PDF.</summary>
    public async Task<byte[]> GetCoeAsync(Guid employeeId, CoeRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/employees/{employeeId}/coe", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    // Separations
    public async Task<IReadOnlyList<SeparationDto>?> GetSeparationsAsync()
        => await GetJsonAsync<List<SeparationDto>>("api/separations");

    public async Task<SeparationDto?> GetSeparationAsync(Guid id)
        => await GetJsonAsync<SeparationDto>($"api/separations/{id}");

    public Task<SeparationDto?> RecordSeparationAsync(RecordSeparationRequest request)
        => SendJsonAsync<SeparationDto>(HttpMethod.Post, "api/separations", request);

    public Task<SeparationDto?> MarkSeparatedAsync(Guid id)
        => SendJsonAsync<SeparationDto>(HttpMethod.Post, $"api/separations/{id}/mark-separated");

    public async Task CancelSeparationAsync(Guid id)
        => await EnsureSuccessAsync(await _http.PostAsync($"api/separations/{id}/cancel", null));

    public Task<SeparationDto?> AddClearanceItemAsync(Guid id, string name)
        => SendJsonAsync<SeparationDto>(HttpMethod.Post, $"api/separations/{id}/clearance", new AddClearanceItemRequest(name));

    public Task<SeparationDto?> ClearItemAsync(Guid id, Guid itemId, string? note)
        => SendJsonAsync<SeparationDto>(HttpMethod.Post, $"api/separations/{id}/clearance/{itemId}/clear", new ClearItemRequest(note));

    public Task<SeparationDto?> UndoClearItemAsync(Guid id, Guid itemId)
        => SendJsonAsync<SeparationDto>(HttpMethod.Post, $"api/separations/{id}/clearance/{itemId}/undo");

    public Task<SeparationDto?> DeleteClearanceItemAsync(Guid id, Guid itemId)
        => SendJsonAsync<SeparationDto>(HttpMethod.Delete, $"api/separations/{id}/clearance/{itemId}");

    // Final pay
    //
    // A 404 from the GET means the separation has no final-pay run yet - the page offers to create
    // one. Any other failure is thrown like every other read, so it is never mistaken for "no run".
    public async Task<FinalPaySummaryDto?> GetFinalPayAsync(Guid separationId)
    {
        var response = await _http.GetAsync($"api/separations/{separationId}/final-pay");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<FinalPaySummaryDto>(JsonOptions);
    }

    public Task<FinalPaySummaryDto?> CreateFinalPayAsync(Guid separationId, FinalPayRequest request)
        => SendJsonAsync<FinalPaySummaryDto>(HttpMethod.Post, $"api/separations/{separationId}/final-pay", request);

    public Task<FinalPaySummaryDto?> UpdateFinalPayAsync(Guid separationId, FinalPayRequest request)
        => SendJsonAsync<FinalPaySummaryDto>(HttpMethod.Put, $"api/separations/{separationId}/final-pay", request);

    // Leave
    public async Task<IReadOnlyList<LeaveBalanceDto>?> GetLeaveBalancesAsync(Guid employeeId)
        => await GetJsonAsync<IReadOnlyList<LeaveBalanceDto>>($"api/leave-balances/{employeeId}");

    public async Task<PagedResult<LeaveRequestDto>?> GetLeaveRequestsAsync(Guid? employeeId = null, int page = 1, int pageSize = 20, string? status = null)
    {
        var query = $"api/leave-requests?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) query += $"&employeeId={employeeId}";
        if (!string.IsNullOrEmpty(status)) query += $"&status={status}";
        return await GetJsonAsync<PagedResult<LeaveRequestDto>>(query);
    }

    public async Task<LeaveRequestDto?> CreateLeaveRequestAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/leave-requests", dto);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }

    // Attendance import
    /// <summary>What importing this file would do, without saving anything.</summary>
    public Task<AttendanceImportPreviewDto?> PreviewAttendanceImportAsync(byte[] content, string fileName)
        => PostAttendanceFileAsync<AttendanceImportPreviewDto>("api/attendance/import?preview=true", content, fileName);

    /// <summary>With <paramref name="replace"/>, days already recorded take the file's times as logged corrections.</summary>
    public Task<AttendanceImportResultDto?> ImportAttendanceAsync(byte[] content, string fileName, bool replace = false)
        => PostAttendanceFileAsync<AttendanceImportResultDto>($"api/attendance/import{(replace ? "?replace=true" : "")}", content, fileName);

    /// <summary>PeopleCore's import layout with example rows; <paramref name="format"/> is "csv" or "xlsx".</summary>
    public async Task<byte[]> GetAttendanceTemplateAsync(string format)
    {
        var response = await _http.GetAsync($"api/attendance/import/template?format={format}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Links an employee to their time clock number; null unlinks them.</summary>
    public Task<AttendanceImportEmployeeDto?> SetBiometricIdAsync(Guid employeeId, string? biometricId)
        => SendJsonAsync<AttendanceImportEmployeeDto>(HttpMethod.Put, $"api/attendance/biometric-ids/{employeeId}", new { biometricId });

    private async Task<T?> PostAttendanceFileAsync<T>(string url, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        var response = await _http.PostAsync(url, form);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    // Attendance
    public async Task<PagedResult<AttendanceRecordDto>?> GetAttendanceAsync(
        Guid? employeeId = null, int page = 1, int pageSize = 20, DateOnly? from = null, DateOnly? to = null)
    {
        var query = $"api/attendance?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) query += $"&employeeId={employeeId}";
        if (from.HasValue) query += $"&from={from:yyyy-MM-dd}";
        if (to.HasValue) query += $"&to={to:yyyy-MM-dd}";
        return await GetJsonAsync<PagedResult<AttendanceRecordDto>>(query);
    }

    // Attendance corrections
    /// <summary>HR sets a day's times directly; it is applied at once and kept in the day's history.</summary>
    public Task<AttendanceCorrectionDto?> CorrectAttendanceAsync(CorrectAttendanceRequest request)
        => SendJsonAsync<AttendanceCorrectionDto>(HttpMethod.Post, "api/attendance-corrections", request);

    /// <summary>The signed-in employee asks for one of their own days to be corrected.</summary>
    public Task<AttendanceCorrectionDto?> RequestAttendanceCorrectionAsync(AttendanceCorrectionRequest request)
        => SendJsonAsync<AttendanceCorrectionDto>(HttpMethod.Post, "api/attendance-corrections/requests", request);

    public async Task<PagedResult<AttendanceCorrectionDto>?> GetAttendanceCorrectionsAsync(
        Guid? employeeId = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var query = $"api/attendance-corrections?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) query += $"&employeeId={employeeId}";
        if (!string.IsNullOrEmpty(status)) query += $"&status={status}";
        return await GetJsonAsync<PagedResult<AttendanceCorrectionDto>>(query);
    }

    public async Task<IReadOnlyList<AttendanceCorrectionDto>?> GetAttendanceCorrectionHistoryAsync(Guid employeeId, DateOnly date)
        => await GetJsonAsync<List<AttendanceCorrectionDto>>($"api/attendance-corrections/history?employeeId={employeeId}&date={date:yyyy-MM-dd}");

    public Task<AttendanceCorrectionDto?> ApproveAttendanceCorrectionAsync(Guid id)
        => SendJsonAsync<AttendanceCorrectionDto>(HttpMethod.Put, $"api/attendance-corrections/{id}/approve");

    public Task<AttendanceCorrectionDto?> RejectAttendanceCorrectionAsync(Guid id, string reason)
        => SendJsonAsync<AttendanceCorrectionDto>(HttpMethod.Put, $"api/attendance-corrections/{id}/reject", new { reason });

    /// <summary>Sends no time: the API stamps the punch with its own clock, in Philippine time.</summary>
    public async Task<AttendanceRecordDto?> TimeInAsync(Guid employeeId)
    {
        var response = await _http.PostAsJsonAsync("api/attendance/time-in", new { employeeId });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>(JsonOptions);
    }

    /// <summary>As <see cref="TimeInAsync"/>.</summary>
    public async Task<AttendanceRecordDto?> TimeOutAsync(Guid employeeId)
    {
        var response = await _http.PostAsJsonAsync("api/attendance/time-out", new { employeeId });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>(JsonOptions);
    }

    // Leave approvals (HR)
    /// <summary>The API records the signed-in account's employee as the approver; the body names nobody.</summary>
    public async Task<LeaveRequestDto?> ApproveLeaveAsync(Guid requestId)
    {
        var response = await _http.PutAsJsonAsync($"api/leave-requests/{requestId}/approve", new { });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }

    public async Task<LeaveRequestDto?> RejectLeaveAsync(Guid requestId, string reason)
    {
        var response = await _http.PutAsJsonAsync($"api/leave-requests/{requestId}/reject", new { rejectionReason = reason });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }

    // Companies
    public async Task<IReadOnlyList<CompanyDto>?> GetCompaniesAsync()
        => await GetJsonAsync<IReadOnlyList<CompanyDto>>("api/companies");

    public Task<CompanyProfileDto?> GetCompanyProfileAsync()
        => GetJsonAsync<CompanyProfileDto>("api/company-profile");

    public Task<CompanyProfileDto?> SaveCompanyProfileAsync(CompanyProfileDto profile)
        => SendJsonAsync<CompanyProfileDto>(HttpMethod.Put, "api/company-profile", profile);

    // Departments
    public async Task<PagedResult<DepartmentDto>?> GetDepartmentsAsync(int page = 1, int pageSize = 50)
        => await GetJsonAsync<PagedResult<DepartmentDto>>($"api/departments?page={page}&pageSize={pageSize}");

    public async Task<DepartmentDto?> CreateDepartmentAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/departments", dto);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<DepartmentDto>(JsonOptions);
    }

    public async Task DeleteDepartmentAsync(Guid id)
        => await EnsureSuccessAsync(await _http.DeleteAsync($"api/departments/{id}"));

    // Positions
    public async Task<PagedResult<PositionDto>?> GetPositionsAsync(Guid? departmentId = null, int page = 1, int pageSize = 50)
    {
        var url = $"api/positions?page={page}&pageSize={pageSize}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await GetJsonAsync<PagedResult<PositionDto>>(url);
    }

    public async Task<PositionDto?> CreatePositionAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/positions", dto);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PositionDto>(JsonOptions);
    }

    // Job Postings
    public async Task<PagedResult<JobPostingDto>?> GetJobPostingsAsync(string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/job-postings?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await GetJsonAsync<PagedResult<JobPostingDto>>(url);
    }

    public async Task<JobPostingDto?> CreateJobPostingAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/job-postings", dto);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JobPostingDto>(JsonOptions);
    }

    public async Task<JobPostingDto?> PublishJobPostingAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/job-postings/{id}/publish", new { });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JobPostingDto>(JsonOptions);
    }

    public async Task<JobPostingDto?> CloseJobPostingAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/job-postings/{id}/close", new { });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JobPostingDto>(JsonOptions);
    }

    // Applicants
    public async Task<PagedResult<ApplicantDto>?> GetApplicantsAsync(Guid? jobPostingId = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/applicants?page={page}&pageSize={pageSize}";
        if (jobPostingId.HasValue) url += $"&jobPostingId={jobPostingId}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await GetJsonAsync<PagedResult<ApplicantDto>>(url);
    }

    // Overtime
    public async Task<PagedResult<OvertimeRequestDto>?> GetOvertimeRequestsAsync(Guid? employeeId = null, string? status = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/overtime-requests?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) url += $"&employeeId={employeeId}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await GetJsonAsync<PagedResult<OvertimeRequestDto>>(url);
    }

    /// <summary>The API approves as the signed-in account's employee; the body names nobody.</summary>
    public async Task ApproveOvertimeAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/overtime-requests/{id}/approve", new { });
        await EnsureSuccessAsync(response);
    }

    public async Task RejectOvertimeAsync(Guid id, string reason)
    {
        var response = await _http.PutAsJsonAsync($"api/overtime-requests/{id}/reject", new { rejectionReason = reason });
        await EnsureSuccessAsync(response);
    }

    // Performance
    public async Task<PagedResult<ReviewCycleDto>?> GetReviewCyclesAsync(int page = 1, int pageSize = 20)
        => await GetJsonAsync<PagedResult<ReviewCycleDto>>($"api/review-cycles?page={page}&pageSize={pageSize}");

    public async Task<PagedResult<PerformanceReviewDto>?> GetPerformanceReviewsAsync(Guid? employeeId = null, Guid? cycleId = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/performance-reviews?page={page}&pageSize={pageSize}";
        if (employeeId.HasValue) url += $"&employeeId={employeeId}";
        if (cycleId.HasValue) url += $"&cycleId={cycleId}";
        return await GetJsonAsync<PagedResult<PerformanceReviewDto>>(url);
    }

    public async Task<ReviewCycleDto?> CreateReviewCycleAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/review-cycles", dto);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ReviewCycleDto>(JsonOptions);
    }

    // HR Analytics
    public async Task<AnalyticsResponse<HeadcountByDepartmentDto>?> GetHeadcountAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/headcount?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await GetJsonAsync<AnalyticsResponse<HeadcountByDepartmentDto>>(url);
    }

    public async Task<AnalyticsResponse<TurnoverDataDto>?> GetTurnoverAnalyticsAsync(DateOnly from, DateOnly to, string groupBy = "month")
        => await GetJsonAsync<AnalyticsResponse<TurnoverDataDto>>($"api/analytics/hr/turnover?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&groupBy={groupBy}");

    public async Task<AnalyticsResponse<AttendanceRateDto>?> GetAttendanceAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/attendance?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await GetJsonAsync<AnalyticsResponse<AttendanceRateDto>>(url);
    }

    public async Task<AnalyticsResponse<LeaveUtilizationDto>?> GetLeaveUtilizationAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/leave-utilization?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await GetJsonAsync<AnalyticsResponse<LeaveUtilizationDto>>(url);
    }

    public async Task<AnalyticsResponse<OvertimeDataDto>?> GetOvertimeAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/overtime?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await GetJsonAsync<AnalyticsResponse<OvertimeDataDto>>(url);
    }

    public async Task<AnalyticsResponse<RecruitmentFunnelDto>?> GetRecruitmentFunnelAnalyticsAsync(DateOnly from, DateOnly to)
        => await GetJsonAsync<AnalyticsResponse<RecruitmentFunnelDto>>($"api/analytics/hr/recruitment-funnel?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public async Task<AnalyticsResponse<PerformanceDistributionDto>?> GetPerformanceDistributionAnalyticsAsync(DateOnly from, DateOnly to)
        => await GetJsonAsync<AnalyticsResponse<PerformanceDistributionDto>>($"api/analytics/hr/performance-distribution?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    // Executive Analytics
    public async Task<AnalyticsResponse<WorkforceSummaryDto>?> GetWorkforceSummaryAsync(DateOnly from, DateOnly to)
        => await GetJsonAsync<AnalyticsResponse<WorkforceSummaryDto>>($"api/analytics/executive/workforce-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public async Task<AnalyticsResponse<HiringTrendDto>?> GetHiringTrendAsync(DateOnly from, DateOnly to)
        => await GetJsonAsync<AnalyticsResponse<HiringTrendDto>>($"api/analytics/executive/hiring-trend?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public async Task<AnalyticsResponse<AttritionDataDto>?> GetAttritionRateAsync(DateOnly from, DateOnly to, string groupBy = "month")
        => await GetJsonAsync<AnalyticsResponse<AttritionDataDto>>($"api/analytics/executive/attrition-rate?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&groupBy={groupBy}");

    public async Task<AnalyticsResponse<LeaveSummaryDto>?> GetLeaveSummaryAsync(DateOnly from, DateOnly to)
        => await GetJsonAsync<AnalyticsResponse<LeaveSummaryDto>>($"api/analytics/executive/leave-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public async Task<AnalyticsResponse<PerformanceOverviewDto>?> GetPerformanceOverviewAsync(Guid? reviewCycleId = null)
    {
        var url = "api/analytics/executive/performance-overview";
        if (reviewCycleId.HasValue) url += $"?reviewCycleId={reviewCycleId}";
        return await GetJsonAsync<AnalyticsResponse<PerformanceOverviewDto>>(url);
    }

    // Payroll
    public async Task<PagedResult<PayrollRunSummaryDto>?> GetPayrollRunsAsync(int page = 1, int pageSize = 20)
        => await GetJsonAsync<PagedResult<PayrollRunSummaryDto>>($"api/payroll-runs?page={page}&pageSize={pageSize}");

    public async Task<PayrollRunDto?> GetPayrollRunAsync(Guid id)
        => await GetJsonAsync<PayrollRunDto>($"api/payroll-runs/{id}");

    public async Task<(PayrollRunDto? Run, string? Error)> CreatePayrollRunAsync(object request)
    {
        var response = await _http.PostAsJsonAsync("api/payroll-runs", request);
        if (!response.IsSuccessStatusCode) return (null, await ReadProblemDetailAsync(response));
        return (await response.Content.ReadFromJsonAsync<PayrollRunDto>(JsonOptions), null);
    }

    public async Task<(bool Ok, string? Error)> ComputePayrollRunAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/payroll-runs/{id}/compute", new { });
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    public async Task<(bool Ok, string? Error)> ApprovePayrollRunAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/payroll-runs/{id}/approve", new { });
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    public async Task<(bool Ok, string? Error)> MarkPayrollRunPaidAsync(Guid id)
    {
        var response = await _http.PutAsJsonAsync($"api/payroll-runs/{id}/mark-paid", new { });
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    /// <summary>Takes an employee off a regular run that isn't paid; the run comes back in Draft.</summary>
    public Task<PayrollRunDto?> RemovePayrollRunEmployeeAsync(Guid runId, Guid employeeId)
        => SendJsonAsync<PayrollRunDto>(HttpMethod.Delete, $"api/payroll-runs/{runId}/employees/{employeeId}");

    // Employee Compensation
    //
    // A 404 here means the employee simply has no compensation row yet - PUT creates one, so the
    // page should render an empty form rather than an error. Any OTHER failure (expired token,
    // database error, etc.) must NOT be folded into that same "no row yet" case: doing so would
    // render a blank form inviting the operator to overwrite real compensation data.
    public async Task<EmployeeCompensationDto?> GetEmployeeCompensationAsync(Guid employeeId)
    {
        var response = await _http.GetAsync($"api/employee-compensation/{employeeId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadProblemDetailAsync(response)
                ?? $"Failed to load compensation ({(int)response.StatusCode}).", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<EmployeeCompensationDto>(JsonOptions);
    }

    public async Task<(bool Ok, string? Error)> UpsertEmployeeCompensationAsync(Guid employeeId, object request)
    {
        var response = await _http.PutAsJsonAsync($"api/employee-compensation/{employeeId}", request);
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    // Payslip downloads
    //
    // A PDF is bytes, not JSON, so these read the raw content of a successful response instead of
    // deserializing it. A failure throws like every other call here, carrying the API's reason:
    // these used to return null, which left the page nothing to say but "Please try again", even
    // when the API had explained why trying again could never work.
    public async Task<byte[]> GetPayslipAsync(Guid runId, Guid employeeId)
    {
        var response = await _http.GetAsync($"api/reports/payslip/{runId}/{employeeId}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]> GetRunPayslipsAsync(Guid runId)
    {
        var response = await _http.GetAsync($"api/reports/payslips/{runId}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]> GetMyPayslipAsync(Guid runId)
    {
        var response = await _http.GetAsync($"api/reports/my-payslip/{runId}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<IReadOnlyList<MyPayslipSummaryDto>?> GetMyPayslipListAsync()
        => await GetJsonAsync<IReadOnlyList<MyPayslipSummaryDto>>("api/reports/my-payslips");

    // BIR Form 2316
    public async Task<IReadOnlyList<int>?> GetBir2316YearsAsync(Guid employeeId)
        => await GetJsonAsync<IReadOnlyList<int>>($"api/reports/2316/years/{employeeId}");

    // Mirrors GetEmployeeCompensationAsync: a 404 here means the employee has no paid runs in
    // that year (Bir2316Service.GetPreviewAsync returns null), which the page should treat as
    // "nothing to show yet", not as an error - any OTHER failure still throws.
    public async Task<Bir2316Dto?> GetBir2316PreviewAsync(Guid employeeId, int year)
    {
        var response = await _http.GetAsync($"api/reports/2316/preview/{employeeId}?year={year}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadProblemDetailAsync(response)
                ?? $"Failed to load 2316 preview ({(int)response.StatusCode}).", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<Bir2316Dto>(JsonOptions);
    }

    // The PDF pair below mirrors the payslip PDF methods above: bytes, not JSON, and a failure throws
    // with the API's reason.
    public async Task<byte[]> GenerateBir2316Async(Guid employeeId, int year, object manualInputs)
    {
        var response = await _http.PostAsJsonAsync($"api/reports/2316/generate/{employeeId}?year={year}", manualInputs);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    // Mirrors GetBir2316PreviewAsync: a 404 here means no employee had a paid run in that year
    // (Bir2316Service.BuildAllAsync returned an empty list) - a legitimate empty result, not a
    // failure, so the page should say so rather than report an error. Any OTHER failure still
    // throws, as GenerateBir2316Async does.
    public async Task<byte[]?> GenerateAllBir2316Async(int year)
    {
        var response = await _http.PostAsync($"api/reports/2316/generate-all?year={year}", null);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadProblemDetailAsync(response)
                ?? $"Failed to generate 2316s ({(int)response.StatusCode}).", null, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    // The 2316 manual inputs last saved for this employee and year (blank if none were), used to
    // pre-fill the Bir2316 page's form. The API always answers 200 - see Bir2316Controller.GetInputs -
    // so this goes through GetJsonAsync like any other read, with no 404 case to special-case.
    public async Task<Bir2316ManualInputsDto?> GetBir2316InputsAsync(Guid employeeId, int year)
        => await GetJsonAsync<Bir2316ManualInputsDto>($"api/reports/2316/inputs/{employeeId}?year={year}");

    // Government remittance reports
    // month is null for the annual reports (the 1604-C alphalist, which take a year and no month);
    // omitting the query parameter entirely rather than sending "&month=" mirrors how the API
    // models it - GovernmentReportsController's month parameter is itself an int?.
    public async Task<GovernmentReportDto?> GetGovernmentReportAsync(string report, int year, int? month)
        => await GetJsonAsync<GovernmentReportDto>($"api/reports/government/{report}?year={year}{MonthQuery(month)}");

    public async Task<byte[]> GetGovernmentReportCsvAsync(string report, int year, int? month)
    {
        var response = await _http.GetAsync($"api/reports/government/{report}?year={year}{MonthQuery(month)}&format=csv");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string MonthQuery(int? month) => month.HasValue ? $"&month={month}" : "";

    // Every JSON read and every command goes through these two rather than GetFromJsonAsync or
    // EnsureSuccessStatusCode. Those throw "Response status code does not indicate success: 409
    // (Conflict)." - which pages then showed to users verbatim - and discard the API's own
    // explanation of what went wrong. The exception thrown here carries that explanation when the
    // API gave one, a plain sentence when it did not, and the status code either way, so a page can
    // show ex.Message as it is and still branch on ex.StatusCode.
    private async Task<T?> GetJsonAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private async Task<T?> SendJsonAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, options: JsonOptions) };
        var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var message = await ReadProblemDetailAsync(response) ?? DescribeFailure(response.StatusCode);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string DescribeFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
        HttpStatusCode.Forbidden => "You do not have permission to do that.",
        HttpStatusCode.NotFound => "The record could not be found.",
        >= HttpStatusCode.InternalServerError => $"The server ran into a problem ({(int)status}). Please try again.",
        _ => $"The request could not be completed ({(int)status})."
    };

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailResponse>(JsonOptions);
            return problem?.Detail;
        }
        catch
        {
            return null;
        }
    }
}

// Client-side DTO copies
public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles, bool MustChangePassword = false);
public record UserAccountDto(string Id, string Email, string? FirstName, string? LastName, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword, Guid? EmployeeId, string? EmployeeName, bool CanManage);
public record CreateUserAccountRequest(string Email, string FirstName, string LastName, Guid? EmployeeId, IReadOnlyList<string> Roles);
public record CreatedUserAccountDto(UserAccountDto Account, string TemporaryPassword);
public record EmployeeLinkDto(Guid EmployeeId, string UserId, bool IsActive);
public record AssignableRoleDto(string Name, bool Grantable, string? Reason);
public record RoleDto(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount, bool CanEdit);
public record PermissionDto(string Key, string Group, string Label, string Description);
public record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
public record TemporaryPasswordDto(string TemporaryPassword);
public record UserProfileDto(string? FirstName, string? LastName, string? Email);
public record EmailSettingsDto(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword,
    string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt);
public record SaveEmailSettingsRequest(string Host, int Port, bool UseStartTls, string? Username, string? Password,
    string FromAddress, string FromName, string AppBaseUrl);
internal record ResetAvailability(bool Available);
internal record TestEmailResult(bool Sent, string? To);
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);
public record EmployeeListDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, string FullName, string WorkEmail, string? DepartmentName, string? PositionTitle, string EmploymentStatus, bool IsActive,
    DateOnly? SeparationDate = null);

// Certificate of Employment - mirrors PeopleCore.Application.Employees.Coe.CoeRequest.
public record CoeRequest(string? Purpose, string? SignatoryName, string? SignatoryTitle, bool IncludeSalary);

// Separations - the enums mirror PeopleCore.Domain.Enums.SeparationEnums field-for-field; they
// travel on the wire as strings, via the JsonStringEnumConverter added to JsonOptions above.
public enum SeparationType { Resignation, TerminationJustCause, AuthorizedCause, EndOfContract, Retirement, Death }
public enum AuthorizedCause { Redundancy, Retrenchment, ClosureNotDueToLosses, ClosureDueToSeriousLosses, LaborSavingDevices, Disease }
public enum SeparationStatus { NoticeGiven, Separated }

public record RecordSeparationRequest(
    Guid EmployeeId,
    SeparationType Type,
    AuthorizedCause? AuthorizedCause,
    DateOnly NoticeDate,
    DateOnly LastWorkingDay,
    string? Reason);

public record SeparationDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    string? Position,
    SeparationType Type,
    AuthorizedCause? AuthorizedCause,
    DateOnly NoticeDate,
    DateOnly LastWorkingDay,
    string? Reason,
    SeparationStatus Status,
    string RecordedBy,
    string? SeparatedBy,
    DateTime? SeparatedAt,
    DateOnly FinalPayDueBy,
    bool FinalPayOverdue,
    int ClearedCount,
    int ClearanceCount,
    IReadOnlyList<ClearanceItemDto> ClearanceItems,
    Guid? FinalPayRunId = null,
    string? FinalPayRunNumber = null,
    // A PayrollRunStatus name, as the run DTOs below carry theirs.
    string? FinalPayStatus = null);

public record ClearanceItemDto(Guid Id, string Name, string? ClearedBy, DateTime? ClearedAt, string? Note,
    string? LastUndoneBy = null, DateTime? LastUndoneAt = null);

// Final pay - mirrors PeopleCore.Application.Payroll.FinalPay.FinalPayDtos. Status and LoanType
// travel as enum names; PayrollLabels turns them into words.
public record FinalPayRequest(
    DateOnly PayDate,
    DateOnly? PeriodStart,
    decimal? SeparationPayOverride,
    decimal? RetirementPayOverride,
    string? OverrideNote,
    IReadOnlyList<FinalPayDeductionDto> Deductions);

public record FinalPayDeductionDto(string Label, decimal Amount);
public record FinalPayLeaveLineDto(string LeaveType, decimal Days, bool CountsAsVacation);
public record FinalPayLoanLineDto(string LoanType, decimal Balance, decimal Deducted, decimal Uncovered);

public record FinalPaySummaryDto(
    Guid RunId,
    string RunNumber,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PayDate,
    decimal WorkingDays,
    bool NoSalaryDays,
    decimal LeaveConversionPay,
    decimal LeaveConversionNonTaxable,
    IReadOnlyList<FinalPayLeaveLineDto> LeaveLines,
    decimal SeparationPay,
    decimal RetirementPay,
    decimal? ComputedSeparationOrRetirementPay,
    string? OverrideNote,
    int ServiceYears,
    IReadOnlyList<FinalPayDeductionDto> Deductions,
    IReadOnlyList<FinalPayLoanLineDto> Loans,
    decimal WithholdingTax,
    decimal GrossPay,
    decimal NetPay,
    bool ClearanceComplete,
    IReadOnlyList<string> OutstandingClearance);
public record ClearItemRequest(string? Note);
public record AddClearanceItemRequest(string Name);
public record LeaveBalanceDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid LeaveTypeId, string LeaveTypeName, int Year, decimal TotalDays, decimal UsedDays, decimal CarriedOverDays, decimal RemainingDays);
public record LeaveRequestDto(Guid Id, Guid EmployeeId, string EmployeeName, string LeaveTypeName, string StartDate, string EndDate, decimal TotalDays, string Status, string? Reason);
public record AttendanceImportEmployeeDto(Guid Id, string EmployeeNumber, string FullName, string? BiometricId, bool IsActive);
public record UnmatchedDeviceIdDto(string DeviceId, int Punches);
public record AttendanceImportPreviewDto(string Layout, int Punches, int MatchedPeople, DateOnly? From, DateOnly? To,
    IReadOnlyList<UnmatchedDeviceIdDto> Unmatched, IReadOnlyList<string> Errors, IReadOnlyList<AttendanceImportEmployeeDto> Employees);
public record AttendanceImportResultDto(int Imported, int Skipped, IReadOnlyList<string> Errors);
public record AttendanceRecordDto(Guid Id, string AttendanceDate, string? TimeIn, string? TimeOut, int LateMinutes, int UndertimeMinutes, bool IsPresent,
    Guid EmployeeId = default, string EmployeeName = "", int OvertimeMinutes = 0);
public record AttendanceCorrectionDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, DateOnly AttendanceDate,
    string? PreviousTimeIn, string? PreviousTimeOut, string? NewTimeIn, string? NewTimeOut,
    string Reason, string Source, string Status, string RequestedBy, DateTime RequestedAt,
    string? ReviewedBy, DateTime? ReviewedAt, string? RejectionReason);
public record CorrectAttendanceRequest(Guid EmployeeId, DateOnly Date, TimeOnly? TimeIn, TimeOnly? TimeOut, string Reason);
public record AttendanceCorrectionRequest(DateOnly Date, TimeOnly? TimeIn, TimeOnly? TimeOut, string Reason);
public record CompanyDto(Guid Id, string Name);
public record CompanyProfileDto(
    string Name, string? Tin, string? RdoCode, string? Address, string? City, string? ZipCode,
    string? ContactEmail, string? ContactPhone, string? SssNumber, string? PhilHealthNumber, string? PagIbigNumber);
public record DepartmentDto(Guid Id, Guid CompanyId, Guid? ParentDepartmentId, string? ParentDepartmentName, string Name, string? Code, int SubDepartmentCount);
public record PositionDto(Guid Id, Guid DepartmentId, string DepartmentName, string Title, string? Level);
public record JobPostingDto(Guid Id, string Title, Guid? DepartmentId, string? DepartmentName, Guid? PositionId, string? PositionTitle, string? Description, string? Requirements, int Vacancies, string Status, DateTime? PostedAt, DateTime? ClosedAt);
public record ApplicantDto(Guid Id, Guid JobPostingId, string JobPostingTitle, string FirstName, string LastName, string Email, string? Phone, string Status, Guid? ConvertedEmployeeId, DateTime AppliedAt);
public record OvertimeRequestDto(Guid Id, Guid EmployeeId, string EmployeeName, string OvertimeDate, string StartTime, string EndTime, int TotalMinutes, string Reason, string Status, string? RejectionReason);
public record ReviewCycleDto(Guid Id, string Name, int Year, int? Quarter, string StartDate, string EndDate, string Status);
public record PerformanceReviewDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid ReviewCycleId, string ReviewCycleName, decimal? FinalScore, string Status);

// Analytics DTOs
public record AnalyticsResponse<T>(AnalyticsPeriodDto Period, IReadOnlyList<T> Data, DateTime GeneratedAt);
public record AnalyticsPeriodDto(DateOnly From, DateOnly To);
public record HeadcountByDepartmentDto(string Department, int Active, int Inactive, int Total);
public record TurnoverDataDto(string Period, int NewHires, int Separations, decimal TurnoverRate);
public record AttendanceRateDto(string Department, decimal OnTimeRate, decimal LateRate, decimal AbsentRate);
public record LeaveUtilizationDto(string LeaveType, decimal TotalAllocated, decimal TotalUsed, decimal UtilizationRate);
public record OvertimeDataDto(string Department, decimal TotalHours, decimal AverageHoursPerEmployee);
public record RecruitmentFunnelDto(string Stage, int Count, decimal ConversionRate);
public record PerformanceDistributionDto(string ScoreRange, int Count, decimal Percentage);
public record WorkforceSummaryDto(int TotalActive, int TotalInactive, IReadOnlyList<HeadcountByDepartmentDto> ByDepartment);
public record HiringTrendDto(string Month, int NewHires);
public record AttritionDataDto(string Period, decimal AttritionRate, int Separations, int AverageHeadcount);
public record LeaveSummaryDto(decimal TotalDaysConsumed, decimal AverageDaysPerEmployee, IReadOnlyList<LeaveUtilizationDto> ByType);
public record PerformanceOverviewDto(string Department, decimal AverageScore, string Cycle);

// Payroll DTOs
public record PayrollRunEmployeeDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    decimal DaysWorked,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay,
    decimal RegularPay,
    decimal OvertimePay,
    decimal HolidayPay,
    decimal NightDiffPay,
    decimal TaxableAllowances,
    decimal NonTaxableAllowances,
    decimal ThirteenthMonth,
    decimal AbsenceDeduction,
    decimal TardinessDeduction,
    decimal SSSEmployee,
    decimal SSSEmployer,
    decimal PhilHealthEmployee,
    decimal PhilHealthEmployer,
    decimal PagIbigEmployee,
    decimal PagIbigEmployer,
    decimal WithholdingTax,
    decimal LoanDeductions,
    decimal OtherDeductions,
    // Final-pay earnings, zero on a regular run; FinalPayNonTaxable is the non-taxable part of
    // all three (it includes LeaveConversionNonTaxable).
    decimal LeaveConversionPay = 0m,
    decimal LeaveConversionNonTaxable = 0m,
    decimal SeparationPay = 0m,
    decimal RetirementPay = 0m,
    decimal FinalPayNonTaxable = 0m);

public record PayrollRunDto(
    Guid Id,
    string RunNumber,
    string PeriodLabel,
    string PeriodStart,
    string PeriodEnd,
    string PayDate,
    string Frequency,
    string Status,
    int EmployeeCount,
    decimal TotalGrossPay,
    decimal TotalDeductions,
    decimal TotalNetPay,
    DateTime CreatedAt,
    Guid? AttendancePeriodId,
    int EmployeesMissingAttendance,
    IReadOnlyList<PayrollRunEmployeeDto> Employees,
    // "Regular" or "FinalPay".
    string RunType = "Regular");

public record PayrollRunSummaryDto(
    Guid Id,
    string RunNumber,
    string PeriodLabel,
    string PeriodStart,
    string PeriodEnd,
    string PayDate,
    string Frequency,
    string Status,
    int EmployeeCount,
    decimal TotalGrossPay,
    decimal TotalNetPay,
    int EmployeesMissingAttendance,
    DateTime CreatedAt,
    string RunType = "Regular");

public record EmployeeCompensationDto(
    Guid Id,
    Guid EmployeeId,
    decimal BasicSalary,
    string PayFrequency,
    string TaxCode,
    int Dependents);

public record MyPayslipSummaryDto(
    Guid RunId,
    string RunNumber,
    string PeriodLabel,
    string PayDate,
    decimal NetPay);

// BIR Form 2316
//
// Mirrors PeopleCore.Application.Payroll.DTOs.Bir2316Dto field-for-field, INCLUDING the
// server's computed ItemNN properties (Item19, Item20, Item21, Item23, Item24, Item26, Item28,
// Item38, Item52): the client never recomputes them, it only displays what the server sent, so
// every one of them needs a settable property here to receive its value off the wire. The
// ItemNN names are kept exactly as-is for the same reason the server keeps them: the number IS
// the link to the numbered box on the form, and a name mismatch here - unlike almost anywhere
// else in this client - would not throw. System.Text.Json's PropertyNameCaseInsensitive option
// silently leaves an unmatched property at its default, so a mistyped field prints a blank or a
// zero on a tax certificate instead of failing to compile or failing at runtime.
public class Bir2316Dto
{
    // Header
    public int Year { get; set; }
    public string PeriodFrom { get; set; } = "";
    public string PeriodTo { get; set; } = "";

    // Part I — Employee Info
    public string EmployeeTin { get; set; } = "";
    public string EmployeeLastName { get; set; } = "";
    public string EmployeeFirstName { get; set; } = "";
    public string EmployeeMiddleName { get; set; } = "";
    public string RdoCode { get; set; } = "";
    public string RegisteredAddress { get; set; } = "";
    public string RegisteredZipCode { get; set; } = "";
    public string LocalHomeAddress { get; set; } = "";
    public string LocalZipCode { get; set; } = "";
    public string ForeignAddress { get; set; } = "";
    public string DateOfBirth { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public decimal StatutoryMinWagePerDay { get; set; }
    public decimal StatutoryMinWagePerMonth { get; set; }
    public bool IsMinimumWageEarner { get; set; }

    // Part II — Employer Info (Present)
    public string EmployerTin { get; set; } = "";
    public string EmployerName { get; set; } = "";
    public string EmployerAddress { get; set; } = "";
    public string EmployerZipCode { get; set; } = "";
    public string EmployerRdoCode { get; set; } = "";
    public bool IsMainEmployer { get; set; } = true;

    // Part III — Employer Info (Previous)
    public string PrevEmployerTin { get; set; } = "";
    public string PrevEmployerName { get; set; } = "";
    public string PrevEmployerAddress { get; set; } = "";
    public string PrevEmployerZipCode { get; set; } = "";

    // Part IV-B Section A — Non-Taxable/Exempt
    public decimal Item29_NonTaxableBasicSalary { get; set; }
    public decimal Item30_HolidayPayMwe { get; set; }
    public decimal Item31_OvertimePayMwe { get; set; }
    public decimal Item32_NightShiftDiffMwe { get; set; }
    public decimal Item33_HazardPayMwe { get; set; }
    public decimal Item34_ThirteenthMonthAndBenefits { get; set; }
    public decimal Item35_DeMinimis { get; set; }
    public decimal Item36_SssPhicPagibigContributions { get; set; }
    public decimal Item37_SalariesOtherForms { get; set; }

    // Part IV-B Section B — Taxable Regular
    public decimal Item39_BasicSalary { get; set; }
    public decimal Item40_Representation { get; set; }
    public decimal Item41_Transportation { get; set; }
    public decimal Item42_Cola { get; set; }
    public decimal Item43_FixedHousing { get; set; }
    public decimal Item44A_OtherAmount { get; set; }
    public string Item44A_OtherLabel { get; set; } = "";
    public decimal Item44B_OtherAmount { get; set; }
    public string Item44B_OtherLabel { get; set; } = "";

    // Supplementary
    public decimal Item45_Commission { get; set; }
    public decimal Item46_ProfitSharing { get; set; }
    public decimal Item47_Fees { get; set; }
    public decimal Item48_TaxableThirteenthMonth { get; set; }
    public decimal Item49_HazardPay { get; set; }
    public decimal Item50_OvertimePay { get; set; }
    public decimal Item51A_OtherAmount { get; set; }
    public string Item51A_OtherLabel { get; set; } = "";
    public decimal Item51B_OtherAmount { get; set; }
    public string Item51B_OtherLabel { get; set; } = "";

    // Part IVA — Summary inputs
    public decimal Item22_PrevTaxableCompensation { get; set; }
    public decimal Item25B_PrevTaxWithheld { get; set; }
    public decimal Item25A_PresentTaxWithheld { get; set; }
    public decimal Item27_PeraTaxCredit { get; set; }

    // Computed server-side - see this record's remarks for why these still need setters here.
    public decimal Item38_TotalNonTaxable { get; set; }
    public decimal Item52_TotalTaxableCompensation { get; set; }
    public decimal Item19_GrossCompensation { get; set; }
    public decimal Item20_LessNonTaxable { get; set; }
    public decimal Item21_TaxableFromPresent { get; set; }
    public decimal Item23_GrossTaxable { get; set; }
    public decimal Item24_TaxDue { get; set; }
    public decimal Item26_TotalTaxWithheld { get; set; }
    public decimal Item28_TotalTaxes { get; set; }
}

// Problem-detail body from ExceptionHandlingMiddleware (400 responses for DomainException)
public record ProblemDetailResponse(string? Title, string? Detail, int? Status);

// One month's SSS / PhilHealth / Pag-IBIG / 1601-C remittance report, or (Month == 0) an annual
// one such as the 1604-C alphalist, whose data lives in Sections rather than the top-level
// Columns/Rows/Totals - see GovernmentReportSectionDto.
public record GovernmentReportDto(string Report, string Title, int Year, int Month, string Basis,
    GovernmentReportEmployerDto Employer, IReadOnlyList<string> Columns, IReadOnlyList<GovernmentReportRowDto> Rows,
    IReadOnlyList<string> Totals, IReadOnlyList<GovernmentReportLineDto> Summary, IReadOnlyList<string> Warnings,
    IReadOnlyList<GovernmentReportSectionDto> Sections);
public record GovernmentReportRowDto(Guid EmployeeId, IReadOnlyList<string> Cells, bool MissingNumber);
public record GovernmentReportLineDto(string Label, decimal Amount);
public record GovernmentReportEmployerDto(string Name, string? Address, string Tin, string? RdoCode, string AgencyNumber);

// One table of a report that has several - the 1604-C alphalist's employment-status groups. A
// report with a single table (every monthly report) uses the top-level Columns/Rows/Totals above
// and an empty Sections list.
public record GovernmentReportSectionDto(string Title, IReadOnlyList<string> Columns,
    IReadOnlyList<GovernmentReportRowDto> Rows, IReadOnlyList<string> Totals, string EmptyMessage);

// The Bir2316ManualInputs fields the Bir2316 page's form can display and edit - see
// ManualInputsFormModel in Bir2316.razor. Deliberately leaves out IsMinimumWageEarner /
// StatutoryMinWagePerDay / StatutoryMinWagePerMonth: the page offers no control for them, and the
// API answers those fields too, so they are simply ignored on deserialization.
public record Bir2316ManualInputsDto(string? PrevEmployerTin, string? PrevEmployerName, string? PrevEmployerAddress,
    string? PrevEmployerZipCode, decimal Item22_PrevTaxableCompensation, decimal Item25B_PrevTaxWithheld,
    decimal Item27_PeraTaxCredit, decimal Item35_DeMinimis, decimal Item33_HazardPayMwe);
