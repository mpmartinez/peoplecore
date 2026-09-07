using System.Globalization;
using System.Linq;
using System.Text;
using FluentAssertions;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Reports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316StamperTests
{
    [Fact]
    public void Stamp_ProducesTheOfficialPageSize()
    {
        var pdf = new Bir2316Stamper().Stamp(SampleDto());

        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(1);
        // 612 x 936 pts = 8.5 x 13in, Philippine folio. The QuestPDF version used Legal
        // (8.5 x 14in) and was a full inch too tall, so nothing aligned on official stock.
        doc.Pages[0].Width.Point.Should().BeApproximately(612, 0.5);
        doc.Pages[0].Height.Point.Should().BeApproximately(936, 0.5);
    }

    [Fact]
    public void Stamp_RendersIdenticallyAcrossRuns()
    {
        // Byte identity is deliberately NOT asserted here: PDFsharp legitimately varies the
        // embedded font's subset tag and the XMP metadata's DocumentID/InstanceID UUIDs on every
        // `Save`, and font subset tags are required by the PDF spec to be unique per subset, so
        // forcing them to a fixed value would make the file technically malformed. What actually
        // must not vary is what ends up on the page: if font resolution ever fell back to a
        // system font instead of the embedded TTF, glyph metrics would differ and the stamped
        // text would land at different coordinates. So this compares the *positioned text runs*
        // extracted from the page content stream instead of the raw bytes.
        var dto = SampleDto();

        var a = new Bir2316Stamper().Stamp(dto);
        var b = new Bir2316Stamper().Stamp(dto);

        var runsA = ExtractPositionedTextRuns(a);
        var runsB = ExtractPositionedTextRuns(b);

        runsA.Should().NotBeEmpty();
        runsA.Should().Equal(runsB);
    }

    /// <summary>
    /// Walks the page's content stream operators and records every shown text run together with
    /// the text position in effect when it was shown. This is a simplified reader — it handles
    /// only the operators <see cref="Bir2316Stamper"/> (via <c>XGraphics.DrawString</c>) actually
    /// emits (<c>Tm</c>/<c>Td</c>/<c>TD</c> to position, <c>Tj</c>/<c>TJ</c> to show text) — not a
    /// general-purpose PDF text extractor.
    /// </summary>
    private static List<(string Text, double X, double Y)> ExtractPositionedTextRuns(byte[] pdfBytes)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.ReadOnly);
        var runs = new List<(string Text, double X, double Y)>();

        foreach (var page in doc.Pages)
        {
            var content = ContentReader.ReadContent(page);
            double x = 0, y = 0;

            foreach (var obj in content)
            {
                if (obj is not COperator op) continue;

                switch (op.OpCode.OpCodeName)
                {
                    case OpCodeName.BT:
                        x = 0;
                        y = 0;
                        break;

                    case OpCodeName.Tm when op.Operands.Count == 6:
                        x = NumberValue(op.Operands[4]);
                        y = NumberValue(op.Operands[5]);
                        break;

                    case OpCodeName.Td or OpCodeName.TD when op.Operands.Count == 2:
                        x += NumberValue(op.Operands[0]);
                        y += NumberValue(op.Operands[1]);
                        break;

                    case OpCodeName.Tj when op.Operands.Count == 1 && op.Operands[0] is CString s:
                        runs.Add((s.Value, x, y));
                        break;

                    case OpCodeName.TJ when op.Operands.Count == 1 && op.Operands[0] is CSequence array:
                        var text = new StringBuilder();
                        foreach (var element in array)
                        {
                            if (element is CString elementString) text.Append(elementString.Value);
                        }
                        runs.Add((text.ToString(), x, y));
                        break;
                }
            }
        }

        return runs;
    }

    [Fact]
    public void Stamp_UsesTheEmbeddedFontRatherThanASystemFont()
    {
        // This pins the actual risk the deleted byte-rewriting was papering over: font
        // resolution must not depend on what is installed on the machine. If the resolver ever
        // fell back to a system font, a developer's Windows box (which has "Arial" installed)
        // would render different glyph metrics than a Linux container (which does not) even
        // though both ran the same code. Testing the resolver directly, at the seam where that
        // risk actually lives, is more honest than inferring it from PDF output.
        EmbeddedFontResolver.EnsureRegistered();
        var resolver = new EmbeddedFontResolver();

        var typeface = resolver.ResolveTypeface("Arial", isBold: false, isItalic: false);

        typeface.Should().NotBeNull();
        var fontBytes = resolver.GetFont(typeface!.FaceName);
        fontBytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Stamp_PlacesEveryComputedFigureOnTheForm()
    {
        // Every non-zero figure the DTO carries should appear, formatted with thousands
        // separators and two decimals, somewhere in the output's extracted text. A field that the
        // stamper forgot to draw - or that never got a Bir2316FieldMap entry - fails here, which a
        // page-count or page-size check would not catch.
        var dto = FullSampleDto();

        var pdf = new Bir2316Stamper().Stamp(dto);
        var runs = ExtractPositionedTextRuns(pdf);
        var text = string.Concat(runs.Select(r => r.Text));

        decimal[] expectedAmounts =
        [
            dto.Item29_NonTaxableBasicSalary, dto.Item30_HolidayPayMwe, dto.Item31_OvertimePayMwe,
            dto.Item32_NightShiftDiffMwe, dto.Item33_HazardPayMwe, dto.Item34_ThirteenthMonthAndBenefits,
            dto.Item35_DeMinimis, dto.Item36_SssPhicPagibigContributions, dto.Item37_SalariesOtherForms,
            dto.Item38_TotalNonTaxable,
            dto.Item39_BasicSalary, dto.Item40_Representation, dto.Item41_Transportation, dto.Item42_Cola,
            dto.Item43_FixedHousing, dto.Item44A_OtherAmount, dto.Item44B_OtherAmount,
            dto.Item45_Commission, dto.Item46_ProfitSharing, dto.Item47_Fees,
            dto.Item48_TaxableThirteenthMonth, dto.Item49_HazardPay, dto.Item50_OvertimePay,
            dto.Item51A_OtherAmount, dto.Item51B_OtherAmount, dto.Item52_TotalTaxableCompensation,
            dto.Item19_GrossCompensation, dto.Item20_LessNonTaxable, dto.Item21_TaxableFromPresent,
            dto.Item22_PrevTaxableCompensation, dto.Item23_GrossTaxable, dto.Item24_TaxDue,
            dto.Item25A_PresentTaxWithheld, dto.Item25B_PrevTaxWithheld, dto.Item26_TotalTaxWithheld,
            dto.Item27_PeraTaxCredit, dto.Item28_TotalTaxes,
            dto.StatutoryMinWagePerDay, dto.StatutoryMinWagePerMonth,
        ];

        foreach (var amount in expectedAmounts.Where(a => a != 0))
        {
            var formatted = amount.ToString("N2", CultureInfo.InvariantCulture);
            text.Should().Contain(formatted, $"the stamped output should include {formatted}");
        }

        // Item 24 (Tax Due) always prints, even when it computes to zero.
        text.Should().Contain(dto.Item24_TaxDue.ToString("N2", CultureInfo.InvariantCulture));

        text.Should().Contain(dto.Item44A_OtherLabel);
        text.Should().Contain(dto.Item44B_OtherLabel);
        text.Should().Contain(dto.Item51A_OtherLabel);
        text.Should().Contain(dto.Item51B_OtherLabel);
    }

    [Fact]
    public void Stamp_PutsMoneyInTheAmountColumn()
    {
        // Extracting mere presence (as the test above does) would not catch a value stamped into
        // the wrong box - e.g. Item 42's figure landing under Item 41's caption instead of in the
        // amount column. This asserts the actual drawn X-coordinate of a known money value falls
        // inside the amount column's bounds (483.8-585.6 pts, per Bir2316FieldMap's Item42_Cola
        // comment), which only holds if the field map entry AND the right-alignment math are both
        // correct.
        var dto = FullSampleDto();

        var pdf = new Bir2316Stamper().Stamp(dto);
        var runs = ExtractPositionedTextRuns(pdf);

        var formatted = dto.Item42_Cola.ToString("N2", CultureInfo.InvariantCulture);
        var run = runs.Should().ContainSingle(r => r.Text == formatted).Subject;

        run.X.Should().BeInRange(483.8, 585.6, "the value should sit inside Item 42's amount box, not spill into a neighboring one");
        run.Y.Should().BeApproximately(514, 1, "it should sit on Item 42's own baseline");
    }

    [Fact]
    public void Stamp_DrawsZipCodeDigitsAsSeparatePositionedRunsOnePerCell()
    {
        // Items 6A, 6C, 14A and 18A print four individual digit-cells with dividers, exactly like
        // TIN/DOB/Contact Number - drawing "1200" as one continuous string would collide with
        // those dividers the same way the pre-fix stamper's TIN/DOB/Contact Number output did (see
        // the Task 3 report). This proves the fix landed by reusing the same content-stream run
        // extraction the other stamper tests use: a value stamped through DrawDigits shows up as
        // one single-character Tj/TJ run per digit, each at its own X, rather than one run holding
        // the whole string.
        var dto = SampleDto(); // RegisteredZipCode is "1200"

        var pdf = new Bir2316Stamper().Stamp(dto);
        var runs = ExtractPositionedTextRuns(pdf);

        runs.Should().NotContain(r => r.Text == "1200",
            "the ZIP code should not be drawn as a single continuous run across the printed cell dividers");

        var digitRuns = runs
            .Where(r => r.Text.Length == 1 && char.IsDigit(r.Text[0]) && r.Y is > 748 and < 750)
            .OrderBy(r => r.X)
            .ToList();

        digitRuns.Select(r => r.Text).Should().Equal(["1", "2", "0", "0"],
            "each digit of the ZIP code should be its own run, in order, on item 6A's baseline");

        for (var i = 1; i < digitRuns.Count; i++)
        {
            (digitRuns[i].X - digitRuns[i - 1].X).Should().BeApproximately(12.005, 0.01,
                "consecutive digits should be spaced by the cell's own advance width, not drawn touching one another");
        }
    }

    private static double NumberValue(CObject obj) => obj switch
    {
        CInteger i => i.Value,
        CReal r => r.Value,
        _ => throw new InvalidOperationException($"Expected a numeric content-stream operand, got {obj.GetType().Name}.")
    };

    private static Bir2316Dto SampleDto() => new()
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
    };

    /// <summary>
    /// <see cref="SampleDto"/> plus a distinct, non-zero value for every money box the form
    /// carries, so <see cref="Stamp_PlacesEveryComputedFigureOnTheForm"/> and
    /// <see cref="Stamp_PutsMoneyInTheAmountColumn"/> have real figures to look for. Each literal
    /// is unique so a value found in the output can only have come from the field it was meant
    /// for, not from a neighbor that happens to share a figure.
    /// </summary>
    private static Bir2316Dto FullSampleDto()
    {
        var dto = SampleDto();

        dto.Item29_NonTaxableBasicSalary = 111_111.11m;
        dto.Item30_HolidayPayMwe = 2_222.22m;
        dto.Item31_OvertimePayMwe = 3_333.33m;
        dto.Item32_NightShiftDiffMwe = 4_444.44m;
        dto.Item33_HazardPayMwe = 5_555.55m;
        dto.Item34_ThirteenthMonthAndBenefits = 6_666.66m;
        dto.Item35_DeMinimis = 7_777.77m;
        dto.Item36_SssPhicPagibigContributions = 8_888.88m;
        dto.Item37_SalariesOtherForms = 9_999.99m;

        dto.Item39_BasicSalary = 211_111.11m;
        dto.Item40_Representation = 1_234.56m;
        dto.Item41_Transportation = 2_345.67m;
        dto.Item42_Cola = 3_456.78m;
        dto.Item43_FixedHousing = 4_567.89m;
        dto.Item44A_OtherAmount = 5_678.90m;
        dto.Item44A_OtherLabel = "Rice Subsidy";
        dto.Item44B_OtherAmount = 6_789.01m;
        dto.Item44B_OtherLabel = "Clothing Allowance";

        dto.Item45_Commission = 1_010.10m;
        dto.Item46_ProfitSharing = 2_020.20m;
        dto.Item47_Fees = 3_030.30m;
        dto.Item48_TaxableThirteenthMonth = 4_040.40m;
        dto.Item49_HazardPay = 5_050.50m;
        dto.Item50_OvertimePay = 6_060.60m;
        dto.Item51A_OtherAmount = 7_070.70m;
        dto.Item51A_OtherLabel = "Retro Pay";
        dto.Item51B_OtherAmount = 8_080.80m;
        dto.Item51B_OtherLabel = "Signing Bonus";

        dto.Item22_PrevTaxableCompensation = 16_500.00m;
        dto.Item25A_PresentTaxWithheld = 12_345.00m;
        dto.Item25B_PrevTaxWithheld = 2_500.00m;
        dto.Item27_PeraTaxCredit = 750.00m;

        dto.StatutoryMinWagePerDay = 570.00m;
        dto.StatutoryMinWagePerMonth = 12_000.00m;

        return dto;
    }
}
