using PeopleCore.Application.Employees.Coe;
using QuestPDF.Fluent;

namespace PeopleCore.Reports;

/// <summary>
/// Thin wrapper over <see cref="CoeDocument"/>: the only place in this project that knows about
/// QuestPDF's <c>GeneratePdf</c> entry point, so <c>CoeService</c> (Application) never has to.
/// </summary>
public class CoeRenderer : ICoeRenderer
{
    public byte[] Render(CoeContent content) => new CoeDocument(content).GeneratePdf();
}
