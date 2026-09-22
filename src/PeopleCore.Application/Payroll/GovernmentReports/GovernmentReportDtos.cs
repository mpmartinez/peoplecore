namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// One agency's report - a single month for SSS, PhilHealth, Pag-IBIG and BIR 1601-C, a whole year
/// for the BIR 1604-C alphalist (see <see cref="IsAnnual"/>) - shaped as a table (or, for the
/// alphalist, <see cref="Sections"/> of tables) so the page and the CSV render every report the
/// same way. <see cref="Summary"/> carries BIR 1601-C's form lines and is empty for the others.
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
    IReadOnlyList<string> Warnings,
    IReadOnlyList<GovernmentReportSectionDto> Sections)
{
    /// <summary>The annual reports (the 1604-C alphalist) use Month = 0.</summary>
    public bool IsAnnual => Month == 0;
}

/// <param name="MissingNumber">The employee has no ID number for this agency.</param>
public sealed record GovernmentReportRowDto(Guid EmployeeId, IReadOnlyList<string> Cells, bool MissingNumber);

public sealed record GovernmentReportLineDto(string Label, decimal Amount);

/// <summary>
/// One table of a report that has several - the 1604-C alphalist's groups. A report with a single
/// table (every monthly report) uses the top-level Columns, Rows and Totals and no sections.
/// </summary>
public sealed record GovernmentReportSectionDto(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<GovernmentReportRowDto> Rows,
    IReadOnlyList<string> Totals,
    string EmptyMessage);

/// <param name="AgencyNumber">The employer's number with this report's agency; the TIN for 1601-C.</param>
public sealed record GovernmentReportEmployerDto(string Name, string? Address, string Tin, string? RdoCode, string AgencyNumber);
