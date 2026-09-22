using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class Bir2316InputsRepository : Repository<Bir2316Inputs>, IBir2316InputsRepository
{
    public Bir2316InputsRepository(AppDbContext context) : base(context) { }

    public async Task<Bir2316Inputs?> GetAsync(Guid employeeId, int year, CancellationToken ct = default)
        => await Context.Bir2316Inputs.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == year, ct);

    public async Task<IReadOnlyDictionary<Guid, Bir2316Inputs>> GetForYearAsync(int year, CancellationToken ct = default)
        => await Context.Bir2316Inputs.Where(x => x.Year == year).ToDictionaryAsync(x => x.EmployeeId, ct);

    public async Task SaveAsync(Guid employeeId, int year, Bir2316ManualInputs inputs, CancellationToken ct = default)
    {
        var row = await GetAsync(employeeId, year, ct);
        if (row is null)
        {
            row = new Bir2316Inputs { EmployeeId = employeeId, Year = year };
            Context.Bir2316Inputs.Add(row);
        }

        row.Apply(inputs);
        await Context.SaveChangesAsync(ct);
    }
}
