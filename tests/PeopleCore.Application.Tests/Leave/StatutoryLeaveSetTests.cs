using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// "Add the statutory set" creates the Philippine statutory leave types a site is missing, matched
/// by code, and gives Service Incentive Leave its accrual policy. Running it again changes nothing.
/// </summary>
public class StatutoryLeaveSetTests
{
    private static readonly string[] AllCodes = ["SIL", "ML", "AML", "PL", "SPL", "VAWC", "SLW"];

    private readonly List<LeaveType> _types = [];
    private readonly List<LeaveAccrualPolicy> _policies = [];
    /// <summary>Each save: the type and the policies that went in with it.</summary>
    private readonly List<(LeaveType Type, IReadOnlyList<LeaveAccrualPolicy> Policies)> _saves = [];
    private readonly Mock<ILeaveTypeRepository> _typeRepo = new();
    private readonly LeaveTypeService _sut;

    public StatutoryLeaveSetTests()
    {
        _typeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _types.ToList());
        _typeRepo.Setup(r => r.AddWithPoliciesAsync(
                It.IsAny<LeaveType>(), It.IsAny<IReadOnlyList<LeaveAccrualPolicy>>(), It.IsAny<CancellationToken>()))
            .Callback((LeaveType lt, IReadOnlyList<LeaveAccrualPolicy> policies, CancellationToken _) =>
            {
                _saves.Add((lt, policies));
                _types.Add(lt);
                _policies.AddRange(policies);
            })
            .ReturnsAsync((LeaveType lt, IReadOnlyList<LeaveAccrualPolicy> _, CancellationToken _) => lt);
        _sut = new LeaveTypeService(_typeRepo.Object);
    }

    private LeaveType Added(string code) => _types.Single(t => t.Code == code);

    [Fact]
    public async Task OnAnEmptySite_AddsAllSeven_InTheTablesOrder()
    {
        var result = await _sut.AddStatutoryAsync();

        result.Added.Should().Equal(AllCodes);
        result.Skipped.Should().BeEmpty();
        _types.Select(t => t.Code).Should().Equal(AllCodes);
    }

    [Fact]
    public async Task EveryType_IsPaid_ActiveAndNotCarriedOver()
    {
        await _sut.AddStatutoryAsync();

        _types.Should().AllSatisfy(t =>
        {
            t.IsPaid.Should().BeTrue();
            t.IsCarryOver.Should().BeFalse();
            t.CarryOverMaxDays.Should().BeNull();
            t.IsActive.Should().BeTrue();
        });
    }

    [Fact]
    public async Task ServiceIncentiveLeave_IsAccrued_ConvertibleToCash_AndCountsAsVacation()
    {
        await _sut.AddStatutoryAsync();

        var sil = Added("SIL");
        sil.Name.Should().Be("Service Incentive Leave");
        sil.EntitlementKind.Should().Be(LeaveEntitlementKind.Accrued);
        sil.MaxDaysPerYear.Should().Be(5m);
        sil.DaysPerEvent.Should().BeNull();
        sil.CountsCalendarDays.Should().BeFalse();
        sil.IsConvertibleToCash.Should().BeTrue();
        sil.CountsAsVacationForDeMinimis.Should().BeTrue();
        sil.GenderRestriction.Should().BeNull();
        sil.RequiresDocument.Should().BeFalse();
        sil.MinServiceMonths.Should().BeNull();
        sil.RequiresMarried.Should().BeFalse();
        sil.RequiresSoloParentId.Should().BeFalse();
        sil.MaxEvents.Should().BeNull();
        sil.IsConfidential.Should().BeFalse();
        sil.IsMaternity.Should().BeFalse();
    }

    [Fact]
    public async Task ServiceIncentiveLeave_GetsAMonthlyPolicyOfFiveDays_FromTwelveMonths_WithNoCeiling()
    {
        await _sut.AddStatutoryAsync();

        var policy = _policies.Should().ContainSingle().Subject;
        policy.LeaveTypeId.Should().Be(Added("SIL").Id);
        policy.TenureMonthsMin.Should().Be(12);
        policy.TenureMonthsMax.Should().BeNull();
        policy.DaysPerYear.Should().Be(5m);
        policy.AccrualFrequency.Should().Be(AccrualFrequency.Monthly);
        policy.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceIncentiveLeave_AndItsPolicy_AreSavedTogether()
    {
        // One save: a failure can't leave SIL without the policy that accrues it.
        await _sut.AddStatutoryAsync();

        var silSave = _saves.Single(s => s.Type.Code == "SIL");
        silSave.Policies.Should().ContainSingle().Which.LeaveTypeId.Should().Be(silSave.Type.Id);
        _saves.Where(s => s.Type.Code != "SIL").Should().OnlyContain(s => s.Policies.Count == 0);
        _typeRepo.Verify(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>One row of the statutory table: what each type other than SIL must say.</summary>
    public record Row(
        string Code, string Name, LeaveEntitlementKind Kind, decimal MaxDaysPerYear, decimal? DaysPerEvent,
        int? MaxEvents, bool CountsCalendarDays, string? Gender, bool RequiresDocument, bool RequiresMarried,
        bool RequiresSoloParentId, int? MinServiceMonths, bool IsConfidential, bool IsMaternity);

    public static TheoryData<Row> Rows => new()
    {
        new Row("ML", "Maternity Leave", LeaveEntitlementKind.PerEvent, 0m, 105m, null, true, "Female", true, false, false, null, false, true),
        new Row("AML", "Maternity Leave Allocated to Father", LeaveEntitlementKind.PerEvent, 0m, 7m, null, true, "Male", true, false, false, null, false, false),
        new Row("PL", "Paternity Leave", LeaveEntitlementKind.PerEvent, 0m, 7m, 4, false, "Male", true, true, false, null, false, false),
        new Row("SPL", "Solo Parent Leave", LeaveEntitlementKind.YearlyAllowance, 7m, null, null, false, null, false, false, true, 6, false, false),
        new Row("VAWC", "VAWC Leave", LeaveEntitlementKind.YearlyAllowance, 10m, null, null, false, "Female", true, false, false, null, true, false),
        new Row("SLW", "Special Leave for Women (Magna Carta)", LeaveEntitlementKind.PerEvent, 0m, 60m, null, true, "Female", true, false, false, 6, false, false),
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task EachOtherType_HasTheTablesSettings(Row row)
    {
        await _sut.AddStatutoryAsync();

        var t = Added(row.Code);
        t.Name.Should().Be(row.Name);
        t.EntitlementKind.Should().Be(row.Kind);
        t.MaxDaysPerYear.Should().Be(row.MaxDaysPerYear);
        t.DaysPerEvent.Should().Be(row.DaysPerEvent);
        t.MaxEvents.Should().Be(row.MaxEvents);
        t.CountsCalendarDays.Should().Be(row.CountsCalendarDays);
        t.GenderRestriction.Should().Be(row.Gender);
        t.RequiresDocument.Should().Be(row.RequiresDocument);
        t.RequiresMarried.Should().Be(row.RequiresMarried);
        t.RequiresSoloParentId.Should().Be(row.RequiresSoloParentId);
        t.MinServiceMonths.Should().Be(row.MinServiceMonths);
        t.IsConfidential.Should().Be(row.IsConfidential);
        t.IsMaternity.Should().Be(row.IsMaternity);
        t.IsConvertibleToCash.Should().BeFalse();
        t.CountsAsVacationForDeMinimis.Should().BeFalse();
    }

    [Fact]
    public async Task AnExistingCode_IsMatchedTrimmedAndIgnoringCase_AndLeftUntouched()
    {
        var existing = new LeaveType { Name = "Our SIL", Code = "sil ", MaxDaysPerYear = 7m, IsConvertibleToCash = false };
        _types.Add(existing);

        var result = await _sut.AddStatutoryAsync();

        result.Skipped.Should().Equal("SIL");
        result.Added.Should().Equal(AllCodes.Skip(1));
        _types.Should().ContainSingle(t => t.Code.Trim().ToUpperInvariant() == "SIL");
        existing.Name.Should().Be("Our SIL");
        existing.Code.Should().Be("sil ");
        existing.MaxDaysPerYear.Should().Be(7m);
        existing.IsConvertibleToCash.Should().BeFalse();
        _typeRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
        _policies.Should().BeEmpty("SIL was already there, so it keeps whatever policies it has");
    }

    [Fact]
    public async Task ASecondRun_AddsNothing()
    {
        await _sut.AddStatutoryAsync();
        _typeRepo.Invocations.Clear();

        var result = await _sut.AddStatutoryAsync();

        result.Added.Should().BeEmpty();
        result.Skipped.Should().Equal(AllCodes);
        _types.Should().HaveCount(7);
        _policies.Should().ContainSingle();
        _typeRepo.Verify(r => r.AddWithPoliciesAsync(
            It.IsAny<LeaveType>(), It.IsAny<IReadOnlyList<LeaveAccrualPolicy>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Definitions_AreFreshInstancesEachTime()
    {
        // The definitions are handed to the repository to insert; sharing instances between runs
        // (or between requests) would re-insert an already-tracked entity.
        StatutoryLeaveSet.Definitions()[0].Should().NotBeSameAs(StatutoryLeaveSet.Definitions()[0]);
    }
}
