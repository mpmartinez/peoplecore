using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Services;

public class PayrollSettingsService : IPayrollSettingsService
{
    private readonly IPayrollSettingsRepository _repo;

    public PayrollSettingsService(IPayrollSettingsRepository repo)
    {
        _repo = repo;
    }

    public async Task<PayrollSettingsDto> GetAsync(Guid companyId, CancellationToken ct = default)
    {
        var settings = await _repo.GetByCompanyIdAsync(companyId, ct);
        return ToDto(settings ?? new PayrollSettings { CompanyId = companyId });
    }

    public async Task UpdateAsync(Guid companyId, PayrollSettingsDto dto, CancellationToken ct = default)
    {
        var settings = await _repo.GetByCompanyIdAsync(companyId, ct);

        if (settings is null)
        {
            // PayrollRun carries no CompanyId (see IPayrollSettingsRepository.GetDefaultAsync),
            // so payroll computation cannot tell which company's settings row to use once more
            // than one exists - it would either throw on every run forever after, or (worse)
            // silently price every company off whichever row EF happens to return first. Refuse
            // the write instead of letting a GET-then-PUT for a second company corrupt every
            // later read.
            var existing = await _repo.GetDefaultAsync(ct);
            if (existing is not null)
            {
                throw new InvalidOperationException(
                    $"Payroll settings already exist for company {existing.CompanyId}. " +
                    "Per-company payroll settings are not yet supported because PayrollRun " +
                    $"carries no CompanyId, so creating a second row for company {companyId} " +
                    "would leave payroll computation unable to determine which row applies. " +
                    "Reuse the existing settings row instead.");
            }

            settings = new PayrollSettings { CompanyId = companyId };
            Apply(settings, dto);
            await _repo.AddAsync(settings, ct);
        }
        else
        {
            Apply(settings, dto);
            await _repo.UpdateAsync(settings, ct);
        }
    }

    private static void Apply(PayrollSettings settings, PayrollSettingsDto dto)
    {
        settings.PhilHealthRate = dto.PhilHealthRate;
        settings.PhilHealthMinShare = dto.PhilHealthMinShare;
        settings.PhilHealthMaxShare = dto.PhilHealthMaxShare;
        settings.PagIbigEmployeeRate = dto.PagIbigEmployeeRate;
        settings.PagIbigLowEmployeeRate = dto.PagIbigLowEmployeeRate;
        settings.PagIbigLowRateThreshold = dto.PagIbigLowRateThreshold;
        settings.PagIbigEmployerRate = dto.PagIbigEmployerRate;
        settings.PagIbigMaxFundSalary = dto.PagIbigMaxFundSalary;
        settings.DailyRateFactor = dto.DailyRateFactor;
        settings.SSSEmployeeRate = dto.SSSEmployeeRate;
        settings.SSSEmployerRate = dto.SSSEmployerRate;
    }

    private static PayrollSettingsDto ToDto(PayrollSettings s) => new(
        s.CompanyId, s.PhilHealthRate, s.PhilHealthMinShare, s.PhilHealthMaxShare,
        s.PagIbigEmployeeRate, s.PagIbigLowEmployeeRate, s.PagIbigLowRateThreshold,
        s.PagIbigEmployerRate, s.PagIbigMaxFundSalary, s.DailyRateFactor,
        s.SSSEmployeeRate, s.SSSEmployerRate);
}
