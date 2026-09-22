using PeopleCore.Application.Employees.Coe;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PeopleCore.Reports;

/// <summary>
/// QuestPDF Certificate of Employment: the company's letterhead (logo if any), a centred bold
/// title, the worded paragraphs justified with space between them, the date line, then a
/// signature block. See <see cref="PayslipDocument"/> for the sibling payslip layout this mirrors.
/// </summary>
public class CoeDocument : IDocument
{
    private readonly CoeContent _content;

    public CoeDocument(CoeContent content) => _content = content;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{_content.Title} - {_content.SignatoryName}",
        Author = _content.CompanyName,
        Subject = _content.Title
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40, Unit.Point);
            page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(11));
            page.Content().Column(col =>
            {
                col.Item().Element(ComposeLetterhead);
                col.Item().Height(24);
                col.Item().AlignCenter().Text(_content.Title).Bold().FontSize(15);
                col.Item().Height(24);
                col.Item().Element(ComposeParagraphs);
                col.Item().Height(16);
                col.Item().Text(_content.DateLine);
                col.Item().Height(48);
                col.Item().Element(ComposeSignature);
            });
        });
    }

    private void ComposeLetterhead(IContainer c)
    {
        c.Row(row =>
        {
            if (_content.Logo is { Length: > 0 } logo)
            {
                row.ConstantItem(50).Image(logo).FitArea();
                row.ConstantItem(12);
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text(_content.CompanyName).Bold().FontSize(13);
                if (!string.IsNullOrWhiteSpace(_content.CompanyAddress))
                    col.Item().Text(_content.CompanyAddress).FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private void ComposeParagraphs(IContainer c)
    {
        c.Column(col =>
        {
            col.Spacing(12);
            foreach (var paragraph in _content.Paragraphs)
                col.Item().Text(paragraph).Justify();
        });
    }

    private void ComposeSignature(IContainer c)
    {
        c.Width(220).Column(col =>
        {
            col.Item().Height(30);
            col.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1).PaddingTop(4)
                .Text(_content.SignatoryName).Bold().FontSize(10);
            col.Item().Text(_content.SignatoryTitle).FontSize(9);
        });
    }
}
