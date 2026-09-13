using System.Net;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>
/// The filters the HR list pages send. The API ignores a query parameter it does not recognise,
/// so a misspelt one would not fail - it would quietly return the unfiltered list. The stub
/// answers anything it was not told about with a 404, which the client throws on, so each call
/// succeeding is the proof that it asked for exactly that URL.
/// </summary>
public class ApiClientHrQueryTests
{
    private const string Empty = """{"items":[],"totalCount":0,"page":1,"pageSize":20,"totalPages":0}""";

    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    [Fact]
    public async Task GetEmployees_SendsNoFilters_WhenNoneAreGiven()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=2&pageSize=20", HttpStatusCode.OK, Empty);

        (await CreateClient().GetEmployeesAsync(2)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task GetEmployees_FiltersBySearchAndActiveFlag(bool isActive, string sent)
    {
        _api.On(HttpMethod.Get, $"/api/employees?page=1&pageSize=20&search=santos&isActive={sent}", HttpStatusCode.OK, Empty);

        (await CreateClient().GetEmployeesAsync(search: "santos", isActive: isActive)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetEmployees_EscapesTheSearchText()
    {
        // Unescaped, "&" would end the search and start a parameter of its own.
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=20&search=Dela%20Cruz%20%26%20Co", HttpStatusCode.OK, Empty);

        (await CreateClient().GetEmployeesAsync(search: "Dela Cruz & Co")).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetEmployees_TreatsBlankSearchAsNoSearch(string search)
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=20", HttpStatusCode.OK, Empty);

        (await CreateClient().GetEmployeesAsync(search: search)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetLeaveRequests_FiltersByStatus_AlongsideTheEmployee()
    {
        var employeeId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/leave-requests?page=1&pageSize=1&employeeId={employeeId}&status=Pending", HttpStatusCode.OK, Empty);

        (await CreateClient().GetLeaveRequestsAsync(employeeId, pageSize: 1, status: "Pending")).Should().NotBeNull();
    }

    [Fact]
    public async Task GetLeaveRequests_SendsNoStatus_WhenItIsEmpty()
    {
        _api.On(HttpMethod.Get, "/api/leave-requests?page=1&pageSize=50", HttpStatusCode.OK, Empty);

        (await CreateClient().GetLeaveRequestsAsync(pageSize: 50, status: "")).Should().NotBeNull();
    }
}
