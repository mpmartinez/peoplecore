using FluentAssertions;
using Moq;
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

public class MaternityClaimServiceTests
{
    private readonly Mock<IMaternityClaimRepository> _claims = new();
    private readonly Mock<ILeaveRequestRepository> _leaveRequests = new();
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly MaternityClaimService _sut;

    private static readonly Employee Maria = new() { FirstName = "Maria", MiddleName = "Reyes", LastName = "Santos" };

    public MaternityClaimServiceTests()
    {
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);
        _claims.Setup(c => c.AddAsync(It.IsAny<MaternityClaim>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((MaternityClaim c, CancellationToken _) => c);
        _sut = new MaternityClaimService(_claims.Object, _leaveRequests.Object, _runs.Object);
    }

    private static LeaveRequest ARequest(bool maternity = true, LeaveStatus status = LeaveStatus.Approved,
        DateOnly? start = null, Employee? employee = null)
    {
        var who = employee ?? Maria;
        var from = start ?? new DateOnly(2026, 8, 10);
        var type = new LeaveType { Name = maternity ? "Maternity Leave" : "Vacation Leave", IsMaternity = maternity };
        return new LeaveRequest
        {
            EmployeeId = who.Id, Employee = who, LeaveTypeId = type.Id, LeaveType = type,
            StartDate = from, EndDate = from.AddDays(104), TotalDays = 105m, Status = status,
        };
    }

    private MaternityClaim AClaim(MaternityClaimStatus status = MaternityClaimStatus.Draft, decimal? allowance = null,
        decimal benefit = 0m, LeaveRequest? request = null)
    {
        var leave = request ?? ARequest();
        var claim = new MaternityClaim
        {
            LeaveRequestId = leave.Id, LeaveRequest = leave, EmployeeId = leave.EmployeeId, Employee = leave.Employee,
            Days = leave.TotalDays, DailyAllowance = allowance, Benefit = benefit, Status = status,
        };
        _claims.Setup(c => c.GetByIdAsync(claim.Id, It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        return claim;
    }

    // ── Creating ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ForAnApprovedMaternityRequest_OpensADraftClaimForItsDays()
    {
        var request = ARequest();
        request.TotalDays = 120m;
        _leaveRequests.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        MaternityClaim? added = null;
        _claims.Setup(c => c.AddAsync(It.IsAny<MaternityClaim>(), It.IsAny<CancellationToken>()))
               .Callback((MaternityClaim c, CancellationToken _) => added = c)
               .ReturnsAsync((MaternityClaim c, CancellationToken _) => c);

        var dto = await _sut.CreateAsync(request.Id);

        added.Should().NotBeNull();
        added!.LeaveRequestId.Should().Be(request.Id);
        added.EmployeeId.Should().Be(Maria.Id);
        added.Days.Should().Be(120m);
        added.Status.Should().Be(MaternityClaimStatus.Draft);
        added.DailyAllowance.Should().BeNull();
        added.Benefit.Should().Be(0m);

        dto.Id.Should().Be(added.Id);
        dto.EmployeeName.Should().Be("Maria Reyes Santos");
        dto.LeaveStart.Should().Be(request.StartDate);
        dto.LeaveEnd.Should().Be(request.EndDate);
        dto.Days.Should().Be(120m);
        dto.Status.Should().Be(MaternityClaimStatus.Draft);
        dto.AdvanceRunNumber.Should().BeNull();
    }

    [Fact]
    public async Task Create_UnknownRequest_IsNotFound()
    {
        var act = () => _sut.CreateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(true, LeaveStatus.Pending)]
    [InlineData(true, LeaveStatus.Rejected)]
    [InlineData(true, LeaveStatus.Cancelled)]
    [InlineData(false, LeaveStatus.Approved)]
    public async Task Create_NotAnApprovedMaternityRequest_IsRefused(bool maternity, LeaveStatus status)
    {
        var request = ARequest(maternity, status);
        _leaveRequests.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var act = () => _sut.CreateAsync(request.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Only an approved maternity leave request can have a claim.");
        _claims.Verify(c => c.AddAsync(It.IsAny<MaternityClaim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ASecondClaimForTheSameLeave_IsRefused()
    {
        var request = ARequest();
        _leaveRequests.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _claims.Setup(c => c.GetByLeaveRequestAsync(request.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new MaternityClaim { LeaveRequestId = request.Id, EmployeeId = Maria.Id });

        var act = () => _sut.CreateAsync(request.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Reyes Santos already has a maternity claim for this leave.");
        _claims.Verify(c => c.AddAsync(It.IsAny<MaternityClaim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Setting the allowance ────────────────────────────────────────────────

    [Fact]
    public async Task SetAllowance_OnADraft_StoresItAndTheBenefitRounded()
    {
        var claim = AClaim();
        claim.Days = 105m;

        var dto = await _sut.SetAllowanceAsync(claim.Id, new SetAllowanceRequest(805.56m));

        claim.DailyAllowance.Should().Be(805.56m);
        claim.Benefit.Should().Be(84_583.80m);
        _claims.Verify(c => c.UpdateAsync(claim, It.IsAny<CancellationToken>()), Times.Once);
        dto.DailyAllowance.Should().Be(805.56m);
        dto.Benefit.Should().Be(84_583.80m);
    }

    [Fact]
    public async Task SetAllowance_ChangesAnAllowanceAlreadySet_WhileDraft()
    {
        var claim = AClaim(allowance: 500m, benefit: 52_500m);
        claim.Days = 60m;

        await _sut.SetAllowanceAsync(claim.Id, new SetAllowanceRequest(1_000.555m));

        // Stored to 2 dp, and the benefit is worked from what is stored.
        claim.DailyAllowance.Should().Be(1_000.56m);
        claim.Benefit.Should().Be(60_033.60m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetAllowance_NotPositive_IsRefused(decimal allowance)
    {
        var claim = AClaim();

        var act = () => _sut.SetAllowanceAsync(claim.Id, new SetAllowanceRequest(allowance));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter the SSS daily maternity allowance.");
        claim.DailyAllowance.Should().BeNull();
        _claims.Verify(c => c.UpdateAsync(It.IsAny<MaternityClaim>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(MaternityClaimStatus.Advanced)]
    [InlineData(MaternityClaimStatus.Reimbursed)]
    [InlineData(MaternityClaimStatus.Denied)]
    public async Task SetAllowance_OnceNoLongerDraft_IsRefused(MaternityClaimStatus status)
    {
        var claim = AClaim(status, allowance: 800m, benefit: 84_000m);

        var act = () => _sut.SetAllowanceAsync(claim.Id, new SetAllowanceRequest(900m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only a draft claim's allowance can be changed.");
        claim.DailyAllowance.Should().Be(800m);
        claim.Benefit.Should().Be(84_000m);
    }

    [Fact]
    public async Task SetAllowance_UnknownClaim_IsNotFound()
    {
        var act = () => _sut.SetAllowanceAsync(Guid.NewGuid(), new SetAllowanceRequest(900m));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Reimbursement ────────────────────────────────────────────────────────

    [Fact]
    public async Task Reimburse_TheFullBenefit_NeedsNoNote()
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var dto = await _sut.ReimburseAsync(claim.Id, new ReimburseRequest(new DateOnly(2026, 11, 3), 84_000m, null));

        claim.Status.Should().Be(MaternityClaimStatus.Reimbursed);
        claim.ReimbursedOn.Should().Be(new DateOnly(2026, 11, 3));
        claim.ReimbursedAmount.Should().Be(84_000m);
        claim.Note.Should().BeNull();
        _claims.Verify(c => c.UpdateAsync(claim, It.IsAny<CancellationToken>()), Times.Once);
        dto.Status.Should().Be(MaternityClaimStatus.Reimbursed);
        dto.ReimbursedAmount.Should().Be(84_000m);
    }

    [Fact]
    public async Task Reimburse_ADifferentAmount_WithANote_IsRecorded()
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        await _sut.ReimburseAsync(claim.Id, new ReimburseRequest(new DateOnly(2026, 11, 3), 80_000m, "  SSS used a lower MSC  "));

        claim.Status.Should().Be(MaternityClaimStatus.Reimbursed);
        claim.ReimbursedAmount.Should().Be(80_000m);
        claim.Note.Should().Be("SSS used a lower MSC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reimburse_ADifferentAmount_WithoutANote_IsRefused(string? note)
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var act = () => _sut.ReimburseAsync(claim.Id, new ReimburseRequest(new DateOnly(2026, 11, 3), 80_000m, note));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Explain why the reimbursement differs from the benefit.");
        claim.Status.Should().Be(MaternityClaimStatus.Advanced);
    }

    [Theory]
    [InlineData(MaternityClaimStatus.Draft)]
    [InlineData(MaternityClaimStatus.Reimbursed)]
    [InlineData(MaternityClaimStatus.Denied)]
    public async Task Reimburse_ANotAdvancedClaim_IsRefused(MaternityClaimStatus status)
    {
        var claim = AClaim(status, 800m, 84_000m);

        var act = () => _sut.ReimburseAsync(claim.Id, new ReimburseRequest(new DateOnly(2026, 11, 3), 84_000m, null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only an advanced claim can be reimbursed.");
        claim.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Reimburse_NoAmount_IsRefused(decimal amount)
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var act = () => _sut.ReimburseAsync(claim.Id, new ReimburseRequest(new DateOnly(2026, 11, 3), amount, "note"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter the amount SSS reimbursed.");
    }

    [Fact]
    public async Task Reimburse_NoDate_IsRefused()
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var act = () => _sut.ReimburseAsync(claim.Id, new ReimburseRequest(default, 84_000m, null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter the date SSS reimbursed the claim.");
    }

    [Fact]
    public async Task Reimburse_ANoteOver500Characters_IsRefused()
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var act = () => _sut.ReimburseAsync(claim.Id, new ReimburseRequest(new DateOnly(2026, 11, 3), 80_000m, new string('x', 501)));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Keep the note to 500 characters.");
    }

    // ── Denial ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deny_AnAdvancedClaim_WithANote_IsRecorded()
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var dto = await _sut.DenyAsync(claim.Id, new DenyRequest(" Fewer than 3 contributions in the window "));

        claim.Status.Should().Be(MaternityClaimStatus.Denied);
        claim.Note.Should().Be("Fewer than 3 contributions in the window");
        _claims.Verify(c => c.UpdateAsync(claim, It.IsAny<CancellationToken>()), Times.Once);
        dto.Status.Should().Be(MaternityClaimStatus.Denied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Deny_WithoutANote_IsRefused(string? note)
    {
        var claim = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);

        var act = () => _sut.DenyAsync(claim.Id, new DenyRequest(note!));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Explain why SSS denied the claim.");
        claim.Status.Should().Be(MaternityClaimStatus.Advanced);
    }

    [Theory]
    [InlineData(MaternityClaimStatus.Draft)]
    [InlineData(MaternityClaimStatus.Reimbursed)]
    [InlineData(MaternityClaimStatus.Denied)]
    public async Task Deny_ANotAdvancedClaim_IsRefused(MaternityClaimStatus status)
    {
        var claim = AClaim(status, 800m, 84_000m);

        var act = () => _sut.DenyAsync(claim.Id, new DenyRequest("Rejected"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Only an advanced claim can be denied.");
        claim.Status.Should().Be(status);
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_MapsEveryClaim_AndTotalsTheBenefitOfAdvancedClaimsAsOutstanding()
    {
        var run = new PayrollRun { RunNumber = "PAY-2026-017" };
        var advanced = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m);
        advanced.AdvanceRunId = run.Id;
        advanced.AdvanceRun = run;
        advanced.AdvancedAt = new DateOnly(2026, 8, 20);
        var advanced2 = AClaim(MaternityClaimStatus.Advanced, 500m, 30_000m);
        var draft = AClaim(MaternityClaimStatus.Draft, 900m, 94_500m);
        var reimbursed = AClaim(MaternityClaimStatus.Reimbursed, 700m, 73_500m);
        var denied = AClaim(MaternityClaimStatus.Denied, 600m, 63_000m);
        _claims.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync([advanced, advanced2, draft, reimbursed, denied]);

        var summary = await _sut.ListAsync();

        summary.Outstanding.Should().Be(114_000m);
        summary.Claims.Select(c => c.Id).Should().Equal(advanced.Id, advanced2.Id, draft.Id, reimbursed.Id, denied.Id);
        var first = summary.Claims[0];
        first.Should().Be(new MaternityClaimDto(advanced.Id, advanced.LeaveRequestId, Maria.Id, "Maria Reyes Santos",
            new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22), 105m, 800m, 84_000m, MaternityClaimStatus.Advanced,
            run.Id, "PAY-2026-017", new DateOnly(2026, 8, 20), null, null, null));
    }

    [Fact]
    public async Task List_NoClaims_HasNothingOutstanding()
    {
        _claims.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var summary = await _sut.ListAsync();

        summary.Claims.Should().BeEmpty();
        summary.Outstanding.Should().Be(0m);
    }

    [Fact]
    public async Task ReadyEmployeeIds_AreThoseWithADraftClaimThatHasAnAllowance()
    {
        var ana = new Employee { FirstName = "Ana", LastName = "Cruz" };
        var bea = new Employee { FirstName = "Bea", LastName = "Lim" };
        var cora = new Employee { FirstName = "Cora", LastName = "Tan" };
        var ready = AClaim(MaternityClaimStatus.Draft, 800m, 84_000m);
        var noAllowance = AClaim(MaternityClaimStatus.Draft, request: ARequest(employee: ana));
        var advanced = AClaim(MaternityClaimStatus.Advanced, 800m, 84_000m, ARequest(employee: bea));
        var denied = AClaim(MaternityClaimStatus.Denied, 800m, 84_000m, ARequest(employee: cora));
        _claims.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([ready, noAllowance, advanced, denied]);

        var ids = await _sut.ReadyEmployeeIdsAsync();

        ids.Should().Equal(Maria.Id);
    }

    [Fact]
    public async Task Eligible_ListsTheUnclaimedApprovedMaternityRequests()
    {
        var request = ARequest();
        _claims.Setup(c => c.GetUnclaimedApprovedRequestsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([request]);

        var eligible = await _sut.EligibleAsync();

        eligible.Should().Equal(new EligibleMaternityLeaveDto(request.Id, Maria.Id, "Maria Reyes Santos",
            new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22), 105m));
    }

    // ── The suggested allowance ──────────────────────────────────────────────

    private static PayrollRun PaidRun(DateOnly periodEnd, PayrollRunType type = PayrollRunType.Regular,
        params (Guid EmployeeId, decimal Sss)[] entries)
    {
        var run = new PayrollRun
        {
            RunNumber = "PAY", PeriodStart = new DateOnly(periodEnd.Year, periodEnd.Month, 1), PeriodEnd = periodEnd,
            PayDate = periodEnd, Status = PayrollRunStatus.Paid, Frequency = PayFrequency.Monthly, RunType = type,
        };
        foreach (var (id, sss) in entries)
            run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = id, SSSEmployee = sss });
        return run;
    }

    private void RunsIn(int year, int month, params PayrollRun[] runs)
        => _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(year, month, It.IsAny<CancellationToken>())).ReturnsAsync(runs);

    [Fact]
    public async Task Suggest_ReadsEachMonthOfTheWindowBeforeTheSemesterOfContingency()
    {
        // Leave starting 10 Aug 2026: Q3, so the semester is Apr-Sep 2026 and the window Apr 2025-Mar 2026.
        var claim = AClaim();

        var suggestion = await _sut.SuggestAsync(claim.Id);

        suggestion.WindowFrom.Should().Be(new DateOnly(2025, 4, 1));
        suggestion.WindowTo.Should().Be(new DateOnly(2026, 3, 31));
        for (var m = new DateOnly(2025, 4, 1); m <= new DateOnly(2026, 3, 1); m = m.AddMonths(1))
            _runs.Verify(r => r.GetPaidRunsByPeriodEndMonthAsync(m.Year, m.Month, It.IsAny<CancellationToken>()), Times.Once);
        _runs.Verify(r => r.GetPaidRunsByPeriodEndMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(12));
    }

    [Fact]
    public async Task Suggest_TakesTheSixHighestMonthlyCredits_Over180()
    {
        var claim = AClaim();
        var other = Guid.NewGuid();
        // MSC = share / 5%, to the nearest 500. Eight months, one of them two semi-monthly cutoffs.
        RunsIn(2025, 4, PaidRun(new(2025, 4, 30), entries: (Maria.Id, 1_000m)));                 // 20,000
        RunsIn(2025, 5, PaidRun(new(2025, 5, 31), entries: (Maria.Id, 500m)));                   // 10,000
        RunsIn(2025, 6, PaidRun(new(2025, 6, 15), entries: (Maria.Id, 875m)),
                        PaidRun(new(2025, 6, 30), entries: [(Maria.Id, 875m), (other, 5_000m)])); // 35,000
        RunsIn(2025, 7, PaidRun(new(2025, 7, 31), entries: (Maria.Id, 1_500m)));                 // 30,000
        RunsIn(2025, 9, PaidRun(new(2025, 9, 30), entries: (Maria.Id, 750m)));                   // 15,000
        RunsIn(2025, 11, PaidRun(new(2025, 11, 30), entries: (Maria.Id, 1_250m)));               // 25,000
        RunsIn(2026, 1, PaidRun(new(2026, 1, 31), entries: (Maria.Id, 1_000m)));                 // 20,000
        RunsIn(2026, 3, PaidRun(new(2026, 3, 31), entries: (Maria.Id, 1_250m)));                 // 25,000

        var suggestion = await _sut.SuggestAsync(claim.Id);

        // 35,000 + 30,000 + 25,000 + 25,000 + 20,000 + 20,000 = 155,000 / 180 = 861.11
        suggestion.DailyAllowance.Should().Be(861.11m);
        suggestion.MonthsFound.Should().Be(8);
    }

    [Fact]
    public async Task Suggest_SkipsFinalPayRuns_MonthsWithoutAnEntry_AndMonthsWithNoSss()
    {
        var claim = AClaim();
        RunsIn(2025, 4, PaidRun(new(2025, 4, 30), entries: (Maria.Id, 1_000m)),                   // 20,000
                        PaidRun(new(2025, 4, 30), PayrollRunType.FinalPay, (Maria.Id, 1_000m)));  // not a regular run
        RunsIn(2025, 5, PaidRun(new(2025, 5, 31), entries: (Guid.NewGuid(), 1_000m)));            // not Maria
        RunsIn(2025, 6, PaidRun(new(2025, 6, 30), entries: (Maria.Id, 0m)));                      // no SSS: no MSC
        RunsIn(2025, 7, PaidRun(new(2025, 7, 31), PayrollRunType.FinalPay, (Maria.Id, 1_500m)));  // only a final pay

        var suggestion = await _sut.SuggestAsync(claim.Id);

        // Fewer than 6 months still gives a suggestion: 20,000 / 180 = 111.11
        suggestion.DailyAllowance.Should().Be(111.11m);
        suggestion.MonthsFound.Should().Be(1);
    }

    [Fact]
    public async Task Suggest_NoPaidPayrollInTheWindow_HasNoSuggestion()
    {
        var claim = AClaim(request: ARequest(start: new DateOnly(2026, 2, 3)));

        var suggestion = await _sut.SuggestAsync(claim.Id);

        // Q1 2026: the semester is Oct 2025-Mar 2026, the window Oct 2024-Sep 2025.
        suggestion.Should().Be(new SuggestedAllowanceDto(null, 0, new DateOnly(2024, 10, 1), new DateOnly(2025, 9, 30)));
    }

    [Fact]
    public async Task Suggest_UnknownClaim_IsNotFound()
    {
        var act = () => _sut.SuggestAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
