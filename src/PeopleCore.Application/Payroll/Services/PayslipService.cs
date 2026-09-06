using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Assembles payslip data and hands it to <see cref="IPayslipRenderer"/>. Deliberately reuses
/// <see cref="IPayrollRunService.GetAsync"/> rather than re-reading
/// <c>IPayrollRunRepository.GetWithEntriesAsync</c> and re-mapping to <see cref="PayrollRunDto"/>
/// / <see cref="PayrollRunEmployeeDto"/> itself: PayrollRunService already owns that mapping, a
/// payslip is not a hot path, and duplicating the mapping here would let the two drift onto two
/// different views of the same run.
/// <para>
/// <see cref="GetMyPayslipsAsync"/> is the one exception - it needs every run a single employee
/// appears in, which IPayrollRunService has no query for, so it goes to
/// <see cref="IPayrollRunRepository"/> directly rather than growing that interface for a
/// single-employee lookup only this method needs.
/// </para>
/// </summary>
public class PayslipService : IPayslipService
{
    private readonly IPayrollRunService _runService;
    private readonly IPayrollRunRepository _runRepository;
    private readonly ICompanyRepository _companyRepo;
    private readonly IPayslipRenderer _renderer;

    public PayslipService(
        IPayrollRunService runService,
        IPayrollRunRepository runRepository,
        ICompanyRepository companyRepo,
        IPayslipRenderer renderer)
    {
        _runService = runService;
        _runRepository = runRepository;
        _companyRepo = companyRepo;
        _renderer = renderer;
    }

    public async Task<byte[]?> GenerateAsync(Guid runId, Guid employeeId, CancellationToken ct = default)
    {
        var run = await _runService.GetAsync(runId, ct);
        if (run is null)
            return null;

        var employee = run.Employees.FirstOrDefault(e => e.EmployeeId == employeeId);
        if (employee is null)
            return null;

        var company = await GetCompanyAsync(ct);
        return _renderer.Render(run, employee, company);
    }

    public async Task<byte[]?> GenerateForRunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runService.GetAsync(runId, ct);
        if (run is null)
            return null;

        var company = await GetCompanyAsync(ct);
        return _renderer.RenderMerged(run, run.Employees, company);
    }

    public async Task<IReadOnlyList<MyPayslipSummaryDto>> GetMyPayslipsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var runs = await _runRepository.GetRunsForEmployeeAsync(employeeId, ct);

        // Each run comes back with every employee's entry (see GetRunsForEmployeeAsync's
        // remarks) - pick out only the caller's own line, exactly as GenerateAsync does for a
        // single run above. Never project anything off the OTHER entries in run.Employees.
        return runs
            .Select(run => (Run: run, Entry: run.Employees.FirstOrDefault(e => e.EmployeeId == employeeId)))
            .Where(x => x.Entry is not null)
            .Select(x => new MyPayslipSummaryDto(
                RunId: x.Run.Id,
                RunNumber: x.Run.RunNumber,
                PeriodLabel: x.Run.PeriodLabel,
                PayDate: x.Run.PayDate,
                NetPay: x.Entry!.NetPay))
            .ToList();
    }

    /// <summary>
    /// The seeder always creates a Company row, so its absence means a misconfigured database
    /// rather than a normal, recoverable state - hence a DomainException naming the problem
    /// rather than a null the caller might otherwise mistake for "run not found".
    /// </summary>
    private async Task<PayslipCompanyDto> GetCompanyAsync(CancellationToken ct)
    {
        var company = await _companyRepo.GetDefaultAsync(ct)
            ?? throw new DomainException(
                "No Company record is configured. The database seeder always creates one, so " +
                "its absence means the database is misconfigured.");

        return new PayslipCompanyDto(
            CompanyName: company.Name,
            Address: company.Address,
            City: company.City,
            ContactNumber: company.ContactPhone,
            Email: company.ContactEmail);
    }
}
