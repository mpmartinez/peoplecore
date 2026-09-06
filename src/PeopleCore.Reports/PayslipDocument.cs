using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Reports;

/// <summary>
/// QuestPDF payslip document for a single employee.
/// </summary>
public class PayslipDocument : IDocument
{
    private readonly PayrollRunEmployeeDto _emp;
    private readonly PayrollRunDto _run;
    private readonly PayslipCompanyDto _company;

    public PayslipDocument(PayrollRunDto run, PayrollRunEmployeeDto emp, PayslipCompanyDto company)
    {
        _run = run;
        _emp = emp;
        _company = company;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Payslip – {_emp.EmployeeName} – {_run.PeriodLabel}",
        Author = _company.CompanyName,
        Subject = "Payslip"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30, Unit.Point);
            page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9));
            page.Content().Column(col =>
            {
                col.Item().Element(ComposeHeader);
                col.Item().Height(8);
                col.Item().Element(ComposeEmployeeInfo);
                col.Item().Height(8);
                col.Item().Element(ComposeEarningsDeductions);
                col.Item().Height(8);
                col.Item().Element(ComposeNetPay);
                col.Item().Height(16);
                col.Item().Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer c)
    {
        c.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
        {
            row.RelativeItem(3).Column(col =>
            {
                col.Item().Text(_company.CompanyName).Bold().FontSize(13).FontColor("#1d4ed8");
                col.Item().Text(_company.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{_company.City}  |  {_company.ContactNumber}  |  {_company.Email}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            row.RelativeItem(2).AlignRight().Column(col =>
            {
                col.Item().Text("PAYSLIP").Bold().FontSize(14).FontColor("#1d4ed8");
                col.Item().Text($"Run No.: {_run.RunNumber}").FontSize(8);
                col.Item().Text($"Period: {_run.PeriodLabel}").FontSize(8);
                col.Item().Text($"Pay Date: {_run.PayDate:MMMM dd, yyyy}").FontSize(8);
            });
        });
    }

    private void ComposeEmployeeInfo(IContainer c)
    {
        // PayZen's PayrollRunEmployeeDto carried EmployeeNumber/Position/Department/BasicSalary/
        // DaysWorked, laid out here as a 12-column, two-row grid. PeopleCore's PayrollRunEmployeeDto
        // carries none of those (BasicSalary deliberately does not exist on it at all - see that
        // record's remarks in PayrollRunDtos.cs), so EmployeeName is the only field this panel can
        // show and the grid collapses to a single full-width cell rather than the original 2x3 layout.
        c.Border(1).BorderColor(Colors.Grey.Lighten2).Background("#eff6ff").Padding(10)
            .Grid(grid =>
            {
                grid.Columns(12);
                InfoCell(grid, "Employee Name:", _emp.EmployeeName, 12);
            });
    }

    private static void InfoCell(GridDescriptor grid, string label, string value, int span)
    {
        grid.Item(span).Column(col =>
        {
            col.Item().Text(label).FontSize(7.5f).FontColor(Colors.Grey.Darken2);
            col.Item().Text(value).Bold().FontSize(9);
        });
    }

    private void ComposeEarningsDeductions(IContainer c)
    {
        // PayZen's PayrollRunEmployeeDto carried free-form EarningLines/DeductionLines
        // (each with a Description/Amount, deduction lines flagged IsEmployer). PeopleCore's
        // PayrollRunEmployeeDto has no such collections - earnings, employee deductions and
        // employer contributions are fixed named fields. The three lists below reproduce the
        // same shape (Description, Amount) from those fixed fields so the rendering loops
        // below are otherwise unchanged from the original.
        var earningLines = new (string Description, decimal Amount)[]
        {
            ("Regular Pay", _emp.RegularPay),
            ("Overtime Pay", _emp.OvertimePay),
            ("Holiday Pay", _emp.HolidayPay),
            ("Night Differential", _emp.NightDiffPay),
            ("Taxable Allowances", _emp.TaxableAllowances),
            ("Non-Taxable Allowances", _emp.NonTaxableAllowances),
            ("13th Month Pay", _emp.ThirteenthMonth),
        };

        var deductionLines = new (string Description, decimal Amount)[]
        {
            ("SSS Contribution", _emp.SSSEmployee),
            ("PhilHealth Contribution", _emp.PhilHealthEmployee),
            ("Pag-IBIG Contribution", _emp.PagIbigEmployee),
            ("Withholding Tax", _emp.WithholdingTax),
            ("Loan Deductions", _emp.LoanDeductions),
            ("Other Deductions", _emp.OtherDeductions),
        };

        var employerContributionLines = new (string Description, decimal Amount)[]
        {
            ("SSS Employer Share", _emp.SSSEmployer),
            ("PhilHealth Employer Share", _emp.PhilHealthEmployer),
            ("Pag-IBIG Employer Share", _emp.PagIbigEmployer),
        };

        c.Row(row =>
        {
            // Earnings column
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Column(col =>
            {
                col.Item().Background("#dcfce7").Padding(6)
                    .Text("EARNINGS").Bold().FontSize(9).FontColor("#166534");

                foreach (var line in earningLines)
                {
                    col.Item().Padding(4).PaddingTop(2).PaddingBottom(2).Row(r =>
                    {
                        r.RelativeItem().Text(line.Description).FontSize(8.5f);
                        r.AutoItem().Text($"₱ {line.Amount:N2}").FontSize(8.5f);
                    });
                }

                col.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1)
                    .Background("#f0fdf4").Padding(5).Row(r =>
                    {
                        r.RelativeItem().Text("GROSS PAY").Bold().FontSize(9);
                        r.AutoItem().Text($"₱ {_emp.GrossPay:N2}").Bold().FontSize(9);
                    });
            });

            row.ConstantItem(8);

            // Deductions column
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Column(col =>
            {
                col.Item().Background("#fee2e2").Padding(6)
                    .Text("DEDUCTIONS").Bold().FontSize(9).FontColor("#991b1b");

                foreach (var line in deductionLines)
                {
                    col.Item().Padding(4).PaddingTop(2).PaddingBottom(2).Row(r =>
                    {
                        r.RelativeItem().Text(line.Description).FontSize(8.5f);
                        r.AutoItem().Text($"₱ {line.Amount:N2}").FontSize(8.5f);
                    });
                }

                col.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1)
                    .Background("#fff1f2").Padding(5).Row(r =>
                    {
                        r.RelativeItem().Text("TOTAL DEDUCTIONS").Bold().FontSize(9);
                        r.AutoItem().Text($"₱ {_emp.TotalDeductions:N2}").Bold().FontSize(9);
                    });

                // Employer contributions (informational)
                col.Item().Background("#f5f3ff").Padding(6).PaddingTop(8)
                    .Text("EMPLOYER CONTRIBUTIONS (informational)").FontSize(7.5f).FontColor("#6b21a8");

                foreach (var line in employerContributionLines)
                {
                    col.Item().Padding(4).PaddingTop(2).PaddingBottom(2).Row(r =>
                    {
                        r.RelativeItem().Text(line.Description).FontSize(8f).FontColor(Colors.Grey.Darken2);
                        r.AutoItem().Text($"₱ {line.Amount:N2}").FontSize(8f).FontColor(Colors.Grey.Darken2);
                    });
                }
            });
        });
    }

    private void ComposeNetPay(IContainer c)
    {
        c.Border(1.5f).BorderColor("#1d4ed8").Background("#eff6ff")
            .Padding(10).Row(row =>
            {
                row.RelativeItem().Text("NET PAY").Bold().FontSize(14).FontColor("#1d4ed8");
                row.AutoItem().Text($"₱ {_emp.NetPay:N2}").Bold().FontSize(14).FontColor("#1d4ed8");
            });
    }

    private void ComposeSignatures(IContainer c)
    {
        c.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Prepared by:").FontSize(8);
                col.Item().Height(30);
                col.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1)
                    .Text("HR / Payroll Officer").FontSize(8).AlignCenter();
            });

            row.ConstantItem(40);

            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Approved by:").FontSize(8);
                col.Item().Height(30);
                col.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1)
                    .Text("Finance Manager").FontSize(8).AlignCenter();
            });

            row.ConstantItem(40);

            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Received by:").FontSize(8);
                col.Item().Height(30);
                col.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1)
                    .Text($"{_emp.EmployeeName}").FontSize(8).AlignCenter();
            });
        });
    }
}
