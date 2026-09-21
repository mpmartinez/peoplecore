using System.Text;
using FluentAssertions;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportCsvTests
{
    private static GovernmentReportDto Report(IReadOnlyList<GovernmentReportLineDto>? summary = null) => new(
        "sss", "SSS contributions", 2026, 3, "Pay earned in March 2026",
        new GovernmentReportEmployerDto("Acme, Inc.", null, "123-456-789-000", "050", "03-9999999-1"),
        ["Employee", "SSS number", "Total"],
        [new GovernmentReportRowDto(Guid.NewGuid(), ["Cruz, Juan", "34-1234567-8", "3030.00"], false)],
        ["Total", "", "3030.00"],
        summary ?? [],
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
        var text = Text(GovernmentReportCsv.Write(Report([new("Total taxes withheld", 9_000m)])));

        text.Should().EndWith("\r\nTotal taxes withheld,9000.00\r\n");
    }

    [Fact]
    public void FileName_IsTheReportAndMonth()
    {
        GovernmentReportCsv.FileName(Report()).Should().Be("sss-2026-03.csv");
    }
}
