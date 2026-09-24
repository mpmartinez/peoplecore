using System.Net;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>
/// The client's copies of the leave-type and accrual-policy records. A field the copy names
/// differently, or leaves out, doesn't fail anything: it deserialises to its default, or is never
/// sent, and an update (a full replacement) then resets it on the server. So these pin every field,
/// by the API's own names and in the API's own order, with the enums travelling as their names.
/// </summary>
public class ApiClientLeaveTypesTests
{
    private static readonly Guid TypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid PolicyId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    // PeopleCore.Application.Leave.DTOs.CreateLeaveTypeDto, parameter by parameter.
    private static readonly string[] CreateLeaveTypeFields =
    [
        "name", "code", "maxDaysPerYear",
        "isPaid", "isCarryOver", "carryOverMaxDays",
        "genderRestriction", "requiresDocument",
        "isConvertibleToCash", "countsAsVacationForDeMinimis",
        "isActive", "entitlementKind",
        "countsCalendarDays", "daysPerEvent", "minServiceMonths",
        "requiresMarried", "requiresSoloParentId", "maxEvents",
        "isConfidential", "isMaternity",
    ];

    // PeopleCore.Application.Leave.DTOs.LeaveTypeDto, parameter by parameter.
    private static readonly string[] LeaveTypeFields =
    [
        "id", "name", "code",
        "maxDaysPerYear", "isPaid", "isCarryOver",
        "carryOverMaxDays", "genderRestriction",
        "requiresDocument", "isActive",
        "isConvertibleToCash", "countsAsVacationForDeMinimis",
        "entitlementKind", "countsCalendarDays",
        "daysPerEvent", "minServiceMonths",
        "requiresMarried", "requiresSoloParentId",
        "maxEvents", "isConfidential", "isMaternity",
    ];

    private const string PaternityJson = """
        {"id":"aaaaaaaa-0000-0000-0000-000000000001","name":"Paternity Leave","code":"PL",
         "maxDaysPerYear":1.5,"isPaid":true,"isCarryOver":true,
         "carryOverMaxDays":2.5,"genderRestriction":"Male",
         "requiresDocument":true,"isActive":false,
         "isConvertibleToCash":true,"countsAsVacationForDeMinimis":false,
         "entitlementKind":"PerEvent","countsCalendarDays":true,
         "daysPerEvent":7,"minServiceMonths":6,
         "requiresMarried":true,"requiresSoloParentId":true,
         "maxEvents":4,"isConfidential":true,"isMaternity":true}
        """;

    private static List<string> PropertyNames(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();

    [Fact]
    public void TheLeaveTypeFixture_HasEveryApiField_InOrder()
    {
        // Guards the fixture itself, so the read test below really does cover every field.
        PropertyNames(PaternityJson).Should().Equal(LeaveTypeFields);
    }

    [Fact]
    public async Task GetLeaveTypes_ReadsEveryField()
    {
        _api.On(HttpMethod.Get, "/api/leave-types", HttpStatusCode.OK, $"[{PaternityJson}]");

        var type = (await CreateClient().GetLeaveTypesAsync())!.Single();

        type.Should().Be(new LeaveTypeDto(
            TypeId, "Paternity Leave", "PL",
            1.5m, true, true,
            2.5m, "Male",
            true, false,
            true, false,
            LeaveEntitlementKind.PerEvent, true,
            7m, 6,
            true, true,
            4, true, true));
    }

    [Fact]
    public async Task CreateLeaveType_SendsEveryField_ByTheApisNames_InTheApisOrder_WithTheKindAsItsName()
    {
        _api.On(HttpMethod.Post, "/api/leave-types", HttpStatusCode.Created, PaternityJson);

        var created = await CreateClient().CreateLeaveTypeAsync(new CreateLeaveTypeDto(
            "Paternity Leave", "PL", 0m,
            true, false, null,
            "Male", true,
            false, false,
            true, LeaveEntitlementKind.PerEvent,
            false, 7m, null,
            true, false, 4,
            false, false));

        created!.Code.Should().Be("PL");
        var body = _api.RequestBodies.Single()!;
        PropertyNames(body).Should().Equal(CreateLeaveTypeFields);
        var root = JsonDocument.Parse(body).RootElement;
        root.GetProperty("entitlementKind").GetString().Should().Be("PerEvent");
        root.GetProperty("daysPerEvent").GetDecimal().Should().Be(7m);
        root.GetProperty("maxEvents").GetInt32().Should().Be(4);
        root.GetProperty("minServiceMonths").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateLeaveType_PutsTheFullReplacement_ToTheTypesAddress()
    {
        _api.On(HttpMethod.Put, $"/api/leave-types/{TypeId}", HttpStatusCode.OK, PaternityJson);

        await CreateClient().UpdateLeaveTypeAsync(TypeId, new CreateLeaveTypeDto(
            "Paternity Leave", "PL", 0m, true, false, null, "Male", true, false, false,
            true, LeaveEntitlementKind.PerEvent, false, 7m, null, true, false, 4, false, false));

        PropertyNames(_api.RequestBodies.Single()!).Should().Equal(CreateLeaveTypeFields);
    }

    [Fact]
    public async Task DeleteLeaveType_ThrowsTheApisReason_WhenTheTypeHasBeenUsed()
    {
        _api.On(HttpMethod.Delete, $"/api/leave-types/{TypeId}", HttpStatusCode.BadRequest,
            """{"title":"Business rule violation","status":400,"detail":"Paternity Leave has been used; deactivate it instead."}""");

        var act = () => CreateClient().DeleteLeaveTypeAsync(TypeId);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Paternity Leave has been used; deactivate it instead.");
    }

    [Fact]
    public async Task AddStatutoryLeaveTypes_PostsNoBody_AndReadsWhatWasAddedAndSkipped()
    {
        _api.On(HttpMethod.Post, "/api/leave-types/statutory", HttpStatusCode.OK,
            """{"added":["SIL","ML"],"skipped":["PL"]}""");

        var result = await CreateClient().AddStatutoryLeaveTypesAsync();

        result!.Added.Should().Equal("SIL", "ML");
        result.Skipped.Should().Equal("PL");
        _api.RequestBodies.Single().Should().BeNull();
    }

    [Fact]
    public async Task GetAccrualPolicies_AsksForTheTypesPolicies_AndReadsEveryField()
    {
        _api.On(HttpMethod.Get, $"/api/leave-accrual-policies?leaveTypeId={TypeId}", HttpStatusCode.OK,
            $$"""
            [{"id":"{{PolicyId}}","leaveTypeId":"{{TypeId}}","leaveTypeName":"Service Incentive Leave",
              "tenureMonthsMin":12,"tenureMonthsMax":60,"daysPerYear":5,"accrualFrequency":"Monthly","isActive":false}]
            """);

        var policy = (await CreateClient().GetAccrualPoliciesAsync(TypeId))!.Single();

        policy.Should().Be(new LeaveAccrualPolicyDto(PolicyId, TypeId, "Service Incentive Leave", 12, 60, 5m, "Monthly", false));
    }

    [Fact]
    public async Task CreateAccrualPolicy_SendsTheRequestsFields_InOrder_WithTheFrequencyAsItsName()
    {
        _api.On(HttpMethod.Post, "/api/leave-accrual-policies", HttpStatusCode.Created,
            $$"""
            {"id":"{{PolicyId}}","leaveTypeId":"{{TypeId}}","leaveTypeName":"SIL","tenureMonthsMin":0,
             "tenureMonthsMax":null,"daysPerYear":3,"accrualFrequency":"Annual","isActive":true}
            """);

        await CreateClient().CreateAccrualPolicyAsync(new CreateLeaveAccrualPolicyRequest(TypeId, 0, null, 3m, AccrualFrequency.Annual));

        var body = _api.RequestBodies.Single()!;
        PropertyNames(body).Should().Equal("leaveTypeId", "tenureMonthsMin", "tenureMonthsMax", "daysPerYear", "accrualFrequency");
        JsonDocument.Parse(body).RootElement.GetProperty("accrualFrequency").GetString().Should().Be("Annual");
    }

    [Fact]
    public async Task UpdateAndDeleteAccrualPolicy_UseThePoliciesAddress_AndExpectNoBodyBack()
    {
        _api.On(HttpMethod.Put, $"/api/leave-accrual-policies/{PolicyId}", HttpStatusCode.NoContent);
        _api.On(HttpMethod.Delete, $"/api/leave-accrual-policies/{PolicyId}", HttpStatusCode.NoContent);
        var client = CreateClient();

        await client.UpdateAccrualPolicyAsync(PolicyId, new CreateLeaveAccrualPolicyRequest(TypeId, 12, null, 5m, AccrualFrequency.Monthly));
        await client.DeleteAccrualPolicyAsync(PolicyId);

        _api.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Put, HttpMethod.Delete);
    }
}
