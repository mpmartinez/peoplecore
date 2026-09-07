using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// BIR Form 2316 endpoints. Unlike <see cref="ReportsController"/>, this controller carries a
/// class-level <see cref="AuthorizeAttribute"/> restricting every action to payroll roles: there
/// is no self-service equivalent here. An employee's own 2316 is handed over by HR after review,
/// not self-served the way a payslip is, so there is no "my 2316" action whose safety would
/// depend on a bare route with no employee id - every action below legitimately takes an
/// employee id, and the role check is what keeps that safe.
/// </summary>
[ApiController]
[Route("api/reports/2316")]
[Authorize(Roles = "Admin,HRManager,PayrollService")]
public class Bir2316Controller : ControllerBase
{
    private readonly IBir2316Service _service;
    private readonly IBir2316Renderer _renderer;

    public Bir2316Controller(IBir2316Service service, IBir2316Renderer renderer)
    {
        _service = service;
        _renderer = renderer;
    }

    /// <summary>Years in which the employee has at least one PAID run, newest first.</summary>
    [HttpGet("years/{employeeId:guid}")]
    public async Task<ActionResult<IReadOnlyList<int>>> GetYears(Guid employeeId, CancellationToken ct = default)
        => Ok(await _service.GetAvailableYearsAsync(employeeId, ct));

    /// <summary>
    /// The form with every derived figure populated and the manual fields blank - what the
    /// Bir2316.razor page loads before the user fills in anything, and exactly what
    /// <see cref="Generate"/> would produce with an empty <see cref="Bir2316ManualInputs"/>.
    /// </summary>
    [HttpGet("preview/{employeeId:guid}")]
    public async Task<ActionResult<Bir2316Dto>> GetPreview(
        Guid employeeId, [FromQuery] int year, CancellationToken ct = default)
    {
        var dto = await _service.GetPreviewAsync(employeeId, year, ct);
        if (dto is null)
            return NotFound();

        return Ok(dto);
    }

    /// <summary>
    /// Renders one employee's 2316 for the year. Takes <see cref="Bir2316ManualInputs"/> in the
    /// body - deliberately NOT a full <see cref="Bir2316Dto"/>. PayZen's equivalent endpoint took
    /// the whole DTO and rendered whatever it was given, so a caller could post any withheld-tax
    /// figure and receive a tax certificate stating it. There is no field on
    /// <see cref="Bir2316ManualInputs"/> capable of carrying a derived figure - no basic salary,
    /// no present-employer withholding, no gross taxable compensation - so this parameter type is
    /// what makes it structurally impossible to render caller-supplied money here, even by
    /// accident: every derived figure in the returned PDF comes back out of
    /// <see cref="IBir2316Service.BuildAsync"/>, recomputed from payroll.
    /// <para>
    /// Do not widen this parameter to <see cref="Bir2316Dto"/>, and do not add a derived-figure
    /// field to <see cref="Bir2316ManualInputs"/> "for convenience" - the next person tempted to
    /// do either should read this comment first.
    /// <c>Bir2316AuthorizationTests.Generate_TakesBir2316ManualInputsNotBir2316Dto</c> pins this
    /// exact parameter type by reflection, so either change fails the build with an explanation.
    /// </para>
    /// </summary>
    [HttpPost("generate/{employeeId:guid}")]
    public async Task<IActionResult> Generate(
        Guid employeeId, [FromQuery] int year, [FromBody] Bir2316ManualInputs manual, CancellationToken ct = default)
    {
        var dto = await _service.BuildAsync(employeeId, year, manual, ct);
        if (dto is null)
            return NotFound();

        var pdf = _renderer.Render(dto);
        var fileName = $"BIR2316-{year}-{dto.EmployeeLastName}{dto.EmployeeFirstName}.pdf";

        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>
    /// Every employee with a PAID run in <paramref name="year"/>, merged into one PDF - the bulk
    /// equivalent of <see cref="Generate"/> for HR running the whole company's certificates at
    /// once. Each employee's form is built with an empty <see cref="Bir2316ManualInputs"/>: the
    /// manual fields (a previous employer, a PERA credit) are per-employee facts nobody has
    /// supplied yet in a bulk run, so they come back blank here exactly as they do from
    /// <see cref="GetPreview"/> - not omitted, not guessed.
    /// <para>
    /// Delegates the whole build to <see cref="IBir2316Service.BuildAllAsync"/> rather than
    /// looping <see cref="Generate"/>'s per-employee <c>BuildAsync</c> here: that loop would
    /// re-query and re-materialise every OTHER employee's entries out of the year's runs once per
    /// employee, which is fine at demo headcount and a timeout at real headcount. The controller
    /// stays a thin dispatch to Application either way.
    /// </para>
    /// </summary>
    [HttpPost("generate-all")]
    public async Task<IActionResult> GenerateAll([FromQuery] int year, CancellationToken ct = default)
    {
        var forms = await _service.BuildAllAsync(year, ct);

        // Mirrors PayslipService.GenerateForRunAsync's reasoning: QuestPDF's Document.Merge over
        // an empty sequence still "succeeds" with an empty byte array rather than throwing, which
        // would otherwise flow out as a 200 OK application/pdf response the user cannot open.
        if (forms.Count == 0)
            return NotFound();

        var pdf = _renderer.RenderMerged(forms);
        var fileName = $"BIR2316-{year}-All.pdf";

        return File(pdf, "application/pdf", fileName);
    }
}
