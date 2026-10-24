using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Identity;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests;

public class AuditTrailCaptureTests : DatabaseTestBase
{
    public AuditTrailCaptureTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class FakeAuditContext : IAuditContext
    {
        public string? UserId { get; init; } = "user-1";
        public string? UserEmail { get; init; } = "hr@example.com";
        public string? IpAddress { get; init; } = "203.0.113.7";
        public string? UserAgent { get; init; } = "TestAgent/1.0";
        public string? CorrelationId { get; init; } = "req-42";
        public string? HttpMethod { get; init; } = "PUT";
        public string? Path { get; init; } = "/api/test";
        public string? SystemActor { get; set; }
    }

    private async Task<List<AuditLog>> TrailAsync()
    {
        await using var reader = NewContext();
        return await reader.AuditLogs.OrderBy(a => a.Id).ToListAsync();
    }

    private static List<(string Field, string? Old, string? New)> ChangesOf(AuditLog log) =>
        JsonSerializer.Deserialize<List<JsonElement>>(log.Changes!)!
            .Select(c => (
                c.GetProperty("field").GetString()!,
                Text(c.GetProperty("old")),
                Text(c.GetProperty("new"))))
            .ToList();

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => value.GetRawText(),
    };

    private async Task<Employee> SeedEmployeeAsync()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        return employee;
    }

    [Fact]
    public async Task AnInsertIsRecordedWithItsValuesActorAndRequest()
    {
        var employee = await SeedEmployeeAsync();
        await using (var context = NewContext(new FakeAuditContext()))
        {
            context.EmployeeCompensations.Add(new EmployeeCompensation { EmployeeId = employee.Id, BasicSalary = 30_000m });
            await context.SaveChangesAsync();
        }

        var entry = (await TrailAsync()).Single(a => a.EntityType == nameof(EmployeeCompensation));

        entry.Category.Should().Be(AuditCategory.DataChange);
        entry.Action.Should().Be("Created");
        entry.UserId.Should().Be("user-1");
        entry.UserEmail.Should().Be("hr@example.com");
        entry.IpAddress.Should().Be("203.0.113.7");
        entry.UserAgent.Should().Be("TestAgent/1.0");
        entry.CorrelationId.Should().Be("req-42");
        entry.SubjectEmployeeId.Should().Be(employee.Id);
        ChangesOf(entry).Should().Contain(("BasicSalary", null, "30000"))
            .And.NotContain(c => c.Field == "CreatedAt" || c.Field == "UpdatedBy");
    }

    [Fact]
    public async Task AnUpdateRecordsOnlyTheFieldsThatChangedWithBeforeAndAfter()
    {
        var employee = await SeedEmployeeAsync();
        Context.EmployeeCompensations.Add(new EmployeeCompensation { EmployeeId = employee.Id, BasicSalary = 30_000m, TaxCode = "ME" });
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            var compensation = await context.EmployeeCompensations.SingleAsync();
            compensation.BasicSalary = 35_000m;
            await context.SaveChangesAsync();
        }

        var update = (await TrailAsync()).Single(a => a.Action == "Updated");
        ChangesOf(update).Should().Equal(("BasicSalary", "30000.00", "35000"));
    }

    [Fact]
    public async Task ADetachedUpdateIsDiffedAgainstTheDatabase()
    {
        // Repository.UpdateAsync attaches a DTO-mapped entity with DbSet.Update, which leaves the
        // tracker believing every field changed to the value it already had.
        var employee = await SeedEmployeeAsync();
        var compensation = new EmployeeCompensation { EmployeeId = employee.Id, BasicSalary = 30_000m, Dependents = 0 };
        Context.EmployeeCompensations.Add(compensation);
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            var detached = new EmployeeCompensation
            {
                Id = compensation.Id,
                EmployeeId = employee.Id,
                BasicSalary = 30_000m,
                TaxCode = compensation.TaxCode,
                PayFrequency = compensation.PayFrequency,
                Dependents = 2,
                CreatedAt = compensation.CreatedAt,
            };
            await new Repository<EmployeeCompensation>(context).UpdateAsync(detached);
        }

        var update = (await TrailAsync()).Single(a => a.Action == "Updated");
        ChangesOf(update).Should().Equal(("Dependents", "0", "2"));
    }

    [Fact]
    public async Task ADeleteRecordsTheValuesTheRowHad()
    {
        var employee = await SeedEmployeeAsync();
        Context.EmployeeGovernmentIds.Add(new EmployeeGovernmentId { EmployeeId = employee.Id, IdType = GovernmentIdType.TIN, IdNumber = "123-456-789" });
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            context.EmployeeGovernmentIds.Remove(await context.EmployeeGovernmentIds.SingleAsync());
            await context.SaveChangesAsync();
        }

        var delete = (await TrailAsync()).Single(a => a.Action == "Deleted");
        delete.EntityType.Should().Be(nameof(EmployeeGovernmentId));
        delete.EntityId.Should().NotBeNullOrEmpty();
        delete.SubjectEmployeeId.Should().Be(employee.Id);
        ChangesOf(delete).Should().Contain(("IdNumber", "123-456-789", null))
            .And.Contain(("IdType", "TIN", null));
    }

    [Fact]
    public async Task AnEmployeeRowIsItsOwnSubject()
    {
        var employee = await SeedEmployeeAsync();
        (await TrailAsync()).Single().SubjectEmployeeId.Should().Be(employee.Id);
    }

    [Fact]
    public async Task SecretsAreRecordedAsChangedNeverByValue()
    {
        Context.Users.Add(new ApplicationUser { UserName = "a@example.com", Email = "a@example.com", PasswordHash = "HASH-ONE" });
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            var user = await context.Users.SingleAsync();
            user.PasswordHash = "HASH-TWO";
            await context.SaveChangesAsync();
        }

        var trail = await TrailAsync();
        trail.Should().OnlyContain(a => !a.Changes!.Contains("HASH-"));
        trail.Single(a => a.Action == "Updated").EntityType.Should().Be("UserAccount");
        ChangesOf(trail.Single(a => a.Action == "Updated"))
            .Should().Contain(("PasswordHash", "(changed)", "(changed)"));
    }

    [Fact]
    public async Task AGeneratedKeyIsRecordedOnceTheDatabaseAssignsIt()
    {
        // Role permissions (AspNetRoleClaims) have an identity key, unknown until the insert runs.
        var role = new ApplicationRole { Name = "Auditor", NormalizedName = "AUDITOR" };
        Context.Roles.Add(role);
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            context.RoleClaims.Add(new() { RoleId = role.Id, ClaimType = "permission", ClaimValue = "audit.view" });
            await context.SaveChangesAsync();
        }

        var entry = (await TrailAsync()).Single(a => a.EntityType == "RolePermission");
        int.Parse(entry.EntityId!).Should().BePositive();
        ChangesOf(entry).Should().Contain(("ClaimValue", null, "audit.view"));
    }

    [Fact]
    public async Task AFailedSaveLeavesNoAuditEntries()
    {
        var employee = await SeedEmployeeAsync();
        Context.EmployeeCompensations.Add(new EmployeeCompensation { EmployeeId = employee.Id, BasicSalary = 1m });
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            // A second compensation row for the same employee breaks the unique index.
            context.EmployeeCompensations.Add(new EmployeeCompensation { EmployeeId = employee.Id, BasicSalary = 2m });
            var save = () => context.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        (await TrailAsync()).Should().HaveCount(2, "only the seeded employee and compensation were saved");
    }

    [Fact]
    public async Task AWriteWithNoRequestIsAttributedToTheSystemOrTheNamedJob()
    {
        Context.Employees.Add(AnEmployee());
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext
        {
            UserId = null, UserEmail = null, IpAddress = null, UserAgent = null,
            CorrelationId = null, HttpMethod = null, Path = null, SystemActor = "Leave accrual job",
        }))
        {
            context.Employees.Add(AnEmployee("Santos"));
            await context.SaveChangesAsync();
        }

        var trail = await TrailAsync();
        trail[0].ActorName.Should().Be("System");
        trail[1].ActorName.Should().Be("System: Leave accrual job");
    }

    [Fact]
    public async Task AnAuditEntryCannotBeChangedOrDeleted()
    {
        await SeedEmployeeAsync();

        await using (var context = NewContext())
        {
            var entry = await context.AuditLogs.SingleAsync();
            entry.Summary = "tampered";
            var save = () => context.SaveChangesAsync();
            await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
        }

        await using (var context = NewContext())
        {
            var update = () => context.Database.ExecuteSqlRawAsync("UPDATE audit_logs SET summary = 'tampered'");
            await update.Should().ThrowAsync<Exception>().WithMessage("*append-only*");

            var delete = () => context.AuditLogs.ExecuteDeleteAsync();
            await delete.Should().ThrowAsync<Exception>().WithMessage("*append-only*");
        }
    }

    [Fact]
    public async Task RecomputingARunRecordsTheRemovedLinesAndTheNewOnes()
    {
        var employee = await SeedEmployeeAsync();
        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20), PayrollRunStatus.Draft);
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id, 10_000m));
        await Context.SaveChangesAsync();

        await using (var context = NewContext(new FakeAuditContext()))
        {
            var repository = new PayrollRunRepository(context);
            var tracked = await context.PayrollRuns.Include(r => r.Employees).SingleAsync();
            await repository.ReplaceEntriesAsync(tracked, [AnEntry(run.Id, employee.Id, 12_000m)]);
        }

        var trail = await TrailAsync();
        var removed = trail.Single(a => a.Action == "LinesRemoved");
        removed.EntityType.Should().Be(nameof(PayrollRun));
        removed.EntityId.Should().Be(run.Id.ToString());
        removed.UserId.Should().Be("user-1");
        trail.Where(a => a.EntityType == nameof(PayrollRunEmployee) && a.Action == "Created")
            .Should().HaveCount(2, "the original line and its recomputed replacement");
    }
}
