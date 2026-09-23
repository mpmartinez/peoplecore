using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Infrastructure.Persistence.Migrations;

namespace PeopleCore.Infrastructure.Tests.Leave;

/// <summary>
/// Unused Service Incentive Leave is commutable to cash (Labor Code Art. 95), and a company's VL
/// usually covers it. Leave types existed before they said whether they convert, and nothing in the
/// app sets it, so the migration marks VL and SIL convertible on an existing database - matching the
/// code however it was typed - and touches nothing else.
/// </summary>
public class MarkVacationLeaveConvertibleTests : DatabaseTestBase
{
    public MarkVacationLeaveConvertibleTests(PostgresFixture fixture) : base(fixture) { }

    private static LeaveType AType(string code, bool countsAsVacation = true) => new()
    {
        Name = $"Leave {code.Trim()}",
        Code = code,
        MaxDaysPerYear = 15m,
        CountsAsVacationForDeMinimis = countsAsVacation,
    };

    [Fact]
    public async Task VlAndSil_BecomeConvertible_AndEverythingElseIsLeftAlone()
    {
        Context.LeaveTypes.AddRange(
            AType("VL"), AType("sil"), AType(" VL ", countsAsVacation: false),
            AType("SL", countsAsVacation: false), AType("VLX"), AType("EL"));
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(MarkVacationLeaveConvertible.MarkConvertibleSql);
        await Context.Database.ExecuteSqlRawAsync(MarkVacationLeaveConvertible.MarkConvertibleSql);

        await using var read = NewContext();
        var stored = await read.LeaveTypes.ToDictionaryAsync(t => t.Code);
        stored.Where(t => t.Value.IsConvertibleToCash).Select(t => t.Key)
            .Should().BeEquivalentTo(["VL", "sil", " VL "], "only VL and SIL convert, and running it twice changes nothing more");
        stored.ToDictionary(t => t.Key, t => t.Value.CountsAsVacationForDeMinimis).Should().BeEquivalentTo(
            new Dictionary<string, bool>
            {
                ["VL"] = true, ["sil"] = true, [" VL "] = false, ["SL"] = false, ["VLX"] = true, ["EL"] = true,
            }, "the de minimis setting is not the migration's to change");
    }
}
