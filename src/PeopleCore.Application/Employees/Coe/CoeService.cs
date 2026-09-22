using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Employees.Coe;

/// <summary>
/// Gathers a current or former employee's facts - name, position, hire/separation dates, the
/// default company's letterhead, their basic salary if asked for, and the requesting HR user as
/// the default signatory - then hands them to the pure <see cref="CoeContent.Build"/> and the
/// document renderer.
/// </summary>
public class CoeService : ICoeService
{
    private readonly IEmployeeRepository _employees;
    private readonly ICompanyRepository _companies;
    private readonly IEmployeeCompensationRepository _compensations;
    private readonly ICurrentUserService _currentUser;
    private readonly ICoeRenderer _renderer;
    private readonly TimeProvider _clock;

    public CoeService(
        IEmployeeRepository employees,
        ICompanyRepository companies,
        IEmployeeCompensationRepository compensations,
        ICurrentUserService currentUser,
        ICoeRenderer renderer,
        TimeProvider clock)
    {
        _employees = employees;
        _companies = companies;
        _compensations = compensations;
        _currentUser = currentUser;
        _renderer = renderer;
        _clock = clock;
    }

    public async Task<(byte[] Pdf, string FileName)> GenerateAsync(Guid employeeId, CoeRequest request, CancellationToken ct = default)
    {
        // Compensation sits behind payroll.manage everywhere else in the app; the salary line on
        // a certificate is no different, so it is refused here rather than left to whatever the
        // caller happened to send.
        if (request.IncludeSalary && !_currentUser.HasPermission(Permissions.PayrollManage))
            throw new DomainException("Only someone who can manage payroll can add the salary to a certificate.");

        var employee = await _employees.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var company = await _companies.GetDefaultAsync(ct);
        var compensation = await _compensations.GetByEmployeeIdAsync(employeeId, ct);
        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));

        // The requesting HR user's own employee record gives their display name when their
        // account is linked to one; ICurrentUserService carries no display-name claim otherwise,
        // so Email is the fallback identity.
        var signatoryEmployee = _currentUser.EmployeeId is { } signatoryId
            ? await _employees.GetByIdAsync(signatoryId, ct)
            : null;
        var defaultSignatoryName = signatoryEmployee?.FullName ?? _currentUser.Email ?? "";

        var facts = new CoeFacts(
            employee.FullName,
            employee.Position?.Title,
            employee.HireDate,
            employee.SeparationDate,
            company?.Name ?? "",
            company?.Address,
            company?.Logo,
            compensation?.BasicSalary,
            employee.Gender.ToString(),
            defaultSignatoryName);

        var content = CoeContent.Build(facts, request, today);
        var pdf = _renderer.Render(content);
        var fileName = $"COE-{employee.LastName}{employee.FirstName}-{today:yyyyMMdd}.pdf";
        return (pdf, fileName);
    }
}
