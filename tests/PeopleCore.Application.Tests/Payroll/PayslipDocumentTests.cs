using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Enums;
using PeopleCore.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayslipDocumentTests
{
    public PayslipDocumentTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void Renders_a_non_empty_pdf_for_a_typical_payslip()
    {
        var pdf = new PayslipDocument(Run(), Employee(), Company()).GeneratePdf();

        pdf.Should().NotBeNullOrEmpty();
        pdf.Take(5).Should().Equal("%PDF-"u8.ToArray(), "output must be a real PDF");
    }

    [Fact]
    public void Renders_when_every_amount_is_zero()
    {
        var act = () => new PayslipDocument(Run(), Employee(zeroed: true), Company()).GeneratePdf();

        act.Should().NotThrow("a new hire with no worked days must still get a payslip");
    }

    [Fact]
    public void Renders_when_names_and_addresses_are_unusually_long()
    {
        var company = Company() with
        {
            CompanyName = new string('M', 200),
            Address = new string('A', 200)
        };

        var act = () => new PayslipDocument(Run(), Employee(), company).GeneratePdf();

        act.Should().NotThrow("QuestPDF throws on layout overflow rather than truncating");
    }

    [Fact]
    public void Renders_with_every_earning_and_deduction_line_populated()
    {
        // PayZen's PayrollRunEmployeeDto carried free-form EarningLines/DeductionLines
        // collections, so this test could inject 25+25 synthetic rows to stress pagination.
        // PeopleCore's PayrollRunEmployeeDto has no such collections - earnings and deductions
        // are fixed named fields (see PayrollRunDtos.cs), capping the document at a small,
        // fixed number of rows. Pagination is no longer reachable through this DTO; what
        // remains worth testing is that every one of those fixed earning, deduction and
        // employer-contribution fields renders correctly when populated with a distinct
        // non-zero amount at once.
        var employee = Employee() with
        {
            RegularPay = 100m,
            OvertimePay = 200m,
            HolidayPay = 300m,
            NightDiffPay = 400m,
            TaxableAllowances = 500m,
            NonTaxableAllowances = 600m,
            ThirteenthMonth = 700m,
            SSSEmployee = 800m,
            SSSEmployer = 900m,
            PhilHealthEmployee = 1_000m,
            PhilHealthEmployer = 1_100m,
            PagIbigEmployee = 1_200m,
            PagIbigEmployer = 1_300m,
            WithholdingTax = 1_400m,
            LoanDeductions = 1_500m,
            OtherDeductions = 1_600m
        };

        var act = () => new PayslipDocument(Run(), employee, Company()).GeneratePdf();

        act.Should().NotThrow("a fully populated breakdown must render without overflow");
    }

    private static PayrollRunDto Run() => new(
        Id: Guid.NewGuid(),
        RunNumber: "PR-2026-001",
        PeriodLabel: "Jan 1 – Jan 15, 2026",
        PeriodStart: new DateOnly(2026, 1, 1),
        PeriodEnd: new DateOnly(2026, 1, 15),
        PayDate: new DateOnly(2026, 1, 20),
        Frequency: PayFrequency.SemiMonthly,
        Status: PayrollRunStatus.Paid,
        EmployeeCount: 1,
        TotalGrossPay: 10_000m,
        TotalDeductions: 761.25m,
        TotalNetPay: 9_238.75m,
        CreatedAt: new DateTime(2026, 1, 16),
        AttendancePeriodId: null,
        EmployeesMissingAttendance: 0,
        Employees: []);

    private static PayrollRunEmployeeDto Employee(bool zeroed = false)
    {
        decimal V(decimal value) => zeroed ? 0m : value;

        return new PayrollRunEmployeeDto(
            Id: Guid.NewGuid(),
            EmployeeId: Guid.NewGuid(),
            EmployeeName: "Dela Cruz, Juan P.",
            EmployeeNumber: "EMP-0042",
            DaysWorked: 22m,
            GrossPay: V(10_000m),
            TotalDeductions: V(761.25m),
            NetPay: V(9_238.75m),
            RegularPay: V(10_000m),
            OvertimePay: 0m,
            HolidayPay: 0m,
            NightDiffPay: 0m,
            TaxableAllowances: 0m,
            NonTaxableAllowances: 0m,
            ThirteenthMonth: 0m,
            SSSEmployee: V(461.25m),
            SSSEmployer: V(978.75m),
            PhilHealthEmployee: V(250m),
            PhilHealthEmployer: V(250m),
            PagIbigEmployee: V(50m),
            PagIbigEmployer: V(50m),
            WithholdingTax: 0m,
            LoanDeductions: 0m,
            OtherDeductions: 0m);
    }

    private static PayslipCompanyDto Company() => new(
        CompanyName: "M2NET Solutions Inc.",
        Address: "123 Ayala Avenue, Makati",
        City: "Makati City",
        ContactNumber: "+63 2 8123 4567",
        Email: "payroll@m2netsolutions.com");
}
