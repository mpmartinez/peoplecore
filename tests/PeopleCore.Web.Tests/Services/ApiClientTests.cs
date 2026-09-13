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

    [Fact]
    public async Task GetPayslip_ReturnsThePdfBytes_OnSuccess_AndNull_OnFailure()
    {
        var runId = Guid.NewGuid();
        var found = Guid.NewGuid();
        var pdf = "%PDF-1.7"u8.ToArray();
        _api.On(HttpMethod.Get, $"/api/reports/payslip/{runId}/{found}", () =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pdf) });
        var client = CreateClient();

        (await client.GetPayslipAsync(runId, found)).Should().Equal(pdf);
        (await client.GetPayslipAsync(runId, Guid.NewGuid())).Should().BeNull();
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
