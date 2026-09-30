using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Maternity claims through the real repositories and Postgres: the suggested allowance over paid
/// payroll runs, the requests that can have a claim, and a claim read back with its employee,
/// leave and advance run.
/// </summary>
public class MaternityClaimServiceDbTests : DatabaseTestBase
{
    public MaternityClaimServiceDbTests(PostgresFixture fixture) : base(fixture) { }

    private static MaternityClaimService Service(AppDbContext context) => new(
        new MaternityClaimRepository(context), new LeaveRequestRepository(context), new PayrollRunRepository(context),
        new PayrollSettingsRepository(context));

    private LeaveType? _maternity;

    private async Task<LeaveType> MaternityTypeAsync()
    {
        if (_maternity is not null) return _maternity;
        _maternity = new LeaveType { Name = "Maternity Leave", Code = "ML", MaxDaysPerYear = 105m, IsMaternity = true };
        Context.LeaveTypes.Add(_maternity);
        await Context.SaveChangesAsync();
        return _maternity;
    }

    private async Task<LeaveRequest> ARequestAsync(Employee employee, LeaveType type, DateOnly start,
        LeaveStatus status = LeaveStatus.Approved)
    {
        var request = new LeaveRequest
        {
            EmployeeId = employee.Id, LeaveTypeId = type.Id, StartDate = start, EndDate = start.AddDays(104),
            TotalDays = 105m, Status = status,
        };
        Context.LeaveRequests.Add(request);
        await Context.SaveChangesAsync();
        return request;
    }

    private int _runCount;

    /// <summary>A regular run ending on <paramref name="periodEnd"/>, with each employee's SSS share.</summary>
    private PayrollRun ARunWith(DateOnly periodEnd, PayrollRunStatus status, PayFrequency frequency,
        params (Employee Employee, decimal Sss)[] entries)
    {
        var run = ARun($"PAY-T-{++_runCount:000}", periodEnd.AddDays(-14), periodEnd, periodEnd.AddDays(5), status);
        run.Frequency = frequency;
        foreach (var (employee, sss) in entries)
        {
            var entry = AnEntry(run.Id, employee.Id);
            entry.SSSEmployee = sss;
            run.Employees.Add(entry);
        }
        Context.PayrollRuns.Add(run);
        return run;
    }

    [Fact]
    public async Task Suggest_OverRealPaidRuns_TakesTheSixHighestFullMonthCreditsInTheWindow()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Cruz", "Ana");
        Context.Employees.AddRange(maria, ana);
        await Context.SaveChangesAsync();
        // Leave starting 10 Aug 2026: Q3, so the semester is Apr-Sep 2026 and the window Apr 2025-Mar 2026.
        var request = await ARequestAsync(maria, await MaternityTypeAsync(), new DateOnly(2026, 8, 10));
        const PayrollRunStatus paid = PayrollRunStatus.Paid;
        const PayFrequency monthly = PayFrequency.Monthly, semi = PayFrequency.SemiMonthly;

        // MSC = share / 5%, to the nearest 500, capped at the Regular SS ceiling of 20,000.
        ARunWith(new(2025, 5, 31), paid, monthly, (maria, 1_000m));                // 20,000
        ARunWith(new(2025, 6, 30), paid, monthly, (maria, 1_000m));                // 20,000
        ARunWith(new(2025, 7, 31), paid, monthly, (maria, 900m));                  // 18,000
        ARunWith(new(2025, 8, 31), paid, monthly, (maria, 1_500m));                // 30,000 -> 20,000
        ARunWith(new(2025, 9, 15), paid, semi, (maria, 875m), (ana, 5_000m));      // \ 1,750: 35,000 -> 20,000
        ARunWith(new(2025, 9, 30), paid, semi, (maria, 875m));                     // /
        ARunWith(new(2025, 10, 31), paid, monthly, (maria, 500m));                 // 10,000
        ARunWith(new(2025, 11, 30), paid, monthly, (maria, 750m));                 // 15,000
        // February: a final pay tops the first cutoff up to the whole month.
        ARunWith(new(2026, 2, 15), paid, semi, (maria, 250m));                     // \ 600: 12,000
        ARunWith(new(2026, 2, 28), paid, semi, (maria, 350m)).RunType = PayrollRunType.FinalPay; // /
        // Left out: before and after the window, a run that isn't paid, and January, whose second
        // cutoff isn't paid (half the month's share would understate the MSC).
        ARunWith(new(2025, 3, 31), paid, monthly, (maria, 2_500m));
        ARunWith(new(2026, 4, 30), paid, monthly, (maria, 2_500m));
        ARunWith(new(2025, 12, 31), PayrollRunStatus.Approved, monthly, (maria, 2_500m));
        ARunWith(new(2026, 1, 15), paid, semi, (maria, 2_500m));
        await Context.SaveChangesAsync();

        var claim = await Service(Context).CreateAsync(request.Id);
        var suggestion = await Service(NewContext()).SuggestAsync(claim.Id);

        // 20,000 + 20,000 + 20,000 + 20,000 + 18,000 + 15,000 = 113,000 / 180 = 627.78
        // (8 months: left out of the six are 12,000 and 10,000)
        suggestion.Should().Be(new SuggestedAllowanceDto(627.78m, 8, new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31)));
    }

    /// <summary>
    /// Re-implements <see cref="IMaternityClaimRepository"/> so the service's duplicate check runs
    /// this instead: the check finds no claim, then a rival create commits before this one saves.
    /// </summary>
    private sealed class RacingClaimRepository(AppDbContext context, Func<Task> rival)
        : MaternityClaimRepository(context), IMaternityClaimRepository
    {
        public new async Task<MaternityClaim?> GetByLeaveRequestAsync(Guid leaveRequestId, CancellationToken ct = default)
        {
            var existing = await base.GetByLeaveRequestAsync(leaveRequestId, ct);
            await rival();
            return existing;
        }
    }

    [Fact]
    public async Task Create_WhenARivalCreateCommitsFirst_GivesTheReadableMessage()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        await Context.SaveChangesAsync();
        var request = await ARequestAsync(maria, await MaternityTypeAsync(), new DateOnly(2026, 8, 10));

        // The rival's claim goes in directly through a second context.
        async Task Rival()
        {
            await using var second = NewContext();
            second.MaternityClaims.Add(new MaternityClaim { LeaveRequestId = request.Id, EmployeeId = maria.Id, Days = 105m });
            await second.SaveChangesAsync();
        }
        await using var context = NewContext();
        var service = new MaternityClaimService(new RacingClaimRepository(context, Rival), new LeaveRequestRepository(context),
            new PayrollRunRepository(context), new PayrollSettingsRepository(context));

        var act = () => service.CreateAsync(request.Id);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Maria Santos already has a maternity claim for this leave.");
        // The rejected claim is detached, so a later save on this context doesn't retry it.
        context.ChangeTracker.Entries<MaternityClaim>().Should().BeEmpty();
        await using var reader = NewContext();
        (await reader.MaternityClaims.CountAsync(c => c.LeaveRequestId == request.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Relink_WhenARivalClaimForTheTargetCommitsFirst_GivesTheReadableMessage()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        await Context.SaveChangesAsync();
        var type = await MaternityTypeAsync();
        var original = await ARequestAsync(maria, type, new DateOnly(2026, 8, 10));
        var refiled = await ARequestAsync(maria, type, new DateOnly(2026, 8, 17));
        var claim = await Service(Context).CreateAsync(original.Id);
        await using (var cancel = NewContext())
        {
            (await cancel.LeaveRequests.SingleAsync(r => r.Id == original.Id)).Status = LeaveStatus.Cancelled;
            await cancel.SaveChangesAsync();
        }

        // The check finds no claim for the refiled leave; then a rival opens one, through a second
        // context, before the move saves.
        async Task Rival()
        {
            await using var second = NewContext();
            second.MaternityClaims.Add(new MaternityClaim { LeaveRequestId = refiled.Id, EmployeeId = maria.Id, Days = 105m });
            await second.SaveChangesAsync();
        }
        await using var context = NewContext();
        var service = new MaternityClaimService(new RacingClaimRepository(context, Rival), new LeaveRequestRepository(context),
            new PayrollRunRepository(context), new PayrollSettingsRepository(context));

        var act = () => service.RelinkAsync(claim.Id, new RelinkRequest(refiled.Id));

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Choose an approved maternity leave of the same employee that has no claim.");
        // The refused move is dropped from the context, so a later save there doesn't try it again,
        // and the claim still has its own leave.
        context.ChangeTracker.Entries<MaternityClaim>().Should().NotContain(e => e.State == EntityState.Modified);
        await using var reader = NewContext();
        (await reader.MaternityClaims.SingleAsync(c => c.Id == claim.Id)).LeaveRequestId.Should().Be(original.Id);
        (await reader.MaternityClaims.CountAsync(c => c.LeaveRequestId == refiled.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Eligible_IsTheApprovedMaternityRequestsWithoutAClaim()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Cruz", "Ana");
        Context.Employees.AddRange(maria, ana);
        var vacation = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15m };
        Context.LeaveTypes.Add(vacation);
        await Context.SaveChangesAsync();
        var maternity = await MaternityTypeAsync();

        var later = await ARequestAsync(maria, maternity, new DateOnly(2026, 9, 1));
        var earlier = await ARequestAsync(ana, maternity, new DateOnly(2026, 7, 1));
        var claimed = await ARequestAsync(ana, maternity, new DateOnly(2025, 1, 6));
        await ARequestAsync(maria, maternity, new DateOnly(2027, 1, 4), LeaveStatus.Pending);
        await ARequestAsync(maria, vacation, new DateOnly(2026, 12, 1));
        Context.MaternityClaims.Add(new MaternityClaim { LeaveRequestId = claimed.Id, EmployeeId = ana.Id, Days = 105m });
        await Context.SaveChangesAsync();

        var eligible = await Service(NewContext()).EligibleAsync();

        eligible.Should().Equal(
            new EligibleMaternityLeaveDto(earlier.Id, ana.Id, "Ana Cruz", new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 13), 105m),
            new EligibleMaternityLeaveDto(later.Id, maria.Id, "Maria Santos", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 14), 105m));
    }

    [Fact]
    public async Task AClaim_ReadBack_CarriesItsEmployeeLeaveAndAdvanceRun()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        await Context.SaveChangesAsync();
        var request = await ARequestAsync(maria, await MaternityTypeAsync(), new DateOnly(2026, 8, 10));
        var created = await Service(Context).CreateAsync(request.Id);
        // 555.56 x 105 = 58,333.80 (an allowance within the 666.67 maximum).
        await Service(NewContext()).SetAllowanceAsync(created.Id, new SetAllowanceRequest(555.56m));

        var run = ARun("PAY-2026-017", new(2026, 8, 1), new(2026, 8, 15), new(2026, 8, 20));
        Context.PayrollRuns.Add(run);
        await Context.SaveChangesAsync();
        await using (var writer = NewContext())
        {
            var stored = await writer.MaternityClaims.FindAsync(created.Id);
            stored!.Status = MaternityClaimStatus.Advanced;
            stored.AdvanceRunId = run.Id;
            stored.AdvancedAt = new DateOnly(2026, 8, 20);
            await writer.SaveChangesAsync();
        }

        var reimbursed = await Service(NewContext()).ReimburseAsync(created.Id,
            new ReimburseRequest(new DateOnly(2026, 11, 3), 58_333.80m, null));
        var summary = await Service(NewContext()).ListAsync();

        reimbursed.Should().Be(new MaternityClaimDto(created.Id, request.Id, maria.Id, "Maria Santos",
            new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22), 105m, 555.56m, 58_333.80m, MaternityClaimStatus.Reimbursed,
            run.Id, "PAY-2026-017", new DateOnly(2026, 8, 20), new DateOnly(2026, 11, 3), 58_333.80m, null));
        summary.Claims.Should().Equal(reimbursed);
        summary.Outstanding.Should().Be(0m);
        (await Service(NewContext()).ReadyEmployeeIdsAsync()).Should().BeEmpty();
    }
}
