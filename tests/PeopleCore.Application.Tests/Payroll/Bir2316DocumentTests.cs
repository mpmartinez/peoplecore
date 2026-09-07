using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316DocumentTests
{
    public Bir2316DocumentTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void Renders_a_non_empty_pdf_for_a_typical_certificate()
    {
        var pdf = new Bir2316Document(Dto()).GeneratePdf();

        pdf.Should().NotBeNullOrEmpty();
        pdf.Take(5).Should().Equal("%PDF-"u8.ToArray(), "output must be a real PDF");
    }

    [Fact]
    public void Renders_when_every_amount_is_zero()
    {
        var act = () => new Bir2316Document(new Bir2316Dto { Year = 2026 }).GeneratePdf();

        act.Should().NotThrow("an employee with no compensation figures yet must still get a document");
    }

    [Fact]
    public void Renders_when_names_and_addresses_are_unusually_long()
    {
        var dto = Dto() with
        {
            EmployeeLastName = new string('D', 100),
            EmployeeFirstName = new string('J', 100),
            RegisteredAddress = new string('A', 200),
            EmployerName = new string('M', 200)
        };

        var act = () => new Bir2316Document(dto).GeneratePdf();

        act.Should().NotThrow("QuestPDF throws on layout overflow rather than truncating");
    }

    [Fact]
    public void Renders_with_every_taxable_and_non_taxable_line_populated()
    {
        var dto = Dto() with
        {
            Item29_NonTaxableBasicSalary = 100m,
            Item30_HolidayPayMwe = 200m,
            Item31_OvertimePayMwe = 300m,
            Item32_NightShiftDiffMwe = 400m,
            Item33_HazardPayMwe = 500m,
            Item34_ThirteenthMonthAndBenefits = 600m,
            Item35_DeMinimis = 700m,
            Item36_SssPhicPagibigContributions = 800m,
            Item37_SalariesOtherForms = 900m,
            Item39_BasicSalary = 1_000m,
            Item40_Representation = 1_100m,
            Item41_Transportation = 1_200m,
            Item42_Cola = 1_300m,
            Item43_FixedHousing = 1_400m,
            Item44A_OtherAmount = 1_500m,
            Item44A_OtherLabel = "Allowance A",
            Item44B_OtherAmount = 1_600m,
            Item44B_OtherLabel = "Allowance B",
            Item45_Commission = 1_700m,
            Item46_ProfitSharing = 1_800m,
            Item47_Fees = 1_900m,
            Item48_TaxableThirteenthMonth = 2_000m,
            Item49_HazardPay = 2_100m,
            Item50_OvertimePay = 2_200m,
            Item51A_OtherAmount = 2_300m,
            Item51A_OtherLabel = "Other A",
            Item51B_OtherAmount = 2_400m,
            Item51B_OtherLabel = "Other B",
            Item22_PrevTaxableCompensation = 2_500m,
            Item25A_PresentTaxWithheld = 2_600m,
            Item25B_PrevTaxWithheld = 2_700m,
            Item27_PeraTaxCredit = 2_800m
        };

        var act = () => new Bir2316Document(dto).GeneratePdf();

        act.Should().NotThrow("a fully populated certificate must render without overflow");
    }

    private static Bir2316Dto Dto() => new()
    {
        Year = 2026,
        PeriodFrom = "January",
        PeriodTo = "December",
        EmployeeTin = "123-456-789-000",
        EmployeeLastName = "Dela Cruz",
        EmployeeFirstName = "Juan",
        EmployeeMiddleName = "Protacio",
        RdoCode = "039",
        RegisteredAddress = "123 Rizal St., Makati City",
        RegisteredZipCode = "1200",
        LocalHomeAddress = "123 Rizal St., Makati City",
        LocalZipCode = "1200",
        DateOfBirth = "01/15/1990",
        ContactNumber = "0917-123-4567",
        EmployerTin = "987-654-321-000",
        EmployerName = "M2NET Solutions Inc.",
        EmployerAddress = "456 Ayala Avenue, Makati City",
        EmployerZipCode = "1226",
        IsMainEmployer = true,
        Item39_BasicSalary = 300_000m
    };
}
