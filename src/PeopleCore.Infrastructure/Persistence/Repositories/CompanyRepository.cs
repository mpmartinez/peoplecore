using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class CompanyRepository : Repository<Company>, ICompanyRepository
{
    public CompanyRepository(AppDbContext context) : base(context) { }

    public async Task<Company?> GetDefaultAsync(CancellationToken ct = default)
        => await Context.Companies.FirstOrDefaultAsync(ct);
}
