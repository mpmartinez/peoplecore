using System.Text.Json;
using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// A leave type says whether its unused days are paid out on final pay, and whether those days
/// count toward the 10-day de minimis ceiling on converted vacation leave.
/// </summary>
public class LeaveTypeServiceTests
{
    private readonly Mock<ILeaveTypeRepository> _repo = new();
    private readonly LeaveTypeService _sut;

    public LeaveTypeServiceTests()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaveType lt, CancellationToken _) => lt);
        _sut = new LeaveTypeService(_repo.Object);
    }

    private static CreateLeaveTypeDto Request(bool convertible, bool countsAsVacation) => new(
        "Vacation Leave", "VL", 15m, true, false, null, null, false, convertible, countsAsVacation);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Create_StoresAndReturnsTheCashSettings(bool convertible, bool countsAsVacation)
    {
        LeaveType? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()))
            .Callback((LeaveType lt, CancellationToken _) => saved = lt)
            .ReturnsAsync((LeaveType lt, CancellationToken _) => lt);

        var dto = await _sut.CreateAsync(Request(convertible, countsAsVacation));

        saved!.IsConvertibleToCash.Should().Be(convertible);
        saved.CountsAsVacationForDeMinimis.Should().Be(countsAsVacation);
        dto.IsConvertibleToCash.Should().Be(convertible);
        dto.CountsAsVacationForDeMinimis.Should().Be(countsAsVacation);
    }

    [Fact]
    public async Task Update_ChangesTheCashSettings()
    {
        var existing = new LeaveType { Name = "Sick Leave", Code = "SL", MaxDaysPerYear = 15m };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var dto = await _sut.UpdateAsync(existing.Id, Request(convertible: true, countsAsVacation: false));

        existing.IsConvertibleToCash.Should().BeTrue();
        existing.CountsAsVacationForDeMinimis.Should().BeFalse();
        dto.IsConvertibleToCash.Should().BeTrue();
        dto.CountsAsVacationForDeMinimis.Should().BeFalse();
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_ReadsTheCashSettings()
    {
        var existing = new LeaveType
        {
            Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15m,
            IsConvertibleToCash = true, CountsAsVacationForDeMinimis = true,
        };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var dto = await _sut.GetByIdAsync(existing.Id);

        dto.IsConvertibleToCash.Should().BeTrue();
        dto.CountsAsVacationForDeMinimis.Should().BeTrue();
    }

    [Fact]
    public void ARequestWithoutTheCashSettings_TakesTheEntitysDefaults()
    {
        // A client written before the settings existed still posts a valid body: not convertible,
        // and counting as vacation should it later be made convertible.
        const string json = """
            {"name":"Sick Leave","code":"SL","maxDaysPerYear":15,"isPaid":true,"isCarryOver":false,
             "carryOverMaxDays":null,"genderRestriction":null,"requiresDocument":false}
            """;

        var dto = JsonSerializer.Deserialize<CreateLeaveTypeDto>(json, JsonSerializerOptions.Web)!;

        dto.IsConvertibleToCash.Should().Be(new LeaveType().IsConvertibleToCash).And.BeFalse();
        dto.CountsAsVacationForDeMinimis.Should().Be(new LeaveType().CountsAsVacationForDeMinimis).And.BeTrue();
    }

    [Fact]
    public void ARequestWithoutTheStatutorySettings_TakesTheEntitysDefaults()
    {
        // The Leave Types page's older body, and the demo seed's, leave every statutory setting out:
        // an active, accrued type with every new rule off, as existing types were migrated.
        const string json = """
            {"name":"Sick Leave","code":"SL","maxDaysPerYear":15,"isPaid":true,"isCarryOver":false,
             "carryOverMaxDays":null,"genderRestriction":null,"requiresDocument":false}
            """;
        var entity = new LeaveType();

        var dto = JsonSerializer.Deserialize<CreateLeaveTypeDto>(json, JsonSerializerOptions.Web)!;

        dto.IsActive.Should().Be(entity.IsActive).And.BeTrue();
        dto.EntitlementKind.Should().Be(entity.EntitlementKind).And.Be(LeaveEntitlementKind.Accrued);
        dto.CountsCalendarDays.Should().Be(entity.CountsCalendarDays).And.BeFalse();
        dto.DaysPerEvent.Should().Be(entity.DaysPerEvent).And.BeNull();
        dto.MinServiceMonths.Should().Be(entity.MinServiceMonths).And.BeNull();
        dto.RequiresMarried.Should().Be(entity.RequiresMarried).And.BeFalse();
        dto.RequiresSoloParentId.Should().Be(entity.RequiresSoloParentId).And.BeFalse();
        dto.MaxEvents.Should().Be(entity.MaxEvents).And.BeNull();
        dto.IsConfidential.Should().Be(entity.IsConfidential).And.BeFalse();
        dto.IsMaternity.Should().Be(entity.IsMaternity).And.BeFalse();
    }

    // ---- every setting ---------------------------------------------------------------------

    /// <summary>A per-event type with every setting moved off its default.</summary>
    private static CreateLeaveTypeDto EverySetting() => new(
        "Maternity Leave", "ML", 0m, IsPaid: false, IsCarryOver: true, CarryOverMaxDays: 3m,
        GenderRestriction: "Female", RequiresDocument: true,
        IsConvertibleToCash: true, CountsAsVacationForDeMinimis: false,
        IsActive: false, EntitlementKind: LeaveEntitlementKind.PerEvent, CountsCalendarDays: true,
        DaysPerEvent: 105m, MinServiceMonths: 6, RequiresMarried: true, RequiresSoloParentId: true,
        MaxEvents: 4, IsConfidential: true, IsMaternity: true);

    private static void ShouldHoldEverySetting(LeaveType lt)
    {
        lt.Name.Should().Be("Maternity Leave");
        lt.Code.Should().Be("ML");
        lt.MaxDaysPerYear.Should().Be(0m);
        lt.IsPaid.Should().BeFalse();
        lt.IsCarryOver.Should().BeTrue();
        lt.CarryOverMaxDays.Should().Be(3m);
        lt.GenderRestriction.Should().Be("Female");
        lt.RequiresDocument.Should().BeTrue();
        lt.IsConvertibleToCash.Should().BeTrue();
        lt.CountsAsVacationForDeMinimis.Should().BeFalse();
        lt.IsActive.Should().BeFalse();
        lt.EntitlementKind.Should().Be(LeaveEntitlementKind.PerEvent);
        lt.CountsCalendarDays.Should().BeTrue();
        lt.DaysPerEvent.Should().Be(105m);
        lt.MinServiceMonths.Should().Be(6);
        lt.RequiresMarried.Should().BeTrue();
        lt.RequiresSoloParentId.Should().BeTrue();
        lt.MaxEvents.Should().Be(4);
        lt.IsConfidential.Should().BeTrue();
        lt.IsMaternity.Should().BeTrue();
    }

    private static void ShouldHoldEverySetting(LeaveTypeDto dto)
    {
        dto.Name.Should().Be("Maternity Leave");
        dto.Code.Should().Be("ML");
        dto.MaxDaysPerYear.Should().Be(0m);
        dto.IsPaid.Should().BeFalse();
        dto.IsCarryOver.Should().BeTrue();
        dto.CarryOverMaxDays.Should().Be(3m);
        dto.GenderRestriction.Should().Be("Female");
        dto.RequiresDocument.Should().BeTrue();
        dto.IsConvertibleToCash.Should().BeTrue();
        dto.CountsAsVacationForDeMinimis.Should().BeFalse();
        dto.IsActive.Should().BeFalse();
        dto.EntitlementKind.Should().Be(LeaveEntitlementKind.PerEvent);
        dto.CountsCalendarDays.Should().BeTrue();
        dto.DaysPerEvent.Should().Be(105m);
        dto.MinServiceMonths.Should().Be(6);
        dto.RequiresMarried.Should().BeTrue();
        dto.RequiresSoloParentId.Should().BeTrue();
        dto.MaxEvents.Should().Be(4);
        dto.IsConfidential.Should().BeTrue();
        dto.IsMaternity.Should().BeTrue();
    }

    [Fact]
    public async Task Create_StoresAndReturnsEverySetting()
    {
        LeaveType? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()))
            .Callback((LeaveType lt, CancellationToken _) => saved = lt)
            .ReturnsAsync((LeaveType lt, CancellationToken _) => lt);

        var dto = await _sut.CreateAsync(EverySetting());

        ShouldHoldEverySetting(saved!);
        ShouldHoldEverySetting(dto);
    }

    [Fact]
    public async Task Update_WritesEverySetting_IncludingIsActive()
    {
        var existing = new LeaveType { Name = "Old", Code = "OLD", MaxDaysPerYear = 15m };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var dto = await _sut.UpdateAsync(existing.Id, EverySetting());

        ShouldHoldEverySetting(existing);
        ShouldHoldEverySetting(dto);
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_ReadsEverySetting()
    {
        var existing = new LeaveType
        {
            Name = "Maternity Leave", Code = "ML", MaxDaysPerYear = 0m, IsPaid = false, IsCarryOver = true,
            CarryOverMaxDays = 3m, GenderRestriction = "Female", RequiresDocument = true,
            IsConvertibleToCash = true, CountsAsVacationForDeMinimis = false, IsActive = false,
            EntitlementKind = LeaveEntitlementKind.PerEvent, CountsCalendarDays = true, DaysPerEvent = 105m,
            MinServiceMonths = 6, RequiresMarried = true, RequiresSoloParentId = true, MaxEvents = 4,
            IsConfidential = true, IsMaternity = true,
        };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        ShouldHoldEverySetting(await _sut.GetByIdAsync(existing.Id));
    }

    [Fact]
    public async Task Update_CanReactivateAType()
    {
        var existing = new LeaveType { Name = "Sick Leave", Code = "SL", MaxDaysPerYear = 15m, IsActive = false };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await _sut.UpdateAsync(existing.Id, new CreateLeaveTypeDto(
            "Sick Leave", "SL", 15m, true, false, null, null, false, IsActive: true));

        existing.IsActive.Should().BeTrue();
    }

    // ---- validation ------------------------------------------------------------------------

    public static TheoryData<CreateLeaveTypeDto, string> Invalid => new()
    {
        { new("Paternity Leave", "PL", 0m, true, false, null, "Male", true,
              EntitlementKind: LeaveEntitlementKind.PerEvent, DaysPerEvent: null), "Set the days per event." },
        { new("Paternity Leave", "PL", 0m, true, false, null, "Male", true,
              EntitlementKind: LeaveEntitlementKind.PerEvent, DaysPerEvent: 0m), "Set the days per event." },
        { new("Solo Parent Leave", "SPL", 0m, true, false, null, null, false,
              EntitlementKind: LeaveEntitlementKind.YearlyAllowance), "Set the days per year." },
        { new("Maternity Leave", "ML", 105m, true, false, null, "Female", true,
              EntitlementKind: LeaveEntitlementKind.Accrued, IsMaternity: true), "A maternity type must be per event." },
        { new("Maternity Leave", "ML", 105m, true, false, null, "Female", true,
              EntitlementKind: LeaveEntitlementKind.YearlyAllowance, IsMaternity: true), "A maternity type must be per event." },
        { new("Paternity Leave", "PL", 0m, true, false, null, "Male", true,
              EntitlementKind: LeaveEntitlementKind.PerEvent, DaysPerEvent: 7m, MaxEvents: 0), "Set at least 1 for the most times allowed." },
        { new("Paternity Leave", "PL", 0m, true, false, null, "Male", true,
              EntitlementKind: LeaveEntitlementKind.PerEvent, DaysPerEvent: 7m, MaxEvents: -2), "Set at least 1 for the most times allowed." },
        { new("Solo Parent Leave", "SPL", 7m, true, false, null, null, false,
              EntitlementKind: LeaveEntitlementKind.YearlyAllowance, MinServiceMonths: -1), "Service months can't be negative." },
        { new("Vacation Leave", "VL", 15m, true, false, null, null, false, MinServiceMonths: -6), "Service months can't be negative." },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Create_RefusesAnIncompleteType(CreateLeaveTypeDto request, string message)
    {
        var act = () => _sut.CreateAsync(request);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(message);
        _repo.Verify(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Update_RefusesAnIncompleteType_AndLeavesItUnchanged(CreateLeaveTypeDto request, string message)
    {
        var existing = new LeaveType { Name = "Old", Code = "OLD", MaxDaysPerYear = 15m };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var act = () => _sut.UpdateAsync(existing.Id, request);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(message);
        existing.Name.Should().Be("Old");
        _repo.Verify(r => r.UpdateAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(LeaveEntitlementKind.Accrued)]
    [InlineData(LeaveEntitlementKind.YearlyAllowance)]
    public async Task MaxEvents_IsClearedOnATypeThatIsNotPerEvent(LeaveEntitlementKind kind)
    {
        var dto = await _sut.CreateAsync(new CreateLeaveTypeDto(
            "Solo Parent Leave", "SPL", 7m, true, false, null, null, false,
            EntitlementKind: kind, MaxEvents: 4));

        dto.MaxEvents.Should().BeNull();
    }

    [Fact]
    public async Task MaxEvents_OnATypeThatIsNotPerEvent_IsClearedRatherThanChecked()
    {
        // It is thrown away anyway, so a stale 0 from a hidden form field doesn't block the save.
        var dto = await _sut.CreateAsync(new CreateLeaveTypeDto(
            "Solo Parent Leave", "SPL", 7m, true, false, null, null, false,
            EntitlementKind: LeaveEntitlementKind.YearlyAllowance, MaxEvents: 0));

        dto.MaxEvents.Should().BeNull();
    }

    [Fact]
    public async Task ZeroServiceMonths_AndOneEvent_AreAllowed()
    {
        var dto = await _sut.CreateAsync(new CreateLeaveTypeDto(
            "Paternity Leave", "PL", 0m, true, false, null, "Male", true,
            EntitlementKind: LeaveEntitlementKind.PerEvent, DaysPerEvent: 7m, MinServiceMonths: 0, MaxEvents: 1));

        dto.MinServiceMonths.Should().Be(0);
        dto.MaxEvents.Should().Be(1);
    }

    [Fact]
    public async Task MaxEvents_IsKeptOnAPerEventType()
    {
        var dto = await _sut.CreateAsync(new CreateLeaveTypeDto(
            "Paternity Leave", "PL", 0m, true, false, null, "Male", true,
            EntitlementKind: LeaveEntitlementKind.PerEvent, DaysPerEvent: 7m, MaxEvents: 4));

        dto.MaxEvents.Should().Be(4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ABlankGenderRestriction_IsStoredAsAny(string blank)
    {
        // LeaveRules compares the restriction to employee.Gender.ToString(), so an empty string
        // would shut every employee out of the type.
        var dto = await _sut.CreateAsync(new CreateLeaveTypeDto(
            "Vacation Leave", "VL", 15m, true, false, null, blank, false));

        dto.GenderRestriction.Should().BeNull();
    }

    // ---- delete ----------------------------------------------------------------------------

    [Fact]
    public async Task Delete_ATypeThatHasBeenUsed_IsRefused()
    {
        var existing = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15m };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repo.Setup(r => r.IsUsedAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _sut.DeleteAsync(existing.Id);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Vacation Leave has been used; deactivate it instead.");
        _repo.Verify(r => r.DeleteAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.DeleteWithPoliciesAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_AnUnusedType_DeletesIt_WithItsAccrualPolicies_InOneStep()
    {
        // The policies' foreign key restricts deletes, so an unused SIL (which the statutory set
        // gives a policy) could not otherwise be deleted. The repository removes both in one save
        // (LeaveTypeRepositoryTests).
        var existing = new LeaveType { Name = "Service Incentive Leave", Code = "SIL", MaxDaysPerYear = 5m };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repo.Setup(r => r.IsUsedAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _sut.DeleteAsync(existing.Id);

        _repo.Verify(r => r.DeleteWithPoliciesAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
