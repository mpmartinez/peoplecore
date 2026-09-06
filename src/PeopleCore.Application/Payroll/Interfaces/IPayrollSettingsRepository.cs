using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayrollSettingsRepository : IRepository<PayrollSettings>
{
    Task<PayrollSettings?> GetByCompanyIdAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// The settings row payroll computation resolves statutory rates from. PayrollRun and
    /// PayrollRunEmployee carry no CompanyId - see IPayrollRunRepository's remarks on what a run
    /// tracks - so a run cannot yet be matched to one company's settings; this returns whichever
    /// row exists, which is correct only for a single-company deployment. Threading CompanyId
    /// onto PayrollRun for true multi-company rate resolution is out of this task's scope.
    /// </summary>
    Task<PayrollSettings?> GetDefaultAsync(CancellationToken ct = default);
}
