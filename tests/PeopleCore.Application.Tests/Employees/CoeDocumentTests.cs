using FluentAssertions;
using PeopleCore.Application.Employees.Coe;
using PeopleCore.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class CoeDocumentTests
{
    public CoeDocumentTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void Renders_a_non_empty_pdf_for_a_typical_certificate()
    {
        var pdf = new CoeDocument(Content()).GeneratePdf();

        pdf.Should().NotBeNullOrEmpty();
        pdf.Take(5).Should().Equal("%PDF-"u8.ToArray(), "output must be a real PDF");
    }

    [Fact]
    public void Renders_with_a_company_logo()
    {
        var logo = TinyPngBytes();
        var content = Content() with { Logo = logo };

        var act = () => new CoeDocument(content).GeneratePdf();

        act.Should().NotThrow();
    }

    [Fact]
    public void Renders_without_a_company_logo()
    {
        var content = Content() with { Logo = null };

        var act = () => new CoeDocument(content).GeneratePdf();

        act.Should().NotThrow();
    }

    [Fact]
    public void Renders_when_names_and_addresses_are_unusually_long()
    {
        var content = Content() with
        {
            CompanyName = new string('M', 200),
            CompanyAddress = new string('A', 200),
            SignatoryName = new string('S', 200),
            Paragraphs =
            [
                "This is to certify that " + new string('N', 300) + " has been employed by " + new string('C', 200) + " from March 1, 2021 to present.",
                "This certification is issued upon the request of the employee for whatever legal purpose it may serve."
            ]
        };

        var act = () => new CoeDocument(content).GeneratePdf();

        act.Should().NotThrow("QuestPDF throws on layout overflow rather than truncating");
    }

    private static CoeContent Content() => new(
        CompanyName: "Acme Inc.",
        CompanyAddress: "123 Ayala Ave, Makati",
        Logo: null,
        Title: "CERTIFICATE OF EMPLOYMENT",
        Paragraphs:
        [
            "This is to certify that Juan Santos Cruz has been employed by Acme Inc. as Payroll Officer from March 1, 2021 to present.",
            "This certification is issued upon the request of the employee for whatever legal purpose it may serve."
        ],
        DateLine: "Issued on September 22, 2026.",
        SignatoryName: "Maria Reyes",
        SignatoryTitle: "HR Manager");

    // A minimal valid 1x1 PNG, just enough for QuestPDF/SkiaSharp to decode as an image.
    private static byte[] TinyPngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
