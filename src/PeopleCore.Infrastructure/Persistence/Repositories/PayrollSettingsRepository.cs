using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class PayrollSettingsRepository : Repository<PayrollSettings>, IPayrollSettingsRepository
{
    public PayrollSettingsRepository(AppDbContext context) : base(context) { }

    public async Task<PayrollSettings?> GetByCompanyIdAsync(Guid companyId, CancellationToken ct = default)
        => await Context.PayrollSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);

    public async Task<PayrollSettings?> GetDefaultAsync(CancellationToken ct = default)
    {
        // PayrollRun carries no CompanyId, so there is no way to pick the "right" settings row
        // if more than one exists - rate resolution would silently use whichever row EF returns
        // first, and a second company's DailyRateFactor/SSS overrides could wrong every run's
        // computed wages without any error. Fail loudly instead until per-company payroll runs
        // are designed.
        var settings = await Context.PayrollSettings.Take(2).ToListAsync(ct);
        if (settings.Count > 1)
            throw new InvalidOperationException(
                "Multiple PayrollSettings rows exist, but PayrollRun carries no CompanyId, so " +
                "per-company payroll settings are not yet supported. Resolve which settings " +
                "row should apply before adding a second row.");

        return settings.SingleOrDefault();
    }
}
