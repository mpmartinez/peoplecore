using FluentAssertions;
using M2NET.Core.Enums;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

/// <summary>
/// Every rule a separation is held to: recording it, completing it, cancelling it, and the
/// clearance checklist that gates it. The clock is fixed at 2026-09-21 in Manila throughout.
/// </summary>
public class SeparationServiceTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private readonly Mock<ISeparationRepository> _separations = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly SeparationService _sut;
    private readonly Employee _employee;

    private Separation? _saved;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public SeparationServiceTests()
    {
        _employee = new Employee
        {
            Id = EmployeeId,
            EmployeeNumber = "EMP-001",
            FirstName = "Juan",
            LastName = "dela Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            WorkEmail = "juan@company.com",
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = EmploymentType.Regular,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true
        };
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _employee);
        _currentUser.Setup(c => c.Email).Returns("hr@company.test");

        _separations.Setup(s => s.AddAsync(It.IsAny<Separation>(), It.IsAny<CancellationToken>()))
            .Callback((Separation s, CancellationToken _) => _saved = s)
            .Returns(Task.CompletedTask);
        _separations.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _saved);
        _separations.Setup(s => s.GetOpenForEmployeeAsync(EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _saved);

        _sut = new SeparationService(_separations.Object, _employees.Object, _currentUser.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));
    }

    private Task<SeparationDto> Recorded(
        DateOnly? noticeDate = null, DateOnly? lastDay = null,
        SeparationType type = SeparationType.Resignation, AuthorizedCause? cause = null, string? reason = "Moving abroad")
        => _sut.RecordAsync(new RecordSeparationRequest(EmployeeId, type, cause,
            noticeDate ?? new DateOnly(2026, 9, 1), lastDay ?? new DateOnly(2026, 9, 30), reason));

    private void NothingSaved() =>
        _separations.Verify(s => s.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);

    // ── RecordAsync ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Record_WhenEmployeeDoesNotExist_ThrowsKeyNotFound()
    {
        _employees.Setup(e => e.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        var act = () => _sut.RecordAsync(new RecordSeparationRequest(Guid.NewGuid(), SeparationType.Resignation, null,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Record_WhenEmployeeIsNotActive_IsRefused()
    {
        _employee.IsActive = false;

        var act = () => Recorded();

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Juan dela Cruz is no longer active.");
    }

    [Fact]
    public async Task Record_WhenEmployeeAlreadyHasASeparation_IsRefused()
    {
        await Recorded();

        var act = () => Recorded();

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Juan dela Cruz already has a separation recorded.");
    }

    [Fact]
    public async Task Record_AuthorizedCauseType_WithoutACause_IsRefused()
    {
        var act = () => Recorded(type: SeparationType.AuthorizedCause, cause: null);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Choose the authorized cause.");
    }

    [Fact]
    public async Task Record_NonAuthorizedCauseType_WithACause_IsRefused()
    {
        var act = () => Recorded(type: SeparationType.Resignation, cause: AuthorizedCause.Redundancy);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Only an authorized-cause separation has a cause.");
    }

    [Fact]
    public async Task Record_LastWorkingDayBeforeNoticeDate_IsRefused()
    {
        var act = () => Recorded(noticeDate: new DateOnly(2026, 9, 10), lastDay: new DateOnly(2026, 9, 5));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("The last working day can't be before the notice date.");
    }

    [Fact]
    public async Task Record_ReasonLongerThan1000Characters_IsRefused()
    {
        var act = () => Recorded(reason: new string('A', 1001));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Keep the reason to 1000 characters.");
    }

    [Fact]
    public async Task Record_CreatesTheFiveDefaultClearanceItemsInOrder()
    {
        var dto = await _sut.RecordAsync(new RecordSeparationRequest(EmployeeId, SeparationType.Resignation, null,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Moving abroad"));

        dto.ClearanceItems.Select(i => i.Name).Should().Equal("HR", "IT", "Finance", "Immediate supervisor", "Property / admin");
        dto.ClearanceCount.Should().Be(5);
        dto.ClearedCount.Should().Be(0);
        dto.RecordedBy.Should().Be("hr@company.test");
        dto.FinalPayDueBy.Should().Be(new DateOnly(2026, 10, 30));
        dto.EmployeeName.Should().Be("Juan dela Cruz");
        dto.EmployeeNumber.Should().Be("EMP-001");
        dto.Status.Should().Be(SeparationStatus.NoticeGiven);
    }

    // ── MarkSeparatedAsync ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkSeparated_WhenAlreadySeparated_IsRefused()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 21));
        await _sut.MarkSeparatedAsync(s.Id);

        var act = () => _sut.MarkSeparatedAsync(s.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("This separation is already complete.");
    }

    [Fact]
    public async Task MarkSeparated_BeforeTheLastWorkingDay_IsRefused()
    {
        // The clock is 2026-09-21 in Manila; the last working day is 2026-09-30.
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 30));

        var act = () => _sut.MarkSeparatedAsync(s.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be("Juan dela Cruz's last working day is Sep 30, 2026; mark them separated on or after it.");
    }

    [Fact]
    public async Task MarkSeparated_DateInMessage_IsFormattedWithInvariantCulture_RegardlessOfCurrentCulture()
    {
        var original = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var s = await Recorded(lastDay: new DateOnly(2026, 9, 30));

            var act = () => _sut.MarkSeparatedAsync(s.Id);

            (await act.Should().ThrowAsync<DomainException>()).Which.Message
                .Should().Be("Juan dela Cruz's last working day is Sep 30, 2026; mark them separated on or after it.");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task MarkSeparated_OnTheLastWorkingDay_DeactivatesTheEmployee()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 21));

        var dto = await _sut.MarkSeparatedAsync(s.Id);

        dto.Status.Should().Be(SeparationStatus.Separated);
        dto.SeparatedBy.Should().Be("hr@company.test");
        dto.SeparatedAt.Should().NotBeNull();
        _employee.IsActive.Should().BeFalse();
        _employee.SeparationDate.Should().Be(new DateOnly(2026, 9, 21));
        _employees.Verify(e => e.UpdateAsync(_employee, It.IsAny<CancellationToken>()), Times.Once);
        _separations.Verify(s => s.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkSeparated_AfterTheLastWorkingDay_DeactivatesTheEmployee()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 1));

        var dto = await _sut.MarkSeparatedAsync(s.Id);

        dto.Status.Should().Be(SeparationStatus.Separated);
        _employee.IsActive.Should().BeFalse();
    }

    // ── CancelAsync ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_WhenAlreadySeparated_IsRefused()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 21));
        await _sut.MarkSeparatedAsync(s.Id);

        var act = () => _sut.CancelAsync(s.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("A completed separation can't be cancelled.");
    }

    [Fact]
    public async Task Cancel_WhenNoticeGiven_DeletesTheRecord()
    {
        var s = await Recorded();

        await _sut.CancelAsync(s.Id);

        _separations.Verify(r => r.DeleteAsync(It.Is<Separation>(x => x.Id == s.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Clearance ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddClearanceItem_WithABlankName_IsRefused(string name)
    {
        var s = await Recorded();

        var act = () => _sut.AddClearanceItemAsync(s.Id, name);

        await act.Should().ThrowAsync<DomainException>();
        NothingSaved();
    }

    [Fact]
    public async Task AddClearanceItem_LongerThan100Characters_IsRefused()
    {
        var s = await Recorded();
        var name = new string('A', 101);

        var act = () => _sut.AddClearanceItemAsync(s.Id, name);

        await act.Should().ThrowAsync<DomainException>();
        NothingSaved();
    }

    [Fact]
    public async Task AddClearanceItem_DuplicateIgnoringCase_IsRefused()
    {
        var s = await Recorded();

        var act = () => _sut.AddClearanceItemAsync(s.Id, "hr");

        // Uses the EXISTING item's stored casing ("HR"), not the caller's ("hr").
        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("There's already a HR item.");
    }

    [Fact]
    public async Task AddClearanceItem_UsesTheRepositorysDedicatedInsert_NotSaveAsyncOnATrackedCollection()
    {
        var s = await Recorded();

        await _sut.AddClearanceItemAsync(s.Id, "Library");

        _separations.Verify(r => r.AddClearanceItemAsync(
            It.Is<SeparationClearanceItem>(i => i.Name == "Library" && i.SeparationId == s.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddClearanceItem_WithAUniqueName_AddsIt()
    {
        var s = await Recorded();

        var dto = await _sut.AddClearanceItemAsync(s.Id, "Library");

        dto.ClearanceItems.Select(i => i.Name).Should().Contain("Library");
        dto.ClearanceCount.Should().Be(6);
    }

    [Fact]
    public async Task ClearItem_AlreadyCleared_IsRefused()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;
        await _sut.ClearItemAsync(s.Id, itemId, "done");

        var act = () => _sut.ClearItemAsync(s.Id, itemId, "again");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("HR is already cleared.");
    }

    [Fact]
    public async Task ClearItem_NotYetCleared_SetsClearedByAndClearedAt()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;

        var dto = await _sut.ClearItemAsync(s.Id, itemId, "handed over badge");

        var item = dto.ClearanceItems.Single(i => i.Id == itemId);
        item.ClearedBy.Should().Be("hr@company.test");
        item.ClearedAt.Should().NotBeNull();
        item.Note.Should().Be("handed over badge");
        dto.ClearedCount.Should().Be(1);
    }

    [Fact]
    public async Task ClearItem_NoteLongerThan500Characters_IsRefused()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;

        var act = () => _sut.ClearItemAsync(s.Id, itemId, new string('A', 501));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Keep the note to 500 characters.");
    }

    [Fact]
    public async Task UndoClearItem_OnAnItemThatIsNotCleared_IsRefused()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;

        var act = () => _sut.UndoClearItemAsync(s.Id, itemId);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("HR isn't cleared.");
    }

    [Fact]
    public async Task UndoClearItem_OnAClearedItem_ClearsClearedByAndClearedAt()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;
        await _sut.ClearItemAsync(s.Id, itemId, "done");

        var dto = await _sut.UndoClearItemAsync(s.Id, itemId);

        var item = dto.ClearanceItems.Single(i => i.Id == itemId);
        item.ClearedBy.Should().BeNull();
        item.ClearedAt.Should().BeNull();
        dto.ClearedCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteClearanceItem_WhenCleared_IsRefused()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;
        await _sut.ClearItemAsync(s.Id, itemId, "done");

        var act = () => _sut.DeleteClearanceItemAsync(s.Id, itemId);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Undo HR's clearance before removing it.");
    }

    [Fact]
    public async Task DeleteClearanceItem_WhenNotCleared_RemovesIt()
    {
        var s = await Recorded();
        var itemId = s.ClearanceItems[0].Id;

        var dto = await _sut.DeleteClearanceItemAsync(s.Id, itemId);

        dto.ClearanceItems.Should().NotContain(i => i.Id == itemId);
        dto.ClearanceCount.Should().Be(4);
    }

    // ── FinalPayOverdue ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FinalPayOverdue_WhenSeparatedAndPastDue_IsTrue()
    {
        // Last working day 2026-08-01 -> final pay due 2026-08-31, before the 2026-09-21 clock.
        var s = await Recorded(noticeDate: new DateOnly(2026, 7, 1), lastDay: new DateOnly(2026, 8, 1));
        var dto = await _sut.MarkSeparatedAsync(s.Id);

        dto.FinalPayOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task FinalPayOverdue_WhenNotSeparated_IsFalse()
    {
        var s = await Recorded(noticeDate: new DateOnly(2026, 7, 1), lastDay: new DateOnly(2026, 8, 1));

        s.FinalPayOverdue.Should().BeFalse();
    }

    // ── SeparateNowAsync (Deactivate) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SeparateNow_WithNoExistingSeparation_AndNoTypeGiven_DefaultsToResignation()
    {
        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 21));

        dto.Status.Should().Be(SeparationStatus.Separated);
        dto.Type.Should().Be(SeparationType.Resignation);
        dto.LastWorkingDay.Should().Be(new DateOnly(2026, 9, 21));
        _employee.IsActive.Should().BeFalse();
        _employee.SeparationDate.Should().Be(new DateOnly(2026, 9, 21));
    }

    [Fact]
    public async Task SeparateNow_WithNoExistingSeparation_AuthorizedCauseWithoutACause_IsRefused()
    {
        var act = () => _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 21), SeparationType.AuthorizedCause);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Choose the authorized cause.");
    }

    [Fact]
    public async Task SeparateNow_WithNoExistingSeparation_AuthorizedCauseWithACause_RecordsIt()
    {
        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 21), SeparationType.AuthorizedCause, AuthorizedCause.Redundancy);

        dto.Type.Should().Be(SeparationType.AuthorizedCause);
        dto.AuthorizedCause.Should().Be(AuthorizedCause.Redundancy);
        dto.Status.Should().Be(SeparationStatus.Separated);
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_AndNoTypeGiven_KeepsItsExistingTypeAndCause()
    {
        // The clock is 2026-09-21; a future last working day would fail MarkSeparatedAsync's date check.
        var s = await Recorded(lastDay: new DateOnly(2026, 10, 30), type: SeparationType.AuthorizedCause, cause: AuthorizedCause.Redundancy);

        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25));

        dto.Id.Should().Be(s.Id);
        dto.Status.Should().Be(SeparationStatus.Separated);
        dto.Type.Should().Be(SeparationType.AuthorizedCause);
        dto.AuthorizedCause.Should().Be(AuthorizedCause.Redundancy);
        dto.LastWorkingDay.Should().Be(new DateOnly(2026, 9, 25));
        _employee.IsActive.Should().BeFalse();
        _employee.SeparationDate.Should().Be(new DateOnly(2026, 9, 25));
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_AndATypeGiven_AppliesItsOwnValidation()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 10, 30));

        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25), SeparationType.AuthorizedCause, AuthorizedCause.Redundancy);

        dto.Id.Should().Be(s.Id);
        dto.Type.Should().Be(SeparationType.AuthorizedCause);
        dto.AuthorizedCause.Should().Be(AuthorizedCause.Redundancy);
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_AuthorizedCauseTypeWithoutACause_IsRefused()
    {
        await Recorded(lastDay: new DateOnly(2026, 10, 30));

        var act = () => _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25), SeparationType.AuthorizedCause);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Choose the authorized cause.");
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_NonAuthorizedCauseTypeWithACause_IsRefused()
    {
        await Recorded(lastDay: new DateOnly(2026, 10, 30));

        var act = () => _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25), SeparationType.Resignation, AuthorizedCause.Redundancy);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Only an authorized-cause separation has a cause.");
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_ACauseGivenWithoutATypeOnANonAuthorizedCauseSeparation_IsRefused()
    {
        // Recorded as a plain Resignation; a cause with no type shouldn't be silently dropped.
        await Recorded(lastDay: new DateOnly(2026, 10, 30), type: SeparationType.Resignation);

        var act = () => _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25), authorizedCause: AuthorizedCause.Redundancy);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Only an authorized-cause separation has a cause.");
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_ACauseGivenWithoutATypeOnAnAuthorizedCauseSeparation_UpdatesTheCause()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 10, 30), type: SeparationType.AuthorizedCause, cause: AuthorizedCause.Redundancy);

        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25), authorizedCause: AuthorizedCause.Retrenchment);

        dto.Id.Should().Be(s.Id);
        dto.Type.Should().Be(SeparationType.AuthorizedCause);
        dto.AuthorizedCause.Should().Be(AuthorizedCause.Retrenchment);
    }

    [Fact]
    public async Task SeparateNow_WithAnExistingNoticeGivenSeparation_IgnoresTheMarkSeparatedDateCheck()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 10, 30));

        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 9, 25));

        dto.Id.Should().Be(s.Id);
        dto.Status.Should().Be(SeparationStatus.Separated);
        dto.LastWorkingDay.Should().Be(new DateOnly(2026, 9, 25));
        _employee.IsActive.Should().BeFalse();
        _employee.SeparationDate.Should().Be(new DateOnly(2026, 9, 25));
    }

    [Fact]
    public async Task SeparateNow_WhenTheGivenLastWorkingDayIsBeforeTheNoticeDate_PullsTheNoticeDateBackInsteadOfRefusing()
    {
        // Recorded with a notice given 2026-09-01 for a 2026-09-30 last day; HR now asserts the
        // employee actually left 2026-08-15 - earlier than the notice date itself.
        var s = await Recorded(noticeDate: new DateOnly(2026, 9, 1), lastDay: new DateOnly(2026, 9, 30));

        var dto = await _sut.SeparateNowAsync(EmployeeId, new DateOnly(2026, 8, 15));

        dto.Id.Should().Be(s.Id);
        dto.NoticeDate.Should().Be(new DateOnly(2026, 8, 15));
        dto.LastWorkingDay.Should().Be(new DateOnly(2026, 8, 15));
        dto.Status.Should().Be(SeparationStatus.Separated);
    }
}
