using QuestPDF.Fluent;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.Reports;

/// <summary>
/// Thin wrapper over <see cref="Bir2316Document"/>: the only place in this project that knows
/// about QuestPDF's <c>GeneratePdf</c> entry point for a 2316, so Bir2316Service (Application)
/// never has to. Mirrors <see cref="PayslipRenderer"/>.
/// </summary>
public class Bir2316Renderer : IBir2316Renderer
{
    public byte[] Render(Bir2316Dto dto) => new Bir2316Document(dto).GeneratePdf();

    public byte[] RenderMerged(IReadOnlyList<Bir2316Dto> forms)
        => Document.Merge(forms.Select(dto => new Bir2316Document(dto))).GeneratePdf();
}
