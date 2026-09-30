using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// The maternity advance, offset and warnings for a regular run (RA 11210).
/// <para>
/// Maria Santos's leave runs 10 Aug to 22 Nov 2026: 105 calendar days (22 in August, 30 in
/// September, 31 in October, 22 in November). Her SSS daily allowance is 666.67, the statutory
/// maximum, so the benefit is 666.67 x 105 = 70,000.35.
/// </para>
/// </summary>
public class MaternityPayCalculatorTests
{
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IMaternityClaimRepository> _claims = new();
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly MaternityPayCalculator _sut;

    private readonly Employee _maria = new() { FirstName = "Maria", LastName = "Santos" };
    private readonly LeaveType _maternityType = new() { Name = "Maternity Leave", Code = "ML", IsMaternity = true, IsPaid = true };
    private readonly LeaveRequest _leaveRequest;

    private static readonly DateOnly LeaveStart = new(2026, 8, 10);
    private static readonly DateOnly LeaveEnd = new(2026, 11, 22);

    public MaternityPayCalculatorTests()
    {
        _leaveRequest = new LeaveRequest
        {
            EmployeeId = _maria.Id, Employee = _maria, LeaveTypeId = _maternityType.Id, LeaveType = _maternityType,
            StartDate = LeaveStart, EndDate = LeaveEnd, TotalDays = 105m, Status = LeaveStatus.Approved
        };
        _leave.Setup(l => l.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                  new[] { _leaveRequest }.Where(r => r.StartDate <= to && r.EndDate >= from).ToList());
        _claims.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([]);
        _runs.Setup(r => r.GetMaternityAdvancesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);
        _employees.Setup(e => e.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([_maria]);
        _sut = new MaternityPayCalculator(_leave.Object, _claims.Object, _runs.Object, _employees.Object);
    }

    private static PayrollRun Cutoff(int month, int firstDay, int lastDay, PayrollRunType type = PayrollRunType.Regular) => new()
    {
        RunNumber = "PAY-2026-020",
        PeriodStart = new DateOnly(2026, month, firstDay),
        PeriodEnd = new DateOnly(2026, month, lastDay),
        PayDate = new DateOnly(2026, month, lastDay),
        Frequency = PayFrequency.SemiMonthly,
        RunType = type,
        Status = PayrollRunStatus.Draft
    };

    private MaternityClaim AClaim(decimal? allowance = 666.67m, MaternityClaimStatus status = MaternityClaimStatus.Draft,
        PayrollRun? advanceRun = null)
    {
        var claim = new MaternityClaim
        {
            EmployeeId = _maria.Id, Employee = _maria, LeaveRequestId = _leaveRequest.Id, LeaveRequest = _leaveRequest,
            Days = 105m, DailyAllowance = allowance,
            Benefit = allowance is decimal a ? MaternityMath.Benefit(a, 105m) : 0m,
            Status = status, AdvanceRun = advanceRun, AdvanceRunId = advanceRun?.Id
        };
        _claims.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([claim]);
        return claim;
    }

    private void AdvancedOn(MaternityClaim claim, string runNumber)
        => _runs.Setup(r => r.GetMaternityAdvancesAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(claim.Id)),
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new MaternityAdvanceInRun(claim.Id, runNumber)]);

    // ---- the advance ---------------------------------------------------------------------------

    [Fact]
    public async Task TheAdvance_IsTheClaimsBenefit_OnTheClaim()
    {
        var claim = AClaim();

        var pay = await _sut.ForAsync(Cutoff(7, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        // 666.67 x 105 = 70,000.35; July 16-31 is before the leave, so there is no offset.
        pay.Advance.Should().Be(70_000.35m);
        pay.ClaimId.Should().Be(claim.Id);
        pay.Offset.Should().Be(0m);
        pay.Warnings.Should().BeEmpty("the run advances the benefit");
    }

    [Fact]
    public async Task TheAdvance_WithNoClaim_IsRefused()
    {
        var act = () => _sut.ForAsync(Cutoff(7, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no maternity claim ready to advance.");
    }

    [Fact]
    public async Task TheAdvance_OnAClaimWithNoAllowance_IsRefused()
    {
        AClaim(allowance: null);

        var act = () => _sut.ForAsync(Cutoff(7, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no maternity claim ready to advance.");
    }

    [Theory]
    [InlineData(MaternityClaimStatus.Advanced)]
    [InlineData(MaternityClaimStatus.Reimbursed)]
    [InlineData(MaternityClaimStatus.Denied)]
    public async Task TheAdvance_OnAClaimAlreadyAdvanced_ForLeaveStillCurrent_IsRefused_NamingTheRun(MaternityClaimStatus status)
    {
        // The leave (Aug 10 - Nov 22) hasn't ended by Aug 16, so this is the same pregnancy's claim.
        AClaim(status: status, advanceRun: new PayrollRun { RunNumber = "PAY-2026-015" });

        var act = () => _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-015.");
    }

    [Fact]
    public async Task TheAdvance_WhenSeveralCurrentClaimsWereAdvanced_NamesTheLatestAdvance()
    {
        var earlier = AClaim(status: MaternityClaimStatus.Denied, advanceRun: new PayrollRun { RunNumber = "PAY-2026-010" });
        earlier.AdvancedAt = new DateOnly(2026, 6, 5);
        var later = AClaim(status: MaternityClaimStatus.Advanced, advanceRun: new PayrollRun { RunNumber = "PAY-2026-015" });
        later.AdvancedAt = new DateOnly(2026, 8, 5);
        _claims.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([earlier, later]);

        var act = () => _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-015.");
    }

    [Fact]
    public async Task TheAdvance_WithADeniedClaimThatNoRunAdvanced_IsRefused_AsNoneReady()
    {
        AClaim(status: MaternityClaimStatus.Denied);

        var act = () => _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no maternity claim ready to advance.");
    }

    [Theory]
    [InlineData(MaternityClaimStatus.Advanced)]
    [InlineData(MaternityClaimStatus.Reimbursed)]
    [InlineData(MaternityClaimStatus.Denied)]
    public async Task TheAdvance_WithOnlyAnEarlierPregnancysClaim_IsRefused_AsNoneReady(MaternityClaimStatus status)
    {
        // An earlier pregnancy: that leave ended 22 Nov 2025, before this Aug 16-31 2026 cutoff.
        _leaveRequest.StartDate = new DateOnly(2025, 8, 10);
        _leaveRequest.EndDate = new DateOnly(2025, 11, 22);
        AClaim(status: status, advanceRun: new PayrollRun { RunNumber = "PAY-2025-015" });

        var act = () => _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos has no maternity claim ready to advance.");
    }

    [Fact]
    public async Task TheAdvance_WhenAnotherRunCarriesIt_IsRefused_NamingThatRun()
    {
        var claim = AClaim();
        AdvancedOn(claim, "PAY-2026-016");
        var run = Cutoff(8, 1, 15);

        var act = () => _sut.ForAsync(run, _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-016.");
        // This run's own stored entry doesn't count against it on a recompute.
        _runs.Verify(r => r.GetMaternityAdvancesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), run.Id, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task TheAdvance_WhenTheOnlyReadyClaimIsCarriedElsewhere_NamesThatRun_EvenBesideAnEarlierAdvancedClaim()
    {
        var earlier = new MaternityClaim
        {
            EmployeeId = _maria.Id, Employee = _maria, LeaveRequestId = Guid.NewGuid(), Days = 105m, DailyAllowance = 600m,
            Benefit = 63_000m, Status = MaternityClaimStatus.Reimbursed, AdvanceRun = new PayrollRun { RunNumber = "PAY-2024-011" }
        };
        var claim = AClaim();
        _claims.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([claim, earlier]);
        AdvancedOn(claim, "PAY-2026-016");

        var act = () => _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, advanceRequested: true, 15_000m, exempt: false);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-016.");
    }

    [Fact]
    public async Task NoAdvanceRequested_AdvancesNothing()
    {
        AClaim();

        var pay = await _sut.ForAsync(Cutoff(7, 16, 31), _maria.Id, advanceRequested: false, 15_000m, exempt: false);

        pay.Advance.Should().Be(0m);
        pay.ClaimId.Should().BeNull();
    }

    // ---- the offset ----------------------------------------------------------------------------

    [Fact]
    public async Task TheOffset_IsSplitAcrossTheCutoffsByTheLeaveDaysInEach()
    {
        AClaim(status: MaternityClaimStatus.Advanced, advanceRun: new PayrollRun { RunNumber = "PAY-2026-015" });

        var first = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: false);
        var second = await _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, false, 15_000m, exempt: false);
        var november = await _sut.ForAsync(Cutoff(11, 16, 30), _maria.Id, false, 15_000m, exempt: false);

        // Aug 10-15 is 6 days: 666.67 x 6 = 4,000.02. Aug 16-31 is 16 days: 666.67 x 16 = 10,666.72.
        // Nov 16-22 is 7 days: 666.67 x 7 = 4,666.69. The claim's status doesn't matter.
        first.Offset.Should().Be(4_000.02m);
        second.Offset.Should().Be(10_666.72m);
        november.Offset.Should().Be(4_666.69m);
        first.Warnings.Should().BeEmpty();

        // The salary differential is the pay for the leave days the offset leaves:
        //   Aug 1-15:  15,000 x 6 / 15 = 6,000.00 - 4,000.02 = 1,999.98;
        //   Aug 16-31: 15,000 x 16 / 16 = 15,000.00 - 10,666.72 = 4,333.28;
        //   Nov 16-30: 15,000 x 7 / 15 = 7,000.00 - 4,666.69 = 2,333.31.
        first.Differential.Should().Be(1_999.98m);
        second.Differential.Should().Be(4_333.28m);
        november.Differential.Should().Be(2_333.31m);
    }

    [Fact]
    public async Task TheOffset_IsNeverMoreThanThePayForTheLeaveDays()
    {
        AClaim();

        var pay = await _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, false, 10_000m, exempt: false);

        // Aug 16-31 is all leave: 10,000 x 16 / 16 = 10,000 for the leave days. 10,666.72 covered,
        // but only 10,000 to take it from, and no differential left.
        pay.Offset.Should().Be(10_000m);
        pay.Differential.Should().Be(0m);
    }

    [Fact]
    public async Task TheOffset_NeverTakesPayForTheDaysOutsideTheLeave()
    {
        AClaim();

        // Absences left 3,000 of the Aug 1-15 regular pay. The 6 leave days' share of it is
        // 3,000 x 6 / 15 = 1,200.00. SSS covers 666.67 x 6 = 4,000.02 of those days, so the offset
        // is the 1,200.00 and no more: the 1,800.00 for Aug 1-9 is still paid. No differential.
        var pay = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 3_000m, exempt: false);

        pay.Offset.Should().Be(1_200m);
        pay.Differential.Should().Be(0m);
    }

    [Fact]
    public async Task TheOffset_ForAnExemptEmployer_IsTheRegularPayForTheLeaveDays_WithOrWithoutAClaim()
    {
        var withoutClaim = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: true);
        AClaim();
        var withClaim = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: true);
        var wholeCutoff = await _sut.ForAsync(Cutoff(8, 16, 31), _maria.Id, false, 15_000m, exempt: true);

        // 15,000 x 6 / 15 calendar days = 6,000.00; Aug 16-31 is all leave: 15,000 x 16 / 16.
        withoutClaim.Offset.Should().Be(6_000m);
        withClaim.Offset.Should().Be(6_000m);
        wholeCutoff.Offset.Should().Be(15_000m);
        // An exempt employer pays no salary differential.
        withoutClaim.Differential.Should().Be(0m);
        withClaim.Differential.Should().Be(0m);
        wholeCutoff.Differential.Should().Be(0m);
    }

    [Fact]
    public async Task OverlappingApprovedRequests_CountEachLeaveDayOnce()
    {
        // A second approved maternity request, Aug 12-20, overlaps the first. Aug 1-15 still holds
        // only Aug 10-15: 6 days, not 6 + 4.
        var claim = AClaim();
        var overlapping = new LeaveRequest
        {
            EmployeeId = _maria.Id, Employee = _maria, LeaveTypeId = _maternityType.Id, LeaveType = _maternityType,
            StartDate = new DateOnly(2026, 8, 12), EndDate = new DateOnly(2026, 8, 20), TotalDays = 9m, Status = LeaveStatus.Approved
        };
        var overlappingClaim = new MaternityClaim
        {
            EmployeeId = _maria.Id, Employee = _maria, LeaveRequestId = overlapping.Id, LeaveRequest = overlapping,
            Days = 9m, DailyAllowance = 500m, Benefit = 4_500m
        };
        _leave.Setup(l => l.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([overlapping, _leaveRequest]);
        _claims.Setup(c => c.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([claim, overlappingClaim]);

        var withAllowance = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: false);
        var exempt = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: true);

        // The earlier request's claim covers all 6 days: 666.67 x 6 = 4,000.02, and the overlapping
        // request adds nothing. Exempt: 15,000 x 6 / 15 = 6,000.00, not 15,000 x 10 / 15.
        withAllowance.Offset.Should().Be(4_000.02m);
        exempt.Offset.Should().Be(6_000m);
        // 15,000 x 6 / 15 = 6,000.00 for the days, 4,000.02 covered: 1,999.98 differential.
        withAllowance.Differential.Should().Be(1_999.98m);
    }

    [Fact]
    public async Task WithNoAllowance_ThereIsNoOffset_AndHrIsWarned()
    {
        AClaim(allowance: null);

        var pay = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: false);

        pay.Offset.Should().Be(0m);
        pay.Differential.Should().Be(0m, "without an allowance nothing is known to be the SSS benefit's");
        pay.Warnings.Should().Equal("Maternity benefit not set up yet for Maria Santos.");
    }

    [Fact]
    public async Task WithNoClaim_ThereIsNoOffset_AndHrIsWarned()
    {
        var pay = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: false);

        pay.Offset.Should().Be(0m);
        pay.Differential.Should().Be(0m);
        pay.Warnings.Should().Equal("Maternity benefit not set up yet for Maria Santos.");
    }

    [Fact]
    public async Task OnlyMaternityLeave_OfThisEmployee_IsOffset()
    {
        AClaim();
        var ana = new Employee { FirstName = "Ana", LastName = "Cruz" };
        _leaveRequest.EmployeeId = ana.Id;
        _leaveRequest.Employee = ana;
        var vacation = new LeaveRequest
        {
            EmployeeId = _maria.Id, Employee = _maria, StartDate = new DateOnly(2026, 8, 3), EndDate = new DateOnly(2026, 8, 5),
            LeaveType = new LeaveType { Name = "Vacation Leave", Code = "VL", IsPaid = true }, Status = LeaveStatus.Approved
        };
        _leave.Setup(l => l.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([_leaveRequest, vacation]);

        var pay = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: false);

        pay.Offset.Should().Be(0m);
    }

    // ---- warnings ------------------------------------------------------------------------------

    [Fact]
    public async Task ADraftClaimWithAnAllowance_NoRunCarrying_IsWarnedAsNotAdvanced()
    {
        AClaim();

        var pay = await _sut.ForAsync(Cutoff(7, 16, 31), _maria.Id, false, 15_000m, exempt: false);

        pay.Warnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
    }

    [Fact]
    public async Task ADraftClaimAnotherRunCarries_IsNotWarned()
    {
        var claim = AClaim();
        AdvancedOn(claim, "PAY-2026-016");

        var pay = await _sut.ForAsync(Cutoff(8, 1, 15), _maria.Id, false, 15_000m, exempt: false);

        pay.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRunsWarnings_AreRebuiltFromItsEntries_AndItsOwnAdvanceCounts()
    {
        var claim = AClaim();
        var ana = new Employee { FirstName = "Ana", LastName = "Cruz" };
        var run = Cutoff(8, 1, 15);
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = _maria.Id, Employee = _maria });
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = ana.Id, Employee = ana });

        var before = await _sut.WarningsAsync(run);
        run.Employees[0].AdvanceMaternityBenefit = true;
        run.Employees[0].MaternityBenefitAdvance = 70_000.35m;
        run.Employees[0].MaternityClaimId = claim.Id;
        var after = await _sut.WarningsAsync(run);

        before.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");
        after.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRunsWarnings_NameEveryoneOnLeaveWithoutASetUpClaim()
    {
        var run = Cutoff(8, 1, 15);
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = _maria.Id, Employee = _maria });

        (await _sut.WarningsAsync(run)).Should().Equal("Maternity benefit not set up yet for Maria Santos.");
    }

    // ---- final pay -----------------------------------------------------------------------------

    [Fact]
    public async Task AFinalPay_HasNoWarnings_AndSettlesNothing()
    {
        AClaim();
        var run = Cutoff(8, 1, 15, PayrollRunType.FinalPay);
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = _maria.Id, Employee = _maria });

        (await _sut.WarningsAsync(run)).Should().BeEmpty();
        (await _sut.SettleAdvancesAsync(run)).Should().BeEmpty();
        _leave.VerifyNoOtherCalls();
        _claims.VerifyNoOtherCalls();
    }

    // ---- Mark Paid -----------------------------------------------------------------------------

    [Fact]
    public async Task Settling_MarksTheAdvancedClaim_WithTheRunAndItsPayDate()
    {
        var claim = AClaim();
        var run = Cutoff(7, 16, 31);
        run.PayDate = new DateOnly(2026, 8, 5);
        run.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = _maria.Id, AdvanceMaternityBenefit = true, MaternityBenefitAdvance = 70_000.35m, MaternityClaimId = claim.Id
        });
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = Guid.NewGuid() });

        var settled = await _sut.SettleAdvancesAsync(run);

        settled.Should().Equal(claim);
        claim.Status.Should().Be(MaternityClaimStatus.Advanced);
        claim.AdvanceRunId.Should().Be(run.Id);
        claim.AdvancedAt.Should().Be(new DateOnly(2026, 8, 5));
    }

    [Fact]
    public async Task Settling_WhenTheBenefitChangedSinceCompute_IsRefused()
    {
        var claim = AClaim(allowance: 600m);   // 600 x 105 = 63,000, not the 70,000.35 computed
        var run = Cutoff(7, 16, 31);
        run.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = _maria.Id, Employee = _maria, AdvanceMaternityBenefit = true, MaternityBenefitAdvance = 70_000.35m,
            MaternityClaimId = claim.Id
        });

        var act = () => _sut.SettleAdvancesAsync(run);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity claim has changed since this payroll was computed; " +
                         "discard this payroll and create it again.");
        claim.Status.Should().Be(MaternityClaimStatus.Draft);
    }

    [Fact]
    public async Task Settling_AClaimAlreadyAdvancedElsewhere_IsRefused_NamingThatRun()
    {
        var claim = AClaim(status: MaternityClaimStatus.Advanced, advanceRun: new PayrollRun { RunNumber = "PAY-2026-015" });
        var run = Cutoff(7, 16, 31);
        run.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = _maria.Id, Employee = _maria, AdvanceMaternityBenefit = true, MaternityBenefitAdvance = 70_000.35m,
            MaternityClaimId = claim.Id
        });

        var act = () => _sut.SettleAdvancesAsync(run);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity benefit was already advanced on PAY-2026-015.");
        claim.AdvanceRunId.Should().NotBe(run.Id);
    }

    [Fact]
    public async Task CheckingBeforeApproval_WhenTheBenefitChangedSinceCompute_IsRefused_WhileARecomputeCanStillFixIt()
    {
        var claim = AClaim(allowance: 600m);   // 63,000 now, 70,000.35 when computed
        var run = Cutoff(7, 16, 31);
        run.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = _maria.Id, Employee = _maria, AdvanceMaternityBenefit = true, MaternityBenefitAdvance = 70_000.35m,
            MaternityClaimId = claim.Id
        });

        var act = () => _sut.EnsureAdvancesCurrentAsync(run);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos's maternity claim has changed since this payroll was computed; recompute it before approving.");
        claim.Status.Should().Be(MaternityClaimStatus.Draft);
    }

    [Fact]
    public async Task CheckingBeforeApproval_AnUnchangedClaim_ChangesNothing()
    {
        var claim = AClaim();
        var run = Cutoff(7, 16, 31);
        run.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = _maria.Id, AdvanceMaternityBenefit = true, MaternityBenefitAdvance = 70_000.35m, MaternityClaimId = claim.Id
        });

        await _sut.EnsureAdvancesCurrentAsync(run);

        claim.Status.Should().Be(MaternityClaimStatus.Draft);
        claim.AdvanceRunId.Should().BeNull();
    }

    [Fact]
    public async Task Settling_ARunWithNoAdvance_ReadsNoClaims()
    {
        var run = Cutoff(8, 1, 15);
        run.Employees.Add(new PayrollRunEmployee { EmployeeId = _maria.Id, MaternityBenefitOffset = 4_000.02m });

        (await _sut.SettleAdvancesAsync(run)).Should().BeEmpty();
        _claims.VerifyNoOtherCalls();
    }
}
