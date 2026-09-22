using System.Text;
using FluentAssertions;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportCsvTests
{
    private static GovernmentReportDto Report(IReadOnlyList<string>? rowCells = null, IReadOnlyList<GovernmentReportLineDto>? summary = null) => new(
        "sss", "SSS contributions", 2026, 3, "Pay earned in March 2026",
        new GovernmentReportEmployerDto("Acme, Inc.", null, "123-456-789-000", "050", "03-9999999-1"),
        ["Employee", "SSS number", "Total"],
        [new GovernmentReportRowDto(Guid.NewGuid(), rowCells ?? ["Cruz, Juan", "34-1234567-8", "3030.00"], false)],
        ["Total", "", "3030.00"],
        summary ?? [],
        [],
        []);

    private static string Text(byte[] bytes)
    {
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble(), "Excel needs the BOM to read UTF-8");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public void Write_PutsTheEmployerAboveTheTable_AndQuotesCommas()
    {
        var lines = Text(GovernmentReportCsv.Write(Report())).Split("\r\n");

        lines.Should().StartWith([
            "\"Acme, Inc.\"",
            "SSS contributions,Pay earned in March 2026",
            "TIN,123-456-789-000",
            "Employer number,03-9999999-1",
            "",
            "Employee,SSS number,Total",
            "\"Cruz, Juan\",34-1234567-8,3030.00",
            "Total,,3030.00"
        ]);
    }

    [Fact]
    public void Write_Adds1601CsLinesBelowTheTable()
    {
        var text = Text(GovernmentReportCsv.Write(Report(summary: [new("Total taxes withheld", 9_000m)])));

        text.Should().EndWith("\r\nTotal taxes withheld,9000.00\r\n");
    }

    [Fact]
    public void FileName_IsTheReportAndMonth()
    {
        GovernmentReportCsv.FileName(Report()).Should().Be("sss-2026-03.csv");
    }

    [Fact]
    public void Write_PrefixesHyperlinkFormulaWithQuoteAndQuotesCell()
    {
        var cell = "=HYPERLINK(\"http://x\",\"y\")";
        var lines = Text(GovernmentReportCsv.Write(Report([cell, "col2", "col3"]))).Split("\r\n");

        lines[6].Should().Contain("'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")");
    }

    [Fact]
    public void Write_NeutralizesAtAndPlusFormulas()
    {
        var atCell = "@SUM(A1)";
        var plusCell = "+639171234567";
        var lines = Text(GovernmentReportCsv.Write(Report([atCell, plusCell, "col3"]))).Split("\r\n");

        lines[6].Should().Be("'@SUM(A1),'+639171234567,col3");
    }

    [Fact]
    public void Write_KeepsNegativeNumbersUnchanged()
    {
        var moneyCell = "-50.00";
        var lines = Text(GovernmentReportCsv.Write(Report([moneyCell, "col2", "col3"]))).Split("\r\n");

        lines[6].Should().Be("-50.00,col2,col3");
    }

    [Fact]
    public void Write_QuotesEmbeddedDoubleQuotes()
    {
        var cellWithQuote = "Juan \"JJ\" Cruz";
        var lines = Text(GovernmentReportCsv.Write(Report([cellWithQuote, "col2", "col3"]))).Split("\r\n");

        lines[6].Should().Be("\"Juan \"\"JJ\"\" Cruz\",col2,col3");
    }

    [Fact]
    public void Write_PutsEachSectionUnderItsTitle_WithABlankLineBetween()
    {
        var report = new GovernmentReportDto(
            "1604c", "BIR 1604-C alphalist", 2026, 0, "Paid in 2026",
            new GovernmentReportEmployerDto("Acme", null, "123-456-789-000", "050", "123-456-789-000"),
            [], [], [], [], [],
            [
                new GovernmentReportSectionDto("Terminated before December 31", ["Employee", "Tax due"],
                    [new GovernmentReportRowDto(Guid.NewGuid(), ["Cruz, Juan", "1000.00"], false)], ["Total", "1000.00"], "No employees in this group."),
                new GovernmentReportSectionDto("Employed as of December 31, with previous employer", ["Employee", "Tax due"],
                    [], [], "No employees in this group.")
            ]);

        var text = Encoding.UTF8.GetString(GovernmentReportCsv.Write(report)[3..]);

        text.Should().Contain("\r\nTerminated before December 31\r\nEmployee,Tax due\r\n\"Cruz, Juan\",1000.00\r\nTotal,1000.00\r\n\r\n");
        // The title itself contains a comma, so - like every other cell - it goes through Quote() and is RFC 4180 quoted.
        text.Should().EndWith("\"Employed as of December 31, with previous employer\"\r\nNo employees in this group.\r\n");
    }

    [Fact]
    public void FileName_ForAnAnnualReport_HasNoMonth()
    {
        var report = new GovernmentReportDto("1604c", "BIR 1604-C alphalist", 2026, 0, "Paid in 2026",
            new GovernmentReportEmployerDto("Acme", null, "", null, ""), [], [], [], [], [], []);

        GovernmentReportCsv.FileName(report).Should().Be("1604c-2026.csv");
    }
}
