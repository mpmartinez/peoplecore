using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Task 1 storage: a FinalPay run type, the new earnings columns on an entry, the per-run
/// FinalPayInputs/FinalPayDeduction tables, the two leave-type settings, and the
/// Separation → final-pay-run link. Behaviour (computing any of these figures) belongs to a later
/// task; this only proves the shapes round-trip.
/// </summary>
public class FinalPayStorageTests : DatabaseTestBase
{
    public FinalPayStorageTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(Context);

    private static Separation ASeparation(Guid employeeId, DateOnly lastDay) => new()
    {
        EmployeeId = employeeId,
        Type = SeparationType.Resignation,
        NoticeDate = lastDay.AddDays(-30),
        LastWorkingDay = lastDay,
        Status = SeparationStatus.NoticeGiven,
        RecordedBy = "hr@company.test",
    };

    [Fact]
    public async Task AFinalPayRun_WithEntryEarningsAndInputsAndDeductions_RoundTripsThroughGetWithEntries()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var separation = ASeparation(employee.Id, new DateOnly(2026, 9, 15));
        Context.Set<Separation>().Add(separation);
        await Context.SaveChangesAsync();

        var run = ARun("FP-2026-001", new(2026, 9, 1), new(2026, 9, 15), new(2026, 9, 20));
        run.RunType = PayrollRunType.FinalPay;

        var entry = AnEntry(run.Id, employee.Id);
        entry.LeaveConversionPay = 5_000m;
        entry.LeaveConversionNonTaxable = 2_000m;
        entry.SeparationPay = 40_000m;
        entry.RetirementPay = 15_000m;
        entry.FinalPayNonTaxable = 55_000m;
        run.Employees = [entry];

        run.FinalPayInputs = new FinalPayInputs
        {
            PayrollRunId = run.Id,
            SeparationId = separation.Id,
            WorkingDays = 10m,
            SeparationPayOverride = 42_000m,
            RetirementPayOverride = null,
            OverrideNote = "Adjusted per CBA",
            Deductions =
            [
                new FinalPayDeduction { Label = "Unreturned laptop", Amount = 25_000m },
                new FinalPayDeduction { Label = "Uniform deposit", Amount = 1_500m },
            ],
        };

        await Sut.AddWithEntriesAsync(run);

        await using var reader = NewContext();
        var loaded = await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id);

        loaded.Should().NotBeNull();
        loaded!.RunType.Should().Be(PayrollRunType.FinalPay);

        var loadedEntry = loaded.Employees.Single();
        loadedEntry.LeaveConversionPay.Should().Be(5_000m);
        loadedEntry.LeaveConversionNonTaxable.Should().Be(2_000m);
        loadedEntry.SeparationPay.Should().Be(40_000m);
        loadedEntry.RetirementPay.Should().Be(15_000m);
        loadedEntry.FinalPayNonTaxable.Should().Be(55_000m);

        loaded.FinalPayInputs.Should().NotBeNull();
        loaded.FinalPayInputs!.SeparationId.Should().Be(separation.Id);
        loaded.FinalPayInputs.WorkingDays.Should().Be(10m);
        loaded.FinalPayInputs.SeparationPayOverride.Should().Be(42_000m);
        loaded.FinalPayInputs.RetirementPayOverride.Should().BeNull();
        loaded.FinalPayInputs.OverrideNote.Should().Be("Adjusted per CBA");
        loaded.FinalPayInputs.Deductions.Select(d => (d.Label, d.Amount)).Should().BeEquivalentTo(
        [
            ("Unreturned laptop", 25_000m),
            ("Uniform deposit", 1_500m),
        ]);
    }

    [Fact]
    public void GrossPay_IncludesLeaveConversionSeparationAndRetirementPay()
    {
        var entry = new PayrollRunEmployee
        {
            RegularPay = 20_000m,
            OvertimePay = 1_000m,
            HolidayPay = 500m,
            NightDiffPay = 200m,
            TaxableAllowances = 300m,
            NonTaxableAllowances = 100m,
            ThirteenthMonth = 0m,
            LeaveConversionPay = 5_000m,
            SeparationPay = 40_000m,
            RetirementPay = 15_000m,
        };

        entry.GrossPay.Should().Be(20_000m + 1_000m + 500m + 200m + 300m + 100m + 0m + 5_000m + 40_000m + 15_000m);
    }

    [Fact]
    public void FinalPayTaxable_IsTheSeparationAndRetirementPayNotExempt()
    {
        // 5,000 of leave, 3,000 of it de minimis; 40,000 + 15,000 separation and retirement pay,
        // 45,000 of it exempt: FinalPayNonTaxable = 3,000 + 45,000 = 48,000.
        var entry = new PayrollRunEmployee
        {
            LeaveConversionPay = 5_000m,
            LeaveConversionNonTaxable = 3_000m,
            SeparationPay = 40_000m,
            RetirementPay = 15_000m,
            FinalPayNonTaxable = 48_000m,
        };

        // 40,000 + 15,000 - (48,000 - 3,000) = 10,000. The 2,000 of leave beyond de minimis isn't
        // taxable outright: it's other benefits, taxed only past the 90,000 exemption.
        entry.FinalPayTaxable.Should().Be(10_000m);
        entry.LeaveConversionOtherBenefits.Should().Be(2_000m);
    }

    [Fact]
    public void ThirteenthMonthAndOtherBenefits_IsThe13thMonthPlusTheLeaveBeyondDeMinimis()
    {
        var entry = new PayrollRunEmployee
        {
            ThirteenthMonth = 4_000m,
            LeaveConversionPay = 5_000m,
            LeaveConversionNonTaxable = 3_000m,
        };

        entry.ThirteenthMonthAndOtherBenefits.Should().Be(6_000m);   // 4,000 + (5,000 - 3,000)
    }

    [Fact]
    public async Task Separation_FinalPayRunId_RoundTrips_AndGetLoadsTheFinalPayRun()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var run = ARun("FP-2026-002", new(2026, 9, 1), new(2026, 9, 15), new(2026, 9, 20));
        Context.PayrollRuns.Add(run);
        await Context.SaveChangesAsync();

        var separation = ASeparation(employee.Id, new DateOnly(2026, 9, 15));
        separation.FinalPayRunId = run.Id;
        Context.Set<Separation>().Add(separation);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var repo = new SeparationRepository(reader);
        var loaded = await repo.GetAsync(separation.Id);

        loaded.Should().NotBeNull();
        loaded!.FinalPayRunId.Should().Be(run.Id);
        loaded.FinalPayRun.Should().NotBeNull();
        loaded.FinalPayRun!.RunNumber.Should().Be("FP-2026-002");
    }

    [Fact]
    public async Task LeaveType_ConversionFlags_RoundTrip()
    {
        var leaveType = new LeaveType
        {
            Name = "Vacation Leave",
            Code = "VL-" + Guid.NewGuid().ToString("N")[..8],
            MaxDaysPerYear = 15m,
            IsConvertibleToCash = true,
            CountsAsVacationForDeMinimis = false,
        };
        Context.Set<LeaveType>().Add(leaveType);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var loaded = await reader.Set<LeaveType>().SingleAsync(lt => lt.Id == leaveType.Id);

        loaded.IsConvertibleToCash.Should().BeTrue();
        loaded.CountsAsVacationForDeMinimis.Should().BeFalse();
    }

    [Fact]
    public async Task LeaveType_ConversionFlags_DefaultToFalseAndTrue()
    {
        var leaveType = new LeaveType
        {
            Name = "Sick Leave",
            Code = "SL-" + Guid.NewGuid().ToString("N")[..8],
            MaxDaysPerYear = 15m,
        };
        Context.Set<LeaveType>().Add(leaveType);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var loaded = await reader.Set<LeaveType>().SingleAsync(lt => lt.Id == leaveType.Id);

        loaded.IsConvertibleToCash.Should().BeFalse();
        loaded.CountsAsVacationForDeMinimis.Should().BeTrue();
    }

    [Fact]
    public async Task ExistingRuns_InsertedWithoutSettingRunType_ReadBackAsRegular()
    {
        var run = ARun("PAY-2026-777", new(2026, 9, 1), new(2026, 9, 15), new(2026, 9, 20));
        Context.PayrollRuns.Add(run);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var loaded = await reader.PayrollRuns.SingleAsync(r => r.Id == run.Id);

        loaded.RunType.Should().Be(PayrollRunType.Regular);
    }
}
