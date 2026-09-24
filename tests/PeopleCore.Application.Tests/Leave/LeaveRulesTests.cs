using FluentAssertions;
using M2NET.Core.Enums;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// <see cref="LeaveRules"/> checks eligibility (1-8) and limits (9, 10, 12, 13) of the Global
/// Constraints table, in order, with the exact messages. Check 11 (overlap) is the service's.
/// </summary>
public class LeaveRulesTests
{
    private static Employee MakeEmployee(
        Gender gender = Gender.Female,
        CivilStatus civilStatus = CivilStatus.Single,
        DateOnly? hireDate = null,
        string? soloParentIdNumber = null,
        DateOnly? soloParentIdValidUntil = null) => new()
    {
        EmployeeNumber = "E0001",
        FirstName = "Juana",
        LastName = "Dela Cruz",
        WorkEmail = "juana@example.com",
        Gender = gender,
        CivilStatus = civilStatus,
        HireDate = hireDate ?? new DateOnly(2020, 1, 1),
        SoloParentIdNumber = soloParentIdNumber,
        SoloParentIdValidUntil = soloParentIdValidUntil,
    };

    private static LeaveType MakeType(
        string name = "Vacation Leave",
        bool isActive = true,
        string? genderRestriction = null,
        int? minServiceMonths = null,
        bool requiresMarried = false,
        bool requiresSoloParentId = false,
        bool isMaternity = false,
        int? maxEvents = null,
        LeaveEntitlementKind kind = LeaveEntitlementKind.Accrued,
        decimal? daysPerEvent = null) => new()
    {
        Name = name,
        Code = name,
        IsActive = isActive,
        GenderRestriction = genderRestriction,
        MinServiceMonths = minServiceMonths,
        RequiresMarried = requiresMarried,
        RequiresSoloParentId = requiresSoloParentId,
        IsMaternity = isMaternity,
        MaxEvents = maxEvents,
        EntitlementKind = kind,
        DaysPerEvent = daysPerEvent,
    };

    private static LeaveRuleContext MakeContext(
        LeaveType type,
        Employee employee,
        DateOnly? start = null,
        DateOnly? end = null,
        IReadOnlyDictionary<int, decimal>? daysByYear = null,
        MaternityCase? maternityCase = null,
        int daysAllocatedToFather = 0,
        int approvedEvents = 0,
        IReadOnlyDictionary<int, decimal>? availableByYear = null)
    {
        var s = start ?? new DateOnly(2026, 9, 24);
        var e = end ?? s;
        return new LeaveRuleContext(
            type,
            employee,
            s,
            e,
            daysByYear ?? new Dictionary<int, decimal> { [s.Year] = 1m },
            maternityCase,
            daysAllocatedToFather,
            approvedEvents,
            availableByYear ?? new Dictionary<int, decimal>());
    }

    // ---- EnsureEligible: checks 1-8 ----

    [Fact]
    public void EnsureEligible_InactiveType_Throws()
    {
        var type = MakeType(name: "Vacation Leave", isActive: false);
        var ctx = MakeContext(type, MakeEmployee());

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>().WithMessage("Vacation Leave is no longer available.");
    }

    [Fact]
    public void EnsureEligible_InactiveAndGenderRestricted_ReportsInactiveFirst()
    {
        var type = MakeType(name: "Maternity Leave", isActive: false, genderRestriction: "Female");
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Male));

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>().WithMessage("Maternity Leave is no longer available.");
    }

    [Fact]
    public void EnsureEligible_GenderMismatch_Throws()
    {
        var type = MakeType(genderRestriction: "Female");
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Male));

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>().WithMessage("Employee is not eligible for this leave type.");
    }

    [Fact]
    public void EnsureEligible_InsufficientService_Throws()
    {
        var type = MakeType(name: "Solo Parent Leave", minServiceMonths: 6);
        var employee = MakeEmployee(hireDate: new DateOnly(2026, 6, 1));
        var ctx = MakeContext(type, employee, start: new DateOnly(2026, 9, 24));

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Solo Parent Leave needs 6 months of service; you'll qualify on Dec 1, 2026.");
    }

    [Fact]
    public void EnsureEligible_NotMarried_Throws()
    {
        var type = MakeType(name: "Paternity Leave", requiresMarried: true);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Single);
        var ctx = MakeContext(type, employee);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>().WithMessage("Paternity Leave is for married employees.");
    }

    [Fact]
    public void EnsureEligible_MissingSoloParentId_Throws()
    {
        var type = MakeType(name: "Solo Parent Leave", requiresSoloParentId: true);
        var ctx = MakeContext(type, MakeEmployee());

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Solo Parent Leave needs a valid solo parent ID on your record; ask HR to add it.");
    }

    [Fact]
    public void EnsureEligible_MaternityCaseMissing_Throws()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Female), maternityCase: null);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Choose whether this is a live birth or a miscarriage or emergency termination.");
    }

    [Fact]
    public void EnsureEligible_FatherAllocationOver7_Throws()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Female),
            maternityCase: MaternityCase.LiveBirth, daysAllocatedToFather: 8);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Up to 7 days can be allocated to the father, for a live birth only.");
    }

    [Fact]
    public void EnsureEligible_FatherAllocationOnMiscarriage_Throws()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Female),
            maternityCase: MaternityCase.MiscarriageOrEmergencyTermination, daysAllocatedToFather: 3);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Up to 7 days can be allocated to the father, for a live birth only.");
    }

    [Fact]
    public void EnsureEligible_PaternityAt4ApprovedEvents_Throws()
    {
        var type = MakeType(name: "Paternity Leave", kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 7, maxEvents: 4);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Married);
        var ctx = MakeContext(type, employee, approvedEvents: 4);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>().WithMessage("You've used Paternity Leave 4 times, the most allowed.");
    }

    [Fact]
    public void EnsureEligible_PaternityAt3ApprovedEvents_Allowed()
    {
        var type = MakeType(name: "Paternity Leave", kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 7, maxEvents: 4);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Married);
        var ctx = MakeContext(type, employee, approvedEvents: 3);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().NotThrow();
    }

    // ---- EnsureWithinLimits: checks 9, 10, 12, 13 ----

    [Fact]
    public void EnsureEligible_NegativeFatherAllocation_Throws()
    {
        // A negative allocation would otherwise raise the limit: 105 - (-15) = 120 without a solo parent ID.
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Female),
            maternityCase: MaternityCase.LiveBirth, daysAllocatedToFather: -15);

        var act = () => LeaveRules.EnsureEligible(ctx);

        act.Should().Throw<DomainException>().WithMessage("Up to 7 days can be allocated to the father, for a live birth only.");
    }

    [Fact]
    public void EnsureSpan_ThreeYears_Throws_TwoYears_Passes()
    {
        var threeYears = () => LeaveRules.EnsureSpan(new DateOnly(2025, 12, 29), new DateOnly(2027, 1, 4));
        var twoYears = () => LeaveRules.EnsureSpan(new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 4));

        threeYears.Should().Throw<DomainException>().WithMessage("A leave request can't span more than two years.");
        twoYears.Should().NotThrow();
    }

    [Fact]
    public void EnsureWithinLimits_ThreeYearSpan_Throws()
    {
        var type = MakeType();
        var ctx = MakeContext(type, MakeEmployee(),
            start: new DateOnly(2025, 1, 1), end: new DateOnly(2027, 1, 1),
            daysByYear: new Dictionary<int, decimal> { [2025] = 1m, [2026] = 1m, [2027] = 1m },
            availableByYear: new Dictionary<int, decimal> { [2025] = 10m, [2026] = 10m, [2027] = 10m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().Throw<DomainException>().WithMessage("A leave request can't span more than two years.");
    }

    [Fact]
    public void EnsureWithinLimits_ZeroDays_Throws()
    {
        var type = MakeType();
        var ctx = MakeContext(type, MakeEmployee(), daysByYear: new Dictionary<int, decimal> { [2026] = 0m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().Throw<DomainException>().WithMessage("There are no working days in that range.");
    }

    [Fact]
    public void EnsureWithinLimits_PerEventLimitExceeded_Throws()
    {
        var type = MakeType(name: "Paternity Leave", kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 7);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Married);
        var ctx = MakeContext(type, employee, daysByYear: new Dictionary<int, decimal> { [2026] = 8m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Paternity Leave is up to 7 days each time; this request is 8.");
    }

    [Fact]
    public void EnsureWithinLimits_MaternityLiveBirthOverLimit_ThrowsMaternityMessage()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Female),
            maternityCase: MaternityCase.LiveBirth,
            daysByYear: new Dictionary<int, decimal> { [2026] = 106m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Maternity leave for a live birth is up to 105 days; this request is 106.");
    }

    [Fact]
    public void EnsureWithinLimits_MaternityMiscarriageOverLimit_ThrowsMaternityMessage()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var ctx = MakeContext(type, MakeEmployee(gender: Gender.Female),
            maternityCase: MaternityCase.MiscarriageOrEmergencyTermination,
            daysByYear: new Dictionary<int, decimal> { [2026] = 61m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().Throw<DomainException>()
           .WithMessage("Maternity leave for a miscarriage or emergency termination is up to 60 days; this request is 61.");
    }

    [Fact]
    public void EnsureWithinLimits_SecondYearBalanceShort_ThrowsForThatYear()
    {
        var type = MakeType(name: "Vacation Leave", kind: LeaveEntitlementKind.Accrued);
        var ctx = MakeContext(type, MakeEmployee(),
            start: new DateOnly(2025, 12, 30), end: new DateOnly(2026, 1, 2),
            daysByYear: new Dictionary<int, decimal> { [2025] = 2m, [2026] = 2m },
            availableByYear: new Dictionary<int, decimal> { [2025] = 5m, [2026] = 1m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().Throw<DomainException>().WithMessage("You have 1 days of Vacation Leave left for 2026.");
    }

    [Fact]
    public void EnsureWithinLimits_PerEventType_SkipsBalanceCheck()
    {
        var type = MakeType(name: "Paternity Leave", kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 7, maxEvents: 4);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Married);
        var ctx = MakeContext(type, employee, daysByYear: new Dictionary<int, decimal> { [2026] = 7m });

        var act = () => LeaveRules.EnsureWithinLimits(ctx);

        act.Should().NotThrow();
    }

    // ---- PerEventLimit ----

    [Fact]
    public void PerEventLimit_NonPerEventType_ReturnsNull()
    {
        var type = MakeType(kind: LeaveEntitlementKind.Accrued);

        var limit = LeaveRules.PerEventLimit(type, MakeEmployee(), new DateOnly(2026, 9, 24), null, 0);

        limit.Should().BeNull();
    }

    [Fact]
    public void PerEventLimit_Maternity_LiveBirth_Is105()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);

        var limit = LeaveRules.PerEventLimit(type, MakeEmployee(gender: Gender.Female), new DateOnly(2026, 9, 24), MaternityCase.LiveBirth, 0);

        limit.Should().Be(105m);
    }

    [Fact]
    public void PerEventLimit_Maternity_LiveBirthAsSoloParent_Is120()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);
        var employee = MakeEmployee(gender: Gender.Female, soloParentIdNumber: "SP-1", soloParentIdValidUntil: new DateOnly(2027, 1, 1));

        var limit = LeaveRules.PerEventLimit(type, employee, new DateOnly(2026, 9, 24), MaternityCase.LiveBirth, 0);

        limit.Should().Be(120m);
    }

    [Fact]
    public void PerEventLimit_Maternity_LiveBirthWith7DaysToFather_Is98()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);

        var limit = LeaveRules.PerEventLimit(type, MakeEmployee(gender: Gender.Female), new DateOnly(2026, 9, 24), MaternityCase.LiveBirth, 7);

        limit.Should().Be(98m);
    }

    [Fact]
    public void PerEventLimit_Maternity_Miscarriage_Is60()
    {
        var type = MakeType(name: "Maternity Leave", isMaternity: true, kind: LeaveEntitlementKind.PerEvent, daysPerEvent: 105);

        var limit = LeaveRules.PerEventLimit(type, MakeEmployee(gender: Gender.Female), new DateOnly(2026, 9, 24), MaternityCase.MiscarriageOrEmergencyTermination, 0);

        limit.Should().Be(60m);
    }

    // ---- IsEligibleOn: checks 1-5 ----

    [Fact]
    public void IsEligibleOn_BeforeServiceMet_IsFalse()
    {
        var type = MakeType(name: "Solo Parent Leave", minServiceMonths: 6);
        var employee = MakeEmployee(hireDate: new DateOnly(2026, 6, 1));

        LeaveRules.IsEligibleOn(type, employee, new DateOnly(2026, 9, 24)).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleOn_AfterServiceMet_IsTrue()
    {
        var type = MakeType(name: "Solo Parent Leave", minServiceMonths: 6);
        var employee = MakeEmployee(hireDate: new DateOnly(2026, 1, 1));

        LeaveRules.IsEligibleOn(type, employee, new DateOnly(2026, 9, 24)).Should().BeTrue();
    }

    [Fact]
    public void IsEligibleOn_NotMarried_IsFalse()
    {
        var type = MakeType(name: "Paternity Leave", requiresMarried: true);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Single);

        LeaveRules.IsEligibleOn(type, employee, new DateOnly(2026, 9, 24)).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleOn_Married_IsTrue()
    {
        var type = MakeType(name: "Paternity Leave", requiresMarried: true);
        var employee = MakeEmployee(gender: Gender.Male, civilStatus: CivilStatus.Married);

        LeaveRules.IsEligibleOn(type, employee, new DateOnly(2026, 9, 24)).Should().BeTrue();
    }

    [Fact]
    public void IsEligibleOn_MissingSoloParentId_IsFalse()
    {
        var type = MakeType(name: "Solo Parent Leave", requiresSoloParentId: true);

        LeaveRules.IsEligibleOn(type, MakeEmployee(), new DateOnly(2026, 9, 24)).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleOn_ValidSoloParentId_IsTrue()
    {
        var type = MakeType(name: "Solo Parent Leave", requiresSoloParentId: true);
        var employee = MakeEmployee(soloParentIdNumber: "SP-1", soloParentIdValidUntil: new DateOnly(2027, 1, 1));

        LeaveRules.IsEligibleOn(type, employee, new DateOnly(2026, 9, 24)).Should().BeTrue();
    }
}
