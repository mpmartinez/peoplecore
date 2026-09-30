using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A final pay handles maternity like a regular run (RA 11210), through the same calculator: the
/// offset and differential for its own period, the benefit advance, and the warnings.
/// <para>
/// The worked example (36,500 a month, daily rate 1,200, final period Mar 1-13, 13 salary days,
/// regular pay 15,600), with Maria on maternity leave from Mar 3 to Jun 15 (105 days): Mar 3-13 is
/// 11 of the period's 13 calendar days. Her SSS daily allowance is 666.67.
///   Maternity days' pay: 15,600 x 11 / 13 = 13,200.00.
///   SSS covers 666.67 x 11 = 7,333.37: the offset. Differential: 13,200 - 7,333.37 = 5,866.63.
///   Regular pay: 15,600 - 7,333.37 = 8,266.63.
///   Benefit: 666.67 x 105 = 70,000.35.
/// </para>
/// </summary>
public partial class FinalPayServiceTests
{
    private readonly Mock<ILeaveRequestRepository> _leaveRequests = new();
    private readonly Mock<IMaternityClaimRepository> _maternityClaims = new();
    private FinalPayService? _maternitySut;

    private FinalPayService MaternitySut => _maternitySut ??= new FinalPayService(
        _separations.Object, _runs.Object, _compensations.Object, _loans.Object, _allowances.Object,
        _leaveBalances.Object, _leaveTypes.Object,
        _shifts.Object, _attendance.Object, _settings.Object,
        new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object),
        new PayrollComputationService(), TimeProvider.System,
        new MaternityPayCalculator(_leaveRequests.Object, _maternityClaims.Object, _runs.Object, _employees.Object));

    /// <summary>Maria's approved maternity leave from Mar 3, and (optionally) her claim.</summary>
    private MaternityClaim? OnMaternityLeave(decimal? allowance = 666.67m, bool withClaim = true)
    {
        var type = new LeaveType { Name = "Maternity Leave", Code = "ML", IsMaternity = true, IsPaid = true };
        var request = new LeaveRequest
        {
            EmployeeId = _employee.Id, Employee = _employee, LeaveType = type, LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 3, 3), EndDate = new DateOnly(2026, 6, 15), TotalDays = 105m,
            Status = LeaveStatus.Approved
        };
        _leaveRequests.Setup(l => l.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync([request]);
        MaternityClaim? claim = withClaim
            ? new MaternityClaim
            {
                EmployeeId = _employee.Id, Employee = _employee, LeaveRequestId = request.Id, LeaveRequest = request,
                Days = 105m, DailyAllowance = allowance, Benefit = allowance is decimal a ? MaternityMath.Benefit(a, 105m) : 0m
            }
            : null;
        _maternityClaims.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(claim is null ? [] : [claim]);
        _runs.Setup(r => r.GetMaternityAdvancesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([_employee]);
        return claim;
    }

    private static FinalPayRequest AdvancingRequest(bool advance) =>
        new(PayDate, null, null, null, null, [], AdvanceMaternityBenefit: advance);

    [Fact]
    public async Task CreateAsync_OffsetsItsOwnPeriod_AdvancesTheBenefit_AndShowsBothInTheSummary()
    {
        var claim = OnMaternityLeave();

        var summary = await MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: true));

        var entry = SavedEntry;
        entry.MaternityBenefitOffset.Should().Be(7_333.37m);
        entry.MaternityDifferential.Should().Be(5_866.63m);
        entry.RegularPay.Should().Be(8_266.63m);
        entry.MaternityBenefitAdvance.Should().Be(70_000.35m);
        entry.AdvanceMaternityBenefit.Should().BeTrue();
        entry.MaternityClaimId.Should().Be(claim!.Id);

        // 13th month: (36,500 + 8,266.63) / 12 = 3,730.5525 -> 3,730.55.
        entry.ThirteenthMonth.Should().Be(3_730.55m);
        // The settle: this entry's base is 8,266.63 - 5,866.63 - 2,862.50 < 0, and the 2316 over
        // February and this entry certifies 36,500 + (8,266.63 - 5,866.63 - 2,862.50) = 36,037.50
        // taxable (Item 37 takes the differential): tax due 0, so February's 2,000 comes back.
        entry.WithholdingTax.Should().Be(-2_000m);
        // Gross 8,266.63 + 3,730.55 + 6,000 + 182,500 + 70,000.35 = 270,497.53. Deductions 2,862.50
        // - 2,000 + 3,000 = 3,862.50. Net 266,635.03; the advance paid the shares, so none is deferred.
        entry.GrossPay.Should().Be(270_497.53m);
        entry.ContributionsDeferred.Should().Be(0m);
        entry.NetPay.Should().Be(266_635.03m);

        summary.AdvanceMaternityBenefit.Should().BeTrue();
        summary.MaternityBenefitAdvance.Should().Be(70_000.35m);
        summary.MaternityBenefitOffset.Should().Be(7_333.37m);
        summary.MaternityDifferential.Should().Be(5_866.63m);
        summary.MaternityWarnings.Should().BeEmpty();
        summary.NetPay.Should().Be(266_635.03m);
    }

    [Fact]
    public async Task CreateAsync_WithoutAnAdvance_StillOffsets_AndWarnsTheBenefitIsNotAdvancedYet()
    {
        OnMaternityLeave();

        var summary = await MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: false));

        SavedEntry.MaternityBenefitAdvance.Should().Be(0m);
        SavedEntry.MaternityBenefitOffset.Should().Be(7_333.37m);
        summary.MaternityWarnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
        // 8,266.63 + 3,730.55 + 6,000 + 182,500 = 200,497.18, less 3,862.50 = 196,634.68.
        summary.NetPay.Should().Be(196_634.68m);
    }

    [Fact]
    public async Task CreateAsync_AnAdvanceWithoutAReadyClaim_IsRefused_AndNothingIsSaved()
    {
        OnMaternityLeave(allowance: null);

        var act = () => MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: true));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no maternity claim ready to advance.");
        _savedRun.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_AnAdvanceAnotherRunCarries_IsRefused_NamingIt()
    {
        var claim = OnMaternityLeave();
        _runs.Setup(r => r.GetMaternityAdvancesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([new PeopleCore.Application.Payroll.Interfaces.MaternityAdvanceInRun(claim!.Id, "PAY-2026-005")]);

        var act = () => MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: true));

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-005.");
    }

    [Fact]
    public async Task CreateAsync_ForAnExemptEmployer_TakesTheLeaveDaysPayOff_WithNoDifferential()
    {
        OnMaternityLeave(withClaim: false);
        _settings.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PayrollSettings { ExemptFromMaternityDifferential = true });

        await MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: false));

        // All 13,200.00 of the leave days' pay; 15,600 - 13,200 = 2,400 left.
        SavedEntry.MaternityBenefitOffset.Should().Be(13_200m);
        SavedEntry.MaternityDifferential.Should().Be(0m);
        SavedEntry.RegularPay.Should().Be(2_400m);
    }

    [Fact]
    public async Task RecomputeAsync_KeepsTheAdvanceTheFinalPayWasCreatedWith()
    {
        var claim = OnMaternityLeave();
        await MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: true));

        var recomputed = (await MaternitySut.RecomputeAsync(_savedRun!)).Single();

        recomputed.AdvanceMaternityBenefit.Should().BeTrue();
        recomputed.MaternityBenefitAdvance.Should().Be(70_000.35m);
        recomputed.MaternityClaimId.Should().Be(claim!.Id);
        recomputed.MaternityBenefitOffset.Should().Be(7_333.37m);
    }

    [Fact]
    public async Task GetAsync_RebuildsTheMaternityWarnings()
    {
        var claim = OnMaternityLeave(allowance: null);
        await MaternitySut.CreateAsync(_separation.Id, AdvancingRequest(advance: false));
        claim!.DailyAllowance = 666.67m;
        claim.Benefit = 70_000.35m;

        var summary = await MaternitySut.GetAsync(_separation.Id);

        summary!.MaternityWarnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
    }
}
