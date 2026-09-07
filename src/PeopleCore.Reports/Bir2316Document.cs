using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Reports;

public class Bir2316Document(Bir2316Dto dto) : IDocument
{
    private static readonly TextStyle LabelStyle = TextStyle.Default.FontSize(6).FontColor(Colors.Grey.Darken2);
    private static readonly TextStyle ValueStyle = TextStyle.Default.FontSize(7.5f).Bold();
    private static readonly TextStyle TitleStyle = TextStyle.Default.FontSize(6.5f).Bold();
    private static readonly TextStyle SectionStyle = TextStyle.Default.FontSize(6.5f).Bold();
    private static readonly TextStyle SmallStyle = TextStyle.Default.FontSize(5.5f);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"BIR Form 2316 – {dto.EmployeeLastName}, {dto.EmployeeFirstName} – {dto.Year}",
        Author = dto.EmployerName,
        Subject = "Certificate of Compensation Payment/Tax Withheld"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Legal);
            page.MarginHorizontal(18, Unit.Point);
            page.MarginVertical(12, Unit.Point);
            page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(7));
            page.Content().Column(col =>
            {
                col.Item().Element(ComposeHeader);
                col.Item().Height(2);
                col.Item().Element(ComposeYearPeriodRow);
                col.Item().Height(2);
                col.Item().Row(row =>
                {
                    row.RelativeItem(1).Element(ComposeLeftColumn);
                    row.ConstantItem(2).Background(Colors.Grey.Lighten2);
                    row.RelativeItem(1).Element(ComposeRightColumn);
                });
                col.Item().Height(4);
                col.Item().Element(ComposeSignatureBlock);
            });
        });
    }

    private void ComposeHeader(IContainer c)
    {
        c.Border(0.5f).BorderColor(Colors.Black).Row(row =>
        {
            row.ConstantItem(55).Padding(2).Column(col =>
            {
                col.Item().Text("For BIR Use Only").Style(SmallStyle);
                col.Item().Text("BCS/ Item:").Style(SmallStyle);
            });
            row.RelativeItem().Padding(3).Column(col =>
            {
                col.Item().AlignCenter().Text("Republic of the Philippines").Style(SmallStyle);
                col.Item().AlignCenter().Text("Department of Finance").Style(SmallStyle);
                col.Item().AlignCenter().Text("Bureau of Internal Revenue").Style(SmallStyle);
                col.Item().Height(2);
                col.Item().AlignCenter().Text("Certificate of Compensation").Style(TextStyle.Default.FontSize(12).Bold());
                col.Item().AlignCenter().Text("Payment/Tax Withheld").Style(TextStyle.Default.FontSize(12).Bold());
                col.Item().AlignCenter().Text("For Compensation Payment With or Without Tax Withheld").Style(SmallStyle);
            });
            row.ConstantItem(65).Padding(2).Column(col =>
            {
                col.Item().AlignRight().Text("BIR Form No.").Style(SmallStyle);
                col.Item().AlignRight().Text("2316").Style(TextStyle.Default.FontSize(14).Bold());
                col.Item().AlignRight().Text("September 2021(ENCS)").Style(SmallStyle);
                col.Item().AlignRight().Text("2316 9/21ENCS").Style(SmallStyle);
            });
        });
    }

    private void ComposeYearPeriodRow(IContainer c)
    {
        c.Border(0.5f).BorderColor(Colors.Black).Row(row =>
        {
            row.RelativeItem(1).BorderRight(0.5f).BorderColor(Colors.Black).Padding(3).Column(col =>
            {
                col.Item().Text("1 For the Year (YYYY)").Style(LabelStyle);
                col.Item().Text(dto.Year.ToString()).Style(ValueStyle);
            });
            row.RelativeItem(2).Padding(3).Row(r =>
            {
                r.RelativeItem().Column(c2 =>
                {
                    c2.Item().Text("2 For the Period").Style(LabelStyle);
                    c2.Item().Text($"From: {dto.PeriodFrom}").Style(ValueStyle);
                });
                r.RelativeItem().Column(c2 =>
                {
                    c2.Item().Text(" ").Style(LabelStyle);
                    c2.Item().Text($"To: {dto.PeriodTo}").Style(ValueStyle);
                });
            });
        });
    }

    private void ComposeLeftColumn(IContainer c)
    {
        c.Column(col =>
        {
            // Part I Header
            col.Item().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).AlignCenter().Text("Part I - Employee Information").Style(SectionStyle);

            Field(col, "3 TIN", dto.EmployeeTin);
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem(3).BorderRight(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("4 Employee's Name (Last Name, First Name, Middle Name)").Style(LabelStyle);
                    inner.Item().Text($"{dto.EmployeeLastName}, {dto.EmployeeFirstName} {dto.EmployeeMiddleName}".TrimEnd()).Style(ValueStyle);
                });
                row.RelativeItem(1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("5 RDO Code").Style(LabelStyle);
                    inner.Item().Text(dto.RdoCode).Style(ValueStyle);
                });
            });
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem(3).BorderRight(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("6 Registered Address").Style(LabelStyle);
                    inner.Item().Text(dto.RegisteredAddress).Style(ValueStyle);
                });
                row.RelativeItem(1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("6A ZIP Code").Style(LabelStyle);
                    inner.Item().Text(dto.RegisteredZipCode).Style(ValueStyle);
                });
            });
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem(3).BorderRight(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("6B Local Home Address").Style(LabelStyle);
                    inner.Item().Text(dto.LocalHomeAddress).Style(ValueStyle);
                });
                row.RelativeItem(1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("6C ZIP Code").Style(LabelStyle);
                    inner.Item().Text(dto.LocalZipCode).Style(ValueStyle);
                });
            });
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem().Padding(2).Column(inner =>
                {
                    inner.Item().Text("6D Foreign Address").Style(LabelStyle);
                    inner.Item().Text(dto.ForeignAddress).Style(ValueStyle);
                });
            });
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem().BorderRight(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("7 Date of Birth (MM/DD/YYYY)").Style(LabelStyle);
                    inner.Item().Text(dto.DateOfBirth).Style(ValueStyle);
                });
                row.RelativeItem().Padding(2).Column(inner =>
                {
                    inner.Item().Text("8 Contact Number").Style(LabelStyle);
                    inner.Item().Text(dto.ContactNumber).Style(ValueStyle);
                });
            });
            Field(col, "9 Statutory Minimum Wage rate per day",
                dto.StatutoryMinWagePerDay > 0 ? $"₱{dto.StatutoryMinWagePerDay:N2}" : "");
            Field(col, "10 Statutory Minimum Wage rate per month",
                dto.StatutoryMinWagePerMonth > 0 ? $"₱{dto.StatutoryMinWagePerMonth:N2}" : "");
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Row(row =>
            {
                row.ConstantItem(10).AlignMiddle()
                    .Border(0.5f).BorderColor(Colors.Black).Padding(1)
                    .AlignCenter().Text(dto.IsMinimumWageEarner ? "X" : "").Style(SmallStyle);
                row.ConstantItem(4);
                row.RelativeItem().Text("11 Minimum Wage Earner (MWE) whose compensation is exempt from withholding tax and not subject to income tax").Style(SmallStyle);
            });

            // Part II
            col.Item().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).AlignCenter().Text("Part II - Employer Information (Present)").Style(SectionStyle);

            Field(col, "12 TIN", dto.EmployerTin);
            Field(col, "13 Employer's Name", dto.EmployerName);
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem(3).BorderRight(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("14 Registered Address").Style(LabelStyle);
                    inner.Item().Text(dto.EmployerAddress).Style(ValueStyle);
                });
                row.RelativeItem(1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("14A ZIP Code").Style(LabelStyle);
                    inner.Item().Text(dto.EmployerZipCode).Style(ValueStyle);
                });
            });
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Row(row =>
            {
                row.RelativeItem().Text("15 Type of Employer:").Style(LabelStyle);
                row.ConstantItem(8).Border(0.5f).BorderColor(Colors.Black).Padding(1)
                    .AlignCenter().Text(dto.IsMainEmployer ? "X" : "").Style(SmallStyle);
                row.ConstantItem(2);
                row.ConstantItem(40).Text("Main Employer").Style(SmallStyle);
                row.ConstantItem(8).Border(0.5f).BorderColor(Colors.Black).Padding(1)
                    .AlignCenter().Text(!dto.IsMainEmployer ? "X" : "").Style(SmallStyle);
                row.ConstantItem(2);
                row.RelativeItem().Text("Secondary Employer").Style(SmallStyle);
            });

            // Part III
            col.Item().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).AlignCenter().Text("Part III - Employer Information (Previous)").Style(SectionStyle);

            Field(col, "16 TIN", dto.PrevEmployerTin);
            Field(col, "17 Employer's Name", dto.PrevEmployerName);
            col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Row(row =>
            {
                row.RelativeItem(3).BorderRight(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("18 Registered Address").Style(LabelStyle);
                    inner.Item().Text(dto.PrevEmployerAddress).Style(ValueStyle);
                });
                row.RelativeItem(1).Padding(2).Column(inner =>
                {
                    inner.Item().Text("18A ZIP Code").Style(LabelStyle);
                    inner.Item().Text(dto.PrevEmployerZipCode).Style(ValueStyle);
                });
            });

            // Part IVA
            col.Item().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).AlignCenter().Text("Part IVA - Summary").Style(SectionStyle);

            SummaryRow(col, "19 Gross Compensation Income from Present Employer (Sum of Items 38 and 52)", dto.Item19_GrossCompensation);
            SummaryRow(col, "20 Less: Total Non-Taxable/Exempt Compensation Income from Present Employer (From Item 38)", dto.Item20_LessNonTaxable);
            SummaryRow(col, "21 Taxable Compensation Income from Present Employer (Item 19 Less Item 20) (From Item 52)", dto.Item21_TaxableFromPresent);
            SummaryRow(col, "22 Add: Taxable Compensation Income from Previous Employer, if applicable", dto.Item22_PrevTaxableCompensation);
            SummaryRow(col, "23 Gross Taxable Compensation Income (Sum of Items 21 and 22)", dto.Item23_GrossTaxable);
            SummaryRow(col, "24 Tax Due", dto.Item24_TaxDue);
            SummaryRow(col, "25A Amount of Taxes Withheld – Present Employer", dto.Item25A_PresentTaxWithheld);
            SummaryRow(col, "25B Previous Employer, if applicable", dto.Item25B_PrevTaxWithheld);
            SummaryRow(col, "26 Total Amount of Taxes Withheld as adjusted (Sum of Items 25A and 25B)", dto.Item26_TotalTaxWithheld);
            SummaryRow(col, "27 5% Tax Credit (PERA Act of 2008)", dto.Item27_PeraTaxCredit);
            SummaryRow(col, "28 Total Taxes Withheld (Sum of Items 26 and 27)", dto.Item28_TotalTaxes);
        });
    }

    private void ComposeRightColumn(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).AlignCenter()
                .Text("Part IV-B Details of Compensation Income & Tax Withheld from Present Employer").Style(SectionStyle);

            col.Item().Background(Colors.Grey.Lighten2).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).Row(row =>
                {
                    row.RelativeItem().Text("A. NON-TAXABLE/EXEMPT COMPENSATION INCOME").Style(TitleStyle);
                    row.ConstantItem(50).AlignRight().Text("Amount").Style(TitleStyle);
                });

            LineItem(col, "29", "Basic Salary (incl. the exempt ₱250,000 & below or the Statutory Minimum Wage of the MWE)", dto.Item29_NonTaxableBasicSalary);
            LineItem(col, "30", "Holiday Pay (MWE)", dto.Item30_HolidayPayMwe);
            LineItem(col, "31", "Overtime Pay (MWE)", dto.Item31_OvertimePayMwe);
            LineItem(col, "32", "Night Shift Differential (MWE)", dto.Item32_NightShiftDiffMwe);
            LineItem(col, "33", "Hazard Pay (MWE)", dto.Item33_HazardPayMwe);
            LineItem(col, "34", "13th Month Pay and Other Benefits (maximum of ₱90,000)", dto.Item34_ThirteenthMonthAndBenefits);
            LineItem(col, "35", "De Minimis Benefits", dto.Item35_DeMinimis);
            LineItem(col, "36", "SSS, GSIS, PHIC & PAG-IBIG Contributions and Union Dues (Employee share only)", dto.Item36_SssPhicPagibigContributions);
            LineItem(col, "37", "Salaries and Other Forms of Compensation", dto.Item37_SalariesOtherForms);
            LineItem(col, "38", "Total Non-Taxable/Exempt Compensation Income (Sum of Items 29 to 37)", dto.Item38_TotalNonTaxable, bold: true);

            col.Item().Background(Colors.Grey.Lighten2).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).Text("B. TAXABLE COMPENSATION INCOME REGULAR").Style(TitleStyle);

            LineItem(col, "39", "Basic Salary", dto.Item39_BasicSalary);
            LineItem(col, "40", "Representation", dto.Item40_Representation);
            LineItem(col, "41", "Transportation", dto.Item41_Transportation);
            LineItem(col, "42", "Cost of Living Allowance (COLA)", dto.Item42_Cola);
            LineItem(col, "43", "Fixed Housing Allowance", dto.Item43_FixedHousing);
            LineItem(col, "44A", string.IsNullOrEmpty(dto.Item44A_OtherLabel) ? "Others (specify)" : dto.Item44A_OtherLabel, dto.Item44A_OtherAmount);
            LineItem(col, "44B", string.IsNullOrEmpty(dto.Item44B_OtherLabel) ? "Others (specify)" : dto.Item44B_OtherLabel, dto.Item44B_OtherAmount);

            col.Item().Background(Colors.Grey.Lighten2).BorderBottom(0.5f).BorderColor(Colors.Black)
                .Padding(2).Text("SUPPLEMENTARY").Style(TitleStyle);

            LineItem(col, "45", "Commission", dto.Item45_Commission);
            LineItem(col, "46", "Profit Sharing", dto.Item46_ProfitSharing);
            LineItem(col, "47", "Fees Including Director's Fees", dto.Item47_Fees);
            LineItem(col, "48", "Taxable 13th Month Benefits", dto.Item48_TaxableThirteenthMonth);
            LineItem(col, "49", "Hazard Pay", dto.Item49_HazardPay);
            LineItem(col, "50", "Overtime Pay", dto.Item50_OvertimePay);
            LineItem(col, "51A", string.IsNullOrEmpty(dto.Item51A_OtherLabel) ? "Others (specify)" : dto.Item51A_OtherLabel, dto.Item51A_OtherAmount);
            LineItem(col, "51B", string.IsNullOrEmpty(dto.Item51B_OtherLabel) ? "Others (specify)" : dto.Item51B_OtherLabel, dto.Item51B_OtherAmount);
            LineItem(col, "52", "Total Taxable Compensation Income (Sum of Items 39 to 51B)", dto.Item52_TotalTaxableCompensation, bold: true);
        });
    }

    private void ComposeSignatureBlock(IContainer c)
    {
        c.Border(0.5f).BorderColor(Colors.Black).Padding(3).Column(col =>
        {
            col.Item().Text(
                "I/We declare, under the penalties of perjury that this certificate has been made in good faith, " +
                "verified by me/us, and to the best of my/our knowledge and belief, is true and correct, pursuant to " +
                "the provisions of the National Internal Revenue Code, as amended, and the regulations issued under " +
                "authority thereof. Further, I/we give my/our consent to the processing of my/our information as " +
                "contemplated under the *Data Privacy Act of 2012 (R.A. No. 10173) for legitimate and lawful purposes."
            ).Style(SmallStyle);
            col.Item().Height(4);
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text("53 ___________________________________").Style(SmallStyle);
                    inner.Item().Text("Present Employer/Authorized Agent Signature over Printed Name").Style(SmallStyle);
                });
                row.ConstantItem(8);
                row.RelativeItem().AlignRight().Text("Date Signed: ___________________").Style(SmallStyle);
            });
            col.Item().Height(4);
            col.Item().Text("CONFORME:").Style(TitleStyle);
            col.Item().Height(3);
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text("54 ___________________________________").Style(SmallStyle);
                    inner.Item().Text("Employee Signature over Printed Name").Style(SmallStyle);
                });
                row.ConstantItem(8);
                row.RelativeItem().AlignRight().Text("Date Signed: ___________________").Style(SmallStyle);
            });
            col.Item().Height(4);
            col.Item().Text("*NOTE: The BIR Data Privacy is in the BIR website (www.bir.gov.ph)").Style(SmallStyle);
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void Field(ColumnDescriptor col, string label, string value) =>
        col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Column(inner =>
        {
            inner.Item().Text(label).Style(TextStyle.Default.FontSize(6).FontColor(Colors.Grey.Darken2));
            inner.Item().Text(value).Style(TextStyle.Default.FontSize(7.5f).Bold());
        });

    private static void SummaryRow(ColumnDescriptor col, string label, decimal value) =>
        col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2).Row(row =>
        {
            row.RelativeItem().Text(label).Style(TextStyle.Default.FontSize(6).FontColor(Colors.Grey.Darken2));
            row.ConstantItem(60).AlignRight().Text(value > 0 ? $"₱{value:N2}" : "").Style(TextStyle.Default.FontSize(7.5f).Bold());
        });

    private static void LineItem(ColumnDescriptor col, string num, string label, decimal value, bool bold = false) =>
        col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2).Row(row =>
        {
            row.ConstantItem(14).Text(num).Style(TextStyle.Default.FontSize(6).FontColor(Colors.Grey.Darken2));
            row.RelativeItem().Text(label).Style(bold ? TextStyle.Default.FontSize(6.5f).Bold() : TextStyle.Default.FontSize(6).FontColor(Colors.Grey.Darken2));
            row.ConstantItem(58).AlignRight().Text(value > 0 ? $"₱{value:N2}" : "").Style(TextStyle.Default.FontSize(7.5f).Bold());
        });
}
