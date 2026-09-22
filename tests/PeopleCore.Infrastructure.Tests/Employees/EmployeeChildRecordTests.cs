using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Employees;

/// <summary>
/// Runs the employee services over the real repository and a real database. The Application tests
/// mock the repository, so they could never see what EF Core does with a new child added to a
/// tracked employee - and every one of these saves returned 500 in production because of it.
/// </summary>
public class EmployeeChildRecordTests : DatabaseTestBase
{
    public EmployeeChildRecordTests(PostgresFixture fixture) : base(fixture) { }

    /// <summary>None of these tests exercise Deactivate, so a bare mock stands in for the separation service.</summary>
    private static ISeparationService NoopSeparationService => new Mock<ISeparationService>().Object;

    private EmployeeService Service => new(new EmployeeRepository(Context), NoopSeparationService);

    private async Task<Guid> AnExistingEmployeeAsync()
    {
        await using var seeder = NewContext();
        var employee = AnEmployee();
        seeder.Employees.Add(employee);
        await seeder.SaveChangesAsync();
        return employee.Id;
    }

    [Fact]
    public async Task UpsertGovernmentId_AddsANewIdToAnExistingEmployee()
    {
        var employeeId = await AnExistingEmployeeAsync();

        await Service.UpsertGovernmentIdAsync(employeeId, new UpsertGovernmentIdDto(GovernmentIdType.SSS, "34-1234567-8"));

        await using var reader = NewContext();
        var stored = await reader.EmployeeGovernmentIds.Where(g => g.EmployeeId == employeeId).ToListAsync();
        stored.Should().ContainSingle()
            .Which.Should().Match<EmployeeGovernmentId>(g => g.IdType == GovernmentIdType.SSS && g.IdNumber == "34-1234567-8");
    }

    [Fact]
    public async Task UpsertGovernmentId_UpdatesTheExistingIdOfTheSameType()
    {
        var employeeId = await AnExistingEmployeeAsync();
        await using (var seeder = NewContext())
        {
            seeder.EmployeeGovernmentIds.Add(new EmployeeGovernmentId
            {
                EmployeeId = employeeId, IdType = GovernmentIdType.SSS, IdNumber = "OLD",
            });
            await seeder.SaveChangesAsync();
        }

        await Service.UpsertGovernmentIdAsync(employeeId, new UpsertGovernmentIdDto(GovernmentIdType.SSS, "NEW"));

        await using var reader = NewContext();
        var stored = await reader.EmployeeGovernmentIds.Where(g => g.EmployeeId == employeeId).ToListAsync();
        stored.Should().ContainSingle().Which.IdNumber.Should().Be("NEW");
    }

    [Fact]
    public async Task UpsertGovernmentId_AddsASecondTypeThenUpdatesTheFirst_InOneScope()
    {
        // What the API does across three requests, but sharing one context the way a single
        // request's scope would if a caller chained them - the tracked graph then already holds
        // saved children alongside the new one.
        var employeeId = await AnExistingEmployeeAsync();
        var service = Service;

        await service.UpsertGovernmentIdAsync(employeeId, new UpsertGovernmentIdDto(GovernmentIdType.SSS, "SSS-1"));
        await service.UpsertGovernmentIdAsync(employeeId, new UpsertGovernmentIdDto(GovernmentIdType.TIN, "TIN-1"));
        await service.UpsertGovernmentIdAsync(employeeId, new UpsertGovernmentIdDto(GovernmentIdType.SSS, "SSS-2"));

        await using var reader = NewContext();
        var stored = await reader.EmployeeGovernmentIds
            .Where(g => g.EmployeeId == employeeId)
            .OrderBy(g => g.IdType)
            .Select(g => new { g.IdType, g.IdNumber })
            .ToListAsync();
        stored.Should().Equal(
            new { IdType = GovernmentIdType.SSS, IdNumber = "SSS-2" },
            new { IdType = GovernmentIdType.TIN, IdNumber = "TIN-1" });
    }

    [Fact]
    public async Task AddEmergencyContact_SavesTheContact()
    {
        var employeeId = await AnExistingEmployeeAsync();

        var returned = await Service.AddEmergencyContactAsync(
            employeeId, new CreateEmergencyContactDto("Maria Dela Cruz", "Spouse", "09171234567", "Manila"));

        await using var reader = NewContext();
        var stored = await reader.EmergencyContacts.Where(c => c.EmployeeId == employeeId).ToListAsync();
        stored.Should().ContainSingle();
        stored[0].Id.Should().Be(returned.Id);
        stored[0].Name.Should().Be("Maria Dela Cruz");
    }

    [Fact]
    public async Task DeleteEmergencyContact_RemovesTheContact()
    {
        var employeeId = await AnExistingEmployeeAsync();
        var contact = await Service.AddEmergencyContactAsync(
            employeeId, new CreateEmergencyContactDto("Maria", "Spouse", "0917", null));

        await new EmployeeService(new EmployeeRepository(NewContext()), NoopSeparationService).DeleteEmergencyContactAsync(employeeId, contact.Id);

        await using var reader = NewContext();
        (await reader.EmergencyContacts.AnyAsync(c => c.EmployeeId == employeeId)).Should().BeFalse();
    }

    [Fact]
    public async Task UploadDocument_SavesTheDocumentRow()
    {
        var employeeId = await AnExistingEmployeeAsync();
        var storage = new Mock<IStorageService>();
        var service = new EmployeeDocumentService(new EmployeeRepository(Context), storage.Object, new DocumentStorageOptions());

        using var file = new MemoryStream([1, 2, 3]);
        var returned = await service.UploadDocumentAsync(
            employeeId, DocumentType.Contract, "contract.pdf", file, "application/pdf");

        await using var reader = NewContext();
        var stored = await reader.EmployeeDocuments.Where(d => d.EmployeeId == employeeId).ToListAsync();
        stored.Should().ContainSingle().Which.Id.Should().Be(returned.Id);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a successful save must not roll back the uploaded object");
    }

    [Fact]
    public async Task UpdateEmployee_StillSavesScalarChangesToATrackedEmployee()
    {
        var employeeId = await AnExistingEmployeeAsync();
        var employee = (await new EmployeeRepository(Context).GetByIdAsync(employeeId))!;

        await Service.UpdateAsync(employeeId, new UpdateEmployeeDto(
            "Pedro", null, "Santos", null, null, null, null, null, null, null, null,
            employee.EmploymentStatus, null, true));

        await using var reader = NewContext();
        (await reader.Employees.SingleAsync(e => e.Id == employeeId)).FirstName.Should().Be("Pedro");
    }

    [Fact]
    public async Task RepositoryUpdate_StillSavesADetachedEntityMappedFromADto()
    {
        // The path Update was written for: an entity the context has never seen, carrying the
        // key of an existing row. It must still be treated as an update of that row.
        var employeeId = await AnExistingEmployeeAsync();
        Employee detached;
        await using (var loader = NewContext())
            detached = await loader.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
        detached.FirstName = "Detached";

        await new Repository<Employee>(Context).UpdateAsync(detached);

        await using var reader = NewContext();
        (await reader.Employees.SingleAsync(e => e.Id == employeeId)).FirstName.Should().Be("Detached");
    }
}
