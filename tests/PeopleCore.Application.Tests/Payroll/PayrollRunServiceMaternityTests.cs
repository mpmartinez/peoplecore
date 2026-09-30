using FluentAssertions;
using FluentAssertions.Equivalency;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;
using Employee = PeopleCore.Domain.Entities.Employees.Employee;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// Maternity pay through the run (RA 11210): the advance, the offset, the warnings, and the claim
/// settled at Mark Paid. Maria Santos earns 30,000 a month, paid semi-monthly, so a cutoff's regular
/// pay is 15,000. Her leave runs 10 Aug to 22 Nov 2026 (105 days) and her SSS daily allowance is
/// 666.67, so the benefit is 666.67 x 105 = 70,000.35.
/// </summary>
public partial class PayrollRunServiceTests
{
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<IMaternityClaimRepository> _claimRepo = new();
    private PayrollRunService? _maternitySut;

    /// <summary>The service with the real maternity calculator over the mocked repositories.</summary>
    private PayrollRunService MaternitySut => _maternitySut ??= new PayrollRunService(
        _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object, _settingsRepo.Object,
        new PayrollComputationService(), _attendanceBridge.Object, _employeeRepo.Object, _separations.Object,
        NullLogger<PayrollRunService>.Instance, _finalPay.Object, _yearEnd.Object, _clock,
        new MaternityPayCalculator(_leaveRepo.Object, _claimRepo.Object, _runRepo.Object, _employeeRepo.Object));

    private static readonly DateOnly MaternityStart = new(2026, 8, 10);
    private static readonly DateOnly MaternityEnd = new(2026, 11, 22);

    /// <summary>Maria, her compensation, her approved maternity leave and (optionally) her claim.</summary>
    private (Employee Maria, EmployeeCompensation Compensation, MaternityClaim? Claim, Func<PayrollRun?> SavedRun)
        MariaOnMaternityLeave(bool withClaim = true, decimal? allowance = 666.67m)
    {
        var maria = new Employee { FirstName = "Maria", LastName = "Santos" };
        var compensation = new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        };
        var savedRun = SetupRoundTripRepositories(compensation);
        _employeeRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync([maria]);

        var type = new LeaveType { Name = "Maternity Leave", Code = "ML", IsMaternity = true, IsPaid = true };
        var request = new LeaveRequest
        {
            EmployeeId = maria.Id, Employee = maria, LeaveType = type, LeaveTypeId = type.Id,
            StartDate = MaternityStart, EndDate = MaternityEnd, TotalDays = 105m, Status = LeaveStatus.Approved
        };
        _leaveRepo.Setup(l => l.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                      new[] { request }.Where(r => r.StartDate <= to && r.EndDate >= from).ToList());

        MaternityClaim? claim = null;
        if (withClaim)
        {
            claim = new MaternityClaim
            {
                EmployeeId = maria.Id, Employee = maria, LeaveRequestId = request.Id, LeaveRequest = request, Days = 105m,
                DailyAllowance = allowance, Benefit = allowance is decimal a ? MaternityMath.Benefit(a, 105m) : 0m
            };
        }
        _claimRepo.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(claim is null ? [] : [claim]);
        NoOtherRunAdvances();
        return (maria, compensation, claim, savedRun);
    }

    private void NoOtherRunAdvances()
        => _runRepo.Setup(r => r.GetMaternityAdvancesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

    private static CreatePayrollRunRequest AugustFirstHalf(Guid employeeId, bool advance) => new(
        new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 20), PayFrequency.SemiMonthly,
        [new PayrollRunEmployeeInput(employeeId, AdvanceMaternityBenefit: advance)]);

    [Fact]
    public async Task CreateAsync_AdvancesTheBenefit_AndOffsetsTheLeaveDays_AndTheDtoCarriesEachFigure()
    {
        var (maria, compensation, claim, savedRun) = MariaOnMaternityLeave();

        var dto = await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));

        // Aug 10-15 is 6 days of leave: 666.67 x 6 = 4,000.02 comes off the 15,000 regular pay,
        // leaving 10,999.98. Gross = 10,999.98 + the 70,000.35 advance = 81,000.33.
        var entry = savedRun()!.Employees.Single();
        entry.AdvanceMaternityBenefit.Should().BeTrue();
        entry.MaternityClaimId.Should().Be(claim!.Id);
        entry.MaternityBenefitAdvance.Should().Be(70_000.35m);
        entry.MaternityBenefitOffset.Should().Be(4_000.02m);
        entry.RegularPay.Should().Be(10_999.98m);
        entry.GrossPay.Should().Be(81_000.33m);
        // The leave days' pay is 15,000 x 6 / 15 = 6,000.00; SSS covers 4,000.02 of it, and the
        // 1,999.98 left is the tax-exempt salary differential.
        entry.MaternityDifferential.Should().Be(1_999.98m);

        // The rest of the entry is exactly what the engine gives for that maternity input: the
        // offset was worked out against the 15,000 before it, not against the reduced figure.
        var direct = new PayrollComputationService().Compute(compensation, savedRun()!, daysWorked: 11m,
            attendance: new PayrollAttendanceInput(), dailyRateFactor: 365m,
            maternity: new MaternityInput(70_000.35m, 4_000.02m, 1_999.98m));
        entry.WithholdingTax.Should().Be(direct.WithholdingTax);
        entry.NetPay.Should().Be(direct.NetPay);
        // Shares on the 30,000 basic, halved: 750 + 375 + 100 = 1,225. Base 10,999.98 - 1,999.98
        // - 1,225 = 7,775 a cutoff, 186,600 a year: no tax. Net 81,000.33 - 1,225 = 79,775.33.
        entry.WithholdingTax.Should().Be(0m);
        entry.NetPay.Should().Be(79_775.33m);

        // Three different figures, so a swap of the DTO's positional members would show.
        var line = dto.Employees.Single();
        line.MaternityBenefitAdvance.Should().Be(70_000.35m);
        line.MaternityBenefitOffset.Should().Be(4_000.02m);
        line.MaternityDifferential.Should().Be(1_999.98m);
        dto.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithoutAnAdvance_OffsetsTheLeave_AndWarnsTheBenefitIsNotAdvancedYet()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();

        var dto = await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));

        var entry = savedRun()!.Employees.Single();
        entry.AdvanceMaternityBenefit.Should().BeFalse();
        entry.MaternityBenefitAdvance.Should().Be(0m);
        // The entry records the claim its offset nets, so the allowance can be locked once paid.
        entry.MaternityClaimId.Should().Be(claim!.Id);
        entry.MaternityBenefitOffset.Should().Be(4_000.02m);
        dto.Warnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
    }

    [Fact]
    public async Task CreateAsync_AnAdvanceWithoutAReadyClaim_IsRefused_AndNothingIsSaved()
    {
        var (maria, _, _, _) = MariaOnMaternityLeave(allowance: null);

        var act = () => MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no maternity claim ready to advance.");
        _runRepo.Verify(r => r.AddWithEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithNoClaim_OffsetsNothing_AndWarnsTheBenefitIsNotSetUp()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave(withClaim: false);

        var dto = await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));

        savedRun()!.Employees.Single().MaternityBenefitOffset.Should().Be(0m);
        savedRun()!.Employees.Single().RegularPay.Should().Be(15_000m);
        dto.Warnings.Should().Equal("Maternity benefit not set up yet for Maria Santos.");
    }

    [Fact]
    public async Task CreateAsync_ForAClaimNotSssQualified_PaysAndTaxesTheLeaveDaysAsOrdinarySalary_AndApprovalAcceptsIt()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        claim!.Status = MaternityClaimStatus.NotQualified;

        var dto = await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        await MaternitySut.ApproveAsync(savedRun()!.Id);

        // No offset: 15,000 of regular pay. Base 15,000 - 1,225 = 13,775 a cutoff, 330,600 a year
        // -> 12,090 -> 503.75 withheld. Net 15,000 - 1,225 - 503.75 = 13,271.25; nothing deferred.
        var entry = savedRun()!.Employees.Single();
        entry.RegularPay.Should().Be(15_000m);
        entry.MaternityBenefitOffset.Should().Be(0m);
        entry.MaternityDifferential.Should().Be(0m);
        entry.MaternityClaimId.Should().BeNull();
        entry.WithholdingTax.Should().Be(503.75m);
        entry.ContributionsDeferred.Should().Be(0m);
        entry.NetPay.Should().Be(13_271.25m);
        dto.Warnings.Should().BeEmpty();
        savedRun()!.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task CreateAsync_ForAnExemptEmployer_OffsetsTheRegularPayForTheLeaveDays_EvenWithoutAClaim()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave(withClaim: false);
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new PayrollSettings { ExemptFromMaternityDifferential = true });

        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));

        // 15,000 x 6 leave days / 15 calendar days = 6,000.00, leaving 9,000.
        var entry = savedRun()!.Employees.Single();
        entry.MaternityBenefitOffset.Should().Be(6_000m);
        entry.RegularPay.Should().Be(9_000m);
        entry.MaternityDifferential.Should().Be(0m, "an exempt employer pays no differential");
    }

    [Fact]
    public async Task ComputeAsync_KeepsTheAdvanceFromTheStoredEntry_AndReproducesTheRun()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));
        var before = savedRun()!.Employees.Single();
        RecomputesInPlace();

        await MaternitySut.ComputeAsync(savedRun()!.Id);

        var after = savedRun()!.Employees.Single();
        after.Should().NotBeSameAs(before);
        after.Should().BeEquivalentTo(before, AsRecomputed);
        after.AdvanceMaternityBenefit.Should().BeTrue();
        after.MaternityClaimId.Should().Be(claim!.Id);
        after.MaternityBenefitAdvance.Should().Be(70_000.35m);
        after.MaternityBenefitOffset.Should().Be(4_000.02m);
    }

    /// <summary>
    /// A recomputed entry is a new row: its keys and audit stamps are new, and its children point at it.
    /// Every other member must come out the same.
    /// </summary>
    internal static EquivalencyOptions<PayrollRunEmployee> AsRecomputed(EquivalencyOptions<PayrollRunEmployee> options)
        => options.Excluding(m => m.Name == "Id" || m.Name == "PayrollRunEmployeeId" || m.Name == "PayrollRunEmployee"
                                  || m.Name == "CreatedAt" || m.Name == "UpdatedAt" || m.Name == "CreatedBy"
                                  || m.Name == "UpdatedBy" || m.Name == "PayrollRun" || m.Name == "Employee");

    [Fact]
    public async Task ApproveAsync_WhenTheClaimChangedSinceCompute_IsRefused_SoTheRunCanStillBeRecomputed()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));
        claim!.DailyAllowance = 600m;
        claim.Benefit = 63_000m;

        var act = () => MaternitySut.ApproveAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity claim has changed since this payroll was computed; recompute it before approving.");
        savedRun()!.Status.Should().Be(PayrollRunStatus.Draft);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_WithMaternityDaysAndNoClaim_IsRefused_AndNothingIsSaved()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave(withClaim: false);
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));

        var act = () => MaternitySut.ApproveAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Set up Maria Santos's maternity claim before approving; this payroll covers 6 maternity day(s).");
        savedRun()!.Status.Should().Be(PayrollRunStatus.Draft);
        _runRepo.Verify(r => r.UpdateAsync(It.IsAny<PayrollRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_WithMaternityDaysAndNoClaim_ForAnExemptEmployer_Approves()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave(withClaim: false);
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new PayrollSettings { ExemptFromMaternityDifferential = true });
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));

        await MaternitySut.ApproveAsync(savedRun()!.Id);

        savedRun()!.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AfterTheClaimWasMarkedNotQualified_IsRefused_AndARecomputeClearsIt()
    {
        // Computed with the 4,000.02 offset; the claim was then marked not qualified, so the run
        // would still take her leave days' pay off against a benefit that won't come.
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        claim!.Status = MaternityClaimStatus.NotQualified;
        savedRun()!.Employees.Single().Employee = maria;
        RecomputesInPlace();

        var act = () => MaternitySut.ApproveAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's maternity claim has changed since this payroll was computed; recompute it before approving.");
        savedRun()!.Status.Should().Be(PayrollRunStatus.Draft);

        await MaternitySut.ComputeAsync(savedRun()!.Id);
        await MaternitySut.ApproveAsync(savedRun()!.Id);

        savedRun()!.Employees.Single().MaternityBenefitOffset.Should().Be(0m);
        savedRun()!.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task MarkPaidAsync_WhenTheAllowanceChangedUnderAnOffset_IsRefused_AndSettlesNothing()
    {
        // No advance on this run - only the 4,000.02 offset, netting 666.67 a day. At 600 a day it
        // would be 3,600.00: the run no longer matches its claim.
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        savedRun()!.Status = PayrollRunStatus.Approved;
        savedRun()!.Employees.Single().Employee = maria;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        claim!.DailyAllowance = 600m;
        claim.Benefit = 63_000m;

        var act = () => MaternitySut.MarkPaidAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's maternity claim has changed since this payroll was computed; recompute it before paying.");
        VerifyNothingSavedAsPaid();

        // The way out: recompute (back to Draft, now 600 x 6 = 3,600.00 off), approve, pay.
        RecomputesInPlace();
        await MaternitySut.ComputeAsync(savedRun()!.Id);
        savedRun()!.Status.Should().Be(PayrollRunStatus.Draft);
        savedRun()!.Employees.Single().MaternityBenefitOffset.Should().Be(3_600m);
        await MaternitySut.ApproveAsync(savedRun()!.Id);
        await MaternitySut.MarkPaidAsync(savedRun()!.Id);
        savedRun()!.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task AnAdvanceOnARunWhoseClaimsLeaveWasCancelled_IsRefusedAtMarkPaid_AndTheRecomputeSaysTheWayOut()
    {
        // Jul 16-31 advances her 70,000.35 and is approved; then her leave is cancelled. The run
        // can't pay an advance for leave that no longer stands, and a recompute can't advance it
        // either: HR moves the claim to the refiled leave, or takes her off the payroll.
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(new CreatePayrollRunRequest(
            new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 5), PayFrequency.SemiMonthly,
            [new PayrollRunEmployeeInput(maria.Id, AdvanceMaternityBenefit: true)]));
        savedRun()!.Status = PayrollRunStatus.Approved;
        savedRun()!.Employees.Single().Employee = maria;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        RecomputesInPlace();
        claim!.LeaveRequest.Status = LeaveStatus.Cancelled;

        var pay = () => MaternitySut.MarkPaidAsync(savedRun()!.Id);
        var recompute = () => MaternitySut.ComputeAsync(savedRun()!.Id);

        await pay.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's maternity claim has changed since this payroll was computed; recompute it before paying.");
        await recompute.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's maternity leave was cancelled; move her claim to the refiled leave, or take her off this payroll.");
        claim.Status.Should().Be(MaternityClaimStatus.Draft);
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task ComputeAsync_AnApprovedRunWhoseMaternityOffsetIsStale_CanBeRecomputed_BackToDraft()
    {
        // Approved with the 4,000.02 offset; her claim was since marked not qualified, so the run
        // can't be paid as it is - and a recompute is the way to put it right.
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        savedRun()!.Status = PayrollRunStatus.Approved;
        claim!.Status = MaternityClaimStatus.NotQualified;
        RecomputesInPlace();

        await MaternitySut.ComputeAsync(savedRun()!.Id);

        savedRun()!.Status.Should().Be(PayrollRunStatus.Draft);
        savedRun()!.Employees.Single().MaternityBenefitOffset.Should().Be(0m);
    }

    [Fact]
    public async Task ComputeAsync_AnApprovedRunWhoseMaternityOffsetStillHolds_IsStillRefused()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        savedRun()!.Status = PayrollRunStatus.Approved;

        var act = () => MaternitySut.ComputeAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only draft or for-approval payroll runs can be recomputed.");
    }

    [Fact]
    public async Task ApproveAsync_WithTheClaimAsComputed_Approves()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));

        await MaternitySut.ApproveAsync(savedRun()!.Id);

        savedRun()!.Status.Should().Be(PayrollRunStatus.Approved);
        claim!.Status.Should().Be(MaternityClaimStatus.Draft, "approving pays nothing yet");
    }

    [Fact]
    public async Task ComputeAsync_WhenAnotherRunNowCarriesTheAdvance_IsRefused()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));
        RecomputesInPlace();
        _runRepo.Setup(r => r.GetMaternityAdvancesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), savedRun()!.Id,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new MaternityAdvanceInRun(claim!.Id, "PAY-2026-021")]);

        var act = () => MaternitySut.ComputeAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-021.");
        _runRepo.Verify(r => r.ReplaceEntriesAsync(It.IsAny<PayrollRun>(), It.IsAny<IReadOnlyList<PayrollRunEmployee>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_RebuildsTheWarnings_SoAClaimSetUpSinceClearsThem()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave(allowance: null);
        var created = await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        claim!.DailyAllowance = 666.67m;
        claim.Benefit = 70_000.35m;

        var loaded = await MaternitySut.GetAsync(savedRun()!.Id);

        created.Warnings.Should().Equal("Maternity benefit not set up yet for Maria Santos.");
        loaded!.Warnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
    }

    [Fact]
    public async Task GetForPayslipAsync_IsTheRunWithoutItsWarnings_AndReadsNoMaternityData()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave(allowance: null);
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: false));
        _leaveRepo.Invocations.Clear();
        _claimRepo.Invocations.Clear();

        var dto = await MaternitySut.GetForPayslipAsync(savedRun()!.Id);

        dto!.Warnings.Should().BeEmpty();
        dto.Employees.Single().MaternityBenefitOffset.Should().Be(0m, "no claim is set up");
        _leaveRepo.VerifyNoOtherCalls();
        _claimRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkPaidAsync_SettlesTheAdvancedClaim_InTheSameSaveAsTheRun()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));
        var run = savedRun()!;
        run.Status = PayrollRunStatus.Approved;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await MaternitySut.MarkPaidAsync(run.Id);

        claim!.Status.Should().Be(MaternityClaimStatus.Advanced);
        claim.AdvanceRunId.Should().Be(run.Id);
        claim.AdvancedAt.Should().Be(new DateOnly(2026, 8, 20));
        _runRepo.Verify(r => r.SavePaidAsync(run, It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
            It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
            It.Is<IReadOnlyCollection<MaternityClaim>>(c => c.Single() == claim),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaidAsync_WhenTheClaimChangedSinceCompute_IsRefused_AndSavesNothing()
    {
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));
        savedRun()!.Status = PayrollRunStatus.Approved;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        claim!.DailyAllowance = 600m;
        claim.Benefit = 63_000m;

        var act = () => MaternitySut.MarkPaidAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity claim has changed since this payroll was computed; " +
                         "recompute it before paying.");
        claim.Status.Should().Be(MaternityClaimStatus.Draft);
        VerifyNothingSavedAsPaid();
    }

    // ---- deferred contributions ----------------------------------------------------------------

    private static CreatePayrollRunRequest MariasCutoff(Guid employeeId, int month, int first, int last) => new(
        new DateOnly(2026, month, first), new DateOnly(2026, month, last), new DateOnly(2026, month, last),
        PayFrequency.SemiMonthly, [new PayrollRunEmployeeInput(employeeId)]);

    private void Outstanding(Guid employeeId, decimal amount)
        => _runRepo.Setup(r => r.GetDeferredContributionsOutstandingAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new DeferredContributionsOutstanding(employeeId, amount)]);

    [Fact]
    public async Task CreateAsync_AFullyCoveredCutoff_DefersTheSharesItCantPay_SoNetPayIsNeverNegative()
    {
        // An exempt employer: Aug 16-31 is all leave, so the offset is the whole 15,000 and the
        // cutoff pays nothing. Shares on the 30,000 basic, halved: 750 + 375 + 100 = 1,225 - kept on
        // the entry for remittance, all deferred. Net 0 - 1,225 + 1,225 = 0.
        var (maria, _, _, savedRun) = MariaOnMaternityLeave(withClaim: false);
        _settingsRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new PayrollSettings { ExemptFromMaternityDifferential = true });

        var dto = await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 8, 16, 31));

        var entry = savedRun()!.Employees.Single();
        entry.MaternityBenefitOffset.Should().Be(15_000m);
        (entry.SSSEmployee + entry.PhilHealthEmployee + entry.PagIbigEmployee).Should().Be(1_225m);
        entry.ContributionsDeferred.Should().Be(1_225m);
        entry.NetPay.Should().Be(0m);
        dto.Employees.Single().ContributionsDeferred.Should().Be(1_225m);
    }

    [Fact]
    public async Task CreateAsync_ALaterCutoff_CollectsWhatHerPaidRunsDeferred()
    {
        // Dec 1-15, after the leave: 15,000 of pay. Base 15,000 - 1,225 = 13,775 a cutoff,
        // 330,600 a year -> 12,090 -> 503.75 withheld. Net before collecting 15,000 - 1,225 - 503.75
        // = 13,271.25 covers the 1,225 outstanding: net 12,046.25.
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        Outstanding(maria.Id, 1_225m);

        var dto = await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 12, 1, 15));

        var entry = savedRun()!.Employees.Single();
        entry.WithholdingTax.Should().Be(503.75m);
        entry.DeferredContributionsCollected.Should().Be(1_225m);
        entry.NetPay.Should().Be(12_046.25m);
        dto.Employees.Single().DeferredContributionsCollected.Should().Be(1_225m);
        _runRepo.Verify(r => r.GetDeferredContributionsOutstandingAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(maria.Id)), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task MarkPaidAsync_WhenHerDeferredContributionsChangedSinceCompute_IsRefused_AndSavesNothing()
    {
        // Computed collecting the 1,225 outstanding; another run has since been paid that collected
        // it, so nothing is outstanding now and this run would collect it twice.
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        Outstanding(maria.Id, 1_225m);
        await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 12, 1, 15));
        savedRun()!.Status = PayrollRunStatus.Approved;
        savedRun()!.Employees.Single().Employee = maria;
        Outstanding(maria.Id, 0m);

        var act = () => MaternitySut.MarkPaidAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's deferred contributions have changed since this payroll was computed; recompute it before paying.");
        savedRun()!.Status.Should().Be(PayrollRunStatus.Approved);
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_WhenAShareWasDeferredSinceCompute_IsRefused_ForTheCashCouldCollectIt()
    {
        // Computed with nothing outstanding; a covered cutoff paid since deferred 1,225, which this
        // cutoff's 13,271.25 of cash would have collected.
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 12, 1, 15));
        savedRun()!.Status = PayrollRunStatus.Approved;
        Outstanding(maria.Id, 1_225m);

        var act = () => MaternitySut.MarkPaidAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*deferred contributions have changed*");
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_RefusedOverDeferredContributions_LeavesTheAdvancedClaimUntouched()
    {
        // The run advances her claim and was computed with nothing outstanding; a covered cutoff
        // paid since deferred 1,225, which this run's cash would collect. The refusal must come
        // before the claim is settled, so nothing is left modified in the context.
        var (maria, _, claim, savedRun) = MariaOnMaternityLeave();
        await MaternitySut.CreateAsync(AugustFirstHalf(maria.Id, advance: true));
        savedRun()!.Status = PayrollRunStatus.Approved;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Outstanding(maria.Id, 1_225m);

        var act = () => MaternitySut.MarkPaidAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*deferred contributions have changed*");
        claim!.Status.Should().Be(MaternityClaimStatus.Draft);
        claim.AdvanceRunId.Should().BeNull();
        claim.AdvancedAt.Should().BeNull();
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_WithHerDeferredContributionsAsComputed_Pays()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        Outstanding(maria.Id, 1_225m);
        await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 12, 1, 15));
        savedRun()!.Status = PayrollRunStatus.Approved;
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await MaternitySut.MarkPaidAsync(savedRun()!.Id);

        savedRun()!.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task ComputeAsync_AnApprovedRunWhoseDeferredContributionsChanged_CanBeRecomputed_BackToDraft()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        Outstanding(maria.Id, 1_225m);
        await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 12, 1, 15));
        savedRun()!.Status = PayrollRunStatus.Approved;
        RecomputesInPlace();
        Outstanding(maria.Id, 0m);

        await MaternitySut.ComputeAsync(savedRun()!.Id);

        // 13,271.25 net with nothing left to collect.
        savedRun()!.Status.Should().Be(PayrollRunStatus.Draft);
        savedRun()!.Employees.Single().DeferredContributionsCollected.Should().Be(0m);
        savedRun()!.Employees.Single().NetPay.Should().Be(13_271.25m);
    }

    [Fact]
    public async Task ComputeAsync_AnApprovedRunWhoseDeferredContributionsStillHold_IsStillRefused()
    {
        var (maria, _, _, savedRun) = MariaOnMaternityLeave();
        Outstanding(maria.Id, 1_225m);
        await MaternitySut.CreateAsync(MariasCutoff(maria.Id, 12, 1, 15));
        savedRun()!.Status = PayrollRunStatus.Approved;

        var act = () => MaternitySut.ComputeAsync(savedRun()!.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only draft or for-approval payroll runs can be recomputed.");
    }

    /// <summary>
    /// Maria's final pay for Aug 1-15 (Status as given) while she is on maternity leave from Aug 10,
    /// with her claim, and an entry that advances it when <paramref name="advancing"/>.
    /// </summary>
    private (PayrollRun Run, MaternityClaim? Claim) MariasFinalPayOnLeave(PayrollRunStatus status, bool withClaim, bool advancing)
    {
        var (maria, _, claim, _) = MariaOnMaternityLeave(withClaim);
        var (run, separation) = ApprovedFinalPay(Item("Laptop", 1, cleared: true));
        run.Status = status;
        run.PeriodStart = new DateOnly(2026, 8, 1);
        run.PeriodEnd = new DateOnly(2026, 8, 15);
        run.PayDate = new DateOnly(2026, 8, 31);
        run.FinalPayInputs!.WorkingDays = 15m;
        separation.EmployeeId = maria.Id;
        separation.Employee = maria;
        run.Employees.Clear();
        run.Employees.Add(new PayrollRunEmployee
        {
            // As computed: with the claim, 4,000.02 of the 6,000.00 leave days' pay is offset and
            // 1,999.98 is differential; without one, the 15,000 is paid as salary.
            PayrollRunId = run.Id, EmployeeId = maria.Id, Employee = maria,
            RegularPay = claim is null ? 15_000m : 10_999.98m,
            MaternityBenefitOffset = claim is null ? 0m : 4_000.02m,
            MaternityDifferential = claim is null ? 0m : 1_999.98m, MaternityClaimId = claim?.Id,
            AdvanceMaternityBenefit = advancing, MaternityBenefitAdvance = advancing ? 70_000.35m : 0m
        });
        _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        return (run, claim);
    }

    [Fact]
    public async Task AFinalPay_ThatAdvancesTheBenefit_SettlesTheClaimInTheSameSave()
    {
        var (run, claim) = MariasFinalPayOnLeave(PayrollRunStatus.Approved, withClaim: true, advancing: true);

        await MaternitySut.MarkPaidAsync(run.Id);

        claim!.Status.Should().Be(MaternityClaimStatus.Advanced);
        claim.AdvanceRunId.Should().Be(run.Id);
        claim.AdvancedAt.Should().Be(new DateOnly(2026, 8, 31));
        _runRepo.Verify(r => r.SavePaidAsync(run, It.IsAny<IReadOnlyCollection<EmployeeLoan>>(),
            It.IsAny<IReadOnlyCollection<PeopleCore.Domain.Entities.Leave.LeaveBalance>>(),
            It.Is<IReadOnlyCollection<MaternityClaim>>(c => c.Single() == claim),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFinalPay_ShowsItsMaternityWarnings()
    {
        var (run, _) = MariasFinalPayOnLeave(PayrollRunStatus.Draft, withClaim: true, advancing: false);

        var dto = await MaternitySut.GetAsync(run.Id);

        dto!.Warnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
    }

    [Fact]
    public async Task ApproveAsync_AFinalPayWithMaternityDaysAndNoClaim_IsRefused()
    {
        var (run, _) = MariasFinalPayOnLeave(PayrollRunStatus.Draft, withClaim: false, advancing: false);

        var act = () => MaternitySut.ApproveAsync(run.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage(
            "Set up Maria Santos's maternity claim before approving; this payroll covers 6 maternity day(s).");
        run.Status.Should().Be(PayrollRunStatus.Draft);
    }

    [Fact]
    public async Task ApproveAsync_AFinalPayWhoseClaimChangedSinceCompute_IsRefused()
    {
        var (run, claim) = MariasFinalPayOnLeave(PayrollRunStatus.Draft, withClaim: true, advancing: true);
        claim!.DailyAllowance = 600m;
        claim.Benefit = 63_000m;

        var act = () => MaternitySut.ApproveAsync(run.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity claim has changed since this payroll was computed; recompute it before approving.");
    }
}
