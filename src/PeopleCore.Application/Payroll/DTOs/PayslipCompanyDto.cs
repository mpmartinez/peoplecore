namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// The company identity a payslip prints. Sourced from Organization.Company, NOT from
/// PayrollSettings: Phase 1 deliberately split company identity from statutory rates, and
/// reintroducing a combined "company settings" concept here would undo that.
/// </summary>
public record PayslipCompanyDto(
    string CompanyName,
    string? Address,
    string City,
    string? ContactNumber,
    string? Email);
