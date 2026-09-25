using System.Net;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>
/// The client's copies of the leave request, balance and filing-option records, and of the employee
/// records the employee form reads and sends. A field named differently here deserialises silently
/// to its default, or is never sent - and an employee update is a full replacement, so a field left
/// out is cleared on the server. These pin every field, by the API's own names and in its order.
/// </summary>
public class ApiClientLeaveFilingTests
{
    private static readonly Guid RequestId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid TypeId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid ApproverId = Guid.Parse("cccccccc-0000-0000-0000-000000000004");

    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    private static List<string> PropertyNames(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();

    // PeopleCore.Application.Leave.DTOs.LeaveRequestDto, parameter by parameter.
    private const string MaternityRequestJson = """
        {"id":"cccccccc-0000-0000-0000-000000000001","employeeId":"cccccccc-0000-0000-0000-000000000002","employeeName":"Maria Santos",
         "leaveTypeId":"cccccccc-0000-0000-0000-000000000003","leaveTypeName":"Maternity Leave",
         "startDate":"2026-10-05","endDate":"2027-01-10",
         "totalDays":98,"reason":"Due date",
         "status":"Approved","approvedBy":"cccccccc-0000-0000-0000-000000000004","approvedAt":"2026-09-21T02:00:00Z",
         "rejectionReason":null,"createdAt":"2026-09-20T01:00:00Z",
         "maternityCase":"LiveBirth","daysAllocatedToFather":7,
         "hasDocument":true,"documentFileName":"medical-certificate.pdf",
         "isConfidential":false}
        """;

    [Fact]
    public async Task LeaveRequests_ReadEveryField_WithTheMaternityCaseByName()
    {
        _api.On(HttpMethod.Get, "/api/leave-requests?page=1&pageSize=20", HttpStatusCode.OK,
            $$"""{"items":[{{MaternityRequestJson}}],"totalCount":1,"page":1,"pageSize":20,"totalPages":1}""");

        var request = (await CreateClient().GetLeaveRequestsAsync())!.Items.Single();

        request.Should().Be(new LeaveRequestDto(
            RequestId, EmployeeId, "Maria Santos",
            TypeId, "Maternity Leave",
            "2026-10-05", "2027-01-10",
            98m, "Due date",
            "Approved", ApproverId, new DateTime(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc),
            null, new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc),
            MaternityCase.LiveBirth, 7,
            true, "medical-certificate.pdf",
            false));
        request.IsMasked.Should().BeFalse();
    }

    [Fact]
    public async Task ARequestMaskedByTheApi_ReadsAsMasked()
    {
        _api.On(HttpMethod.Get, "/api/leave-requests?page=1&pageSize=20", HttpStatusCode.OK,
            """
            {"items":[{"id":"cccccccc-0000-0000-0000-000000000001","employeeId":"cccccccc-0000-0000-0000-000000000002","employeeName":"Maria Santos",
             "leaveTypeId":"00000000-0000-0000-0000-000000000000","leaveTypeName":"Leave","startDate":"2026-10-05","endDate":"2026-10-07",
             "totalDays":3,"reason":null,"status":"Pending","approvedBy":null,"approvedAt":null,"rejectionReason":null,
             "createdAt":"2026-09-20T01:00:00Z","maternityCase":null,"daysAllocatedToFather":0,"hasDocument":false,
             "documentFileName":null,"isConfidential":false}],"totalCount":1,"page":1,"pageSize":20,"totalPages":1}
            """);

        (await CreateClient().GetLeaveRequestsAsync())!.Items.Single().IsMasked.Should().BeTrue();
    }

    [Fact]
    public async Task LeaveBalances_ReadEveryField()
    {
        _api.On(HttpMethod.Get, $"/api/leave-balances/{EmployeeId}", HttpStatusCode.OK,
            $$"""
            [{"id":"{{RequestId}}","employeeId":"{{EmployeeId}}","employeeName":"Maria Santos",
              "leaveTypeId":"{{TypeId}}","leaveTypeName":"VAWC Leave",
              "year":2026,"totalDays":10,"usedDays":2,
              "carriedOverDays":0,"remainingDays":8,
              "isConfidential":true}]
            """);

        var balance = (await CreateClient().GetLeaveBalancesAsync(EmployeeId))!.Single();

        balance.Should().Be(new LeaveBalanceDto(RequestId, EmployeeId, "Maria Santos", TypeId, "VAWC Leave", 2026, 10m, 2m, 0m, 8m, true));
    }

    [Fact]
    public async Task FilingOptions_ReadEveryField_WithTheKindByName()
    {
        _api.On(HttpMethod.Get, "/api/leave-requests/options", HttpStatusCode.OK,
            $$"""
            [{"leaveTypeId":"{{TypeId}}","name":"Maternity Leave","code":"ML","kind":"PerEvent",
              "countsCalendarDays":true,"requiresDocument":true,"isMaternity":true,
              "daysLeftThisYear":null,
              "daysPerEvent":105,
              "maxEvents":2,"eventsUsed":1,
              "hasSoloParentBonus":true}]
            """);

        var option = (await CreateClient().GetLeaveFilingOptionsAsync())!.Single();

        option.Should().Be(new LeaveFilingOptionDto(
            TypeId, "Maternity Leave", "ML", LeaveEntitlementKind.PerEvent,
            true, true, true, null, 105m, 2, 1, true));
    }

    [Fact]
    public async Task FilingALeaveRequest_SendsEveryField_InTheApisOrder_WithTheMaternityCaseAsItsName()
    {
        _api.On(HttpMethod.Post, "/api/leave-requests", HttpStatusCode.Created, MaternityRequestJson);

        var filed = await CreateClient().CreateLeaveRequestAsync(new CreateLeaveRequestDto(
            EmployeeId, TypeId, new DateOnly(2026, 10, 5), new DateOnly(2027, 1, 10), "Due date",
            MaternityCase.MiscarriageOrEmergencyTermination, 0));

        filed!.Id.Should().Be(RequestId);
        var body = _api.RequestBodies.Single()!;
        PropertyNames(body).Should().Equal(
            "employeeId", "leaveTypeId", "startDate", "endDate", "reason", "maternityCase", "daysAllocatedToFather");
        var root = JsonDocument.Parse(body).RootElement;
        root.GetProperty("maternityCase").GetString().Should().Be("MiscarriageOrEmergencyTermination");
        root.GetProperty("startDate").GetString().Should().Be("2026-10-05");
    }

    [Fact]
    public async Task UploadingADocument_PutsItAsTheFormsFileField()
    {
        _api.On(HttpMethod.Put, $"/api/leave-requests/{RequestId}/document", HttpStatusCode.OK, MaternityRequestJson);

        var updated = await CreateClient().UploadLeaveDocumentAsync(RequestId, [1, 2, 3], "scan.png", "image/png");

        updated!.HasDocument.Should().BeTrue();
        var request = _api.Requests.Single();
        request.Content!.Headers.ContentType!.MediaType.Should().Be("multipart/form-data");
        _api.RequestBodies.Single().Should().Contain("name=file").And.Contain("filename=scan.png").And.Contain("Content-Type: image/png");
    }

    [Fact]
    public async Task AnUploadTheApiRefuses_ThrowsItsReason()
    {
        _api.On(HttpMethod.Put, $"/api/leave-requests/{RequestId}/document", HttpStatusCode.BadRequest,
            """{"title":"Business Rule Violation","detail":"You can only attach documents to your own leave requests.","status":400}""");

        var act = () => CreateClient().UploadLeaveDocumentAsync(RequestId, [1], "scan.pdf", "application/pdf");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Be("You can only attach documents to your own leave requests.");
    }

    [Fact]
    public async Task AnUploadThatNeverReachedTheServer_SaysSo_RatherThanBlamingTheFile()
    {
        // The page refuses a file over 10 MB before sending it, so a failure with no response is
        // the connection, not the file.
        _api.On(HttpMethod.Put, $"/api/leave-requests/{RequestId}/document", () => throw new HttpRequestException("Connection reset."));

        var act = () => CreateClient().UploadLeaveDocumentAsync(RequestId, [1], "scan.pdf", "application/pdf");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Be("The upload didn't reach the server. Check your connection and try again.");
    }

    [Fact]
    public async Task TheDocumentLink_IsReadFromTheApisUrlField()
    {
        _api.On(HttpMethod.Get, $"/api/leave-requests/{RequestId}/document", HttpStatusCode.OK,
            """{"url":"https://files.test/doc.pdf?sig=1"}""");

        (await CreateClient().GetLeaveDocumentUrlAsync(RequestId)).Should().Be("https://files.test/doc.pdf?sig=1");
    }

    // PeopleCore.Application.Employees.DTOs.EmployeeDto, parameter by parameter.
    private const string EmployeeJson = """
        {"id":"cccccccc-0000-0000-0000-000000000002","employeeNumber":"EMP-0042","firstName":"Maria","middleName":"Luna","lastName":"Santos",
         "fullName":"Maria Luna Santos","dateOfBirth":"1990-05-01","gender":"Female","civilStatus":"Single",
         "workEmail":"maria@company.test","mobileNumber":"0917",
         "departmentId":null,"departmentName":null,"positionId":null,"positionTitle":null,
         "reportingManagerId":null,"reportingManagerName":null,"teamId":null,
         "employmentStatus":"Regular","employmentType":"Regular","hireDate":"2020-02-03","regularizationDate":null,
         "isActive":true,"is13thMonthEligible":true,"separationDate":null,
         "soloParentIdNumber":"SP-1","soloParentIdValidUntil":"2027-01-31",
         "personalEmail":"maria@home.test","address":"12 Mabini Street"}
        """;

    [Fact]
    public async Task TheEmployeeRecord_ReadsEveryField()
    {
        _api.On(HttpMethod.Get, $"/api/employees/{EmployeeId}", HttpStatusCode.OK, EmployeeJson);

        var employee = await CreateClient().GetEmployeeRecordAsync(EmployeeId);

        employee.Should().Be(new EmployeeDto(
            EmployeeId, "EMP-0042", "Maria", "Luna", "Santos", "Maria Luna Santos",
            new DateOnly(1990, 5, 1), "Female", "Single", "maria@company.test", "0917",
            null, null, null, null, null, null, null,
            "Regular", "Regular", new DateOnly(2020, 2, 3), null,
            true, true, null,
            "SP-1", new DateOnly(2027, 1, 31),
            "maria@home.test", "12 Mabini Street"));
    }

    [Fact]
    public async Task CreatingAnEmployee_SendsEveryField_InTheApisOrder()
    {
        _api.On(HttpMethod.Post, "/api/employees", HttpStatusCode.Created, EmployeeJson);

        await CreateClient().CreateEmployeeAsync(new CreateEmployeeDto(
            "EMP-0042", "Maria", null, "Santos", new DateOnly(1990, 5, 1), "Female", "maria@company.test", null,
            null, null, null, "Probationary", "Regular", new DateOnly(2026, 9, 1), "SP-1", new DateOnly(2027, 1, 31),
            "Married"));

        PropertyNames(_api.RequestBodies.Single()!).Should().Equal(
            "employeeNumber", "firstName", "middleName", "lastName", "dateOfBirth", "gender", "workEmail", "mobileNumber",
            "departmentId", "positionId", "reportingManagerId", "employmentStatus", "employmentType", "hireDate",
            "soloParentIdNumber", "soloParentIdValidUntil", "civilStatus");
    }

    [Fact]
    public async Task UpdatingAnEmployee_PutsEveryField_InTheApisOrder()
    {
        _api.On(HttpMethod.Put, $"/api/employees/{EmployeeId}", HttpStatusCode.OK, EmployeeJson);

        await CreateClient().UpdateEmployeeAsync(EmployeeId, new UpdateEmployeeDto(
            "Maria", null, "Santos", "Single", null, null, null, null, null, null, null,
            "Regular", null, true, null, null));

        PropertyNames(_api.RequestBodies.Single()!).Should().Equal(
            "firstName", "middleName", "lastName", "civilStatus", "personalEmail", "mobileNumber", "address",
            "departmentId", "positionId", "teamId", "reportingManagerId", "employmentStatus", "regularizationDate",
            "is13thMonthEligible", "soloParentIdNumber", "soloParentIdValidUntil");
    }
}
