using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Maternity pay through the real repositories and Postgres, from creating the run to Mark Paid.
/// Maria Santos earns 30,000 a month, paid semi-monthly (15,000 a cutoff). Her maternity leave runs
/// 10 Aug to 22 Nov 2026 (105 days) and her SSS daily allowance is 666.67, so the benefit is
/// 666.67 x 105 = 70,000.35. Every step runs on a fresh context, as each request does.
/// </summary>
public class MaternityPayDbTests : DatabaseTestBase
{
    public MaternityPayDbTests(PostgresFixture fixture) : base(fixture) { }

    private static PayrollRunService Payroll(AppDbContext context)
    {
        var runs = new PayrollRunRepository(context);
        var bridge = new Mock<IPayrollAttendanceBridge>();
        bridge.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
              .ReturnsAsync((IReadOnlyList<Guid> ids, DateOnly _, DateOnly _, CancellationToken _) =>
                  new AttendanceBridgeResult(ids.ToDictionary(id => id, _ => new PayrollAttendanceInput()), []));
        return new PayrollRunService(
            runs, new EmployeeCompensationRepository(context), new EmployeeAllowanceRepository(context),
            new EmployeeLoanRepository(context), new PayrollSettingsRepository(context),
            new PayrollComputationService(), bridge.Object, new EmployeeRepository(context),
            new SeparationRepository(context), NullLogger<PayrollRunService>.Instance,
            maternityPay: new MaternityPayCalculator(new LeaveRequestRepository(context),
                new MaternityClaimRepository(context), runs, new EmployeeRepository(context)));
    }

    private static MaternityClaimService Claims(AppDbContext context) => new(
        new MaternityClaimRepository(context), new LeaveRequestRepository(context), new PayrollRunRepository(context),
        new PayrollSettingsRepository(context));

    /// <summary>Maria, her compensation, her approved maternity leave, and her claim with the allowance set.</summary>
    private async Task<(Employee Maria, Guid ClaimId)> MariaWithAReadyClaimAsync()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 30_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        });
        var type = new LeaveType { Name = "Maternity Leave", Code = "ML", MaxDaysPerYear = 105m, IsMaternity = true, IsPaid = true };
        Context.LeaveTypes.Add(type);
        var request = new LeaveRequest
        {
            EmployeeId = maria.Id, LeaveTypeId = type.Id, StartDate = new DateOnly(2026, 8, 10),
            EndDate = new DateOnly(2026, 11, 22), TotalDays = 105m, Status = LeaveStatus.Approved
        };
        Context.LeaveRequests.Add(request);
        await Context.SaveChangesAsync();

        await using var context = NewContext();
        var claim = await Claims(context).CreateAsync(request.Id);
        await Claims(context).SetAllowanceAsync(claim.Id, new SetAllowanceRequest(666.67m));
        return (maria, claim.Id);
    }

    private static CreatePayrollRunRequest Cutoff(Guid employeeId, int month, int first, int last, DateOnly payDate, bool advance)
        => new(new DateOnly(2026, month, first), new DateOnly(2026, month, last), payDate, PayFrequency.SemiMonthly,
            [new PayrollRunEmployeeInput(employeeId, AdvanceMaternityBenefit: advance)]);

    private async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request)
    {
        await using var context = NewContext();
        return await Payroll(context).CreateAsync(request);
    }

    private async Task<MaternityClaim> ClaimAsync(Guid claimId)
    {
        await using var reader = NewContext();
        return await new MaternityClaimRepository(reader).GetByIdAsync(claimId)
            ?? throw new InvalidOperationException("The claim is gone.");
    }

    [Fact]
    public async Task ARunAdvancesTheBenefit_TheLeaveCutoffsOffsetIt_AndMarkPaidSettlesTheClaim()
    {
        var (maria, claimId) = await MariaWithAReadyClaimAsync();

        // Jul 16-31, paid Aug 5, advances the benefit. The leave hasn't started: no offset.
        var july = await CreateAsync(Cutoff(maria.Id, 7, 16, 31, new DateOnly(2026, 8, 5), advance: true));
        // Aug 1-15 holds 6 leave days (Aug 10-15): 666.67 x 6 = 4,000.02 off the 15,000.
        var august = await CreateAsync(Cutoff(maria.Id, 8, 1, 15, new DateOnly(2026, 8, 20), advance: false));

        var julyLine = july.Employees.Single();
        julyLine.MaternityBenefitAdvance.Should().Be(70_000.35m);
        julyLine.MaternityBenefitOffset.Should().Be(0m);
        julyLine.RegularPay.Should().Be(15_000m);
        julyLine.GrossPay.Should().Be(85_000.35m);
        july.Warnings.Should().BeEmpty();
        var augustLine = august.Employees.Single();
        augustLine.MaternityBenefitAdvance.Should().Be(0m);
        augustLine.MaternityBenefitOffset.Should().Be(4_000.02m);
        augustLine.RegularPay.Should().Be(10_999.98m);
        august.Warnings.Should().BeEmpty("the July run carries the advance");

        await using (var context = NewContext())
        {
            await Payroll(context).ApproveAsync(july.Id);
        }
        (await ClaimAsync(claimId)).Status.Should().Be(MaternityClaimStatus.Draft, "approving pays nothing yet");
        await using (var context = NewContext())
        {
            await Payroll(context).MarkPaidAsync(july.Id);
        }

        var claim = await ClaimAsync(claimId);
        claim.Status.Should().Be(MaternityClaimStatus.Advanced);
        claim.AdvanceRunId.Should().Be(july.Id);
        claim.AdvancedAt.Should().Be(new DateOnly(2026, 8, 5));
        claim.AdvanceRun!.RunNumber.Should().Be(july.RunNumber);
        await using (var reader = NewContext())
        {
            (await reader.PayrollRuns.SingleAsync(r => r.Id == july.Id)).Status.Should().Be(PayrollRunStatus.Paid);
            var entry = await reader.PayrollRunEmployees.SingleAsync(e => e.PayrollRunId == july.Id);
            entry.AdvanceMaternityBenefit.Should().BeTrue();
            entry.MaternityClaimId.Should().Be(claimId);
        }

        // Paid once: a later run can't advance it again while the leave is current.
        var again = () => CreateAsync(Cutoff(maria.Id, 8, 16, 31, new DateOnly(2026, 9, 5), advance: true));
        await again.Should().ThrowAsync<DomainException>()
            .WithMessage($"Maria Santos's maternity benefit was already advanced on {july.RunNumber}.");
    }

    [Fact]
    public async Task AnotherUnpaidRunCarryingTheAdvance_RefusesASecond()
    {
        var (maria, _) = await MariaWithAReadyClaimAsync();
        var first = await CreateAsync(Cutoff(maria.Id, 7, 16, 31, new DateOnly(2026, 8, 5), advance: true));

        var second = () => CreateAsync(Cutoff(maria.Id, 8, 1, 15, new DateOnly(2026, 8, 20), advance: true));

        await second.Should().ThrowAsync<DomainException>()
            .WithMessage($"Maria Santos's maternity benefit was already advanced on {first.RunNumber}.");
    }

    [Fact]
    public async Task ARecompute_KeepsTheAdvance_AndReproducesTheEntry()
    {
        var (maria, claimId) = await MariaWithAReadyClaimAsync();
        var created = await CreateAsync(Cutoff(maria.Id, 8, 1, 15, new DateOnly(2026, 8, 20), advance: true));
        PayrollRunEmployee before;
        await using (var reader = NewContext())
        {
            before = await reader.PayrollRunEmployees.Include(e => e.PremiumDays).Include(e => e.LoanDeductionLines)
                .SingleAsync(e => e.PayrollRunId == created.Id);
        }

        await using (var context = NewContext())
        {
            await Payroll(context).ComputeAsync(created.Id);
        }

        await using var after = NewContext();
        var entry = await after.PayrollRunEmployees.Include(e => e.PremiumDays).Include(e => e.LoanDeductionLines)
            .SingleAsync(e => e.PayrollRunId == created.Id);
        entry.Id.Should().NotBe(before.Id, "a recompute replaces the entry");
        entry.Should().BeEquivalentTo(before, o => o.Excluding(m => m.Name == "Id" || m.Name == "PayrollRunEmployeeId"
            || m.Name == "PayrollRunEmployee" || m.Name == "CreatedAt" || m.Name == "UpdatedAt" || m.Name == "CreatedBy"
            || m.Name == "UpdatedBy" || m.Name == "PayrollRun" || m.Name == "Employee"));
        entry.AdvanceMaternityBenefit.Should().BeTrue();
        entry.MaternityClaimId.Should().Be(claimId);
        entry.MaternityBenefitAdvance.Should().Be(70_000.35m);
        entry.MaternityBenefitOffset.Should().Be(4_000.02m);
    }

    [Fact]
    public async Task DiscardingARunThatCarriedTheAdvance_LeavesTheClaimDraft_ForAnotherRunToAdvance()
    {
        var (maria, claimId) = await MariaWithAReadyClaimAsync();
        var discarded = await CreateAsync(Cutoff(maria.Id, 7, 16, 31, new DateOnly(2026, 8, 5), advance: true));

        await using (var context = NewContext())
        {
            await Payroll(context).DiscardAsync(discarded.Id);
        }

        var claim = await ClaimAsync(claimId);
        claim.Status.Should().Be(MaternityClaimStatus.Draft);
        claim.AdvanceRunId.Should().BeNull();
        var next = await CreateAsync(Cutoff(maria.Id, 8, 1, 15, new DateOnly(2026, 8, 20), advance: true));
        next.Employees.Single().MaternityBenefitAdvance.Should().Be(70_000.35m);
    }

    [Fact]
    public async Task TheAllowance_IsLockedWhileAnUnpaidRunAdvancesIt_AndOnceItIsPaid()
    {
        var (maria, claimId) = await MariaWithAReadyClaimAsync();
        var run = await CreateAsync(Cutoff(maria.Id, 7, 16, 31, new DateOnly(2026, 8, 5), advance: true));

        async Task SetAllowance()
        {
            await using var context = NewContext();
            await Claims(context).SetAllowanceAsync(claimId, new SetAllowanceRequest(600m));
        }

        await FluentActions.Awaiting(SetAllowance).Should().ThrowAsync<DomainException>()
            .WithMessage($"{run.RunNumber} advances this benefit; discard it or pay it first.");

        await using (var context = NewContext())
        {
            await Payroll(context).ApproveAsync(run.Id);
        }
        await using (var context = NewContext())
        {
            await Payroll(context).MarkPaidAsync(run.Id);
        }
        await FluentActions.Awaiting(SetAllowance).Should().ThrowAsync<DomainException>()
            .WithMessage("Only a draft claim's allowance can be changed.");
        (await ClaimAsync(claimId)).DailyAllowance.Should().Be(666.67m);
    }

    private async Task ExemptFromTheDifferentialAsync()
    {
        var company = ACompany();
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id, ExemptFromMaternityDifferential = true });
        await Context.SaveChangesAsync();
    }

    private async Task ApproveAndPayAsync(Guid runId)
    {
        await using (var context = NewContext())
            await Payroll(context).ApproveAsync(runId);
        await using (var context = NewContext())
            await Payroll(context).MarkPaidAsync(runId);
    }

    [Fact]
    public async Task ACoveredCutoffDefersHerShares_ALaterOneCollectsThem_AndAChangeSinceComputeIsCaughtAtMarkPaid()
    {
        await ExemptFromTheDifferentialAsync();
        var (maria, _) = await MariaWithAReadyClaimAsync();

        // Aug 16-31 is all leave and the employer is exempt: the offset takes the whole 15,000.
        // Shares 750 + 375 + 100 = 1,225 stay on the entry and are all deferred: net 0.
        var august = await CreateAsync(Cutoff(maria.Id, 8, 16, 31, new DateOnly(2026, 9, 5), advance: false));
        var augustLine = august.Employees.Single();
        augustLine.MaternityBenefitOffset.Should().Be(15_000m);
        augustLine.ContributionsDeferred.Should().Be(1_225m);
        augustLine.NetPay.Should().Be(0m);

        // Dec 1-15 computed while August is unpaid: nothing outstanding yet, nothing collected.
        // Net 15,000 - 1,225 - 503.75 (13,775 a cutoff, 330,600 a year) = 13,271.25.
        var december = await CreateAsync(Cutoff(maria.Id, 12, 1, 15, new DateOnly(2026, 12, 20), advance: false));
        december.Employees.Single().DeferredContributionsCollected.Should().Be(0m);
        december.Employees.Single().NetPay.Should().Be(13_271.25m);
        await using (var context = NewContext())
            await Payroll(context).ApproveAsync(december.Id);

        await ApproveAndPayAsync(august.Id);
        await using (var reader = NewContext())
            (await new PayrollRunRepository(reader).GetDeferredContributionsOutstandingAsync([maria.Id]))
                .Should().Equal(new DeferredContributionsOutstanding(maria.Id, 1_225m));

        // December was computed before August deferred anything: paying it now would leave the
        // 1,225 uncollected though it has the cash, so it is refused until recomputed.
        async Task PayDecember()
        {
            await using var context = NewContext();
            await Payroll(context).MarkPaidAsync(december.Id);
        }
        await FluentActions.Awaiting(PayDecember).Should().ThrowAsync<DomainException>().WithMessage(
            "Maria Santos's deferred contributions have changed since this payroll was computed; recompute it before paying.");

        // Approved, but recomputable for this: it goes back to Draft and collects the 1,225.
        // Net 13,271.25 - 1,225 = 12,046.25.
        await using (var context = NewContext())
            await Payroll(context).ComputeAsync(december.Id);
        await using (var context = NewContext())
        {
            var recomputed = (await Payroll(context).GetAsync(december.Id))!;
            recomputed.Status.Should().Be(PayrollRunStatus.Draft);
            recomputed.Employees.Single().DeferredContributionsCollected.Should().Be(1_225m);
            recomputed.Employees.Single().NetPay.Should().Be(12_046.25m);
        }
        await ApproveAndPayAsync(december.Id);

        await using var after = NewContext();
        (await new PayrollRunRepository(after).GetDeferredContributionsOutstandingAsync([maria.Id]))
            .Should().BeEmpty("everything deferred was collected");
    }

    [Fact]
    public async Task Outstanding_IsWhatHerPaidEntriesDeferred_LessWhatTheyCollected()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Cruz", "Ana");
        Context.Employees.AddRange(maria, ana);
        await Context.SaveChangesAsync();

        async Task RunAsync(string number, PayrollRunStatus status, params PayrollRunEmployee[] entries)
        {
            var run = new PayrollRun
            {
                RunNumber = number, PeriodStart = new DateOnly(2026, 8, 1), PeriodEnd = new DateOnly(2026, 8, 15),
                PayDate = new DateOnly(2026, 8, 20), Frequency = PayFrequency.SemiMonthly, Status = status
            };
            foreach (var entry in entries)
                entry.PayrollRunId = run.Id;
            run.Employees = entries.ToList();
            await using var context = NewContext();
            await new PayrollRunRepository(context).AddWithEntriesAsync(run);
        }

        // Maria: 1,225 deferred, then 500 deferred and 300 collected on Paid runs = 1,425. An
        // Approved run's 999 doesn't count until it is paid. Ana: 100 on a Paid run.
        await RunAsync("PAY-2026-D01", PayrollRunStatus.Paid,
            new PayrollRunEmployee { EmployeeId = maria.Id, ContributionsDeferred = 1_225m },
            new PayrollRunEmployee { EmployeeId = ana.Id, ContributionsDeferred = 100m });
        await RunAsync("PAY-2026-D02", PayrollRunStatus.Paid,
            new PayrollRunEmployee { EmployeeId = maria.Id, ContributionsDeferred = 500m, DeferredContributionsCollected = 300m });
        await RunAsync("PAY-2026-D03", PayrollRunStatus.Approved,
            new PayrollRunEmployee { EmployeeId = maria.Id, ContributionsDeferred = 999m });

        await using var reader = NewContext();
        var repository = new PayrollRunRepository(reader);
        (await repository.GetDeferredContributionsOutstandingAsync([maria.Id]))
            .Should().Equal(new DeferredContributionsOutstanding(maria.Id, 1_425m));
        (await repository.GetDeferredContributionsOutstandingAsync([maria.Id, ana.Id]))
            .Should().BeEquivalentTo([new DeferredContributionsOutstanding(maria.Id, 1_425m),
                new DeferredContributionsOutstanding(ana.Id, 100m)]);
    }

    [Fact]
    public async Task AnEntry_RoundTripsWhatItDeferredAndCollected()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        await Context.SaveChangesAsync();
        var run = new PayrollRun
        {
            RunNumber = "PAY-2026-D04", PeriodStart = new DateOnly(2026, 8, 1), PeriodEnd = new DateOnly(2026, 8, 15),
            PayDate = new DateOnly(2026, 8, 20), Frequency = PayFrequency.SemiMonthly
        };
        run.Employees = [new PayrollRunEmployee
        {
            PayrollRunId = run.Id, EmployeeId = maria.Id, ContributionsDeferred = 825.5m, DeferredContributionsCollected = 400.25m
        }];
        await using (var context = NewContext())
            await new PayrollRunRepository(context).AddWithEntriesAsync(run);

        await using var reader = NewContext();
        var entry = await reader.PayrollRunEmployees.SingleAsync(e => e.PayrollRunId == run.Id);
        entry.ContributionsDeferred.Should().Be(825.5m);
        entry.DeferredContributionsCollected.Should().Be(400.25m);
    }

    [Fact]
    public async Task TheRunsWarnings_AreRebuiltOnEveryLoad()
    {
        var (maria, _) = await MariaWithAReadyClaimAsync();
        var august = await CreateAsync(Cutoff(maria.Id, 8, 1, 15, new DateOnly(2026, 8, 20), advance: false));
        august.Warnings.Should().Equal("Maternity benefit not advanced yet for Maria Santos.");

        await CreateAsync(Cutoff(maria.Id, 7, 16, 31, new DateOnly(2026, 8, 5), advance: true));

        await using var context = NewContext();
        (await Payroll(context).GetAsync(august.Id))!.Warnings.Should().BeEmpty();
    }
}
