using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayslipServiceTests
{
    private readonly Mock<IPayrollRunService> _runService = new();
    private readonly Mock<IPayrollRunRepository> _runRepository = new();
    private readonly Mock<ICompanyRepository> _companyRepo = new();
    private readonly Mock<IPayslipRenderer> _renderer = new();
    private readonly PayslipService _sut;

    public PayslipServiceTests()
    {
        _companyRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Company());

        _sut = new PayslipService(_runService.Object, _runRepository.Object, _companyRepo.Object, _renderer.Object);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheRunDoesNotExist_ReturnsNull()
    {
        _runService.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PayrollRunDto?)null);

        var result = await _sut.GenerateAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
        _renderer.Verify(r => r.Render(It.IsAny<PayrollRunDto>(), It.IsAny<PayrollRunEmployeeDto>(), It.IsAny<PayslipCompanyDto>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheEmployeeIsNotInTheRun_ReturnsNull()
    {
        var run = RunWith(Employee(Guid.NewGuid()));
        _runService.Setup(s => s.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var result = await _sut.GenerateAsync(run.Id, Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
        _renderer.Verify(r => r.Render(It.IsAny<PayrollRunDto>(), It.IsAny<PayrollRunEmployeeDto>(), It.IsAny<PayslipCompanyDto>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_ProducesAPdfForAnEmployeeInTheRun()
    {
        var targetEmployeeId = Guid.NewGuid();
        var target = Employee(targetEmployeeId, employeeName: "Dela Cruz, Juan P.");
        var other = Employee(Guid.NewGuid(), employeeName: "Santos, Maria L.");
        var run = RunWith(other, target);
        _runService.Setup(s => s.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var expectedPdf = "%PDF-fake"u8.ToArray();
        _renderer
            .Setup(r => r.Render(run, target, It.IsAny<PayslipCompanyDto>()))
            .Returns(expectedPdf);

        var result = await _sut.GenerateAsync(run.Id, targetEmployeeId, CancellationToken.None);

        result.Should().BeSameAs(expectedPdf);
        _renderer.Verify(r => r.Render(run, target, It.IsAny<PayslipCompanyDto>()), Times.Once);
        // Not just "something was returned" - the renderer must have been handed THIS employee's
        // entry, not the other one that also happens to be in the run.
        _renderer.Verify(r => r.Render(run, other, It.IsAny<PayslipCompanyDto>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForRunAsync_WhenTheRunHasNoEntries_ReturnsNull()
    {
        // QuestPDF's Document.Merge over an empty sequence returns a "successful" empty byte
        // array rather than throwing - left unguarded, that becomes a 200 OK application/pdf
        // response the user cannot open. Returning null here instead lets the controller 404.
        var run = RunWith();
        _runService.Setup(s => s.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var result = await _sut.GenerateForRunAsync(run.Id, CancellationToken.None);

        result.Should().BeNull();
        _renderer.Verify(r => r.RenderMerged(It.IsAny<PayrollRunDto>(), It.IsAny<IReadOnlyList<PayrollRunEmployeeDto>>(), It.IsAny<PayslipCompanyDto>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForRunAsync_MergesEveryEntryIntoOneDocument()
    {
        var first = Employee(Guid.NewGuid(), employeeName: "Dela Cruz, Juan P.");
        var second = Employee(Guid.NewGuid(), employeeName: "Santos, Maria L.");
        var run = RunWith(first, second);
        _runService.Setup(s => s.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var expectedPdf = "%PDF-merged"u8.ToArray();
        _renderer
            .Setup(r => r.RenderMerged(run, It.Is<IReadOnlyList<PayrollRunEmployeeDto>>(
                list => list.Count == 2 && list.Contains(first) && list.Contains(second)),
                It.IsAny<PayslipCompanyDto>()))
            .Returns(expectedPdf);

        var result = await _sut.GenerateForRunAsync(run.Id, CancellationToken.None);

        result.Should().BeSameAs(expectedPdf);
        _renderer.Verify(r => r.RenderMerged(run, It.Is<IReadOnlyList<PayrollRunEmployeeDto>>(
            list => list.Count == 2 && list.Contains(first) && list.Contains(second)),
            It.IsAny<PayslipCompanyDto>()), Times.Once);
    }

    [Fact]
    public async Task GetMyPayslipsAsync_ReturnsOnlyTheCallersOwnRows()
    {
        var targetEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        // One run containing two employees' entries - the security property under test is that
        // the service picks out only the caller's own line and never projects the other one.
        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            RunNumber = "PR-2026-001",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 1, 15),
            PayDate = new DateOnly(2026, 1, 20),
            Status = PayrollRunStatus.Approved
        };
        var targetEntry = RunEmployee(run, targetEmployeeId, netPay: 9_238.75m);
        var otherEntry = RunEmployee(run, otherEmployeeId, netPay: 15_000m);
        run.Employees = [targetEntry, otherEntry];

        _runRepository
            .Setup(r => r.GetRunsForEmployeeAsync(targetEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([run]);

        var result = await _sut.GetMyPayslipsAsync(targetEmployeeId, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].RunId.Should().Be(run.Id);
        result[0].RunNumber.Should().Be(run.RunNumber);
        result[0].NetPay.Should().Be(targetEntry.NetPay);
        result[0].NetPay.Should().NotBe(otherEntry.NetPay);
    }

    [Fact]
    public async Task GetMyPayslipsAsync_ExcludesDraftRuns()
    {
        // Approval is what makes a run's figures final (PayrollRunService.ApproveAsync). A
        // Draft run can still be recomputed to a different net pay, so it must not show up in
        // the ESS list at all - not even the caller's own line in it.
        var employeeId = Guid.NewGuid();
        var draftRun = new PayrollRun
        {
            Id = Guid.NewGuid(),
            RunNumber = "PR-2026-002",
            PeriodStart = new DateOnly(2026, 2, 1),
            PeriodEnd = new DateOnly(2026, 2, 15),
            PayDate = new DateOnly(2026, 2, 20),
            Status = PayrollRunStatus.Draft
        };
        draftRun.Employees = [RunEmployee(draftRun, employeeId, netPay: 9_238.75m)];

        _runRepository
            .Setup(r => r.GetRunsForEmployeeAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([draftRun]);

        var result = await _sut.GetMyPayslipsAsync(employeeId, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateForSelfServiceAsync_WhenTheRunIsDraft_ReturnsNull()
    {
        var employeeId = Guid.NewGuid();
        var run = RunWith(Employee(employeeId)) with { Status = PayrollRunStatus.Draft };
        _runService.Setup(s => s.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var result = await _sut.GenerateForSelfServiceAsync(run.Id, employeeId, CancellationToken.None);

        result.Should().BeNull();
        _renderer.Verify(r => r.Render(It.IsAny<PayrollRunDto>(), It.IsAny<PayrollRunEmployeeDto>(), It.IsAny<PayslipCompanyDto>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_ForADraftRun_StillWorks_OnTheHrPath()
    {
        // GenerateAsync is the HR path (ReportsController.GetPayslip calls it directly, with no
        // status gate) - proofing a run before approving it is legitimate, so this must keep
        // producing a PDF for a Draft run. The employee-facing gate lives only in
        // GenerateForSelfServiceAsync above.
        var employeeId = Guid.NewGuid();
        var target = Employee(employeeId);
        var run = RunWith(target) with { Status = PayrollRunStatus.Draft };
        _runService.Setup(s => s.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var expectedPdf = "%PDF-draft"u8.ToArray();
        _renderer
            .Setup(r => r.Render(run, target, It.IsAny<PayslipCompanyDto>()))
            .Returns(expectedPdf);

        var result = await _sut.GenerateAsync(run.Id, employeeId, CancellationToken.None);

        result.Should().BeSameAs(expectedPdf);
    }

    private static PayrollRunEmployee RunEmployee(PayrollRun run, Guid employeeId, decimal netPay) => new()
    {
        Id = Guid.NewGuid(),
        PayrollRunId = run.Id,
        PayrollRun = run,
        EmployeeId = employeeId,
        RegularPay = netPay,
        SSSEmployee = 0m,
        PhilHealthEmployee = 0m,
        PagIbigEmployee = 0m,
        WithholdingTax = 0m,
        LoanDeductions = 0m,
        OtherDeductions = 0m
    };

    private static Company Company() => new()
    {
        Name = "M2NET Solutions Inc.",
        Address = "123 Ayala Avenue, Makati",
        City = "Makati City",
        ContactPhone = "+63 2 8123 4567",
        ContactEmail = "payroll@m2netsolutions.com"
    };

    private static PayrollRunDto RunWith(params PayrollRunEmployeeDto[] employees) => new(
        Id: Guid.NewGuid(),
        RunNumber: "PR-2026-001",
        PeriodLabel: "Jan 1 - Jan 15, 2026",
        PeriodStart: new DateOnly(2026, 1, 1),
        PeriodEnd: new DateOnly(2026, 1, 15),
        PayDate: new DateOnly(2026, 1, 20),
        Frequency: PayFrequency.SemiMonthly,
        Status: PayrollRunStatus.Paid,
        EmployeeCount: employees.Length,
        TotalGrossPay: 0m,
        TotalDeductions: 0m,
        TotalNetPay: 0m,
        CreatedAt: new DateTime(2026, 1, 16),
        AttendancePeriodId: null,
        EmployeesMissingAttendance: 0,
        Employees: employees);

    private static PayrollRunEmployeeDto Employee(Guid employeeId, string employeeName = "Dela Cruz, Juan P.") => new(
        Id: Guid.NewGuid(),
        EmployeeId: employeeId,
        EmployeeName: employeeName,
        EmployeeNumber: "EMP-0042",
        DaysWorked: 22m,
        GrossPay: 10_000m,
        TotalDeductions: 761.25m,
        NetPay: 9_238.75m,
        RegularPay: 10_000m,
        OvertimePay: 0m,
        HolidayPay: 0m,
        NightDiffPay: 0m,
        TaxableAllowances: 0m,
        NonTaxableAllowances: 0m,
        ThirteenthMonth: 0m,
        AbsenceDeduction: 0m,
        TardinessDeduction: 0m,
        SSSEmployee: 461.25m,
        SSSEmployer: 978.75m,
        PhilHealthEmployee: 250m,
        PhilHealthEmployer: 250m,
        PagIbigEmployee: 50m,
        PagIbigEmployer: 50m,
        WithholdingTax: 0m,
        LoanDeductions: 0m,
        OtherDeductions: 0m);
}
