using System.Text;
using FluentAssertions;
using PeopleCore.Application.Auditing.DTOs;
using PeopleCore.Application.Auditing.Services;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests;

public class AuditLogQueryTests : DatabaseTestBase
{
    public AuditLogQueryTests(PostgresFixture fixture) : base(fixture) { }

    private AuditLogQueryService Service() => new(new AuditLogRepository(NewContext()));

    private async Task<List<AuditLog>> SeedAsync(params AuditLog[] logs)
    {
        await using var context = NewContext();
        context.AuditLogs.AddRange(logs);
        await context.SaveChangesAsync();
        return logs.ToList();
    }

    private static AuditLog Entry(
        string action = "Updated", AuditCategory category = AuditCategory.DataChange, DateTime? at = null,
        string? email = "hr@example.com", Guid? employee = null, string? correlation = null,
        AuditOutcome outcome = AuditOutcome.Success, string? changes = null) => new()
    {
        Action = action,
        Category = category,
        OccurredAt = at ?? DateTime.UtcNow,
        UserEmail = email,
        SubjectEmployeeId = employee,
        CorrelationId = correlation,
        Outcome = outcome,
        Changes = changes,
        EntityType = "EmployeeCompensation",
        EntityId = "c-1",
        Summary = "EmployeeCompensation updated",
    };

    [Fact]
    public async Task ListsNewestFirst_AndFiltersByCategoryOutcomeUserAndEmployee()
    {
        var employee = AnEmployee("Reyes", "Maria");
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await SeedAsync(
            Entry(action: "auth.login-failed", category: AuditCategory.Security, outcome: AuditOutcome.Failure, email: "ana@example.com"),
            Entry(employee: employee.Id, changes: """[{"field":"BasicSalary","old":30000,"new":35000}]"""),
            Entry(action: "auth.login-succeeded", category: AuditCategory.Security, email: "ana@example.com"));

        var security = await Service().SearchAsync(new AuditLogFilter(Category: AuditCategory.Security));
        security.Items.Select(i => i.Action).Should().Equal("auth.login-succeeded", "auth.login-failed");

        var failed = await Service().SearchAsync(new AuditLogFilter(Outcome: AuditOutcome.Failure, User: "ANA@"));
        failed.Items.Should().ContainSingle().Which.Action.Should().Be("auth.login-failed");

        var history = await Service().SearchAsync(new AuditLogFilter(EmployeeId: employee.Id));
        var change = history.Items.Should().ContainSingle(i => i.Category == AuditCategory.DataChange && i.EntityType == "EmployeeCompensation").Subject;
        change.SubjectEmployeeName.Should().Be("Maria Reyes");
        change.ChangeCount.Should().Be(1);
    }

    [Fact]
    public async Task DatesArePhilippineCalendarDays()
    {
        // 2026-10-23 23:30 in Manila is 15:30 UTC that day; 2026-10-24 00:30 in Manila is 16:30 UTC on the 23rd.
        await SeedAsync(
            Entry(action: "late-on-the-23rd", at: new DateTime(2026, 10, 23, 15, 30, 0, DateTimeKind.Utc)),
            Entry(action: "early-on-the-24th", at: new DateTime(2026, 10, 23, 16, 30, 0, DateTimeKind.Utc)));

        var result = await Service().SearchAsync(new AuditLogFilter(From: new DateOnly(2026, 10, 24), To: new DateOnly(2026, 10, 24)));

        result.Items.Should().ContainSingle().Which.Action.Should().Be("early-on-the-24th");
    }

    [Fact]
    public async Task ADetailCarriesItsChangesMetadataAndTheRestOfItsRequest()
    {
        var logs = await SeedAsync(
            Entry(action: "payroll-run.marked-paid", category: AuditCategory.Action, correlation: "req-1"),
            Entry(correlation: "req-1", changes: """[{"field":"Status","old":"Approved","new":"Paid"},{"field":"PasswordHash","old":"(changed)","new":"(changed)"}]"""),
            Entry(correlation: "req-2"));
        await using (var context = NewContext())
        {
            context.AuditLogs.Add(new AuditLog { Action = "role.updated", Category = AuditCategory.Security, Metadata = """{"permissionsAdded":["audit.view","payroll.manage"],"nameAfter":"Auditor"}""" });
            await context.SaveChangesAsync();
        }

        var detail = (await Service().GetAsync(logs[1].Id))!;
        detail.Changes.Should().Equal(
            new AuditFieldChangeDto("Status", "Approved", "Paid"),
            new AuditFieldChangeDto("PasswordHash", "(changed)", "(changed)"));
        detail.Related.Should().ContainSingle().Which.Action.Should().Be("payroll-run.marked-paid");

        var search = await Service().SearchAsync(new AuditLogFilter(Action: "role.updated"));
        var role = (await Service().GetAsync(search.Items.Single().Id))!;
        role.Metadata.Should().Contain("permissionsAdded", "audit.view, payroll.manage").And.Contain("nameAfter", "Auditor");
    }

    [Fact]
    public async Task FacetsListTheRecordTypesAndActionsInUse()
    {
        await SeedAsync(Entry(), Entry(action: "auth.logout", category: AuditCategory.Security));

        var facets = await Service().GetFacetsAsync();

        facets.EntityTypes.Should().Equal("EmployeeCompensation");
        facets.Actions.Should().Equal("Updated", "auth.logout");
    }

    [Fact]
    public async Task TheExportIsCsvWithEveryMatchAndDefusesFormulas()
    {
        await SeedAsync(Entry(email: "=HYPERLINK(\"x\")"), Entry(action: "auth.logout", category: AuditCategory.Security));

        var csv = Encoding.UTF8.GetString(await Service().ExportCsvAsync(new AuditLogFilter(Category: AuditCategory.DataChange)));
        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("Id,OccurredAtUtc,Category,Action");
        lines[1].Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"");
    }
}
