using FluentAssertions;
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
    public void Stamp_IsDeterministic()
    {
        var dto = SampleDto();
        var a = new Bir2316Stamper().Stamp(dto);
        var b = new Bir2316Stamper().Stamp(dto);

        // Font resolution must not depend on the machine. If this fails, the resolver is
        // falling back to a system font rather than the embedded one.
        a.Should().Equal(b);
    }

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
}
