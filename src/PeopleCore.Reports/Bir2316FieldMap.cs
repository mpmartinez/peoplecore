namespace PeopleCore.Reports;

/// <summary>
/// Where each <see cref="PeopleCore.Application.Payroll.DTOs.Bir2316Dto"/> value lands on the
/// September 2021 ENCS form, in PDF points, origin bottom-left on the 612 x 936 page.
/// <para>
/// Derived from the blank form's own printed labels and box outlines rather than guessed. The
/// labels were read with a small <c>pypdf</c> script that walks the content stream's positioned
/// text runs (the technique <c>Bir2316StamperTests</c> also uses on the stamper's OUTPUT); the
/// box outlines — needed because a value sits beside or below its caption, not on top of it — were
/// read with <c>pdfplumber</c>'s <c>page.rects</c>, which (unlike a plain <c>get_drawings()</c>
/// pass) reliably reports every rectangle on this particular PDF. Both derivations are recorded in
/// full in the Task 2 report so the next person can re-run them when BIR revises the form.
/// </para>
/// <para>
/// Each entry's comment names the label and/or box it was measured against. A value on the same
/// line as its caption uses that caption's own baseline; a value in a box below its caption (the
/// Part I/II/III lines, which are typed on the ruled line under the label, not beside it) uses
/// that box's bottom edge plus a small fixed offset so the text sits inside the box rather than on
/// its border.
/// </para>
/// <para>
/// Money columns are right-aligned on the official form, flush against the box's right edge minus
/// a small margin — <see cref="Bir2316Stamper"/> resolves <see cref="Align.Right"/> by measuring
/// the formatted string and subtracting its width from <see cref="Field.X"/>, so <c>X</c> for a
/// right-aligned field is the column's right edge, not a left starting point.
/// </para>
/// </summary>
internal static class Bir2316FieldMap
{
    internal enum Align { Left, Right }

    internal readonly record struct Field(double X, double Y, Align Align = Align.Left);

    // ── Header ──────────────────────────────────────────────────────────────────────────────

    // "1 For the Year" at x=39.8 y=835.8; the year box (124.8-195.7, 826.6-841.7) brackets that
    // same baseline, so the value sits on the caption's own line.
    internal static readonly Field Year = new(128, 836);

    // "2 For the Period" / "From (MM/DD)" at y=835.8/826.1; box (386.8-456.7, 826.6-841.4).
    internal static readonly Field PeriodFrom = new(390, 836);

    // "To (MM/DD)" at x=476.6 y=826.1, same row; box (512.4-582.3, 826.6-841.4).
    internal static readonly Field PeriodTo = new(515, 836);

    // ── Part I - Employee Information ──────────────────────────────────────────────────────

    // "3 TIN" at x=39.8 y=807.2 - same baseline, right of the caption. The TIN digit-cell group
    // (identical layout is reused for items 12 and 16) starts at x=86.2; 95 clears the caption.
    internal static readonly Field EmployeeTin = new(95, 807);

    // "4 Employee's Name (Last Name, First Name, Middle Name)" at y=788.6 - written on the ruled
    // line below it (box 42.2-253.6, 772.5-787.5), not beside it. LastName/FirstName/MiddleName
    // combine into one "Last, First Middle" line, same as the QuestPDF renderer this replaces.
    internal static readonly Field EmployeeName = new(45, 777);

    // "5 RDO Code" at y=788.6, same row as item 4 - line below, box 264.5-302.5, 772.5-787.5.
    internal static readonly Field RdoCode = new(268, 777);

    // "6 Registered Address" at y=761.1 - line below, box 42.6-253.6, 744.6-759.4.
    internal static readonly Field RegisteredAddress = new(46, 749);

    // "6A ZIP Code" at y=761.1, same row - line below, box 261.4-309.4, 744.5-759.3.
    internal static readonly Field RegisteredZipCode = new(264, 749);

    // "6B Local Home Address" at y=735.4 - line below, box 42.6-253.6, 719.3-734.1.
    internal static readonly Field LocalHomeAddress = new(46, 723);

    // "6C ZIP Code" at y=735.4, same row - line below, box 261.5-309.4, 719.5-734.2.
    internal static readonly Field LocalZipCode = new(265, 723);

    // "6D Foreign Address" at y=709.8 - line below, box 42.6-309.4, 693.6-708.6.
    internal static readonly Field ForeignAddress = new(46, 698);

    // "7 Date of Birth (MM/DD/YYYY)" at y=682.5 - line below, digit cells 46.9-147.6, 667.3-681.9.
    internal static readonly Field DateOfBirth = new(50, 671);

    // "8 Contact Number" at y=682.5, same row - line below, box 167.2-309.4, 666.9-681.8.
    internal static readonly Field ContactNumber = new(170, 671);

    // "9 Statutory Minimum Wage rate per day" at y=653.7 - the box (209.9-308.6, 648.9-663.7)
    // brackets that baseline, so this sits beside the caption on the same line, like the money
    // columns, and is right-aligned the same way.
    internal static readonly Field StatutoryMinWagePerDay = new(303, 654, Align.Right);

    // "10 Statutory Minimum Wage rate per month" at y=635.2; box 209.9-308.6, 630.4-645.3.
    internal static readonly Field StatutoryMinWagePerMonth = new(303, 635, Align.Right);

    // "11 Minimum Wage Earner (MWE)..." checkbox at 47.7-61.5, 613.6-626.1 (item number "11"
    // baseline 617.6). Bir2316Dto.IsMinimumWageEarner is never set true today - see that
    // property's doc comment - but the box is mapped so it is ready the day it is.
    internal static readonly Field MinimumWageEarnerCheckbox = new(51, 617);

    // ── Part II - Employer Information (Present) ───────────────────────────────────────────

    // "12 TIN" at y=594.7 - same TIN digit-cell layout as item 3, starting at x=86.2.
    internal static readonly Field EmployerTin = new(95, 595);

    // "13 Employer's Name" at y=575.8 - line below, box 42.2-308.4, 560.0-575.0.
    internal static readonly Field EmployerName = new(45, 564);

    // "14 Registered Address" at y=550.0 - line below, box 42.6-253.6, 533.9-548.8.
    internal static readonly Field EmployerAddress = new(46, 538);

    // "14A ZIP Code" at y=550.0, same row - line below, box 259.3-307.3, 533.8-547.9.
    internal static readonly Field EmployerZipCode = new(262, 538);

    // "15 Type of Employer" / "Main Employer" caption at y=524.0/521.1; checkbox 115.9-129.6,
    // 517.3-529.8.
    internal static readonly Field MainEmployerCheckbox = new(119, 521);

    // "Secondary Employer" caption, same row; checkbox 199.6-213.4, 517.3-529.8.
    internal static readonly Field SecondaryEmployerCheckbox = new(203, 521);

    // ── Part III - Employer Information (Previous) ─────────────────────────────────────────

    // "16 TIN" at y=498.5 - same TIN digit-cell layout, starting at x=86.2.
    internal static readonly Field PrevEmployerTin = new(95, 499);

    // "17 Employer's Name" at y=479.7 - line below, box 42.2-308.4, 463.9-478.9.
    internal static readonly Field PrevEmployerName = new(45, 468);

    // "18 Registered Address" at y=453.2 - line below, box 43.0-254.1, 437.1-452.1.
    internal static readonly Field PrevEmployerAddress = new(46, 441);

    // "18A ZIP Code" at y=453.2, same row - line below, box 259.8-307.7, 437.0-452.0.
    internal static readonly Field PrevEmployerZipCode = new(263, 441);

    // ── Part IVA - Summary (left amount column; every box's right edge is ~309, so every
    //    right-aligned X below is 303 = edge minus a ~6pt margin) ───────────────────────────

    // "19 Gross Compensation Income from Present Employer..." baseline y=418.1; box 207.4-309.2,
    // 408.4-423.8.
    internal static readonly Field Item19_GrossCompensation = new(303, 418, Align.Right);

    // "20 Less: Total Non-Taxable/Exempt Compensation Income..." baseline y=398.7; box
    // 207.4-309.2, 388.9-404.4.
    internal static readonly Field Item20_LessNonTaxable = new(303, 399, Align.Right);

    // "21 Taxable Compensation Income from Present Employer..." baseline y=379.2; box
    // 207.4-309.2, 369.5-384.9.
    internal static readonly Field Item21_TaxableFromPresent = new(303, 379, Align.Right);

    // "22 Add: Taxable Compensation Income from Previous Employer..." baseline y=359.8; box
    // 207.4-309.2, 350.1-365.5.
    internal static readonly Field Item22_PrevTaxableCompensation = new(303, 360, Align.Right);

    // "23 Gross Taxable Compensation Income..." baseline y=340.3; box 207.8-309.7, 331.0-346.4.
    internal static readonly Field Item23_GrossTaxable = new(303, 340, Align.Right);

    // "24 Tax Due" baseline y=316.5; box 207.8-309.7, 311.6-327.0. Item 24 is always printed,
    // even ₱0.00 - see Bir2316Stamper.Stamp - because a zero tax due is a real, meaningful
    // result (it says withholding exceeded liability), unlike the other boxes here which are
    // legitimately blank when nothing was computed for them.
    internal static readonly Field Item24_TaxDue = new(303, 317, Align.Right);

    // "25A Present Employer" baseline y=291.7; box 207.4-309.2, 292.1-307.5.
    internal static readonly Field Item25A_PresentTaxWithheld = new(303, 292, Align.Right);

    // "25B Previous Employer, if applicable" baseline y=277.7; box 207.4-309.2, 272.3-287.7.
    internal static readonly Field Item25B_PrevTaxWithheld = new(303, 278, Align.Right);

    // "26 Total Amount of Taxes Withheld as adjusted..." baseline y=262.6; box 207.4-309.2,
    // 252.4-268.3.
    internal static readonly Field Item26_TotalTaxWithheld = new(303, 263, Align.Right);

    // "27 5% Tax Credit (PERA Act of 2008)" baseline y=239.2; box 207.4-309.1, 232.6-248.4.
    internal static readonly Field Item27_PeraTaxCredit = new(303, 239, Align.Right);

    // "28 Total Taxes Withheld..." baseline y=219.0; box 207.4-309.1, 213.1-228.9.
    internal static readonly Field Item28_TotalTaxes = new(303, 219, Align.Right);

    // ── Part IV-B Section A - Non-Taxable/Exempt (right amount column; every box's right edge
    //    is ~585.6, so every right-aligned X below is 579 = edge minus a ~6.6pt margin) ──────

    // "29 Basic Salary (including the exempt ₱250,000 & below) / or the Statutory Minimum Wage
    // of the MWE" - two-line caption, first line's baseline y=788.4 used; box 483.8-585.6,
    // 779.4-794.6.
    internal static readonly Field Item29_NonTaxableBasicSalary = new(579, 788, Align.Right);

    // "30 Holiday Pay (MWE)" baseline y=765.1; box 483.8-585.6, 760.4-775.5.
    internal static readonly Field Item30_HolidayPayMwe = new(579, 765, Align.Right);

    // "31 Overtime Pay (MWE)" baseline y=745.5; box 483.8-585.6, 740.7-755.6.
    internal static readonly Field Item31_OvertimePayMwe = new(579, 746, Align.Right);

    // "32 Night Shift Differential (MWE)" baseline y=725.5; box 483.8-585.6, 720.6-735.4.
    internal static readonly Field Item32_NightShiftDiffMwe = new(579, 726, Align.Right);

    // "33 Hazard Pay (MWE)" baseline y=705.3; box 483.8-585.6, 700.4-715.4.
    internal static readonly Field Item33_HazardPayMwe = new(579, 705, Align.Right);

    // "34 13th Month Pay and Other Benefits" (two-line caption, first line used) baseline
    // y=690.3; box 483.9-585.6, 681.0-695.9.
    internal static readonly Field Item34_ThirteenthMonthAndBenefits = new(579, 690, Align.Right);

    // "35 De Minimis Benefits" baseline y=666.9; box 483.8-585.6, 662.2-677.0.
    internal static readonly Field Item35_DeMinimis = new(579, 667, Align.Right);

    // "36 SSS, GSIS, PHIC & PAG-IBIG Contributions and Union Dues (Employee share only)"
    // baseline y=651.9; box 483.8-585.6, 642.9-657.5.
    internal static readonly Field Item36_SssPhicPagibigContributions = new(579, 652, Align.Right);

    // "37 Salaries and Other Forms of Compensation" baseline y=628.3; box 483.8-585.6,
    // 623.5-638.3.
    internal static readonly Field Item37_SalariesOtherForms = new(579, 628, Align.Right);

    // "38 Total Non-Taxable/Exempt Compensation Income (Sum of Items 29 to 37)" baseline
    // y=612.9; box 484.2-586.0, 604.1-619.3.
    internal static readonly Field Item38_TotalNonTaxable = new(579, 613, Align.Right);

    // ── Part IV-B Section B - Taxable Compensation Income Regular ─────────────────────────

    // "39 Basic Salary" baseline y=571.4; box 483.8-585.6, 566.5-581.5.
    internal static readonly Field Item39_BasicSalary = new(579, 571, Align.Right);

    // "40 Representation" baseline y=552.2; box 483.8-585.6, 547.1-562.0.
    internal static readonly Field Item40_Representation = new(579, 552, Align.Right);

    // "41 Transportation" baseline y=533.0; box 483.8-585.6, 527.8-543.1.
    internal static readonly Field Item41_Transportation = new(579, 533, Align.Right);

    // "42 Cost of Living Allowance (COLA)" baseline y=513.5; box 483.8-585.6, 508.4-523.6.
    internal static readonly Field Item42_Cola = new(579, 514, Align.Right);

    // "43 Fixed Housing Allowance" baseline y=494.4; box 483.8-585.6, 489.2-504.6.
    internal static readonly Field Item43_FixedHousing = new(579, 494, Align.Right);

    // "44 Others (specify)" / "44A" amount box 483.8-585.6, 461.8-477.2; baseline y=466.9.
    internal static readonly Field Item44A_OtherAmount = new(579, 467, Align.Right);

    // "44A" description box (the "(specify)" text field) 341.9-474.9, 461.8-477.2, same row.
    internal static readonly Field Item44A_OtherLabel = new(345, 467);

    // "44B" amount box 483.8-585.6, 443.8-459.2; baseline y=448.7.
    internal static readonly Field Item44B_OtherAmount = new(579, 449, Align.Right);

    // "44B" description box 342.4-475.2, 443.4-458.8, same row.
    internal static readonly Field Item44B_OtherLabel = new(345, 449);

    // ── SUPPLEMENTARY ───────────────────────────────────────────────────────────────────────

    // "45 Commission" baseline y=423.2; box 483.8-585.6, 418.1-432.9.
    internal static readonly Field Item45_Commission = new(579, 423, Align.Right);

    // "46 Profit Sharing" baseline y=404.0; box 483.8-585.6, 398.7-414.1.
    internal static readonly Field Item46_ProfitSharing = new(579, 404, Align.Right);

    // "47 Fees Including Director's Fees" baseline y=384.5; box 483.8-585.6, 379.2-394.6.
    internal static readonly Field Item47_Fees = new(579, 385, Align.Right);

    // "48 Taxable 13th Month Benefits" baseline y=365.1; box 483.8-585.6, 359.8-375.2.
    internal static readonly Field Item48_TaxableThirteenthMonth = new(579, 365, Align.Right);

    // "49 Hazard Pay" baseline y=345.6; box 483.8-585.6, 340.4-355.8.
    internal static readonly Field Item49_HazardPay = new(579, 346, Align.Right);

    // "50 Overtime Pay" baseline y=326.2; box 483.8-585.6, 320.9-336.3.
    internal static readonly Field Item50_OvertimePay = new(579, 326, Align.Right);

    // "51 Others (specify)" / "51A" amount box 483.8-585.6, 291.8-307.2; baseline y=297.0.
    internal static readonly Field Item51A_OtherAmount = new(579, 297, Align.Right);

    // "51A" description box 341.4-477.0, 291.8-307.2, same row.
    internal static readonly Field Item51A_OtherLabel = new(344, 297);

    // "51B" amount box 483.8-585.6, 272.3-287.7; baseline y=277.6.
    internal static readonly Field Item51B_OtherAmount = new(579, 278, Align.Right);

    // "51B" description box 342.4-477.5, 272.3-287.7, same row.
    internal static readonly Field Item51B_OtherLabel = new(345, 278);

    // "52 Total Taxable Compensation Income (Sum of Items 39 to 51B)" baseline y=262.6; box
    // 483.8-585.6, 254.3-268.6.
    internal static readonly Field Item52_TotalTaxableCompensation = new(579, 263, Align.Right);
}
