using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>
/// The client's copies of the maternity-claim, payroll-settings and maternity run records. A field
/// the copy names differently, or leaves out, doesn't fail anything: it deserialises to its default,
/// or is never sent - and the settings PUT is a full replacement, so a missing field would reset the
/// setting on the server. These pin every field, by the API's own names and in its own order.
/// </summary>
public class ApiClientMaternityTests
{
    private static readonly Guid ClaimId = Guid.Parse("c1a1c1a1-0000-0000-0000-000000000001");
    private static readonly Guid LeaveId = Guid.Parse("1ea0e000-0000-0000-0000-000000000001");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");
    private static readonly Guid RunId = Guid.Parse("5d2c8e61-9f4a-4b37-8c15-a7e3b0d92f48");
    private static readonly Guid CompanyId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000001");

    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    // PeopleCore.Application.Payroll.Maternity.MaternityDtos, parameter by parameter.
    private static readonly string[] ClaimFields =
    [
        "id", "leaveRequestId", "employeeId", "employeeName",
        "leaveStart", "leaveEnd", "days", "dailyAllowance", "benefit",
        "status", "advanceRunId", "advanceRunNumber", "advancedAt",
        "reimbursedOn", "reimbursedAmount", "note", "carriedByRunNumber", "leaveCancelled", "nettedByRunNumber",
        "warning",
    ];

    private static readonly string[] SuggestionFields = ["dailyAllowance", "monthsFound", "windowFrom", "windowTo", "ratesOverridden"];

    private static readonly string[] ReimburseFields = ["reimbursedOn", "reimbursedAmount", "note"];

    private static readonly string[] EligibleFields = ["leaveRequestId", "employeeId", "employeeName", "startDate", "endDate", "days"];

    // PeopleCore.Application.Payroll.DTOs.PayrollSettingsDto, parameter by parameter.
    private static readonly string[] SettingsFields =
    [
        "companyId", "philHealthRate", "philHealthMinShare", "philHealthMaxShare",
        "pagIbigEmployeeRate", "pagIbigLowEmployeeRate", "pagIbigLowRateThreshold",
        "pagIbigEmployerRate", "pagIbigMaxFundSalary", "dailyRateFactor",
        "sssEmployeeRate", "sssEmployerRate", "exemptFromMaternityDifferential",
    ];

    private static string ClaimJson => $$"""
        {"id":"{{ClaimId}}","leaveRequestId":"{{LeaveId}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos",
         "leaveStart":"2026-08-10","leaveEnd":"2026-11-22","days":105,"dailyAllowance":666.67,"benefit":70000.35,
         "status":"Reimbursed","advanceRunId":"{{RunId}}","advanceRunNumber":"PR-2026-0015","advancedAt":"2026-08-05",
         "reimbursedOn":"2026-10-01","reimbursedAmount":70000,"note":"SSS rounded down",
         "carriedByRunNumber":"PR-2026-0016","leaveCancelled":true,"nettedByRunNumber":"PR-2026-0014",
         "warning":"Check the days"}
        """;

    private const string SettingsJson = """
        {"companyId":"c0c0c0c0-0000-0000-0000-000000000001","philHealthRate":0.05,"philHealthMinShare":500,"philHealthMaxShare":5000,
         "pagIbigEmployeeRate":0.02,"pagIbigLowEmployeeRate":0.01,"pagIbigLowRateThreshold":1500,
         "pagIbigEmployerRate":0.02,"pagIbigMaxFundSalary":10000,"dailyRateFactor":261,
         "sssEmployeeRate":null,"sssEmployerRate":0.1,"exemptFromMaternityDifferential":true}
        """;

    private static List<string> PropertyNames(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();

    /// <summary>The record's positional members, camelCased as they travel - that is the mirror's own order.</summary>
    private static List<string> MembersOf<T>() =>
        typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single(c => c.GetParameters() is not [{ } only] || only.ParameterType != typeof(T))
            .GetParameters().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!)).ToList();

    [Fact]
    public void TheMirrors_HaveEveryApiField_InTheApisOrder()
    {
        MembersOf<MaternityClaimDto>().Should().Equal(ClaimFields);
        MembersOf<SuggestedAllowanceDto>().Should().Equal(SuggestionFields);
        MembersOf<SetAllowanceRequest>().Should().Equal("dailyAllowance");
        MembersOf<ReimburseRequest>().Should().Equal(ReimburseFields);
        MembersOf<DenyRequest>().Should().Equal("note");
        MembersOf<VoidRequest>().Should().Equal("note");
        MembersOf<RelinkRequest>().Should().Equal("leaveRequestId");
        MembersOf<NotQualifiedRequest>().Should().Equal("note");
        MembersOf<MaternityClaimsSummaryDto>().Should().Equal("claims", "outstanding");
        MembersOf<EligibleMaternityLeaveDto>().Should().Equal(EligibleFields);
        MembersOf<PayrollSettingsDto>().Should().Equal(SettingsFields);
        Enum.GetNames<MaternityClaimStatus>().Should().Equal("Draft", "Advanced", "Reimbursed", "Denied", "Voided", "NotQualified");
    }

    [Fact]
    public void TheFinalPayMirrors_EndWithTheMaternityFields()
    {
        MembersOf<FinalPayRequest>().TakeLast(2).Should().Equal("deductions", "advanceMaternityBenefit");
        MembersOf<FinalPaySummaryDto>().TakeLast(10).Should().Equal(
            "clearanceComplete", "outstandingClearance",
            "advanceMaternityBenefit", "maternityBenefitAdvance", "maternityBenefitOffset", "maternityDifferential",
            "contributionsDeferred", "deferredContributionsCollected", "maternityWarnings", "deferredContributionsUncollected");
    }

    [Fact]
    public void TheRunMirrors_EndWithTheMaternityFigures_AndTheWarnings()
    {
        // PayrollRunEmployeeDto's maternity members and PayrollRunDto's last one, as the API has them.
        MembersOf<PayrollRunEmployeeDto>().TakeLast(10).Should().Equal(
            "leaveConversionPay", "leaveConversionNonTaxable", "separationPay", "retirementPay", "finalPayNonTaxable",
            "maternityBenefitAdvance", "maternityBenefitOffset", "maternityDifferential",
            "contributionsDeferred", "deferredContributionsCollected");
        MembersOf<PayrollRunDto>().TakeLast(4).Should().Equal(
            "runType", "includesLeaveConversion", "includesThirteenthMonth", "warnings");
    }

    [Fact]
    public void TheClaimFixture_HasEveryApiField_InOrder() => PropertyNames(ClaimJson).Should().Equal(ClaimFields);

    [Fact]
    public async Task GetMaternityClaims_ReadsEveryField_AndTheOutstandingTotal()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims", HttpStatusCode.OK, $$"""{"claims":[{{ClaimJson}}],"outstanding":84583.8}""");

        var summary = await CreateClient().GetMaternityClaimsAsync();

        summary!.Outstanding.Should().Be(84583.8m);
        summary.Claims.Single().Should().Be(new MaternityClaimDto(
            ClaimId, LeaveId, MariaId, "Maria Santos",
            new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22), 105m, 666.67m, 70000.35m,
            MaternityClaimStatus.Reimbursed, RunId, "PR-2026-0015", new DateOnly(2026, 8, 5),
            new DateOnly(2026, 10, 1), 70000m, "SSS rounded down", "PR-2026-0016", true, "PR-2026-0014", "Check the days"));
    }

    [Fact]
    public async Task GetEligibleMaternityLeave_ReadsEveryField()
    {
        var json = $$"""[{"leaveRequestId":"{{LeaveId}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","startDate":"2026-08-10","endDate":"2026-11-22","days":105}]""";
        PropertyNames(json[1..^1]).Should().Equal(EligibleFields);
        _api.On(HttpMethod.Get, "/api/maternity-claims/eligible", HttpStatusCode.OK, json);

        var eligible = await CreateClient().GetEligibleMaternityLeaveAsync();

        eligible!.Single().Should().Be(new EligibleMaternityLeaveDto(
            LeaveId, MariaId, "Maria Santos", new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22), 105m));
    }

    [Fact]
    public async Task GetMaternityReadyEmployeeIds_ReadsTheIds()
    {
        _api.On(HttpMethod.Get, "/api/maternity-claims/ready", HttpStatusCode.OK, $$"""["{{MariaId}}"]""");

        (await CreateClient().GetMaternityReadyEmployeeIdsAsync()).Should().Equal(MariaId);
    }

    [Fact]
    public async Task CreateMaternityClaim_PostsToTheLeaveRequestsAddress_WithNoBody()
    {
        _api.On(HttpMethod.Post, $"/api/maternity-claims/{LeaveId}", HttpStatusCode.Created, ClaimJson);

        var claim = await CreateClient().CreateMaternityClaimAsync(LeaveId);

        claim!.Id.Should().Be(ClaimId);
        _api.RequestBodies.Single().Should().BeNull();
    }

    [Fact]
    public async Task CreateMaternityClaim_ThrowsTheApisReason()
    {
        _api.On(HttpMethod.Post, $"/api/maternity-claims/{LeaveId}", HttpStatusCode.BadRequest,
            """{"title":"Business rule violation","status":400,"detail":"Maria Santos already has a maternity claim for this leave."}""");

        var act = () => CreateClient().CreateMaternityClaimAsync(LeaveId);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Maria Santos already has a maternity claim for this leave.");
    }

    [Fact]
    public async Task GetMaternityAllowanceSuggestion_ReadsEveryField()
    {
        const string json = """{"dailyAllowance":null,"monthsFound":0,"windowFrom":"2025-04-01","windowTo":"2026-03-31","ratesOverridden":true}""";
        PropertyNames(json).Should().Equal(SuggestionFields);
        _api.On(HttpMethod.Get, $"/api/maternity-claims/{ClaimId}/suggestion", HttpStatusCode.OK, json);

        var suggestion = await CreateClient().GetMaternityAllowanceSuggestionAsync(ClaimId);

        suggestion.Should().Be(new SuggestedAllowanceDto(null, 0, new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31), true));
    }

    [Fact]
    public async Task SetMaternityAllowance_PutsTheAllowance()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/allowance", HttpStatusCode.OK, ClaimJson);

        await CreateClient().SetMaternityAllowanceAsync(ClaimId, 666.67m);

        _api.RequestBodies.Single().Should().Be("""{"dailyAllowance":666.67}""");
    }

    [Fact]
    public async Task ReimburseMaternityClaim_PutsEveryField_InTheApisOrder()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/reimburse", HttpStatusCode.OK, ClaimJson);

        await CreateClient().ReimburseMaternityClaimAsync(ClaimId, new ReimburseRequest(new DateOnly(2026, 10, 1), 70000m, "SSS rounded down"));

        _api.RequestBodies.Single().Should().Be("""{"reimbursedOn":"2026-10-01","reimbursedAmount":70000,"note":"SSS rounded down"}""");
    }

    [Fact]
    public async Task DenyMaternityClaim_PutsTheNote()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/deny", HttpStatusCode.OK, ClaimJson);

        await CreateClient().DenyMaternityClaimAsync(ClaimId, "No MAT-1 on file");

        _api.RequestBodies.Single().Should().Be("""{"note":"No MAT-1 on file"}""");
    }

    [Fact]
    public async Task VoidMaternityClaim_PutsTheNote()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/void", HttpStatusCode.OK, ClaimJson);

        await CreateClient().VoidMaternityClaimAsync(ClaimId, "Leave refiled");

        _api.RequestBodies.Single().Should().Be("""{"note":"Leave refiled"}""");
    }

    [Fact]
    public async Task MarkMaternityClaimNotQualified_PutsTheNote()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/not-qualified", HttpStatusCode.OK, ClaimJson);

        await CreateClient().MarkMaternityClaimNotQualifiedAsync(ClaimId, "No contributions");

        _api.RequestBodies.Single().Should().Be("""{"note":"No contributions"}""");
    }

    [Fact]
    public async Task ReopenMaternityClaim_PutsNothing()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/reopen", HttpStatusCode.OK, ClaimJson);

        (await CreateClient().ReopenMaternityClaimAsync(ClaimId))!.Id.Should().Be(ClaimId);

        _api.RequestBodies.Single().Should().BeNull();
    }

    [Fact]
    public async Task RelinkMaternityClaim_PutsTheLeaveRequest()
    {
        _api.On(HttpMethod.Put, $"/api/maternity-claims/{ClaimId}/relink", HttpStatusCode.OK, ClaimJson);

        await CreateClient().RelinkMaternityClaimAsync(ClaimId, LeaveId);

        _api.RequestBodies.Single().Should().Be($$"""{"leaveRequestId":"{{LeaveId}}"}""");
    }

    [Fact]
    public void TheSettingsFixture_HasEveryApiField_InOrder() => PropertyNames(SettingsJson).Should().Equal(SettingsFields);

    [Fact]
    public async Task GetPayrollSettings_ReadsEveryField()
    {
        // The row payroll computes from, whatever company it belongs to.
        _api.On(HttpMethod.Get, "/api/payroll-settings/default", HttpStatusCode.OK, SettingsJson);

        var settings = await CreateClient().GetPayrollSettingsAsync();

        settings.Should().Be(new PayrollSettingsDto(CompanyId, 0.05m, 500m, 5000m, 0.02m, 0.01m, 1500m, 0.02m, 10000m, 261m,
            null, 0.1m, true));
    }

    [Fact]
    public async Task SavePayrollSettings_PutsTheWholeRecord_AndExpectsNoBodyBack()
    {
        _api.On(HttpMethod.Put, "/api/payroll-settings/default", HttpStatusCode.NoContent);

        await CreateClient().SavePayrollSettingsAsync(new PayrollSettingsDto(CompanyId, 0.05m, 500m, 5000m, 0.02m, 0.01m, 1500m,
            0.02m, 10000m, 261m, null, 0.1m, true));

        var body = _api.RequestBodies.Single()!;
        PropertyNames(body).Should().Equal(SettingsFields);
        JsonDocument.Parse(body).RootElement.GetProperty("exemptFromMaternityDifferential").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ARunsMaternityFigures_AndWarnings_AreRead()
    {
        _api.On(HttpMethod.Get, $"/api/payroll-runs/{RunId}", HttpStatusCode.OK, $$"""
            {"id":"{{RunId}}","runNumber":"PR-2026-0015","periodLabel":"Jul 16-31, 2026","periodStart":"2026-07-16",
             "periodEnd":"2026-07-31","payDate":"2026-08-05","frequency":"SemiMonthly","status":"Draft",
             "employeeCount":1,"totalGrossPay":0,"totalDeductions":0,"totalNetPay":0,"createdAt":"2026-07-01T00:00:00Z",
             "attendancePeriodId":null,"employeesMissingAttendance":0,
             "employees":[{"id":"{{Guid.NewGuid()}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-0042",
                           "maternityBenefitAdvance":70000.35,"maternityBenefitOffset":4000.02}],
             "runType":"Regular","includesLeaveConversion":false,"includesThirteenthMonth":false,
             "warnings":["Maternity benefit not advanced yet for Ana Cruz."]}
            """);

        var run = await CreateClient().GetPayrollRunAsync(RunId);

        run!.Employees.Single().MaternityBenefitAdvance.Should().Be(70000.35m);
        run.Employees.Single().MaternityBenefitOffset.Should().Be(4000.02m);
        run.Warnings.Should().Equal("Maternity benefit not advanced yet for Ana Cruz.");
    }
}
