using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Employees;

public class SeparationRepositoryTests : DatabaseTestBase
{
    public SeparationRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> AnEmployeeIdAsync()
    {
        var e = AnEmployee();
        Context.Employees.Add(e);
        await Context.SaveChangesAsync();
        return e.Id;
    }

    private static Separation ASeparation(Guid employeeId, DateOnly lastDay) => new()
    {
        EmployeeId = employeeId, Type = SeparationType.Resignation, NoticeDate = lastDay.AddDays(-30),
        LastWorkingDay = lastDay, Status = SeparationStatus.NoticeGiven, RecordedBy = "hr@company.test",
        ClearanceItems = [new SeparationClearanceItem { Name = "HR", SortOrder = 0 }, new SeparationClearanceItem { Name = "IT", SortOrder = 1 }]
    };

    [Fact]
    public async Task Add_ThenGet_ReturnsTheSeparationWithItsClearanceItemsInOrder()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        await new SeparationRepository(NewContext()).AddAsync(separation);

        var loaded = await new SeparationRepository(NewContext()).GetAsync(separation.Id);

        loaded!.Employee.Should().NotBeNull();
        loaded.ClearanceItems.OrderBy(i => i.SortOrder).Select(i => i.Name).Should().Equal("HR", "IT");
    }

    [Fact]
    public async Task GetOpenForEmployee_FindsTheirSeparation()
    {
        var id = await AnEmployeeIdAsync();
        await new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 9, 30)));

        (await new SeparationRepository(NewContext()).GetOpenForEmployeeAsync(id)).Should().NotBeNull();
        (await new SeparationRepository(NewContext()).GetOpenForEmployeeAsync(Guid.NewGuid())).Should().BeNull();
    }

    /// <summary>
    /// Two HR users recording a separation for the same employee at once: the app-level "does this
    /// employee already have one" check in SeparationService can't see the other request's row
    /// until it commits, so the unique index is the real guard. AddAsync converts the resulting
    /// constraint violation into the same DomainException RecordAsync's own check throws, so the
    /// loser of the race gets a normal business message instead of a raw 500.
    /// </summary>
    [Fact]
    public async Task AnEmployee_CanHaveOnlyOneSeparation()
    {
        var id = await AnEmployeeIdAsync();
        await new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 9, 30)));

        var second = ASeparation(id, new DateOnly(2026, 10, 31));
        var act = () => new SeparationRepository(NewContext()).AddAsync(second);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("already has a separation recorded.");
    }

    /// <summary>
    /// Documents the trap <c>Repository&lt;T&gt;.UpdateAsync</c> already explains: every
    /// <c>AuditableEntity</c> gets its Guid at construction, so adding a brand-new child straight
    /// into a tracked parent's collection makes it indistinguishable from an existing row to
    /// <c>DetectChanges</c> - it goes out as an UPDATE that matches nothing, not an INSERT.
    /// <see cref="SeparationRepository.AddClearanceItemAsync"/> exists precisely so callers never
    /// have to hit this; this test is the reproduction that justifies it, not a path anyone should
    /// use in production code.
    /// </summary>
    [Fact]
    public async Task AddingAClearanceItemStraightIntoATrackedCollection_FailsWithAConcurrencyError()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        var repo = new SeparationRepository(Context);
        await repo.AddAsync(separation);

        var loaded = await repo.GetAsync(separation.Id);
        loaded!.ClearanceItems.Add(new SeparationClearanceItem { SeparationId = loaded.Id, Name = "Library", SortOrder = 2 });

        var act = () => repo.SaveAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task AddClearanceItem_ThenReadingThroughAFreshContext_ReturnsIt()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        await new SeparationRepository(NewContext()).AddAsync(separation);

        await using (var ctx = NewContext())
        {
            var repo = new SeparationRepository(ctx);
            var loaded = await repo.GetAsync(separation.Id);
            var item = new SeparationClearanceItem { SeparationId = loaded!.Id, Name = "Library", SortOrder = 2 };

            await repo.AddClearanceItemAsync(item);
        }

        var reloaded = await new SeparationRepository(NewContext()).GetAsync(separation.Id);
        reloaded!.ClearanceItems.Select(i => i.Name).Should().Contain("Library");
        reloaded.ClearanceItems.Should().HaveCount(3);
    }

    [Fact]
    public async Task ClearAnItem_ThenUndoIt_RoundTripsThroughFreshContexts()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        await new SeparationRepository(NewContext()).AddAsync(separation);
        var itemId = separation.ClearanceItems[0].Id;

        await using (var ctx = NewContext())
        {
            var repo = new SeparationRepository(ctx);
            var loaded = await repo.GetAsync(separation.Id);
            var item = loaded!.ClearanceItems.Single(i => i.Id == itemId);
            item.ClearedBy = "hr@company.test";
            item.ClearedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
            item.Note = "handed over badge";
            await repo.SaveAsync();
        }

        await using (var ctx = NewContext())
        {
            var repo = new SeparationRepository(ctx);
            var loaded = await repo.GetAsync(separation.Id);
            loaded!.ClearanceItems.Single(i => i.Id == itemId).ClearedAt.Should().NotBeNull();
        }

        await using (var ctx = NewContext())
        {
            var repo = new SeparationRepository(ctx);
            var loaded = await repo.GetAsync(separation.Id);
            var item = loaded!.ClearanceItems.Single(i => i.Id == itemId);
            item.ClearedBy = null;
            item.ClearedAt = null;
            item.Note = null;
            await repo.SaveAsync();
        }

        var reloaded = await new SeparationRepository(NewContext()).GetAsync(separation.Id);
        var undone = reloaded!.ClearanceItems.Single(i => i.Id == itemId);
        undone.ClearedBy.Should().BeNull();
        undone.ClearedAt.Should().BeNull();
        undone.Note.Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheSeparationAndItsItems()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        await new SeparationRepository(NewContext()).AddAsync(separation);

        await using (var ctx = NewContext())
        {
            var repo = new SeparationRepository(ctx);
            await repo.DeleteAsync((await repo.GetAsync(separation.Id))!);
        }

        await using var reader = NewContext();
        reader.Set<Separation>().Count().Should().Be(0);
        reader.Set<SeparationClearanceItem>().Count().Should().Be(0);
    }
}
