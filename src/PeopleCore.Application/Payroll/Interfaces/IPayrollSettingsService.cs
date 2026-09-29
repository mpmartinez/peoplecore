using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayrollSettingsService
{
    /// <summary>Returns the company's settings, or entity defaults if none have been saved yet.</summary>
    Task<PayrollSettingsDto> GetAsync(Guid companyId, CancellationToken ct = default);
    Task UpdateAsync(Guid companyId, PayrollSettingsDto dto, CancellationToken ct = default);

    /// <summary>
    /// The settings row payroll computes from (<see cref="IPayrollSettingsRepository.GetDefaultAsync"/>),
    /// whichever company it belongs to; with no row, the defaults payroll computes with, for no company.
    /// </summary>
    Task<PayrollSettingsDto> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Changes the row payroll computes from. It keeps its own company, whatever <paramref name="dto"/>
    /// names. Refused when there is no row yet: the API seeds it with the first company.
    /// </summary>
    Task UpdateDefaultAsync(PayrollSettingsDto dto, CancellationToken ct = default);
}
