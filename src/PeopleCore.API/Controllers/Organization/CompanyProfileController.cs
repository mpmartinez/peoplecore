using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Organization.Interfaces;

namespace PeopleCore.API.Controllers.Organization;

/// <summary>
/// The employer identity printed on payslips and BIR Form 2316 (name, TIN, address, ZIP, RDO and
/// the agency numbers). Without it the 2316's employer section prints blank.
/// </summary>
[ApiController]
[Route("api/company-profile")]
[RequirePermission(Permissions.SettingsManage)]
public class CompanyProfileController : ControllerBase
{
    private readonly ICompanyRepository _companies;

    public CompanyProfileController(ICompanyRepository companies) => _companies = companies;

    [HttpGet]
    public async Task<ActionResult<CompanyProfileDto>> Get(CancellationToken ct) =>
        await _companies.GetDefaultAsync(ct) is { } company ? Ok(ToDto(company)) : NotFound();

    [HttpPut]
    public async Task<ActionResult<CompanyProfileDto>> Save([FromBody] CompanyProfileDto request, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
            return BadRequest(new ProblemDetails { Title = "Company not saved", Detail = problem, Status = StatusCodes.Status400BadRequest });

        var company = await _companies.GetDefaultAsync(ct);
        if (company is null) return NotFound();

        company.Name = request.Name.Trim();
        company.TIN = Clean(request.Tin);
        company.RdoCode = NullIfBlank(request.RdoCode);
        company.Address = NullIfBlank(request.Address);
        company.City = Clean(request.City);
        company.ZipCode = NullIfBlank(request.ZipCode);
        company.ContactEmail = NullIfBlank(request.ContactEmail);
        company.ContactPhone = NullIfBlank(request.ContactPhone);
        company.SSSNumber = Clean(request.SssNumber);
        company.PhilHealthNumber = Clean(request.PhilHealthNumber);
        company.PagIbigNumber = Clean(request.PagIbigNumber);

        await _companies.UpdateAsync(company, ct);
        return Ok(ToDto(company));
    }

    private static string? Validate(CompanyProfileDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Enter the company's registered name.";

        // 9-digit TIN, optionally followed by a 3- or 5-digit branch code; dashes and spaces are fine.
        var tinDigits = Clean(request.Tin).Count(char.IsDigit);
        if (Clean(request.Tin).Any(c => !char.IsDigit(c) && c is not '-' and not ' ')
            || tinDigits is not (0 or 9 or 12 or 14))
            return "Enter the TIN as 9 digits, optionally with its branch code (for example 123-456-789-00000).";

        var zip = Clean(request.ZipCode);
        if (zip.Length > 0 && (zip.Length != 4 || !zip.All(char.IsDigit)))
            return "Enter the ZIP code as 4 digits.";

        return null;
    }

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CompanyProfileDto ToDto(Domain.Entities.Organization.Company c) =>
        new(c.Name, c.TIN, c.RdoCode, c.Address, c.City, c.ZipCode, c.ContactEmail, c.ContactPhone,
            c.SSSNumber, c.PhilHealthNumber, c.PagIbigNumber);
}

public record CompanyProfileDto(
    string Name, string? Tin, string? RdoCode, string? Address, string? City, string? ZipCode,
    string? ContactEmail, string? ContactPhone, string? SssNumber, string? PhilHealthNumber, string? PagIbigNumber);
