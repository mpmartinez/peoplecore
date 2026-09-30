using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Storage for maternity pay: the MaternityClaim table, the entry advance flag and its
/// advance/offset amounts, and the differential-exemption setting. Behaviour belongs to later
/// tasks; this only proves the shapes round-trip and the constraints are real.
/// </summary>
public class MaternityPayStorageTests : DatabaseTestBase
{
    public MaternityPayStorageTests(PostgresFixture fixture) : base(fixture) { }

    private MaternityClaimRepository Sut => new(Context);

    private async Task<(Employee Employee, LeaveRequest Request)> AMaternityRequestAsync(
        string lastName = "Dela Cruz", DateOnly? start = null)
    {
        var employee = AnEmployee(lastName);
        var type = new LeaveType { Name = "Maternity " + Guid.NewGuid().ToString("N")[..6], Code = Guid.NewGuid().ToString("N")[..6], MaxDaysPerYear = 105m };
        Context.Employees.Add(employee);
        Context.LeaveTypes.Add(type);
        await Context.SaveChangesAsync();

        var from = start ?? new DateOnly(2026, 8, 1);
        var request = new LeaveRequest
        {
            EmployeeId = employee.Id,
            LeaveTypeId = type.Id,
            StartDate = from,
            EndDate = from.AddDays(104),
            TotalDays = 105m,
            Status = LeaveStatus.Approved,
        };
        Context.LeaveRequests.Add(request);
        await Context.SaveChangesAsync();
        return (employee, request);
    }

    private static MaternityClaim AClaim(Employee employee, LeaveRequest request) => new()
    {
        LeaveRequestId = request.Id,
        EmployeeId = employee.Id,
    };

    [Fact]
    public async Task AClaim_RoundTripsEveryField()
    {
        var (employee, request) = await AMaternityRequestAsync();
        var run = ARun("PAY-2026-M01", new(2026, 8, 1), new(2026, 8, 15), new(2026, 8, 20));
        Context.PayrollRuns.Add(run);
        await Context.SaveChangesAsync();

        var claim = AClaim(employee, request);
        claim.DailyAllowance = 1_000.55m;
        claim.Days = 105m;
        claim.Benefit = 105_057.75m;
        claim.Status = MaternityClaimStatus.Reimbursed;
        claim.AdvanceRunId = run.Id;
        claim.AdvancedAt = new DateOnly(2026, 8, 20);
        claim.ReimbursedOn = new DateOnly(2026, 11, 3);
        claim.ReimbursedAmount = 100_000m;
        claim.Note = "SSS paid less";
        await Sut.AddAsync(claim);

        await using var reader = NewContext();
        var stored = await new MaternityClaimRepository(reader).GetByLeaveRequestAsync(request.Id);

        stored.Should().NotBeNull();
        stored!.EmployeeId.Should().Be(employee.Id);
        stored.DailyAllowance.Should().Be(1_000.55m);
        stored.Days.Should().Be(105m);
        stored.Benefit.Should().Be(105_057.75m);
        stored.Status.Should().Be(MaternityClaimStatus.Reimbursed);
        stored.AdvanceRunId.Should().Be(run.Id);
        stored.AdvancedAt.Should().Be(new DateOnly(2026, 8, 20));
        stored.ReimbursedOn.Should().Be(new DateOnly(2026, 11, 3));
        stored.ReimbursedAmount.Should().Be(100_000m);
        stored.Note.Should().Be("SSS paid less");
    }

    [Fact]
    public async Task ANewClaim_DefaultsToDraftWithNoBenefit()
    {
        var (employee, request) = await AMaternityRequestAsync();
        await Sut.AddAsync(AClaim(employee, request));

        await using var reader = NewContext();
        var stored = await new MaternityClaimRepository(reader).GetByLeaveRequestAsync(request.Id);

        stored!.Status.Should().Be(MaternityClaimStatus.Draft);
        stored.Benefit.Should().Be(0m);
        stored.DailyAllowance.Should().BeNull();
        stored.AdvanceRunId.Should().BeNull();
    }

    [Fact]
    public async Task TheStatusIsStoredAsAString()
    {
        var (employee, request) = await AMaternityRequestAsync();
        var claim = AClaim(employee, request);
        claim.Status = MaternityClaimStatus.Advanced;
        await Sut.AddAsync(claim);

        var status = await Context.Database
            .SqlQuery<string>($"select status as \"Value\" from maternity_claims")
            .SingleAsync();

        status.Should().Be("Advanced");
    }

    [Fact]
    public async Task ASecondClaimForTheSameLeaveRequest_IsRefusedByTheDatabase()
    {
        var (employee, request) = await AMaternityRequestAsync();
        await Sut.AddAsync(AClaim(employee, request));

        var act = async () => await new MaternityClaimRepository(NewContext()).AddAsync(AClaim(employee, request));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingTheAdvanceRun_NullsTheClaimsAdvanceRunId()
    {
        var (employee, request) = await AMaternityRequestAsync();
        var run = ARun("PAY-2026-M02", new(2026, 8, 1), new(2026, 8, 15), new(2026, 8, 20), PayrollRunStatus.Draft);
        Context.PayrollRuns.Add(run);
        await Context.SaveChangesAsync();

        var claim = AClaim(employee, request);
        claim.AdvanceRunId = run.Id;
        await Sut.AddAsync(claim);

        await using var writer = NewContext();
        writer.PayrollRuns.Remove(await writer.PayrollRuns.SingleAsync(r => r.Id == run.Id));
        await writer.SaveChangesAsync();

        await using var reader = NewContext();
        var stored = await new MaternityClaimRepository(reader).GetByLeaveRequestAsync(request.Id);
        stored.Should().NotBeNull();
        stored!.AdvanceRunId.Should().BeNull();
    }

    [Fact]
    public async Task GetForEmployee_ReturnsOnlyThatEmployeesClaims_NewestFirst_WithTheLeaveRequest()
    {
        var (employee, firstRequest) = await AMaternityRequestAsync("Reyes", new DateOnly(2025, 1, 1));
        var (other, otherRequest) = await AMaternityRequestAsync("Santos");

        var secondRequest = new LeaveRequest
        {
            EmployeeId = employee.Id,
            LeaveTypeId = firstRequest.LeaveTypeId,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 11, 13),
            TotalDays = 105m,
            Status = LeaveStatus.Approved,
        };
        Context.LeaveRequests.Add(secondRequest);
        await Context.SaveChangesAsync();

        // CreatedAt is stamped on save, so the claims are saved in the order they should come back reversed.
        await Sut.AddAsync(AClaim(employee, firstRequest));
        await Task.Delay(20);
        await Sut.AddAsync(AClaim(other, otherRequest));
        await Task.Delay(20);
        await Sut.AddAsync(AClaim(employee, secondRequest));

        await using var reader = NewContext();
        var claims = await new MaternityClaimRepository(reader).GetForEmployeeAsync(employee.Id);

        claims.Select(c => c.LeaveRequestId).Should().ContainInOrder(secondRequest.Id, firstRequest.Id);
        claims.Should().HaveCount(2);
        claims.Should().OnlyContain(c => c.LeaveRequest != null);
        claims[0].LeaveRequest.StartDate.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task GetAll_LoadsTheEmployeeAndTheLeaveRequest()
    {
        var (employee, request) = await AMaternityRequestAsync("Reyes");
        await Sut.AddAsync(AClaim(employee, request));

        await using var reader = NewContext();
        var claims = await new MaternityClaimRepository(reader).GetAllAsync();

        var claim = claims.Should().ContainSingle().Subject;
        claim.Employee.LastName.Should().Be("Reyes");
        claim.LeaveRequest.Id.Should().Be(request.Id);
    }

    [Fact]
    public async Task Update_SavesAChangedClaim()
    {
        var (employee, request) = await AMaternityRequestAsync();
        var claim = await Sut.AddAsync(AClaim(employee, request));

        claim.DailyAllowance = 900m;
        claim.Benefit = 94_500m;
        await Sut.UpdateAsync(claim);

        await using var reader = NewContext();
        var stored = await new MaternityClaimRepository(reader).GetByLeaveRequestAsync(request.Id);
        stored!.Benefit.Should().Be(94_500m);
        stored.DailyAllowance.Should().Be(900m);
    }

    [Fact]
    public async Task GetByLeaveRequest_ReturnsNullWhenThereIsNoClaim()
    {
        (await Sut.GetByLeaveRequestAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task AnEntry_RoundTripsTheAdvanceFlagAndTheAdvanceAndOffsetAmounts()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var run = ARun("PAY-2026-M03", new(2026, 8, 1), new(2026, 8, 15), new(2026, 8, 20));
        var entry = AnEntry(run.Id, employee.Id);
        entry.AdvanceMaternityBenefit = true;
        entry.MaternityBenefitAdvance = 105_057.75m;
        entry.MaternityBenefitOffset = 12_345.67m;
        entry.MaternityDifferential = 2_345.68m;
        run.Employees = [entry];
        await new PayrollRunRepository(Context).AddWithEntriesAsync(run);

        await using var reader = NewContext();
        var loaded = (await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id))!.Employees.Single();

        loaded.AdvanceMaternityBenefit.Should().BeTrue();
        loaded.MaternityBenefitAdvance.Should().Be(105_057.75m);
        loaded.MaternityBenefitOffset.Should().Be(12_345.67m);
        loaded.MaternityDifferential.Should().Be(2_345.68m);
    }

    [Fact]
    public async Task AnEntry_DefaultsToNoAdvanceAndNoOffset()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var run = ARun("PAY-2026-M04", new(2026, 8, 1), new(2026, 8, 15), new(2026, 8, 20));
        run.Employees = [AnEntry(run.Id, employee.Id)];
        await new PayrollRunRepository(Context).AddWithEntriesAsync(run);

        await using var reader = NewContext();
        var loaded = (await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id))!.Employees.Single();

        loaded.AdvanceMaternityBenefit.Should().BeFalse();
        loaded.MaternityBenefitAdvance.Should().Be(0m);
        loaded.MaternityBenefitOffset.Should().Be(0m);
        loaded.MaternityDifferential.Should().Be(0m);
    }

    [Fact]
    public async Task TheDifferentialExemption_RoundTripsAndDefaultsToFalse()
    {
        var company = ACompany();
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id });
        await Context.SaveChangesAsync();

        await using (var reader = NewContext())
            (await new PayrollSettingsRepository(reader).GetByCompanyIdAsync(company.Id))!
                .ExemptFromMaternityDifferential.Should().BeFalse();

        await using (var writer = NewContext())
        {
            var settings = (await new PayrollSettingsRepository(writer).GetByCompanyIdAsync(company.Id))!;
            settings.ExemptFromMaternityDifferential = true;
            await writer.SaveChangesAsync();
        }

        await using var again = NewContext();
        (await new PayrollSettingsRepository(again).GetByCompanyIdAsync(company.Id))!
            .ExemptFromMaternityDifferential.Should().BeTrue();
    }
}
