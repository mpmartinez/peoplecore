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
        => await Context.PayrollSettings.FirstOrDefaultAsync(ct);
}
