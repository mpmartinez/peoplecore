using FluentAssertions;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class EmployeeCompensationRepositoryTests : DatabaseTestBase
{
    public EmployeeCompensationRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private EmployeeCompensationRepository Sut => new(Context);

    private static EmployeeCompensation ACompensation(Guid employeeId, decimal basicSalary)
        => new()
        {
            EmployeeId = employeeId,
            BasicSalary = basicSalary,
            PayFrequency = PayFrequency.SemiMonthly,
            TaxCode = "ME",
        };

    [Fact]
    public async Task ANumericColumnSilentlyDropsAThirdDecimalPlace()
    {
        // The premise the validation phase relied on when it started rejecting a third decimal:
        // the column is numeric(18,2), so the stored salary would otherwise differ from the
        // submitted one with nothing reporting it. Asserted here for the first time.
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await Sut.AddAsync(ACompensation(employee.Id, 30_000.567m));

        await using var reader = NewContext();
        var stored = await new EmployeeCompensationRepository(reader).GetByEmployeeIdAsync(employee.Id);

        stored!.BasicSalary.Should().Be(30_000.57m, "numeric(18,2) rounds to two places on save");
    }

    [Fact]
    public async Task ATwoDecimalSalaryRoundTripsExactly()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await Sut.AddAsync(ACompensation(employee.Id, 30_000.12m));

        await using var reader = NewContext();
        var stored = await new EmployeeCompensationRepository(reader).GetByEmployeeIdAsync(employee.Id);

        stored!.BasicSalary.Should().Be(30_000.12m);
    }

    [Fact]
    public async Task GetByEmployeeId_ReturnsNullWhenThereIsNoCompensationRow()
    {
        (await Sut.GetByEmployeeIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetByEmployeeIds_ReturnsOnlyTheRequestedEmployees()
    {
        var wanted = AnEmployee(lastName: "Santos");
        var other = AnEmployee(lastName: "Reyes");
        Context.Employees.AddRange(wanted, other);
        await Context.SaveChangesAsync();

        await Sut.AddAsync(ACompensation(wanted.Id, 30_000m));
        await Sut.AddAsync(ACompensation(other.Id, 40_000m));

        var result = await Sut.GetByEmployeeIdsAsync([wanted.Id]);

        result.Should().ContainSingle().Which.EmployeeId.Should().Be(wanted.Id);
    }
}
