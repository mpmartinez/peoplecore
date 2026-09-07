using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Infrastructure.Tests;

public class AuditStampingTests : DatabaseTestBase
{
    public AuditStampingTests(PostgresFixture fixture) : base(fixture) { }

    private static ICurrentUserService AUserService(Guid userId)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.UserId).Returns(userId);
        return mock.Object;
    }

    [Fact]
    public async Task Insert_StampsCreatedByFromTheCurrentUser()
    {
        // These columns going unpopulated was a real defect, fixed earlier in this work and
        // verified by nothing since.
        var userId = Guid.NewGuid();
        await using var context = NewContext(AUserService(userId));

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        context.PayrollRuns.Add(run);
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedBy.Should().Be(userId);
        stored.UpdatedBy.Should().Be(userId);
    }

    [Fact]
    public async Task Insert_OverwritesTheTimestampTheEntityWasConstructedWith()
    {
        // AuditableEntity sets CreatedAt to UtcNow in its initialiser, so a test that only checked
        // "CreatedAt is roughly now" would pass even if StampAuditColumns never ran. Setting a
        // distinctive value first is what makes this test able to fail.
        await using var context = NewContext(AUserService(Guid.NewGuid()));

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        run.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        context.PayrollRuns.Add(run);
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedAt.Should().BeAfter(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Update_StampsUpdatedByAndLeavesCreatedByAlone()
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();

        await using (var first = NewContext(AUserService(creator)))
        {
            first.PayrollRuns.Add(ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20)));
            await first.SaveChangesAsync();
        }

        await using (var second = NewContext(AUserService(editor)))
        {
            var run = await second.PayrollRuns.SingleAsync();
            run.Status = PayrollRunStatus.Approved;
            await second.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedBy.Should().Be(creator, "an update must not rewrite who created the row");
        stored.UpdatedBy.Should().Be(editor);
    }

    [Fact]
    public async Task ANullCurrentUserServiceStampsTimestampsAndLeavesTheUserColumnsNull()
    {
        // The constructor parameter is optional, and background work - the leave accrual hosted
        // service, migrations, the seeder - resolves a context with no user. That must save
        // rather than throw.
        await using var context = NewContext(currentUser: null);

        var save = async () =>
        {
            context.PayrollRuns.Add(ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20)));
            await context.SaveChangesAsync();
        };

        await save.Should().NotThrowAsync();

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedBy.Should().BeNull();
        stored.CreatedAt.Should().BeAfter(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
