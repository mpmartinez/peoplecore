using FluentAssertions;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
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

    [Fact]
    public async Task AnEmployee_CanHaveOnlyOneSeparation()
    {
        var id = await AnEmployeeIdAsync();
        await new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 9, 30)));

        var act = () => new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 10, 31)));

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
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
