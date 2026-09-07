using System.Globalization;
using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Internal;
using PdfSharp.Pdf.IO;
using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Reports;

/// <summary>
/// Stamps a <see cref="Bir2316Dto"/> onto the Bureau's own BIR Form 2316 PDF rather than
/// redrawing the form from scratch, the way the retired QuestPDF <c>Bir2316Document</c> used to.
/// <para>
/// The official form ships with no AcroForm fields — <c>get_fields()</c> on the source PDF
/// returns none — so it cannot be filled the way a normal PDF form is; every value has to be
/// drawn onto the page at an absolute coordinate with <see cref="XGraphics"/>, the same way you'd
/// line up a typewriter over pre-printed stock. <see cref="Bir2316FieldMap"/> supplies every one
/// of those coordinates, derived from the blank form itself; <see cref="Bir2316Renderer"/> calls
/// into this class rather than QuestPDF.
/// </para>
/// <para>
/// <see cref="XGraphics.FromPdfPage(PdfPage)"/> uses a top-left, Y-down coordinate space (the same
/// convention as GDI+), while every coordinate in <see cref="Bir2316FieldMap"/> — like the
/// pypdf/pdfplumber readings it was derived from — is in the PDF's own bottom-left, Y-up space.
/// <see cref="Draw"/> converts between the two with <c>pageHeight - field.Y</c> once, at the point
/// of drawing, so the field map itself never has to think about which convention PDFsharp wants.
/// </para>
/// </summary>
public sealed class Bir2316Stamper
{
    private const string EmbeddedFormResourceName = "PeopleCore.Reports.Forms.bir-2316-2021-encs.pdf";

    /// <summary>
    /// Pinned to a fixed instant rather than the wall clock so that generating the same
    /// certificate twice produces the same <c>/CreationDate</c> and <c>/ModificationDate</c>.
    /// This is a legitimate reproducibility property of a generated document — unlike the
    /// per-save subset-font and XMP identifiers PDFsharp also varies, <see cref="PdfDocumentInformation.CreationDate"/>
    /// and <see cref="PdfDocumentInformation.ModificationDate"/> are public, documented API with
    /// no structural role in the file, so pinning them carries no risk of producing a malformed PDF.
    /// </summary>
    private static readonly DateTime FixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// PDFsharp writes a 16-byte "second" document identifier into the trailer's <c>/ID</c> array
    /// on every <c>Modify</c>-mode open, generated from <c>Guid.NewGuid()</c>. Overwritten with
    /// this fixed value before <c>Save</c> so the trailer does not vary between renders. Like the
    /// timestamps above, this is a public, documented PDFsharp API — it does not touch the saved
    /// bytes after the fact the way the font-subset-tag and XMP-UUID rewriting used to.
    /// </summary>
    private static readonly byte[] FixedSecondDocumentId = new byte[16];

    public Bir2316Stamper()
    {
        EmbeddedFontResolver.EnsureRegistered();
    }

    public byte[] Stamp(Bir2316Dto dto)
    {
        using var formStream = OpenEmbeddedForm();
        using var document = PdfReader.Open(formStream, PdfDocumentOpenMode.Modify);

        document.Info.CreationDate = FixedTimestamp;
        document.Info.ModificationDate = FixedTimestamp;
        document.Internals.SecondDocumentID =
            PdfEncoders.RawEncoding.GetString(FixedSecondDocumentId, 0, FixedSecondDocumentId.Length);

        var page = document.Pages[0];
        var pageHeight = page.Height.Point;
        using var gfx = XGraphics.FromPdfPage(page);
        const string FontFamily = "Arial";
        const double BaseFontSize = 8;
        var font = new XFont(FontFamily, BaseFontSize);

        void Draw(Bir2316FieldMap.Field field, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var drawFont = font;
            var drawText = text;
            if (field.MaxWidth is { } maxWidth)
            {
                (drawFont, drawText) = FitToWidth(gfx, text, font, FontFamily, maxWidth);
            }

            var x = field.X;
            if (field.Align == Bir2316FieldMap.Align.Right)
            {
                x -= gfx.MeasureString(drawText, drawFont).Width;
            }

            gfx.DrawString(drawText, drawFont, XBrushes.Black, new XPoint(x, pageHeight - field.Y));
        }

        // Draws only the digit characters of `raw` into a run of per-digit cells (see
        // Bir2316FieldMap.DigitField), one character per cell at `X + i * Advance`, instead of
        // one continuous string across the whole row. This is the Task 3 fix for TIN (items 3,
        // 12, 16 - four cell groups, taken in order) and Date of Birth / Contact Number (a single
        // group each), extended in Task 4 to the four ZIP Code fields (items 6A, 6C, 14A, 18A -
        // one 4-digit cell group each): the pre-fix stamper drew those as one string, which
        // visually collided with the form's own printed cell dividers. Non-digit characters (TIN's
        // dashes, DOB's slashes) are stripped before laying out cells - the form already prints
        // its own separators between the digit cells, so nothing needs to be drawn there.
        //
        // Whole-branch review Fix 4: if `raw` has MORE digits than the cells can hold, nothing is
        // drawn for this field at all, rather than the pre-fix behaviour of silently drawing the
        // first N digits and dropping the rest. Dropping trailing digits does not overflow the
        // box, but it is not "safe" the way it looked - it prints a different, shorter number
        // that still looks like a complete, confidently-stated value. ContactNumber is free text
        // (Employee.MobileNumber): a stored "+639171234567" has 12 digits after stripping the
        // "+", so truncating to the 11-digit cell run prints "63917123456" - a wrong phone number
        // with no visual sign anything was cut. TIN and ZIP are effectively bounded by how they
        // are entered elsewhere, so this all-or-nothing rule costs them nothing in practice; for
        // ContactNumber it is the actual fix. If `raw` has fewer digits than the cells provide,
        // the trailing cells are still simply left blank - that is a genuinely incomplete value,
        // not a wrong one, and printing what was actually given is correct.
        void DrawDigits(IReadOnlyList<Bir2316FieldMap.DigitField> groups, string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return;

            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return;

            var capacity = groups.Sum(g => g.Count);
            if (digits.Length > capacity) return;

            var pos = 0;
            foreach (var group in groups)
            {
                var remaining = digits.Length - pos;
                if (remaining <= 0) break;

                var take = Math.Min(remaining, group.Count);
                for (var i = 0; i < take; i++)
                {
                    var x = group.X + i * group.Advance;
                    gfx.DrawString(digits[pos + i].ToString(), font, XBrushes.Black,
                        new XPoint(x, pageHeight - group.Y));
                }

                pos += take;
            }
        }

        // Header
        Draw(Bir2316FieldMap.Year, dto.Year.ToString(CultureInfo.InvariantCulture));
        Draw(Bir2316FieldMap.PeriodFrom, dto.PeriodFrom);
        Draw(Bir2316FieldMap.PeriodTo, dto.PeriodTo);

        // Part I - Employee Information
        DrawDigits(Bir2316FieldMap.EmployeeTinDigits, dto.EmployeeTin);
        Draw(Bir2316FieldMap.EmployeeName,
            $"{dto.EmployeeLastName}, {dto.EmployeeFirstName} {dto.EmployeeMiddleName}".TrimEnd());
        Draw(Bir2316FieldMap.RdoCode, dto.RdoCode);
        Draw(Bir2316FieldMap.RegisteredAddress, dto.RegisteredAddress);
        DrawDigits(Bir2316FieldMap.RegisteredZipCodeDigits, dto.RegisteredZipCode);
        Draw(Bir2316FieldMap.LocalHomeAddress, dto.LocalHomeAddress);
        DrawDigits(Bir2316FieldMap.LocalZipCodeDigits, dto.LocalZipCode);
        Draw(Bir2316FieldMap.ForeignAddress, dto.ForeignAddress);
        DrawDigits(Bir2316FieldMap.DateOfBirthDigits, dto.DateOfBirth);
        DrawDigits(Bir2316FieldMap.ContactNumberDigits, dto.ContactNumber);
        Draw(Bir2316FieldMap.StatutoryMinWagePerDay, Money(dto.StatutoryMinWagePerDay));
        Draw(Bir2316FieldMap.StatutoryMinWagePerMonth, Money(dto.StatutoryMinWagePerMonth));
        if (dto.IsMinimumWageEarner) Draw(Bir2316FieldMap.MinimumWageEarnerCheckbox, "X");

        // Part II - Employer Information (Present)
        DrawDigits(Bir2316FieldMap.EmployerTinDigits, dto.EmployerTin);
        Draw(Bir2316FieldMap.EmployerName, dto.EmployerName);
        Draw(Bir2316FieldMap.EmployerAddress, dto.EmployerAddress);
        DrawDigits(Bir2316FieldMap.EmployerZipCodeDigits, dto.EmployerZipCode);
        Draw(dto.IsMainEmployer ? Bir2316FieldMap.MainEmployerCheckbox : Bir2316FieldMap.SecondaryEmployerCheckbox, "X");

        // Part III - Employer Information (Previous)
        DrawDigits(Bir2316FieldMap.PrevEmployerTinDigits, dto.PrevEmployerTin);
        Draw(Bir2316FieldMap.PrevEmployerName, dto.PrevEmployerName);
        Draw(Bir2316FieldMap.PrevEmployerAddress, dto.PrevEmployerAddress);
        DrawDigits(Bir2316FieldMap.PrevEmployerZipCodeDigits, dto.PrevEmployerZipCode);

        // Part IVA - Summary
        Draw(Bir2316FieldMap.Item19_GrossCompensation, Money(dto.Item19_GrossCompensation));
        Draw(Bir2316FieldMap.Item20_LessNonTaxable, Money(dto.Item20_LessNonTaxable));
        Draw(Bir2316FieldMap.Item21_TaxableFromPresent, Money(dto.Item21_TaxableFromPresent));
        Draw(Bir2316FieldMap.Item22_PrevTaxableCompensation, Money(dto.Item22_PrevTaxableCompensation));
        Draw(Bir2316FieldMap.Item23_GrossTaxable, Money(dto.Item23_GrossTaxable));
        // Item 24 (Tax Due) always prints, even ₱0.00 - it is a real, meaningful result (it says
        // withholding met or exceeded liability), not an unset optional box. See the field map's
        // comment on Item24_TaxDue and Bir2316Dto.Item24_TaxDue's own doc comment.
        Draw(Bir2316FieldMap.Item24_TaxDue, Money(dto.Item24_TaxDue, alwaysPrint: true));
        Draw(Bir2316FieldMap.Item25A_PresentTaxWithheld, Money(dto.Item25A_PresentTaxWithheld));
        Draw(Bir2316FieldMap.Item25B_PrevTaxWithheld, Money(dto.Item25B_PrevTaxWithheld));
        Draw(Bir2316FieldMap.Item26_TotalTaxWithheld, Money(dto.Item26_TotalTaxWithheld));
        Draw(Bir2316FieldMap.Item27_PeraTaxCredit, Money(dto.Item27_PeraTaxCredit));
        Draw(Bir2316FieldMap.Item28_TotalTaxes, Money(dto.Item28_TotalTaxes));

        // Part IV-B Section A - Non-Taxable/Exempt Compensation Income
        Draw(Bir2316FieldMap.Item29_NonTaxableBasicSalary, Money(dto.Item29_NonTaxableBasicSalary));
        Draw(Bir2316FieldMap.Item30_HolidayPayMwe, Money(dto.Item30_HolidayPayMwe));
        Draw(Bir2316FieldMap.Item31_OvertimePayMwe, Money(dto.Item31_OvertimePayMwe));
        Draw(Bir2316FieldMap.Item32_NightShiftDiffMwe, Money(dto.Item32_NightShiftDiffMwe));
        Draw(Bir2316FieldMap.Item33_HazardPayMwe, Money(dto.Item33_HazardPayMwe));
        Draw(Bir2316FieldMap.Item34_ThirteenthMonthAndBenefits, Money(dto.Item34_ThirteenthMonthAndBenefits));
        Draw(Bir2316FieldMap.Item35_DeMinimis, Money(dto.Item35_DeMinimis));
        Draw(Bir2316FieldMap.Item36_SssPhicPagibigContributions, Money(dto.Item36_SssPhicPagibigContributions));
        Draw(Bir2316FieldMap.Item37_SalariesOtherForms, Money(dto.Item37_SalariesOtherForms));
        Draw(Bir2316FieldMap.Item38_TotalNonTaxable, Money(dto.Item38_TotalNonTaxable));

        // Part IV-B Section B - Taxable Compensation Income Regular
        Draw(Bir2316FieldMap.Item39_BasicSalary, Money(dto.Item39_BasicSalary));
        Draw(Bir2316FieldMap.Item40_Representation, Money(dto.Item40_Representation));
        Draw(Bir2316FieldMap.Item41_Transportation, Money(dto.Item41_Transportation));
        Draw(Bir2316FieldMap.Item42_Cola, Money(dto.Item42_Cola));
        Draw(Bir2316FieldMap.Item43_FixedHousing, Money(dto.Item43_FixedHousing));
        Draw(Bir2316FieldMap.Item44A_OtherAmount, Money(dto.Item44A_OtherAmount));
        Draw(Bir2316FieldMap.Item44A_OtherLabel, dto.Item44A_OtherLabel);
        Draw(Bir2316FieldMap.Item44B_OtherAmount, Money(dto.Item44B_OtherAmount));
        Draw(Bir2316FieldMap.Item44B_OtherLabel, dto.Item44B_OtherLabel);

        // Supplementary
        Draw(Bir2316FieldMap.Item45_Commission, Money(dto.Item45_Commission));
        Draw(Bir2316FieldMap.Item46_ProfitSharing, Money(dto.Item46_ProfitSharing));
        Draw(Bir2316FieldMap.Item47_Fees, Money(dto.Item47_Fees));
        Draw(Bir2316FieldMap.Item48_TaxableThirteenthMonth, Money(dto.Item48_TaxableThirteenthMonth));
        Draw(Bir2316FieldMap.Item49_HazardPay, Money(dto.Item49_HazardPay));
        Draw(Bir2316FieldMap.Item50_OvertimePay, Money(dto.Item50_OvertimePay));
        Draw(Bir2316FieldMap.Item51A_OtherAmount, Money(dto.Item51A_OtherAmount));
        Draw(Bir2316FieldMap.Item51A_OtherLabel, dto.Item51A_OtherLabel);
        Draw(Bir2316FieldMap.Item51B_OtherAmount, Money(dto.Item51B_OtherAmount));
        Draw(Bir2316FieldMap.Item51B_OtherLabel, dto.Item51B_OtherLabel);
        Draw(Bir2316FieldMap.Item52_TotalTaxableCompensation, Money(dto.Item52_TotalTaxableCompensation));

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    /// <summary>
    /// Formats a money figure with thousands separators and two decimals, the way it reads on a
    /// payslip - e.g. <c>12,345.67</c>. An optional box that computed to exactly zero prints
    /// nothing: the official form leaves unused boxes blank, and a page full of <c>0.00</c> both
    /// reads worse and implies a figure was computed when it was not. <paramref name="alwaysPrint"/>
    /// overrides that for the one box (Item 24, Tax Due) where zero is itself a meaningful result.
    /// </summary>
    private static string Money(decimal value, bool alwaysPrint = false) =>
        value == 0 && !alwaysPrint ? "" : value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>
    /// The smallest font size <see cref="FitToWidth"/> will shrink to before it starts
    /// ellipsizing instead. 6pt (three-quarters of the form's normal 8pt) is still legibly
    /// printed at the form's actual size - smaller than that reads as an apology rather than a
    /// value on an official tax certificate, so a value that still would not fit at 6pt is
    /// ellipsized instead of shrunk further.
    /// </summary>
    private const double MinFontSize = 6.0;

    /// <summary>
    /// Whole-branch review Fix 1 (CRITICAL): every field was previously drawn as one unclipped
    /// run at a fixed 8pt, so a long name or address did not just get cut off - it ran across
    /// whatever the next box over happened to be (a registered address measured 464pt wide,
    /// spilling through the 6A ZIP cells and into items 30/31's captions). This shrinks the font
    /// down to <see cref="MinFontSize"/> first, in 0.5pt steps, re-measuring at each step - most
    /// real names and addresses fit well before the floor and print at a size a reader would not
    /// even notice was reduced. Only a value that still overflows its box at the floor size loses
    /// characters, and even then it is marked with "..." rather than silently and invisibly cut
    /// off, so nobody mistakes a truncated address for a complete one.
    /// </summary>
    private static (XFont Font, string Text) FitToWidth(
        XGraphics gfx, string text, XFont baseFont, string fontFamily, double maxWidth)
    {
        var font = baseFont;
        if (gfx.MeasureString(text, font).Width <= maxWidth) return (font, text);

        var size = baseFont.Size;
        while (size > MinFontSize)
        {
            size = Math.Max(MinFontSize, size - 0.5);
            font = new XFont(fontFamily, size);
            if (gfx.MeasureString(text, font).Width <= maxWidth) return (font, text);
        }

        const string Ellipsis = "...";
        var truncated = text;
        while (truncated.Length > 0 && gfx.MeasureString(truncated + Ellipsis, font).Width > maxWidth)
        {
            truncated = truncated[..^1];
        }

        return (font, truncated.Length == 0 ? Ellipsis : truncated + Ellipsis);
    }

    private static Stream OpenEmbeddedForm()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream(EmbeddedFormResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded form resource '{EmbeddedFormResourceName}' was not found in {assembly.FullName}.");
    }
}
