using System.Text;
using FluentAssertions;
using PeopleCore.Infrastructure.Attendance;

namespace PeopleCore.Infrastructure.Tests.Attendance;

/// <summary>
/// The downloadable template has to be something the import reads: uploaded unchanged, either form
/// yields exactly its example rows, and nothing is refused.
/// </summary>
public class AttendanceImportTemplateTests
{
    private static readonly (string, DateTime)[] ExampleRows =
    [
        ("EMP-001", new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc)),
        ("EMP-001", new DateTime(2026, 3, 10, 17, 0, 0, DateTimeKind.Utc)),
        ("EMP-001", new DateTime(2026, 3, 11, 7, 55, 0, DateTimeKind.Utc)),
        ("EMP-001", new DateTime(2026, 3, 11, 17, 30, 0, DateTimeKind.Utc)),
        ("EMP-002", new DateTime(2026, 3, 10, 8, 10, 0, DateTimeKind.Utc)),
    ];

    [Fact]
    public void The_CSV_template_imports_as_its_example_rows()
    {
        var result = AttendanceFile.Parse(new MemoryStream(AttendanceImportTemplate.Csv()), "attendance-import-template.csv");

        result.Layout.Should().Be(AttendanceFileLayout.DailyInOut);
        result.Errors.Should().BeEmpty();
        result.Punches.Select(p => (p.EmployeeNumber, p.PunchTime)).Should().Equal(ExampleRows);
    }

    [Fact]
    public void The_CSV_template_starts_with_a_BOM_so_Excel_opens_it_as_UTF8()
    {
        AttendanceImportTemplate.Csv().Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
    }

    [Fact]
    public void The_Excel_template_imports_as_its_example_rows()
    {
        var result = AttendanceFile.Parse(new MemoryStream(AttendanceImportTemplate.Xlsx()), "attendance-import-template.xlsx");

        result.Layout.Should().Be(AttendanceFileLayout.DailyInOut);
        result.Errors.Should().BeEmpty();
        result.Punches.Select(p => (p.EmployeeNumber, p.PunchTime)).Should().Equal(ExampleRows);
    }
}
