using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayrollSettingsService
{
    /// <summary>Returns the company's settings, or entity defaults if none have been saved yet.</summary>
    Task<PayrollSettingsDto> GetAsync(Guid companyId, CancellationToken ct = default);
    Task UpdateAsync(Guid companyId, PayrollSettingsDto dto, CancellationToken ct = default);
}
