using System.Globalization;
using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir1604CAlphalistTests
{
    private static readonly GovernmentReportEmployerDto Employer = new("Acme", null, "123-456-789-000", "050", "123-456-789-000");

    private static Bir2316Dto Form(string last, string first, string tin = "111-222-333-000",
        decimal basic = 400_000m, decimal presentWithheld = 30_000m, decimal prevTaxable = 0m, decimal prevWithheld = 0m,
        decimal pera = 0m) => new()
    {
        Year = 2026, EmployeeTin = tin, EmployeeLastName = last, EmployeeFirstName = first, EmployeeMiddleName = "",
        Item39_BasicSalary = basic, Item36_SssPhicPagibigContributions = 20_000m, Item34_ThirteenthMonthAndBenefits = 33_333.33m,
        Item48_TaxableThirteenthMonth = 0m, Item25A_PresentTaxWithheld = presentWithheld,
        Item22_PrevTaxableCompensation = prevTaxable, Item25B_PrevTaxWithheld = prevWithheld, Item27_PeraTaxCredit = pera
    };

    private static Bir1604CAlphalist.Person Person(Bir2316Dto form, DateOnly? separated = null, DateOnly? hired = null,
        decimal janToNov = 27_500m, decimal december = 2_500m)
        => new(Guid.NewGuid(), form, hired ?? new DateOnly(2020, 1, 6), separated, janToNov, december);

    [Fact]
    public void GroupsEmployees_TheWayBirSchedulesDo()
    {
        var left = Person(Form("Reyes", "Ana"), separated: new DateOnly(2026, 6, 30));
        var stayedToYearEnd = Person(Form("Cruz", "Juan"), separated: new DateOnly(2026, 12, 31));
        var withPrevious = Person(Form("Santos", "Maria", prevTaxable: 80_000m, prevWithheld: 4_000m));

        var report = Bir1604CAlphalist.Build(2026, Employer, [left, stayedToYearEnd, withPrevious], unpaidRuns: 0);

        report.Sections.Select(s => s.Title).Should().Equal(
            "Terminated before December 31",
            "Employed as of December 31, no previous employer",
            "Employed as of December 31, with previous employer");
        report.Sections[0].Rows.Select(r => r.Cells[1]).Should().Equal("Reyes");
        report.Sections[1].Rows.Select(r => r.Cells[1]).Should().Equal(new[] { "Cruz" }, "separated on December 31 isn't 'before'");
        report.Sections[2].Rows.Select(r => r.Cells[1]).Should().Equal("Santos");
    }

    [Fact]
    public void ARow_ShowsThe2316sFigures_TheWithheldSplit_AndWhatIsLeftToCollect()
    {
        // 2316 derived totals: gross = non-taxable (33,333.33 + 20,000) + taxable (400,000) = 453,333.33.
        var form = Form("Cruz", "Juan");
        var report = Bir1604CAlphalist.Build(2026, Employer, [Person(form)], unpaidRuns: 0);

        var columns = report.Sections[1].Columns;
        var cells = report.Sections[1].Rows.Single().Cells;
        string Cell(string column) => cells[columns.ToList().IndexOf(column)];

        Cell("TIN").Should().Be("111-222-333-000");
        Cell("Employed from").Should().Be("2026-01-01", "clamped to the year");
        Cell("Employed to").Should().Be("2026-12-31");
        Cell("Gross compensation").Should().Be(GovernmentReportMath.Money(form.Item19_GrossCompensation));
        Cell("Total non-taxable").Should().Be(GovernmentReportMath.Money(form.Item38_TotalNonTaxable));
        Cell("Basic salary").Should().Be("400000.00");
        Cell("Other taxable compensation").Should().Be(
            GovernmentReportMath.Money(form.Item52_TotalTaxableCompensation - form.Item39_BasicSalary - form.Item48_TaxableThirteenthMonth));
        Cell("Tax due").Should().Be(GovernmentReportMath.Money(form.Item24_TaxDue));
        Cell("Tax withheld, January to November").Should().Be("27500.00");
        Cell("Tax withheld, December").Should().Be("2500.00");
        Cell("Total tax withheld").Should().Be(GovernmentReportMath.Money(form.Item26_TotalTaxWithheld));
        Cell("To collect / (refund)").Should().Be(
            GovernmentReportMath.Money(form.Item24_TaxDue - form.Item26_TotalTaxWithheld - form.Item27_PeraTaxCredit));
    }

    [Fact]
    public void ASeparationInAnEarlierYear_StillLandsInTerminated_WithBothDatesClampedToTheYear()
    {
        // Left in December 2025; final pay (and so this year's 2316) released in 2026. Still not
        // "employed as of December 31, 2026" -- BIR's group 1 explicitly includes this case.
        var priorYearSeparation = Person(Form("Reyes", "Ana"), separated: new DateOnly(2025, 12, 15));

        var report = Bir1604CAlphalist.Build(2026, Employer, [priorYearSeparation], unpaidRuns: 0);

        report.Sections[0].Rows.Select(r => r.Cells[1]).Should().Equal("Reyes");
        var columns = report.Sections[0].Columns;
        var cells = report.Sections[0].Rows.Single().Cells;
        cells[columns.ToList().IndexOf("Employed from")].Should().Be("2026-01-01", "a prior-year separation shows the year's first day for both");
        cells[columns.ToList().IndexOf("Employed to")].Should().Be("2026-01-01");
    }

    [Fact]
    public void HiredAndSeparatedWithinTheYear_ShowsThoseActualDates()
    {
        var person = Person(Form("Cruz", "Juan"), hired: new DateOnly(2026, 3, 1), separated: new DateOnly(2026, 9, 30));

        var report = Bir1604CAlphalist.Build(2026, Employer, [person], unpaidRuns: 0);

        var columns = report.Sections[0].Columns;
        var cells = report.Sections[0].Rows.Single().Cells;
        cells[columns.ToList().IndexOf("Employed from")].Should().Be("2026-03-01");
        cells[columns.ToList().IndexOf("Employed to")].Should().Be("2026-09-30");
    }

    [Fact]
    public void TerminatedAndWithPreviousEmployerGroups_HaveThePreviousEmployerColumns_NotTheNoPreviousGroup()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Santos", "Maria", prevTaxable: 80_000m, prevWithheld: 4_000m))], unpaidRuns: 0);

        report.Sections[0].Columns.Should().Contain(["Previous employer's taxable compensation", "Previous employer's tax withheld"]);
        report.Sections[2].Columns.Should().Contain(["Previous employer's taxable compensation", "Previous employer's tax withheld"]);
        report.Sections[1].Columns.Should().NotContain("Previous employer's taxable compensation");
        var cells = report.Sections[2].Rows.Single().Cells;
        cells[report.Sections[2].Columns.ToList().IndexOf("Previous employer's tax withheld")].Should().Be("4000.00");
    }

    [Fact]
    public void ASeparatedEmployeeWithAPreviousEmployer_ShowsThosePreviousEmployerColumns_InTerminated()
    {
        // Someone who left during the year can also have had an earlier employer that same year,
        // so the terminated group carries the previous-employer columns too.
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Santos", "Maria", prevTaxable: 80_000m, prevWithheld: 4_000m), separated: new DateOnly(2026, 6, 30))],
            unpaidRuns: 0);

        report.Sections[0].Columns.Should().Contain(["Previous employer's taxable compensation", "Previous employer's tax withheld"]);
        var cells = report.Sections[0].Rows.Single().Cells;
        cells[report.Sections[0].Columns.ToList().IndexOf("Previous employer's tax withheld")].Should().Be("4000.00");
    }

    [Fact]
    public void EachGroup_HasATotalsRow_AndAnEmptyGroupSaysSo()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Cruz", "Juan", basic: 100_000m)), Person(Form("Dizon", "Rosa", basic: 200_000m))], unpaidRuns: 0);

        var group = report.Sections[1];
        group.Totals[0].Should().Be("Total");
        group.Totals[group.Columns.ToList().IndexOf("Basic salary")].Should().Be("300000.00");
        report.Sections[0].Rows.Should().BeEmpty();
        report.Sections[0].EmptyMessage.Should().Be("No employees in this group.");
    }

    [Fact]
    public void RowsAreSortedByLastNameThenFirstName()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Santos", "Ana")), Person(Form("Cruz", "Juan")), Person(Form("Cruz", "Ben"))], unpaidRuns: 0);

        report.Sections[1].Rows.Select(r => $"{r.Cells[1]}, {r.Cells[2]}").Should().Equal("Cruz, Ben", "Cruz, Juan", "Santos, Ana");
    }

    [Fact]
    public void Warns_AboutMissingTins_UnpaidRuns_AndHiresWithNoPreviousEmployer()
    {
        var noTin = Person(Form("Cruz", "Juan", tin: ""));
        var hiredThisYear = Person(Form("Reyes", "Ana"), hired: new DateOnly(2026, 4, 1));

        var report = Bir1604CAlphalist.Build(2026, Employer, [noTin, hiredThisYear], unpaidRuns: 2);

        report.Sections[1].Rows.Single(r => r.Cells[1] == "Cruz").MissingNumber.Should().BeTrue();
        report.Warnings.Should().Contain("1 employee has no TIN.");
        report.Warnings.Should().Contain("2 payroll runs paid this year aren't paid yet and aren't included.");
        report.Warnings.Should().Contain(
            "1 employee hired this year has no previous employer entered. If they worked elsewhere earlier in the year, add it on their BIR 2316 so they move to the right group.");
        report.Warnings.Should().Contain(w => w.Contains("minimum wage earner"));
    }

    [Fact]
    public void ToCollectOrRefund_DropsByThePeraTaxCredit()
    {
        const decimal pera = 1_000m;
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Cruz", "Juan")), Person(Form("Dizon", "Rosa", pera: pera))], unpaidRuns: 0);

        var columns = report.Sections[1].Columns;
        var rows = report.Sections[1].Rows;
        decimal Collect(string last) => decimal.Parse(
            rows.Single(r => r.Cells[1] == last).Cells[columns.ToList().IndexOf("To collect / (refund)")],
            CultureInfo.InvariantCulture);

        (Collect("Cruz") - Collect("Dizon")).Should().Be(pera);
    }

    [Fact]
    public void ToCollectOrRefund_PinsTheComputedAmount_ForATypicalEmployee()
    {
        // Item23 (gross taxable) = Item52 (basic salary only here) = 400,000.
        // BirWithholdingTax bracket 250,000-400,000: base 0 + 15% of (400,000 - 250,000) = 22,500 = Item24 (tax due).
        // Item26 (total tax withheld) = present withheld 30,000 (no previous employer). Item27 (PERA) = 0.
        // To collect / (refund) = 22,500 - 30,000 - 0 = -7,500 (a refund).
        var report = Bir1604CAlphalist.Build(2026, Employer, [Person(Form("Cruz", "Juan"))], unpaidRuns: 0);

        var columns = report.Sections[1].Columns;
        var cells = report.Sections[1].Rows.Single().Cells;
        cells[columns.ToList().IndexOf("To collect / (refund)")].Should().Be("-7500.00");
    }

    [Fact]
    public void TheReport_IsAnnual_WithTheAlphalistKey()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer, [], unpaidRuns: 0);

        report.Report.Should().Be("1604c");
        report.IsAnnual.Should().BeTrue();
        report.Basis.Should().Be("Paid in 2026");
        report.Columns.Should().BeEmpty();
    }
}
