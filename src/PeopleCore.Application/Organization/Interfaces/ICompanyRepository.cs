using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Application.Organization.Interfaces;

public interface ICompanyRepository : IRepository<Company>
{
    /// <summary>
    /// The single Company row a single-company deployment carries - the identity payslips and
    /// other company-branded documents print. Null when the database has not been seeded.
    /// </summary>
    Task<Company?> GetDefaultAsync(CancellationToken ct = default);
}
