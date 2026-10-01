using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>
/// The client's copies of the opening-balance records and the 2316, and the opening-balance calls.
/// A field the copy names differently, or leaves out, fails nothing: it reads as its default, or is
/// never sent. These pin every field, by the API's own names and in its own order.
/// </summary>
public class ApiClientOpeningBalanceTests
{
    private static readonly Guid BalanceId = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    // PeopleCore.Application.Payroll.OpeningBalances.OpeningBalanceDtos, parameter by parameter.
    private static readonly string[] BalanceFields =
    [
        "id", "employeeId", "employeeName", "employeeNumber", "year", "throughDate",
        "basicSalary", "thirteenthMonthPaid", "otherBenefitsPaid", "otherTaxablePay", "deMinimis",
        "otherNonTaxable", "employeeContributions", "taxWithheld", "deMinimisLeaveDays", "warnings",
    ];

    private static readonly string[] RequestFields =
    [
        "employeeId", "year", "throughDate",
        "basicSalary", "thirteenthMonthPaid", "otherBenefitsPaid", "otherTaxablePay", "deMinimis",
        "otherNonTaxable", "employeeContributions", "taxWithheld", "deMinimisLeaveDays",
    ];

    // PeopleCore.Application.Payroll.DTOs.Bir2316Dto, property by property.
    private static readonly string[] Bir2316Fields =
    [
        "EmployeeId", "Year", "PeriodFrom", "PeriodTo",
        "EmployeeTin", "EmployeeLastName", "EmployeeFirstName", "EmployeeMiddleName", "RdoCode",
        "RegisteredAddress", "RegisteredZipCode", "LocalHomeAddress", "LocalZipCode", "ForeignAddress",
        "DateOfBirth", "ContactNumber", "StatutoryMinWagePerDay", "StatutoryMinWagePerMonth", "IsMinimumWageEarner",
        "EmployerTin", "EmployerName", "EmployerAddress", "EmployerZipCode", "EmployerRdoCode", "IsMainEmployer",
        "PrevEmployerTin", "PrevEmployerName", "PrevEmployerAddress", "PrevEmployerZipCode",
        "Item29_NonTaxableBasicSalary", "Item30_HolidayPayMwe", "Item31_OvertimePayMwe", "Item32_NightShiftDiffMwe",
        "Item33_HazardPayMwe", "Item34_ThirteenthMonthAndBenefits", "Item35_DeMinimis",
        "Item36_SssPhicPagibigContributions", "Item37_SalariesOtherForms",
        "Item39_BasicSalary", "Item40_Representation", "Item41_Transportation", "Item42_Cola", "Item43_FixedHousing",
        "Item44A_OtherAmount", "Item44A_OtherLabel", "Item44B_OtherAmount", "Item44B_OtherLabel",
        "Item45_Commission", "Item46_ProfitSharing", "Item47_Fees", "Item48_TaxableThirteenthMonth",
        "Item49_HazardPay", "Item50_OvertimePay", "Item51A_OtherAmount", "Item51A_OtherLabel",
        "Item51B_OtherAmount", "Item51B_OtherLabel",
        "Item22_PrevTaxableCompensation", "Item25B_PrevTaxWithheld", "Item25A_PresentTaxWithheld", "Item27_PeraTaxCredit",
        "Item38_TotalNonTaxable", "Item52_TotalTaxableCompensation", "Item19_GrossCompensation", "Item20_LessNonTaxable",
        "Item21_TaxableFromPresent", "Item23_GrossTaxable", "Item24_TaxDue", "Item26_TotalTaxWithheld", "Item28_TotalTaxes",
        "OpeningBalanceThrough", "OpeningBalanceTaxWithheld",
    ];

    private static string BalanceJson => $$"""
        {"id":"{{BalanceId}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"E-001","year":2026,
         "throughDate":"2026-03-31","basicSalary":90000,"thirteenthMonthPaid":1000.5,"otherBenefitsPaid":2000,
         "otherTaxablePay":3000,"deMinimis":4000,"otherNonTaxable":5000,"employeeContributions":6000,"taxWithheld":7000,
         "deMinimisLeaveDays":2.5,"warnings":["Check it"]}
        """;

    private static readonly OpeningBalanceDto Balance = new(
        BalanceId, MariaId, "Maria Santos", "E-001", 2026, new DateOnly(2026, 3, 31),
        90000m, 1000.5m, 2000m, 3000m, 4000m, 5000m, 6000m, 7000m, 2.5m, ["Check it"]);

    private static readonly OpeningBalanceRequest Request = new(
        MariaId, 2026, new DateOnly(2026, 3, 31), 90000m, 1000.5m, 2000m, 3000m, 4000m, 5000m, 6000m, 7000m, 2.5m);

    private static List<string> PropertyNames(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();

    private static List<string> MembersOf<T>() =>
        typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single(c => c.GetParameters() is not [{ } only] || only.ParameterType != typeof(T))
            .GetParameters().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!)).ToList();

    private static void ShouldBeTheSame(OpeningBalanceDto actual, OpeningBalanceDto expected)
    {
        actual.Should().BeEquivalentTo(expected, o => o.Excluding(b => b.Warnings));
        actual.Warnings.Should().Equal(expected.Warnings);
    }

    [Fact]
    public void TheMirrors_HaveEveryApiField_InTheApisOrder()
    {
        MembersOf<OpeningBalanceDto>().Should().Equal(BalanceFields);
        MembersOf<OpeningBalanceRequest>().Should().Equal(RequestFields);
        MembersOf<OpeningBalanceImportDto>().Should().Equal("created", "updated", "warnings");
        PropertyNames(BalanceJson).Should().Equal(BalanceFields);
    }

    [Fact]
    public void The2316Mirror_HasEveryApiProperty_InTheApisOrder()
    {
        typeof(Bir2316Dto).GetProperties().Select(p => p.Name).Should().Equal(Bir2316Fields);
        typeof(Bir2316Dto).GetProperty(nameof(Bir2316Dto.OpeningBalanceThrough))!.PropertyType.Should().Be<DateOnly?>();
        typeof(Bir2316Dto).GetProperty(nameof(Bir2316Dto.OpeningBalanceTaxWithheld))!.PropertyType.Should().Be<decimal>();
    }

    [Fact]
    public async Task The2316Preview_ReadsTheOpeningBalanceFields()
    {
        var employeeId = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"/api/reports/2316/preview/{employeeId}?year=2026", HttpStatusCode.OK,
            $$"""{"employeeId":"{{employeeId}}","year":2026,"openingBalanceThrough":"2026-03-31","openingBalanceTaxWithheld":9000.5}""");

        var preview = await CreateClient().GetBir2316PreviewAsync(employeeId, 2026);

        preview!.EmployeeId.Should().Be(employeeId);
        preview.OpeningBalanceThrough.Should().Be(new DateOnly(2026, 3, 31));
        preview.OpeningBalanceTaxWithheld.Should().Be(9000.5m);
    }

    [Fact]
    public async Task GetOpeningBalances_ReadsTheYearsBalances_WithEveryField()
    {
        _api.On(HttpMethod.Get, "/api/payroll-opening-balances?year=2026", HttpStatusCode.OK, $"[{BalanceJson}]");

        var balances = await CreateClient().GetOpeningBalancesAsync(2026);

        ShouldBeTheSame(balances!.Single(), Balance);
    }

    [Fact]
    public async Task CreateOpeningBalance_PostsEveryField_InTheApisOrder_AndReadsTheSavedBalance()
    {
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances", HttpStatusCode.Created, BalanceJson);

        var saved = await CreateClient().CreateOpeningBalanceAsync(Request);

        ShouldBeTheSame(saved!, Balance);
        _api.RequestBodies.Single().Should().Be(
            $$"""{"employeeId":"{{MariaId}}","year":2026,"throughDate":"2026-03-31","basicSalary":90000,"thirteenthMonthPaid":1000.5,"otherBenefitsPaid":2000,"otherTaxablePay":3000,"deMinimis":4000,"otherNonTaxable":5000,"employeeContributions":6000,"taxWithheld":7000,"deMinimisLeaveDays":2.5}""");
    }

    [Fact]
    public async Task UpdateOpeningBalance_PutsToTheBalance()
    {
        _api.On(HttpMethod.Put, $"/api/payroll-opening-balances/{BalanceId}", HttpStatusCode.OK, BalanceJson);

        var saved = await CreateClient().UpdateOpeningBalanceAsync(BalanceId, Request);

        saved!.Warnings.Should().Equal("Check it");
        PropertyNames(_api.RequestBodies.Single()!).Should().Equal(RequestFields);
    }

    [Fact]
    public async Task ASaveTheApiRefuses_ThrowsItsReason()
    {
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances", HttpStatusCode.BadRequest,
            """{"title":"Business Rule Violation","status":400,"detail":"Maria Santos already has an opening balance for 2026."}""");

        var act = () => CreateClient().CreateOpeningBalanceAsync(Request);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Maria Santos already has an opening balance for 2026.");
    }

    [Fact]
    public async Task DeleteOpeningBalance_DeletesTheBalance()
    {
        _api.On(HttpMethod.Delete, $"/api/payroll-opening-balances/{BalanceId}", HttpStatusCode.NoContent);

        await CreateClient().DeleteOpeningBalanceAsync(BalanceId);

        _api.Requests.Single().Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task GetOpeningBalanceTemplate_ReadsTheFilesBytes()
    {
        _api.On(HttpMethod.Get, "/api/payroll-opening-balances/template",
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("EmployeeNumber,Year\r\n"u8.ToArray()) });

        var bytes = await CreateClient().GetOpeningBalanceTemplateAsync();

        Encoding.UTF8.GetString(bytes).Should().Be("EmployeeNumber,Year\r\n");
    }

    [Fact]
    public async Task ImportOpeningBalances_PostsTheFileAsFile_AndReadsTheCountsAndWarnings()
    {
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances/import", HttpStatusCode.OK,
            """{"created":3,"updated":2,"warnings":["E-001 Maria Santos: PAY-2026-007 used these figures."]}""");

        var outcome = await CreateClient().ImportOpeningBalancesAsync("EmployeeNumber\r\n"u8.ToArray(), "balances.csv");

        outcome.Errors.Should().BeEmpty();
        outcome.Imported!.Created.Should().Be(3);
        outcome.Imported.Updated.Should().Be(2);
        outcome.Imported.Warnings.Should().Equal("E-001 Maria Santos: PAY-2026-007 used these figures.");
        // The multipart body as sent: one part, the field the API binds ("file"), with the file's name.
        _api.Requests.Single().Content.Should().BeOfType<MultipartFormDataContent>();
        var body = _api.RequestBodies.Single()!;
        body.Should().Contain("Content-Disposition: form-data; name=file; filename=balances.csv")
            .And.Contain("Content-Type: text/csv").And.Contain("EmployeeNumber");
    }

    [Fact]
    public async Task ARefusedImport_ReturnsEveryRowError()
    {
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances/import", HttpStatusCode.BadRequest,
            """{"title":"File not imported","status":400,"detail":"Row 3: Unknown employee number E-999.\nRow 4: Enter a date as yyyy-MM-dd.","errors":["Row 3: Unknown employee number E-999.","Row 4: Enter a date as yyyy-MM-dd."]}""");

        var outcome = await CreateClient().ImportOpeningBalancesAsync([1], "balances.csv");

        outcome.Imported.Should().BeNull();
        outcome.Errors.Should().Equal("Row 3: Unknown employee number E-999.", "Row 4: Enter a date as yyyy-MM-dd.");
    }

    [Fact]
    public async Task ARefusalWithoutAnErrorList_FallsBackToItsDetail()
    {
        // The middleware's refusals (a body over the limit, a rival save) carry only a detail.
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances/import", HttpStatusCode.BadRequest,
            """{"title":"Business Rule Violation","status":400,"detail":"Choose a CSV file of at most 2 MB."}""");

        var outcome = await CreateClient().ImportOpeningBalancesAsync([1], "balances.csv");

        outcome.Errors.Should().Equal("Choose a CSV file of at most 2 MB.");
    }

    [Fact]
    public async Task AnErrorsObjectRatherThanAList_IsNotReadAsRows_AndTheDetailIsUsed()
    {
        // ASP.NET's own validation problem has errors as an object keyed by field.
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances/import", HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"detail":"The file is missing.\nChoose a file.","errors":{"file":["The file field is required."]}}""");

        var outcome = await CreateClient().ImportOpeningBalancesAsync([1], "balances.csv");

        outcome.Errors.Should().Equal("The file is missing.", "Choose a file.");
    }

    [Fact]
    public async Task AnImportFailingForAnotherReason_Throws()
    {
        _api.On(HttpMethod.Post, "/api/payroll-opening-balances/import", HttpStatusCode.Forbidden);

        var act = () => CreateClient().ImportOpeningBalancesAsync([1], "balances.csv");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("You do not have permission to do that.");
    }
}
