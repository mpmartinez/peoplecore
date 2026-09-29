using System.Net;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

public class ApiClientTests
{
    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    private const string ProblemJson = """{"title":"Bad Request","status":400,"detail":"Run is not in Draft status."}""";

    [Fact]
    public async Task Login_ReturnsTheTokenAndRoles_OnSuccess()
    {
        _api.On(HttpMethod.Post, "/api/auth/login", HttpStatusCode.OK,
            """{"token":"abc","email":"hr@company.test","roles":["HRManager"]}""");

        var result = await CreateClient().LoginAsync("hr@company.test", "secret");

        result.Should().NotBeNull();
        result!.Token.Should().Be("abc");
        result.Roles.Should().Equal("HRManager");
        _api.RequestBodies.Single().Should().Contain("\"email\":\"hr@company.test\"");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Login_ReturnsNull_OnAnyFailure(HttpStatusCode status)
    {
        _api.On(HttpMethod.Post, "/api/auth/login", status);

        var result = await CreateClient().LoginAsync("hr@company.test", "wrong");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLeaveRequests_OnlyFiltersByEmployee_WhenOneIsGiven()
    {
        var employeeId = Guid.NewGuid();
        const string empty = """{"items":[],"totalCount":0,"page":1,"pageSize":20,"totalPages":0}""";
        _api.On(HttpMethod.Get, "/api/leave-requests?page=1&pageSize=20", HttpStatusCode.OK, empty)
            .On(HttpMethod.Get, $"/api/leave-requests?page=2&pageSize=10&employeeId={employeeId}", HttpStatusCode.OK, empty);
        var client = CreateClient();

        (await client.GetLeaveRequestsAsync()).Should().NotBeNull();
        (await client.GetLeaveRequestsAsync(employeeId, page: 2, pageSize: 10)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetEmployeeCompensation_ReturnsNull_WhenTheEmployeeHasNoCompensationYet()
    {
        var employeeId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/employee-compensation/{employeeId}", HttpStatusCode.NotFound);

        var result = await CreateClient().GetEmployeeCompensationAsync(employeeId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEmployeeCompensation_Throws_OnAnyOtherFailure()
    {
        // The page renders an empty, saveable form for null. Folding a server error into null would
        // invite an operator to overwrite real compensation data with that blank form.
        var employeeId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/employee-compensation/{employeeId}", HttpStatusCode.InternalServerError);

        var act = () => CreateClient().GetEmployeeCompensationAsync(employeeId);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Failed to load compensation (500).");
    }

    [Fact]
    public async Task GetEmployeeCompensation_ThrowsWithTheServersExplanation_WhenItGivesOne()
    {
        var employeeId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/employee-compensation/{employeeId}", HttpStatusCode.Forbidden,
            """{"detail":"You may not view this employee."}""");

        var act = () => CreateClient().GetEmployeeCompensationAsync(employeeId);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("You may not view this employee.");
    }

    [Fact]
    public async Task AFailedRead_CarriesTheApisExplanationAndStatus()
    {
        // Pages show ex.Message as it is, so it has to be the API's reason, not HttpClient's
        // "Response status code does not indicate success" boilerplate.
        _api.On(HttpMethod.Get, "/api/companies", HttpStatusCode.Forbidden, """{"detail":"Companies are admin-only."}""");

        var act = () => CreateClient().GetCompaniesAsync();

        var thrown = await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Companies are admin-only.");
        thrown.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Your session has expired. Please sign in again.")]
    [InlineData(HttpStatusCode.Forbidden, "You do not have permission to do that.")]
    [InlineData(HttpStatusCode.NotFound, "The record could not be found.")]
    [InlineData(HttpStatusCode.Conflict, "The request could not be completed (409).")]
    [InlineData(HttpStatusCode.BadGateway, "The server ran into a problem (502). Please try again.")]
    public async Task AFailedCommand_WithNoExplanation_StillSaysSomethingAUserCanRead(HttpStatusCode status, string expected)
    {
        var requestId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/leave-requests/{requestId}/approve", () =>
            new HttpResponseMessage(status) { Content = new StringContent("<html>error</html>") });

        var act = () => CreateClient().ApproveLeaveAsync(requestId);

        var thrown = await act.Should().ThrowAsync<HttpRequestException>().WithMessage(expected);
        thrown.Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task DeleteDepartment_CarriesTheApisExplanation_WhenRefused()
    {
        var id = Guid.NewGuid();
        _api.On(HttpMethod.Delete, $"/api/departments/{id}", HttpStatusCode.Conflict,
            """{"detail":"Department still has positions."}""");

        var act = () => CreateClient().DeleteDepartmentAsync(id);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Department still has positions.");
    }

    [Fact]
    public async Task ComputePayrollRun_ReturnsTheProblemDetail_WhenRejected()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/compute", HttpStatusCode.BadRequest, ProblemJson);

        var (ok, error) = await CreateClient().ComputePayrollRunAsync(runId);

        ok.Should().BeFalse();
        error.Should().Be("Run is not in Draft status.");
    }

    [Fact]
    public async Task ApprovePayrollRun_ReturnsNullError_WhenTheFailureBodyIsNotJson()
    {
        // A proxy's HTML error page must not surface as a deserialization exception.
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/approve", () =>
            new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>502</html>") });

        var (ok, error) = await CreateClient().ApprovePayrollRunAsync(runId);

        ok.Should().BeFalse();
        error.Should().BeNull();
    }

    [Fact]
    public async Task MarkPayrollRunPaid_ReportsSuccess()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/mark-paid", HttpStatusCode.NoContent);

        var (ok, error) = await CreateClient().MarkPayrollRunPaidAsync(runId);

        ok.Should().BeTrue();
        error.Should().BeNull();
    }

    private static string RunWithLeaveConversion(Guid runId, bool includes) =>
        $$"""
        {"id":"{{runId}}","runNumber":"PR-2026-0024","periodLabel":"Dec 16-31, 2026","periodStart":"2026-12-16",
         "periodEnd":"2026-12-31","payDate":"2027-01-05","frequency":"SemiMonthly","status":"Draft",
         "employeeCount":0,"totalGrossPay":0,"totalDeductions":0,"totalNetPay":0,"createdAt":"2026-12-01T00:00:00Z",
         "attendancePeriodId":null,"employeesMissingAttendance":0,"employees":[],"runType":"Regular",
         "includesLeaveConversion":{{(includes ? "true" : "false")}}}
        """;

    [Fact]
    public async Task GetPayrollRun_ReadsWhetherItConvertsLeave()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/payroll-runs/{runId}", HttpStatusCode.OK, RunWithLeaveConversion(runId, true));

        var run = await CreateClient().GetPayrollRunAsync(runId);

        run!.IncludesLeaveConversion.Should().BeTrue();
    }

    [Fact]
    public async Task GetPayrollRuns_ReadsWhichRunsConvertLeave()
    {
        _api.On(HttpMethod.Get, "/api/payroll-runs?page=1&pageSize=20", HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{Guid.NewGuid()}}","runNumber":"PR-2026-0024","periodLabel":"Dec 16-31, 2026",
              "periodStart":"2026-12-16","periodEnd":"2026-12-31","payDate":"2027-01-05","frequency":"SemiMonthly",
              "status":"Draft","employeeCount":0,"totalGrossPay":0,"totalNetPay":0,"employeesMissingAttendance":0,
              "createdAt":"2026-12-01T00:00:00Z","runType":"Regular","includesLeaveConversion":true}],
             "totalCount":1,"page":1,"pageSize":20,"totalPages":1}
            """);

        var runs = await CreateClient().GetPayrollRunsAsync();

        runs!.Items.Single().IncludesLeaveConversion.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, """{"include":true}""")]
    [InlineData(false, """{"include":false}""")]
    public async Task SetPayrollRunLeaveConversion_PutsTheChoice_AndReadsTheRunBack(bool include, string body)
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/leave-conversion", HttpStatusCode.OK, RunWithLeaveConversion(runId, include));

        var run = await CreateClient().SetPayrollRunLeaveConversionAsync(runId, include);

        run!.IncludesLeaveConversion.Should().Be(include);
        _api.RequestBodies.Single().Should().Be(body);
    }

    [Fact]
    public async Task SetPayrollRunLeaveConversion_ThrowsTheApisReason_WhenRefused()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/leave-conversion", HttpStatusCode.BadRequest,
            """{"title":"Business rule violation","status":400,"detail":"Year-end leave conversion goes on a December payroll."}""");

        var act = () => CreateClient().SetPayrollRunLeaveConversionAsync(runId, true);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Year-end leave conversion goes on a December payroll.");
    }

    private static string RunWithThirteenthMonth(Guid runId, bool includes) =>
        $$"""
        {"id":"{{runId}}","runNumber":"PR-2026-0021","periodLabel":"Nov 1-30, 2026","periodStart":"2026-11-01",
         "periodEnd":"2026-11-30","payDate":"2026-11-30","frequency":"Monthly","status":"Draft",
         "employeeCount":0,"totalGrossPay":0,"totalDeductions":0,"totalNetPay":0,"createdAt":"2026-11-01T00:00:00Z",
         "attendancePeriodId":null,"employeesMissingAttendance":0,"employees":[],"runType":"Regular",
         "includesLeaveConversion":false,"includesThirteenthMonth":{{(includes ? "true" : "false")}}}
        """;

    [Fact]
    public async Task GetPayrollRun_ReadsWhetherItIncludesThe13thMonth()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/payroll-runs/{runId}", HttpStatusCode.OK, RunWithThirteenthMonth(runId, true));

        var run = await CreateClient().GetPayrollRunAsync(runId);

        run!.IncludesThirteenthMonth.Should().BeTrue();
    }

    [Fact]
    public async Task GetPayrollRuns_ReadsWhichRunsIncludeThe13thMonth()
    {
        _api.On(HttpMethod.Get, "/api/payroll-runs?page=1&pageSize=20", HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{Guid.NewGuid()}}","runNumber":"PR-2026-0021","periodLabel":"Nov 1-30, 2026",
              "periodStart":"2026-11-01","periodEnd":"2026-11-30","payDate":"2026-11-30","frequency":"Monthly",
              "status":"Draft","employeeCount":0,"totalGrossPay":0,"totalNetPay":0,"employeesMissingAttendance":0,
              "createdAt":"2026-11-01T00:00:00Z","runType":"Regular","includesLeaveConversion":false,
              "includesThirteenthMonth":true}],
             "totalCount":1,"page":1,"pageSize":20,"totalPages":1}
            """);

        var runs = await CreateClient().GetPayrollRunsAsync();

        runs!.Items.Single().IncludesThirteenthMonth.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, """{"include":true}""")]
    [InlineData(false, """{"include":false}""")]
    public async Task SetPayrollRunThirteenthMonth_PutsTheChoice_AndReadsTheRunBack(bool include, string body)
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/thirteenth-month", HttpStatusCode.OK, RunWithThirteenthMonth(runId, include));

        var run = await CreateClient().SetPayrollRunThirteenthMonthAsync(runId, include);

        run!.IncludesThirteenthMonth.Should().Be(include);
        _api.RequestBodies.Single().Should().Be(body);
    }

    [Fact]
    public async Task SetPayrollRunThirteenthMonth_ThrowsTheApisReason_WhenRefused()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Put, $"/api/payroll-runs/{runId}/thirteenth-month", HttpStatusCode.BadRequest,
            """{"title":"Business rule violation","status":400,"detail":"A paid payroll run can't be changed."}""");

        var act = () => CreateClient().SetPayrollRunThirteenthMonthAsync(runId, true);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("A paid payroll run can't be changed.");
    }

    [Fact]
    public async Task GetPayslip_ReturnsThePdfBytes_OnSuccess()
    {
        var runId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pdf = "%PDF-1.7"u8.ToArray();
        _api.On(HttpMethod.Get, $"/api/reports/payslip/{runId}/{employeeId}", () =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pdf) });

        (await CreateClient().GetPayslipAsync(runId, employeeId)).Should().Equal(pdf);
    }

    [Fact]
    public async Task PdfDownloads_CarryTheApisExplanation_WhenRefused()
    {
        // These used to return null on any failure, which left the page nothing to say but
        // "Please try again" - even when trying again could never work.
        var runId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string reason = """{"detail":"Payslips are released once the run is paid."}""";
        _api.On(HttpMethod.Get, $"/api/reports/payslip/{runId}/{employeeId}", HttpStatusCode.Conflict, reason)
            .On(HttpMethod.Get, $"/api/reports/payslips/{runId}", HttpStatusCode.Conflict, reason)
            .On(HttpMethod.Get, $"/api/reports/my-payslip/{runId}", HttpStatusCode.Conflict, reason)
            .On(HttpMethod.Post, $"/api/reports/2316/generate/{employeeId}?year=2025", HttpStatusCode.Conflict, reason);
        var client = CreateClient();

        foreach (var download in new Func<Task>[]
                 {
                     () => client.GetPayslipAsync(runId, employeeId),
                     () => client.GetRunPayslipsAsync(runId),
                     () => client.GetMyPayslipAsync(runId),
                     () => client.GenerateBir2316Async(employeeId, 2025, new { })
                 })
        {
            var thrown = await download.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("Payslips are released once the run is paid.");
            thrown.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task GetBir2316Preview_ReturnsNull_WhenTheEmployeeHasNoPaidRunsThatYear()
    {
        var employeeId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/reports/2316/preview/{employeeId}?year=2025", HttpStatusCode.NotFound);

        (await CreateClient().GetBir2316PreviewAsync(employeeId, 2025)).Should().BeNull();
    }

    [Fact]
    public async Task GenerateAllBir2316_ReturnsNullForNoPaidRuns_ButThrowsOnRealFailures()
    {
        _api.On(HttpMethod.Post, "/api/reports/2316/generate-all?year=2024", HttpStatusCode.NotFound)
            .On(HttpMethod.Post, "/api/reports/2316/generate-all?year=2025", HttpStatusCode.InternalServerError);
        var client = CreateClient();

        (await client.GenerateAllBir2316Async(2024)).Should().BeNull();

        var act = () => client.GenerateAllBir2316Async(2025);
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Failed to generate 2316s (500).");
    }

    [Fact]
    public async Task HeadcountAnalytics_FormatsDatesAsIso()
    {
        // DateOnly's default ToString follows the current culture; the API only accepts yyyy-MM-dd.
        const string body = """{"period":{"from":"2025-01-01","to":"2025-12-31"},"data":[],"generatedAt":"2025-12-31T00:00:00Z"}""";
        _api.On(HttpMethod.Get, "/api/analytics/hr/headcount?from=2025-01-01&to=2025-12-31", HttpStatusCode.OK, body);

        var result = await CreateClient().GetHeadcountAnalyticsAsync(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        result.Should().NotBeNull();
        result!.Period.To.Should().Be(new DateOnly(2025, 12, 31));
    }
}
