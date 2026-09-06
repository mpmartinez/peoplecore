using System.Net;
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
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    // Employees
    public async Task<PagedResult<EmployeeListDto>?> GetEmployeesAsync(int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<EmployeeListDto>>($"api/employees?page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<EmployeeListDto?> GetEmployeeAsync(Guid id)
        => await _http.GetFromJsonAsync<EmployeeListDto>($"api/employees/{id}", JsonOptions);

    public async Task<EmployeeListDto?> CreateEmployeeAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/employees", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeListDto>(JsonOptions);
    }

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

    // HR Analytics
    public async Task<AnalyticsResponse<HeadcountByDepartmentDto>?> GetHeadcountAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/headcount?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await _http.GetFromJsonAsync<AnalyticsResponse<HeadcountByDepartmentDto>>(url, JsonOptions);
    }

    public async Task<AnalyticsResponse<TurnoverDataDto>?> GetTurnoverAnalyticsAsync(DateOnly from, DateOnly to, string groupBy = "month")
        => await _http.GetFromJsonAsync<AnalyticsResponse<TurnoverDataDto>>($"api/analytics/hr/turnover?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&groupBy={groupBy}", JsonOptions);

    public async Task<AnalyticsResponse<AttendanceRateDto>?> GetAttendanceAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/attendance?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await _http.GetFromJsonAsync<AnalyticsResponse<AttendanceRateDto>>(url, JsonOptions);
    }

    public async Task<AnalyticsResponse<LeaveUtilizationDto>?> GetLeaveUtilizationAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/leave-utilization?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await _http.GetFromJsonAsync<AnalyticsResponse<LeaveUtilizationDto>>(url, JsonOptions);
    }

    public async Task<AnalyticsResponse<OvertimeDataDto>?> GetOvertimeAnalyticsAsync(DateOnly from, DateOnly to, Guid? departmentId = null)
    {
        var url = $"api/analytics/hr/overtime?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId}";
        return await _http.GetFromJsonAsync<AnalyticsResponse<OvertimeDataDto>>(url, JsonOptions);
    }

    public async Task<AnalyticsResponse<RecruitmentFunnelDto>?> GetRecruitmentFunnelAnalyticsAsync(DateOnly from, DateOnly to)
        => await _http.GetFromJsonAsync<AnalyticsResponse<RecruitmentFunnelDto>>($"api/analytics/hr/recruitment-funnel?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", JsonOptions);

    public async Task<AnalyticsResponse<PerformanceDistributionDto>?> GetPerformanceDistributionAnalyticsAsync(DateOnly from, DateOnly to)
        => await _http.GetFromJsonAsync<AnalyticsResponse<PerformanceDistributionDto>>($"api/analytics/hr/performance-distribution?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", JsonOptions);

    // Executive Analytics
    public async Task<AnalyticsResponse<WorkforceSummaryDto>?> GetWorkforceSummaryAsync(DateOnly from, DateOnly to)
        => await _http.GetFromJsonAsync<AnalyticsResponse<WorkforceSummaryDto>>($"api/analytics/executive/workforce-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", JsonOptions);

    public async Task<AnalyticsResponse<HiringTrendDto>?> GetHiringTrendAsync(DateOnly from, DateOnly to)
        => await _http.GetFromJsonAsync<AnalyticsResponse<HiringTrendDto>>($"api/analytics/executive/hiring-trend?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", JsonOptions);

    public async Task<AnalyticsResponse<AttritionDataDto>?> GetAttritionRateAsync(DateOnly from, DateOnly to, string groupBy = "month")
        => await _http.GetFromJsonAsync<AnalyticsResponse<AttritionDataDto>>($"api/analytics/executive/attrition-rate?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&groupBy={groupBy}", JsonOptions);

    public async Task<AnalyticsResponse<LeaveSummaryDto>?> GetLeaveSummaryAsync(DateOnly from, DateOnly to)
        => await _http.GetFromJsonAsync<AnalyticsResponse<LeaveSummaryDto>>($"api/analytics/executive/leave-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", JsonOptions);

    public async Task<AnalyticsResponse<PerformanceOverviewDto>?> GetPerformanceOverviewAsync(Guid? reviewCycleId = null)
    {
        var url = "api/analytics/executive/performance-overview";
        if (reviewCycleId.HasValue) url += $"?reviewCycleId={reviewCycleId}";
        return await _http.GetFromJsonAsync<AnalyticsResponse<PerformanceOverviewDto>>(url, JsonOptions);
    }

    // Payroll
    public async Task<PagedResult<PayrollRunSummaryDto>?> GetPayrollRunsAsync(int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<PayrollRunSummaryDto>>($"api/payroll-runs?page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<PayrollRunDto?> GetPayrollRunAsync(Guid id)
        => await _http.GetFromJsonAsync<PayrollRunDto>($"api/payroll-runs/{id}", JsonOptions);

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
                ?? $"Failed to load compensation ({(int)response.StatusCode}).");
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
    // A PDF is bytes, not JSON, so these three differ from every other method here: they read
    // the raw byte content of a successful response instead of deserializing JSON, and return
    // null (rather than throwing) on failure so callers can show an inline error the same way
    // the JSON-returning methods above do via ReadProblemDetailAsync.
    public async Task<byte[]?> GetPayslipAsync(Guid runId, Guid employeeId)
    {
        var response = await _http.GetAsync($"api/reports/payslip/{runId}/{employeeId}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]?> GetRunPayslipsAsync(Guid runId)
    {
        var response = await _http.GetAsync($"api/reports/payslips/{runId}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]?> GetMyPayslipAsync(Guid runId)
    {
        var response = await _http.GetAsync($"api/reports/my-payslip/{runId}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<IReadOnlyList<MyPayslipSummaryDto>?> GetMyPayslipListAsync()
        => await _http.GetFromJsonAsync<IReadOnlyList<MyPayslipSummaryDto>>("api/reports/my-payslips", JsonOptions);

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
public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles);
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);
public record EmployeeListDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, string FullName, string WorkEmail, string? DepartmentName, string? PositionTitle, string EmploymentStatus, bool IsActive);
public record LeaveBalanceDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid LeaveTypeId, string LeaveTypeName, int Year, decimal TotalDays, decimal UsedDays, decimal CarriedOverDays, decimal RemainingDays);
public record LeaveRequestDto(Guid Id, Guid EmployeeId, string EmployeeName, string LeaveTypeName, string StartDate, string EndDate, decimal TotalDays, string Status, string? Reason);
public record AttendanceRecordDto(Guid Id, string AttendanceDate, string? TimeIn, string? TimeOut, int LateMinutes, int UndertimeMinutes, bool IsPresent);
public record CompanyDto(Guid Id, string Name);
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
    decimal SSSEmployee,
    decimal SSSEmployer,
    decimal PhilHealthEmployee,
    decimal PhilHealthEmployer,
    decimal PagIbigEmployee,
    decimal PagIbigEmployer,
    decimal WithholdingTax,
    decimal LoanDeductions,
    decimal OtherDeductions);

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
    IReadOnlyList<PayrollRunEmployeeDto> Employees);

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
    DateTime CreatedAt);

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

// Problem-detail body from ExceptionHandlingMiddleware (400 responses for DomainException)
public record ProblemDetailResponse(string? Title, string? Detail, int? Status);
