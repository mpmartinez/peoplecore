using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Migrations;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Task 1 storage: a leave type's ConvertsAtYearEnd setting, a payroll run's
/// IncludesLeaveConversion flag, and the migration that turns ConvertsAtYearEnd on for SIL.
/// Behaviour (computing or paying a conversion) belongs to a later task; this only proves the
/// shapes round-trip.
/// </summary>
public class YearEndLeaveConversionStorageTests : DatabaseTestBase
{
    public YearEndLeaveConversionStorageTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(Context);

    private static LeaveType AType(string code, bool convertsAtYearEnd = false) => new()
    {
        Name = $"Leave {code.Trim()}",
        Code = code,
        MaxDaysPerYear = 15m,
        ConvertsAtYearEnd = convertsAtYearEnd,
    };

    [Fact]
    public async Task LeaveType_ConvertsAtYearEnd_RoundTrips()
    {
        var on = AType("SIL", convertsAtYearEnd: true);
        var off = AType("VL", convertsAtYearEnd: false);
        Context.LeaveTypes.AddRange(on, off);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var stored = await reader.LeaveTypes.ToDictionaryAsync(t => t.Code);

        stored["SIL"].ConvertsAtYearEnd.Should().BeTrue();
        stored["VL"].ConvertsAtYearEnd.Should().BeFalse();
    }

    [Fact]
    public async Task PayrollRun_IncludesLeaveConversion_RoundTrips()
    {
        var employeeOne = AnEmployee("Reyes", "Maria");
        var employeeTwo = AnEmployee("Santos", "Pedro");
        Context.Employees.AddRange(employeeOne, employeeTwo);
        await Context.SaveChangesAsync();

        var withConversion = ARun("PAY-2026-YE1", new(2026, 12, 1), new(2026, 12, 31), new(2027, 1, 5));
        withConversion.IncludesLeaveConversion = true;
        withConversion.Employees = [AnEntry(withConversion.Id, employeeOne.Id)];

        var withoutConversion = ARun("PAY-2026-YE2", new(2026, 11, 1), new(2026, 11, 30), new(2026, 12, 5));
        withoutConversion.Employees = [AnEntry(withoutConversion.Id, employeeTwo.Id)];

        await Sut.AddWithEntriesAsync(withConversion);
        await Sut.AddWithEntriesAsync(withoutConversion);

        await using var reader = NewContext();
        var repo = new PayrollRunRepository(reader);

        (await repo.GetWithEntriesAsync(withConversion.Id))!.IncludesLeaveConversion.Should().BeTrue();
        (await repo.GetWithEntriesAsync(withoutConversion.Id))!.IncludesLeaveConversion.Should().BeFalse();
    }

    [Theory]
    [InlineData("SIL")]
    [InlineData("sil")]
    [InlineData(" SIL ")]
    public async Task Migration_TurnsConvertsAtYearEndOn_ForSil_HoweverCased(string code)
    {
        Context.LeaveTypes.Add(AType(code));
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(AddYearEndLeaveConversion.MarkSilConvertsAtYearEndSql);

        await using var reader = NewContext();
        (await reader.LeaveTypes.SingleAsync()).ConvertsAtYearEnd.Should().BeTrue();
    }

    [Fact]
    public async Task Migration_LeavesNonSilTypesAlone()
    {
        Context.LeaveTypes.Add(AType("VL"));
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(AddYearEndLeaveConversion.MarkSilConvertsAtYearEndSql);

        await using var reader = NewContext();
        (await reader.LeaveTypes.SingleAsync()).ConvertsAtYearEnd.Should().BeFalse();
    }
}
