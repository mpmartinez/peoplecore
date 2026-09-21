namespace PeopleCore.Application.Payroll.GovernmentReports;

public interface IGovernmentReportService
{
    /// <summary>
    /// One agency's report for a month: <c>sss</c>, <c>philhealth</c>, <c>pagibig</c> or <c>1601c</c>.
    /// Throws <see cref="KeyNotFoundException"/> for any other name, and
    /// <see cref="Domain.Exceptions.DomainException"/> for a month outside 1-12 or still ahead.
    /// </summary>
    Task<GovernmentReportDto> BuildAsync(string report, int year, int month, CancellationToken ct = default);
}
