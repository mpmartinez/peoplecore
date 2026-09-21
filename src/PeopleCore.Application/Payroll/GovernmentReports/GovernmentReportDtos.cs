namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// One agency's report for one month, shaped as a table so the page and the CSV render every
/// report the same way. <see cref="Summary"/> carries BIR 1601-C's form lines and is empty for
/// the others.
/// </summary>
public sealed record GovernmentReportDto(
    string Report,
    string Title,
    int Year,
    int Month,
    string Basis,
    GovernmentReportEmployerDto Employer,
    IReadOnlyList<string> Columns,
    IReadOnlyList<GovernmentReportRowDto> Rows,
    IReadOnlyList<string> Totals,
    IReadOnlyList<GovernmentReportLineDto> Summary,
    IReadOnlyList<string> Warnings);

/// <param name="MissingNumber">The employee has no ID number for this agency.</param>
public sealed record GovernmentReportRowDto(Guid EmployeeId, IReadOnlyList<string> Cells, bool MissingNumber);

public sealed record GovernmentReportLineDto(string Label, decimal Amount);

/// <param name="AgencyNumber">The employer's number with this report's agency; the TIN for 1601-C.</param>
public sealed record GovernmentReportEmployerDto(string Name, string? Address, string Tin, string? RdoCode, string AgencyNumber);
