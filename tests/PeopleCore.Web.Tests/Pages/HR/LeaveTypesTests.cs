using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.HR;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.HR;

/// <summary>
/// The Leave Types page: the list with readable kinds, limits and rules; the create and edit form
/// that shows only the settings a kind uses and sends every setting; an accrued type's accrual
/// rules; the Philippine statutory set; and delete, which the API refuses for a used type.
/// </summary>
public class LeaveTypesTests : BunitContext
{
    private static readonly Guid SilId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid MlId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid PlId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid VawcId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid OldId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid VlId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid PolicyId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private const string TypesPath = "/api/leave-types";
    private const string PoliciesPath = "/api/leave-accrual-policies";

    private const string MaternityNote = "Paid through regular payroll for now; the SSS benefit split comes in a later release.";

    private const string SilNote = "Unused SIL must be converted to cash at year-end (Labor Code Art. 95); PeopleCore doesn't do this automatically yet.";

    private readonly StubHttpHandler _api = new();

    public LeaveTypesTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("hr@company.test");
        auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
    }

    /// <summary>A LeaveTypeDto as the API sends it: every field, in the record's order.</summary>
    private static string LeaveType(
        Guid id, string name, string code, string kind = "Accrued", decimal maxDaysPerYear = 0m,
        bool isPaid = true, bool isCarryOver = false, decimal? carryOverMaxDays = null, string? gender = null,
        bool requiresDocument = false, bool isActive = true, bool isConvertibleToCash = false,
        bool countsAsVacationForDeMinimis = false, bool countsCalendarDays = false, decimal? daysPerEvent = null,
        int? minServiceMonths = null, bool requiresMarried = false, bool requiresSoloParentId = false,
        int? maxEvents = null, bool isConfidential = false, bool isMaternity = false) =>
        JsonSerializer.Serialize(new
        {
            id, name, code, maxDaysPerYear, isPaid, isCarryOver, carryOverMaxDays,
            genderRestriction = gender, requiresDocument, isActive, isConvertibleToCash,
            countsAsVacationForDeMinimis, entitlementKind = kind, countsCalendarDays, daysPerEvent,
            minServiceMonths, requiresMarried, requiresSoloParentId, maxEvents, isConfidential, isMaternity,
        });

    private static readonly string Sil = LeaveType(SilId, "Service Incentive Leave", "SIL", maxDaysPerYear: 5m,
        isConvertibleToCash: true, countsAsVacationForDeMinimis: true);

    private static readonly string Ml = LeaveType(MlId, "Maternity Leave", "ML", "PerEvent", daysPerEvent: 105m,
        countsCalendarDays: true, gender: "Female", requiresDocument: true, isMaternity: true);

    private static readonly string Pl = LeaveType(PlId, "Paternity Leave", "PL", "PerEvent", daysPerEvent: 7m, maxEvents: 4,
        gender: "Male", requiresMarried: true, requiresDocument: true);

    private static readonly string Vawc = LeaveType(VawcId, "VAWC Leave", "VAWC", "YearlyAllowance", maxDaysPerYear: 10m,
        gender: "Female", isConfidential: true, requiresDocument: true);

    private static readonly string Old = LeaveType(OldId, "Old Leave", "OLD", isActive: false);

    private static readonly string Vl = LeaveType(VlId, "Vacation Leave", "VL", maxDaysPerYear: 15m, isCarryOver: true,
        carryOverMaxDays: 5m, isConvertibleToCash: true, countsAsVacationForDeMinimis: true);

    private static string List(params string[] types) => $"[{string.Join(",", types)}]";

    private static string Policy(Guid id, int min, int? max, decimal days, string frequency, bool active = true) =>
        JsonSerializer.Serialize(new
        {
            id, leaveTypeId = SilId, leaveTypeName = "Service Incentive Leave", tenureMonthsMin = min,
            tenureMonthsMax = max, daysPerYear = days, accrualFrequency = frequency, isActive = active,
        });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Problem(string detail) =>
        JsonSerializer.Serialize(new { title = "Business rule violation", status = 400, detail });

    private IRenderedComponent<LeaveTypes> RenderPage(params string[] types)
    {
        _api.On(HttpMethod.Get, TypesPath, HttpStatusCode.OK, List(types));
        var cut = Render<LeaveTypes>();
        cut.WaitForAssertion(() => cut.FindAll("[data-leave-type]").Should().NotBeEmpty());
        return cut;
    }

    private static IElement Row(IRenderedComponent<LeaveTypes> cut, string code) => cut.Find($"[data-leave-type='{code}']");

    private static IElement ButtonIn(IElement scope, string text) =>
        scope.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private int Calls(HttpMethod method, string pathAndQuery) =>
        _api.Requests.Count(r => r.Method == method && r.RequestUri!.PathAndQuery == pathAndQuery);

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindLastIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"a {method} to {path} was expected");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    private static string PoliciesOf(Guid typeId) => $"{PoliciesPath}?leaveTypeId={typeId}";

    // ---- Route and access -------------------------------------------------------------------

    [Fact]
    public void ThePage_IsAtLeaveTypes_AndNeedsLeaveManage()
    {
        typeof(LeaveTypes).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).Should().Equal("/leave-types");
        typeof(LeaveTypes).GetCustomAttribute<RequirePermissionAttribute>()!.Policy.Should().Be("permission:leave.manage");
    }

    // ---- List -------------------------------------------------------------------------------

    [Fact]
    public void EachType_IsListed_WithItsKindLimitDaysStatusAndRules_InWords()
    {
        var cut = RenderPage(Sil, Ml, Pl, Vawc, Old);

        cut.Find("[data-leave-types]").Should().NotBeNull();

        var sil = Row(cut, "SIL");
        sil.TextContent.Should().Contain("Service Incentive Leave").And.Contain("SIL");
        sil.QuerySelector("[data-kind]")!.TextContent.Trim().Should().Be("Accrued");
        sil.QuerySelector("[data-limit]")!.TextContent.Trim().Should().Be("Set by accrual rules");
        sil.QuerySelector("[data-days]")!.TextContent.Trim().Should().Be("Working days");
        sil.QuerySelector("[data-status]")!.TextContent.Trim().Should().Be("Active");

        var pl = Row(cut, "PL");
        pl.QuerySelector("[data-kind]")!.TextContent.Trim().Should().Be("Per event");
        pl.QuerySelector("[data-limit]")!.TextContent.Trim().Should().Be("7 days each time, up to 4 times");
        pl.QuerySelector("[data-rules]")!.TextContent.Trim().Should().Be("Male · Married · Document");

        var ml = Row(cut, "ML");
        ml.QuerySelector("[data-limit]")!.TextContent.Trim().Should().Be("105 days a birth");
        ml.QuerySelector("[data-days]")!.TextContent.Trim().Should().Be("Calendar days");

        var vawc = Row(cut, "VAWC");
        vawc.QuerySelector("[data-kind]")!.TextContent.Trim().Should().Be("Yearly allowance");
        vawc.QuerySelector("[data-limit]")!.TextContent.Trim().Should().Be("10 days a year");
        vawc.QuerySelector("[data-rules]")!.TextContent.Trim().Should().Be("Female · Document · Confidential");

        Row(cut, "OLD").QuerySelector("[data-status]")!.TextContent.Trim().Should().Be("Inactive");

        cut.Find("[data-leave-types]").TextContent.Should().NotContain("PerEvent").And.NotContain("YearlyAllowance");
    }

    [Fact]
    public void ServiceAndSoloParentRules_AreSummarised_AndATypeWithNoRulesSaysSo()
    {
        var spl = LeaveType(Guid.NewGuid(), "Solo Parent Leave", "SPL", "YearlyAllowance", maxDaysPerYear: 7m,
            requiresSoloParentId: true, minServiceMonths: 6);
        var cut = RenderPage(spl, Sil);

        Row(cut, "SPL").QuerySelector("[data-rules]")!.TextContent.Trim().Should().Be("Solo parent ID · 6 months' service");
        Row(cut, "SIL").QuerySelector("[data-rules]")!.TextContent.Trim().Should().Be("None");
    }

    [Fact]
    public void AnAccruedType_SaysItsDaysComeFromItsAccrualRules_NotFromItsYearlyFigure_WithoutLoadingThem()
    {
        // SIL's yearly figure is 5, but nothing grants an accrued type days except its rules - and
        // the list doesn't load every type's rules just to describe them.
        var cut = RenderPage(Sil, Old);

        Row(cut, "SIL").QuerySelector("[data-limit]")!.TextContent.Trim().Should().Be("Set by accrual rules");
        Row(cut, "OLD").QuerySelector("[data-limit]")!.TextContent.Trim().Should().Be("Set by accrual rules");
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == PoliciesPath);
    }

    [Fact]
    public void AMaternityType_CarriesThePayrollNote_AndOthersDoNot()
    {
        var cut = RenderPage(Ml, Pl);

        Row(cut, "ML").QuerySelector("[data-maternity-note]")!.TextContent.Trim().Should().Be(MaternityNote);
        Row(cut, "PL").QuerySelector("[data-maternity-note]").Should().BeNull();
    }

    [Fact]
    public void SilCarriesTheYearEndCashConversionNote_AndOthersDoNot()
    {
        var cut = RenderPage(Sil, Vl, Ml);

        Row(cut, "SIL").QuerySelector("[data-sil-note]")!.TextContent.Trim().Should().Be(SilNote);
        Row(cut, "VL").QuerySelector("[data-sil-note]").Should().BeNull();
        Row(cut, "ML").QuerySelector("[data-sil-note]").Should().BeNull();
    }

    [Fact]
    public async Task SilsForm_CarriesTheYearEndCashConversionNote()
    {
        _api.On(HttpMethod.Get, PoliciesOf(SilId), HttpStatusCode.OK, "[]");
        _api.On(HttpMethod.Get, PoliciesOf(VlId), HttpStatusCode.OK, "[]");
        var cut = RenderPage(Sil, Vl);

        await ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.Find("[data-leave-type-form] [data-sil-note]").TextContent.Trim().Should().Be(SilNote);
        await ButtonIn(cut.Find("[data-leave-type-dialog]"), "Cancel").ClickAsync(new MouseEventArgs());

        await ButtonIn(Row(cut, "VL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.FindAll("[data-leave-type-form] [data-sil-note]").Should().BeEmpty();
    }

    [Fact]
    public async Task ANewTypeCodedSil_CarriesTheNoteToo()
    {
        var cut = RenderPage(Vl);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        cut.FindAll("[data-leave-type-form] [data-sil-note]").Should().BeEmpty();
        await cut.Find("#lt-code").InputAsync(new ChangeEventArgs { Value = " sil " });

        cut.Find("[data-leave-type-form] [data-sil-note]").TextContent.Trim().Should().Be(SilNote);
    }

    [Fact]
    public void AListThatFailsToLoad_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Get, TypesPath, HttpStatusCode.InternalServerError, Problem("The database is unavailable."));

        var cut = Render<LeaveTypes>();

        cut.WaitForAssertion(() => cut.Find("[data-load-error]").TextContent.Should().Contain("The database is unavailable."));
    }

    // ---- Create and edit form ---------------------------------------------------------------

    [Fact]
    public async Task TheKindPicker_ShowsOnlyTheFieldsThatKindUses()
    {
        var cut = RenderPage(Sil);
        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());

        // A new type starts as Accrued: its days come from accrual rules, added once it is saved.
        cut.FindAll("#lt-max-days").Should().BeEmpty();
        cut.FindAll("#lt-days-per-event").Should().BeEmpty();
        cut.FindAll("#lt-max-events").Should().BeEmpty();
        cut.Find("[data-accrual-policies]").TextContent.Should().Contain("Save the type first");

        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "PerEvent" });
        cut.FindAll("#lt-days-per-event").Should().ContainSingle();
        cut.FindAll("#lt-max-events").Should().ContainSingle();
        cut.FindAll("#lt-max-days").Should().BeEmpty();
        cut.FindAll("[data-accrual-policies]").Should().BeEmpty();

        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "YearlyAllowance" });
        cut.FindAll("#lt-max-days").Should().ContainSingle();
        cut.FindAll("#lt-days-per-event").Should().BeEmpty();
        cut.FindAll("#lt-max-events").Should().BeEmpty();
        cut.FindAll("[data-accrual-policies]").Should().BeEmpty();
    }

    [Fact]
    public async Task TheKindAndGenderPickers_OfferReadableChoices()
    {
        var cut = RenderPage(Sil);
        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());

        cut.FindAll("#lt-kind option").Select(o => (o.GetAttribute("value"), o.TextContent.Trim()))
            .Should().Equal(("Accrued", "Accrued"), ("YearlyAllowance", "Yearly allowance"), ("PerEvent", "Per event"));
        cut.FindAll("#lt-gender option").Select(o => (o.GetAttribute("value"), o.TextContent.Trim()))
            .Should().Equal(("", "Any"), ("Female", "Female"), ("Male", "Male"));
    }

    [Fact]
    public async Task CreatingAType_SendsEverySetting_AndReloadsTheList()
    {
        _api.On(HttpMethod.Post, TypesPath, HttpStatusCode.Created, Pl);
        var cut = RenderPage(Sil);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        await cut.Find("#lt-name").InputAsync(new ChangeEventArgs { Value = "Paternity Leave" });
        await cut.Find("#lt-code").InputAsync(new ChangeEventArgs { Value = "PL" });
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "PerEvent" });
        await cut.Find("#lt-days-per-event").InputAsync(new ChangeEventArgs { Value = "7" });
        await cut.Find("#lt-max-events").InputAsync(new ChangeEventArgs { Value = "4" });
        await cut.Find("#lt-gender").ChangeAsync(new ChangeEventArgs { Value = "Male" });
        await cut.Find("#lt-min-service").InputAsync(new ChangeEventArgs { Value = "3" });
        await cut.Find("[data-setting='married']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-setting='document']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-setting='calendar-days']").ClickAsync(new MouseEventArgs());
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
        var body = BodyOf(HttpMethod.Post, TypesPath);
        body.EnumerateObject().Select(p => p.Name).Should().HaveCount(20);
        body.GetProperty("name").GetString().Should().Be("Paternity Leave");
        body.GetProperty("code").GetString().Should().Be("PL");
        body.GetProperty("entitlementKind").GetString().Should().Be("PerEvent");
        body.GetProperty("daysPerEvent").GetDecimal().Should().Be(7m);
        body.GetProperty("maxEvents").GetInt32().Should().Be(4);
        body.GetProperty("maxDaysPerYear").GetDecimal().Should().Be(0m);
        body.GetProperty("genderRestriction").GetString().Should().Be("Male");
        body.GetProperty("minServiceMonths").GetInt32().Should().Be(3);
        body.GetProperty("requiresMarried").GetBoolean().Should().BeTrue();
        body.GetProperty("requiresDocument").GetBoolean().Should().BeTrue();
        body.GetProperty("countsCalendarDays").GetBoolean().Should().BeTrue();
        body.GetProperty("requiresSoloParentId").GetBoolean().Should().BeFalse();
        body.GetProperty("isConfidential").GetBoolean().Should().BeFalse();
        body.GetProperty("isMaternity").GetBoolean().Should().BeFalse();
        body.GetProperty("isPaid").GetBoolean().Should().BeTrue();
        body.GetProperty("isActive").GetBoolean().Should().BeTrue();
        body.GetProperty("isCarryOver").GetBoolean().Should().BeFalse();
        body.GetProperty("carryOverMaxDays").ValueKind.Should().Be(JsonValueKind.Null);
        Calls(HttpMethod.Get, TypesPath).Should().Be(2);
    }

    [Fact]
    public async Task EditingAType_SendsItsSettingsBack_IncludingThoseTheFormDoesNotShowForItsKind()
    {
        _api.On(HttpMethod.Put, $"{TypesPath}/{VlId}", HttpStatusCode.OK, Vl);
        _api.On(HttpMethod.Get, PoliciesOf(VlId), HttpStatusCode.OK, "[]");
        var cut = RenderPage(Vl);

        await ButtonIn(Row(cut, "VL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.Find("#lt-name").GetAttribute("value").Should().Be("Vacation Leave");
        await cut.Find("#lt-name").InputAsync(new ChangeEventArgs { Value = "Vacation Leave (2026)" });
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
        var body = BodyOf(HttpMethod.Put, $"{TypesPath}/{VlId}");
        body.GetProperty("name").GetString().Should().Be("Vacation Leave (2026)");
        body.GetProperty("code").GetString().Should().Be("VL");
        body.GetProperty("entitlementKind").GetString().Should().Be("Accrued");
        body.GetProperty("maxDaysPerYear").GetDecimal().Should().Be(15m, "an accrued type keeps its yearly figure");
        body.GetProperty("isCarryOver").GetBoolean().Should().BeTrue();
        body.GetProperty("carryOverMaxDays").GetDecimal().Should().Be(5m);
        body.GetProperty("isConvertibleToCash").GetBoolean().Should().BeTrue();
        body.GetProperty("countsAsVacationForDeMinimis").GetBoolean().Should().BeTrue();
        body.GetProperty("genderRestriction").ValueKind.Should().Be(JsonValueKind.Null, "Any is sent as no restriction");
        body.GetProperty("daysPerEvent").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("maxEvents").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task EditingAType_CanDeactivateIt()
    {
        _api.On(HttpMethod.Put, $"{TypesPath}/{PlId}", HttpStatusCode.OK, Pl);
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        // Blazor renders a true bool attribute bare and drops a false one.
        cut.Find("[data-setting='active']").HasAttribute("aria-checked").Should().BeTrue();
        await cut.Find("[data-setting='active']").ClickAsync(new MouseEventArgs());
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, $"{TypesPath}/{PlId}").GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ChangingAPerEventTypeToAnotherKind_SendsNoPerEventFigures()
    {
        _api.On(HttpMethod.Put, $"{TypesPath}/{PlId}", HttpStatusCode.OK, Pl);
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "YearlyAllowance" });
        await cut.Find("#lt-max-days").InputAsync(new ChangeEventArgs { Value = "7" });
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
        var body = BodyOf(HttpMethod.Put, $"{TypesPath}/{PlId}");
        body.GetProperty("entitlementKind").GetString().Should().Be("YearlyAllowance");
        body.GetProperty("maxDaysPerYear").GetDecimal().Should().Be(7m);
        body.GetProperty("daysPerEvent").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("maxEvents").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("isMaternity").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("Accrued", true)]
    [InlineData("YearlyAllowance", false)]
    [InlineData("PerEvent", false)]
    public async Task CarryOver_IsOfferedOnlyForAnAccruedType(string kind, bool offered)
    {
        // Only an accrued type has a yearly balance whose unused days could move on; the API
        // refuses carry-over on any other kind.
        var cut = RenderPage(Sil);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = kind });

        cut.FindAll("[data-setting='carry-over']").Should().HaveCount(offered ? 1 : 0);
    }

    [Fact]
    public async Task ChangingACarryingOverAccruedTypeToAnotherKind_SendsNoCarryOver()
    {
        _api.On(HttpMethod.Put, $"{TypesPath}/{VlId}", HttpStatusCode.OK, Vl);
        _api.On(HttpMethod.Get, PoliciesOf(VlId), HttpStatusCode.OK, "[]");
        var cut = RenderPage(Vl);

        await ButtonIn(Row(cut, "VL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.Find("#lt-carry-over-max").GetAttribute("value").Should().Be("5");
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "YearlyAllowance" });
        cut.FindAll("#lt-carry-over-max").Should().BeEmpty();
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
        var body = BodyOf(HttpMethod.Put, $"{TypesPath}/{VlId}");
        body.GetProperty("isCarryOver").GetBoolean().Should().BeFalse();
        body.GetProperty("carryOverMaxDays").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Set at least 1 for the most times allowed.")]
    [InlineData("Service months can't be negative.")]
    [InlineData("Set the days per event.")]
    public async Task ASaveTheApiRefuses_ShowsItsReason_AndKeepsTheForm(string reason)
    {
        _api.On(HttpMethod.Put, $"{TypesPath}/{PlId}", HttpStatusCode.BadRequest, Problem(reason));
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.Find("[data-leave-type-error]").TextContent.Trim().Should().Be(reason));
        cut.FindAll("form[data-leave-type-form]").Should().ContainSingle();
    }

    [Fact]
    public async Task ATypeWithNoNameOrCode_IsNotSent()
    {
        var cut = RenderPage(Sil);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.Find("[data-leave-type-error]").TextContent.Should().Contain("name and a code");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task WhileASaveIsInFlight_TheSaveButtonIsDisabled_AndASecondSubmitSendsNothing()
    {
        var gate = _api.OnGated(HttpMethod.Put, $"{TypesPath}/{PlId}");
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.Find("form[data-leave-type-form]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-save-leave-type]").HasAttribute("disabled").Should().BeTrue());
        ButtonIn(cut.Find("[data-leave-type-dialog]"), "Cancel").HasAttribute("disabled").Should().BeTrue();
        cut.Find("form[data-leave-type-form]").Submit();
        Calls(HttpMethod.Put, $"{TypesPath}/{PlId}").Should().Be(1);

        gate.SetResult(Json(Pl));
        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
    }

    [Fact]
    public async Task ASaveThatFailsAfterTheDialogMovedOnToAnotherType_LeavesThatTypesFormAlone()
    {
        var gate = _api.OnGated(HttpMethod.Put, $"{TypesPath}/{PlId}");
        _api.On(HttpMethod.Get, PoliciesOf(VlId), HttpStatusCode.OK, "[]");
        var cut = RenderPage(Pl, Vl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        // Not awaited yet: the save is held until the gate opens.
        var save = cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);
        cut.WaitForAssertion(() => Calls(HttpMethod.Put, $"{TypesPath}/{PlId}").Should().Be(1));
        // Cancel is disabled, but the dialog's own close button still works.
        await ButtonIn(cut.Find("[data-leave-type-dialog]"), "Close").ClickAsync(new MouseEventArgs());
        await ButtonIn(Row(cut, "VL"), "Edit").ClickAsync(new MouseEventArgs());

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(Problem("Set the days per event."), Encoding.UTF8, "application/json")
        });
        await save;

        cut.FindAll("[data-leave-type-error]").Should().BeEmpty("PL's refusal is not about the type now open");
        cut.Find("#lt-name").GetAttribute("value").Should().Be("Vacation Leave");
    }

    [Fact]
    public async Task ASaveThatSucceedsAfterTheDialogMovedOnToAnotherType_KeepsThatTypesFormOpen_AndReloadsTheList()
    {
        var gate = _api.OnGated(HttpMethod.Put, $"{TypesPath}/{PlId}");
        _api.On(HttpMethod.Get, PoliciesOf(VlId), HttpStatusCode.OK, "[]");
        var cut = RenderPage(Pl, Vl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        var save = cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);
        cut.WaitForAssertion(() => Calls(HttpMethod.Put, $"{TypesPath}/{PlId}").Should().Be(1));
        await ButtonIn(cut.Find("[data-leave-type-dialog]"), "Close").ClickAsync(new MouseEventArgs());
        await ButtonIn(Row(cut, "VL"), "Edit").ClickAsync(new MouseEventArgs());

        gate.SetResult(Json(Pl));
        await save;

        cut.FindAll("form[data-leave-type-form]").Should().ContainSingle("VL's form was not the one saved");
        cut.Find("#lt-name").GetAttribute("value").Should().Be("Vacation Leave");
        Calls(HttpMethod.Get, TypesPath).Should().Be(2);
    }

    [Fact]
    public async Task ClearingTheDaysPerYear_OfAYearlyAllowance_IsRefused_BeforeAnythingIsSent()
    {
        var cut = RenderPage(Vawc);

        await ButtonIn(Row(cut, "VAWC"), "Edit").ClickAsync(new MouseEventArgs());
        cut.Find("#lt-max-days").GetAttribute("value").Should().Be("10");
        await cut.Find("#lt-max-days").InputAsync(new ChangeEventArgs { Value = "" });
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.Find("[data-leave-type-error]").TextContent.Trim().Should().Be("Set the days per year.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task ANewYearlyAllowance_StartsWithNoDaysPerYear_AndIsRefusedUntilItHasSome()
    {
        _api.On(HttpMethod.Post, TypesPath, HttpStatusCode.Created, Vawc);
        var cut = RenderPage(Sil);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        await cut.Find("#lt-name").InputAsync(new ChangeEventArgs { Value = "Wellness Leave" });
        await cut.Find("#lt-code").InputAsync(new ChangeEventArgs { Value = "WL" });
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "YearlyAllowance" });
        cut.Find("#lt-max-days").GetAttribute("value").Should().Be("");
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.Find("[data-leave-type-error]").TextContent.Trim().Should().Be("Set the days per year.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);

        await cut.Find("#lt-max-days").InputAsync(new ChangeEventArgs { Value = "3" });
        await cut.Find("form[data-leave-type-form]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-leave-type-form]").Should().BeEmpty());
        BodyOf(HttpMethod.Post, TypesPath).GetProperty("maxDaysPerYear").GetDecimal().Should().Be(3m);
    }

    [Fact]
    public async Task ChangingASavedTypesKind_SaysExistingBalancesAndRequestsStay()
    {
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.FindAll("[data-kind-change-note]").Should().BeEmpty();

        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "YearlyAllowance" });
        cut.Find("[data-kind-change-note]").TextContent.Trim().Should().Be("Existing balances and requests stay as they are.");

        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "PerEvent" });
        cut.FindAll("[data-kind-change-note]").Should().BeEmpty();
    }

    [Fact]
    public async Task ANewTypesKind_NeedsNoNoteAboutExistingBalances()
    {
        var cut = RenderPage(Sil);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "PerEvent" });

        cut.FindAll("[data-kind-change-note]").Should().BeEmpty();
    }

    [Fact]
    public async Task TickingMaternity_ShowsThePayrollNote_InTheForm()
    {
        var cut = RenderPage(Sil);

        await cut.Find("[data-new-leave-type]").ClickAsync(new MouseEventArgs());
        cut.FindAll("[data-setting='maternity']").Should().BeEmpty("only a per-event type can be the maternity type");
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "PerEvent" });
        cut.FindAll("[data-leave-type-form] [data-maternity-note]").Should().BeEmpty();

        await cut.Find("[data-setting='maternity']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-leave-type-form] [data-maternity-note]").TextContent.Trim().Should().Be(MaternityNote);
    }

    // ---- Accrual policies -------------------------------------------------------------------

    [Fact]
    public async Task EditingAnAccruedType_ListsItsAccrualRules_InWords()
    {
        _api.On(HttpMethod.Get, PoliciesOf(SilId), HttpStatusCode.OK,
            $"[{Policy(PolicyId, 12, null, 5m, "Monthly")},{Policy(Guid.NewGuid(), 0, 11, 2.5m, "Annual", active: false)}]");
        var cut = RenderPage(Sil);

        await ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.FindAll("[data-accrual-policy]").Should().HaveCount(2));
        var rows = cut.FindAll("[data-accrual-policy]");
        rows[0].TextContent.Should().Contain("From 12 months").And.Contain("5 days a year").And.Contain("Monthly").And.Contain("Active");
        rows[1].TextContent.Should().Contain("0 to 11 months").And.Contain("2.5 days a year").And.Contain("Once a year").And.Contain("Inactive");
    }

    [Fact]
    public async Task EditingAPerEventType_DoesNotAskForAccrualRules()
    {
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-accrual-policies]").Should().BeEmpty();
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == PoliciesPath);
    }

    [Fact]
    public async Task SwitchingASavedTypeToAccrued_OffersNoRules_UntilTheChangeIsSaved()
    {
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Edit").ClickAsync(new MouseEventArgs());
        await cut.Find("#lt-kind").ChangeAsync(new ChangeEventArgs { Value = "Accrued" });

        cut.Find("[data-accrual-policies]").TextContent.Should().Contain("Save the change of kind first");
        cut.FindAll("[data-add-policy]").Should().BeEmpty();
        _api.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == PoliciesPath);
    }

    [Fact]
    public async Task AddingAnAccrualRule_SendsIt_AndReloadsTheRules()
    {
        var policies = 0;
        _api.On(HttpMethod.Get, PoliciesOf(SilId), () =>
        {
            policies++;
            return Json(policies == 1 ? "[]" : $"[{Policy(PolicyId, 0, 11, 3m, "Annual")}]");
        });
        _api.On(HttpMethod.Post, PoliciesPath, HttpStatusCode.Created, Policy(PolicyId, 0, 11, 3m, "Annual"));
        var cut = RenderPage(Sil);

        await ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.Find("[data-accrual-policies]").TextContent.Should().Contain("No accrual rules"));
        await cut.Find("[data-add-policy]").ClickAsync(new MouseEventArgs());
        await cut.Find("#ap-min").InputAsync(new ChangeEventArgs { Value = "0" });
        await cut.Find("#ap-max").InputAsync(new ChangeEventArgs { Value = "11" });
        await cut.Find("#ap-days").InputAsync(new ChangeEventArgs { Value = "3" });
        await cut.Find("#ap-frequency").ChangeAsync(new ChangeEventArgs { Value = "Annual" });
        await cut.Find("[data-save-policy]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.FindAll("[data-accrual-policy]").Should().ContainSingle());
        var body = BodyOf(HttpMethod.Post, PoliciesPath);
        body.GetProperty("leaveTypeId").GetGuid().Should().Be(SilId);
        body.GetProperty("tenureMonthsMin").GetInt32().Should().Be(0);
        body.GetProperty("tenureMonthsMax").GetInt32().Should().Be(11);
        body.GetProperty("daysPerYear").GetDecimal().Should().Be(3m);
        body.GetProperty("accrualFrequency").GetString().Should().Be("Annual");
        policies.Should().Be(2);
        cut.FindAll("form[data-leave-type-form]").Should().ContainSingle("the type's own form stays open");
    }

    [Fact]
    public async Task EditingAnAccrualRule_StartsFromItsValues_AndPutsTheChange()
    {
        _api.On(HttpMethod.Get, PoliciesOf(SilId), HttpStatusCode.OK, $"[{Policy(PolicyId, 12, null, 5m, "Monthly")}]");
        _api.On(HttpMethod.Put, $"{PoliciesPath}/{PolicyId}", HttpStatusCode.NoContent);
        var cut = RenderPage(Sil);

        await ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.FindAll("[data-accrual-policy]").Should().ContainSingle());
        await cut.Find("[data-edit-policy]").ClickAsync(new MouseEventArgs());
        cut.Find("#ap-min").GetAttribute("value").Should().Be("12");
        cut.Find("#ap-max").GetAttribute("value").Should().Be("");
        cut.Find("#ap-frequency").GetAttribute("value").Should().Be("Monthly");
        await cut.Find("#ap-days").InputAsync(new ChangeEventArgs { Value = "6" });
        await cut.Find("[data-save-policy]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Calls(HttpMethod.Get, PoliciesOf(SilId)).Should().Be(2));
        var body = BodyOf(HttpMethod.Put, $"{PoliciesPath}/{PolicyId}");
        body.GetProperty("leaveTypeId").GetGuid().Should().Be(SilId);
        body.GetProperty("tenureMonthsMin").GetInt32().Should().Be(12);
        body.GetProperty("tenureMonthsMax").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("daysPerYear").GetDecimal().Should().Be(6m);
        body.GetProperty("accrualFrequency").GetString().Should().Be("Monthly");
    }

    [Fact]
    public async Task RemovingAnAccrualRule_AsksFirst_ThenDeletesIt()
    {
        _api.On(HttpMethod.Get, PoliciesOf(SilId), HttpStatusCode.OK, $"[{Policy(PolicyId, 12, null, 5m, "Monthly")}]");
        _api.On(HttpMethod.Delete, $"{PoliciesPath}/{PolicyId}", HttpStatusCode.NoContent);
        var cut = RenderPage(Sil);

        await ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.FindAll("[data-accrual-policy]").Should().ContainSingle());
        await cut.Find("[data-remove-policy]").ClickAsync(new MouseEventArgs());

        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete, "nothing is removed before it is confirmed");
        await ButtonIn(cut.Find("[data-confirm-remove-policy]"), "Remove").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Calls(HttpMethod.Get, PoliciesOf(SilId)).Should().Be(2));
        Calls(HttpMethod.Delete, $"{PoliciesPath}/{PolicyId}").Should().Be(1);
    }

    [Fact]
    public async Task AnAccrualRuleTheApiRefuses_ShowsItsReason()
    {
        _api.On(HttpMethod.Get, PoliciesOf(SilId), HttpStatusCode.OK, "[]");
        _api.On(HttpMethod.Post, PoliciesPath, HttpStatusCode.BadRequest, Problem("Days per year must be positive."));
        var cut = RenderPage(Sil);

        await ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.Find("[data-add-policy]"));
        await cut.Find("[data-add-policy]").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-save-policy]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-accrual-policy-error]").TextContent.Should().Contain("Days per year must be positive."));
    }

    [Fact]
    public async Task ASlowAnswerForTheTypeEditedBefore_DoesNotReplaceTheRulesOfTheTypeEditedNow()
    {
        var silGate = _api.OnGated(HttpMethod.Get, PoliciesOf(SilId));
        _api.On(HttpMethod.Get, PoliciesOf(VlId), HttpStatusCode.OK,
            $"[{Policy(PolicyId, 0, null, 15m, "Monthly")}]");
        var cut = RenderPage(Sil, Vl);

        // Not awaited yet: this handler is held on SIL's rules until the gate opens.
        var silEdit = ButtonIn(Row(cut, "SIL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Calls(HttpMethod.Get, PoliciesOf(SilId)).Should().Be(1));
        await ButtonIn(cut.Find("[data-leave-type-dialog]"), "Cancel").ClickAsync(new MouseEventArgs());
        await ButtonIn(Row(cut, "VL"), "Edit").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.Find("[data-accrual-policy]").TextContent.Should().Contain("15 days a year"));

        silGate.SetResult(Json($"[{Policy(Guid.NewGuid(), 12, null, 5m, "Monthly")}]"));
        // Once SIL's handler has finished, its late answer has been dealt with, one way or the other.
        await silEdit;

        cut.FindAll("[data-accrual-policy]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("15 days a year");
        cut.Find("#lt-name").GetAttribute("value").Should().Be("Vacation Leave");
    }

    // ---- Statutory set ----------------------------------------------------------------------

    [Fact]
    public async Task AddingTheStatutorySet_SaysWhatWasAddedAndSkipped_AndReloadsTheList()
    {
        _api.On(HttpMethod.Post, $"{TypesPath}/statutory", HttpStatusCode.OK,
            """{"added":["SIL","ML","AML"],"skipped":["PL"]}""");
        var cut = RenderPage(Pl);

        await cut.Find("[data-add-statutory]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-statutory-result]").TextContent.Should()
            .Contain("Added: SIL, ML, AML").And.Contain("Skipped: PL"));
        Calls(HttpMethod.Post, $"{TypesPath}/statutory").Should().Be(1);
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)].Should().BeNull();
        Calls(HttpMethod.Get, TypesPath).Should().Be(2);
    }

    [Fact]
    public async Task WhenTheSiteAlreadyHasTheWholeSet_TheResultSaysNoneWereAdded()
    {
        _api.On(HttpMethod.Post, $"{TypesPath}/statutory", HttpStatusCode.OK,
            """{"added":[],"skipped":["SIL","ML"]}""");
        var cut = RenderPage(Sil, Ml);

        await cut.Find("[data-add-statutory]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-statutory-result]").TextContent.Should()
            .Contain("Added: none").And.Contain("Skipped: SIL, ML"));
    }

    [Fact]
    public void WhileTheStatutorySetIsBeingAdded_TheButtonIsDisabled()
    {
        var gate = _api.OnGated(HttpMethod.Post, $"{TypesPath}/statutory");
        var cut = RenderPage(Sil);

        cut.Find("[data-add-statutory]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-add-statutory]").HasAttribute("disabled").Should().BeTrue());
        gate.SetResult(Json("""{"added":["ML"],"skipped":["SIL"]}"""));
        cut.WaitForAssertion(() => cut.Find("[data-statutory-result]"));
        cut.Find("[data-add-statutory]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task AStatutoryCallTheApiRefuses_ShowsItsReason()
    {
        _api.On(HttpMethod.Post, $"{TypesPath}/statutory", HttpStatusCode.Forbidden);
        var cut = RenderPage(Sil);

        await cut.Find("[data-add-statutory]").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should().Contain("You do not have permission to do that."));
    }

    [Fact]
    public async Task AnEarlierListLoadThatAnswersLate_DoesNotOverwriteALaterOne()
    {
        // First load: the page's own. Second: after the statutory call, held back. Third: after a
        // delete, answered at once. The held second answer must not replace the third.
        var loads = 0;
        var held = new TaskCompletionSource<HttpResponseMessage>();
        _api.OnAsync(HttpMethod.Get, TypesPath, () =>
        {
            loads++;
            return loads switch
            {
                1 => Task.FromResult(Json(List(Sil, Pl))),
                2 => held.Task,
                _ => Task.FromResult(Json(List(Sil))),
            };
        });
        _api.On(HttpMethod.Post, $"{TypesPath}/statutory", HttpStatusCode.OK, """{"added":[],"skipped":["SIL"]}""");
        _api.On(HttpMethod.Delete, $"{TypesPath}/{PlId}", HttpStatusCode.NoContent);
        var cut = Render<LeaveTypes>();
        cut.WaitForAssertion(() => cut.FindAll("[data-leave-type]").Should().HaveCount(2));

        // Not awaited yet: this handler is held on its reload until the test releases it.
        var statutory = cut.Find("[data-add-statutory]").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => loads.Should().Be(2));
        await ButtonIn(Row(cut, "PL"), "Delete").ClickAsync(new MouseEventArgs());
        await ButtonIn(cut.Find("[data-confirm-delete]"), "Delete").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.FindAll("[data-leave-type]").Should().ContainSingle());

        held.SetResult(Json(List(Sil, Pl, Ml)));
        await statutory;

        cut.Find("[data-statutory-result]").TextContent.Should().Contain("Skipped: SIL");
        cut.FindAll("[data-leave-type]").Should().ContainSingle();
    }

    // ---- Delete -----------------------------------------------------------------------------

    [Fact]
    public async Task DeletingAType_AsksFirst_AndCancellingSendsNothing()
    {
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Delete").ClickAsync(new MouseEventArgs());

        cut.Find("[data-confirm-delete]").TextContent.Should().Contain("Delete Paternity Leave?");
        await ButtonIn(cut.Find("[data-confirm-delete]"), "Cancel").ClickAsync(new MouseEventArgs());
        cut.FindAll("[data-confirm-delete]").Should().BeEmpty();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task DeletingAnUnusedType_RemovesIt_AndReloadsTheList()
    {
        _api.On(HttpMethod.Delete, $"{TypesPath}/{PlId}", HttpStatusCode.NoContent);
        var cut = RenderPage(Pl);

        await ButtonIn(Row(cut, "PL"), "Delete").ClickAsync(new MouseEventArgs());
        await ButtonIn(cut.Find("[data-confirm-delete]"), "Delete").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Calls(HttpMethod.Get, TypesPath).Should().Be(2));
        Calls(HttpMethod.Delete, $"{TypesPath}/{PlId}").Should().Be(1);
        cut.FindAll("[data-confirm-delete]").Should().BeEmpty();
    }

    [Fact]
    public async Task DeletingAUsedType_ShowsTheApisRefusal()
    {
        _api.On(HttpMethod.Delete, $"{TypesPath}/{VlId}", HttpStatusCode.BadRequest,
            Problem("Vacation Leave has been used; deactivate it instead."));
        var cut = RenderPage(Vl);

        await ButtonIn(Row(cut, "VL"), "Delete").ClickAsync(new MouseEventArgs());
        await ButtonIn(cut.Find("[data-confirm-delete]"), "Delete").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Trim()
            .Should().Be("Vacation Leave has been used; deactivate it instead."));
        cut.FindAll("[data-confirm-delete]").Should().BeEmpty();
    }
}
