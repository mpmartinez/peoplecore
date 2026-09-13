using System.Net;

namespace PeopleCore.Web.Tests.Pages.Payroll;

/// <summary>JSON bodies and responses shared by the payroll page tests.</summary>
internal static class PayrollTestData
{
    /// <summary>The first bytes of a PDF - enough for a page to base64 and hand to the download script.</summary>
    public static readonly byte[] PdfBytes = "%PDF-1.7"u8.ToArray();

    public static string PdfBase64 => Convert.ToBase64String(PdfBytes);

    public static HttpResponseMessage Pdf() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(PdfBytes) };

    public static string Employee(Guid id, string number, string first, string last, bool isActive = true) =>
        $$"""
        {"id":"{{id}}","employeeNumber":"{{number}}","firstName":"{{first}}","lastName":"{{last}}",
         "fullName":"{{first}} {{last}}","workEmail":"{{first.ToLowerInvariant()}}@company.test",
         "departmentName":"Finance","positionTitle":"Accountant","employmentStatus":"Regular","isActive":{{(isActive ? "true" : "false")}}}
        """;

    public static string EmployeePage(int page, int totalPages, params string[] employees) =>
        $$"""{"items":[{{string.Join(",", employees)}}],"totalCount":{{employees.Length}},"page":{{page}},"pageSize":100,"totalPages":{{totalPages}}}""";
}
