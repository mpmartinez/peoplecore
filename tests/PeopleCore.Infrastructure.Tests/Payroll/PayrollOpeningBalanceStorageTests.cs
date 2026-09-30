using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// Storage for payroll opening balances: the table round-trips every figure, the unique
/// (employee, year) index is real, and the repository's reads load what they promise.
/// </summary>
public class PayrollOpeningBalanceStorageTests : DatabaseTestBase
{
    public PayrollOpeningBalanceStorageTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollOpeningBalanceRepository Sut => new(Context);

    private async Task<Employee> AnEmployeeAsync(string lastName = "Santos", string firstName = "Maria")
    {
        var employee = AnEmployee(lastName, firstName);
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        return employee;
    }

    private static PayrollOpeningBalance ABalance(Employee employee, int year = 2026) => new()
    {
        EmployeeId = employee.Id,
        Year = year,
        ThroughDate = new DateOnly(year, 3, 31),
    };

    [Fact]
    public async Task ABalance_RoundTripsEveryField()
    {
        var employee = await AnEmployeeAsync();
        var balance = ABalance(employee);
        balance.BasicSalary = 150_000.25m;
        balance.ThirteenthMonthPaid = 1_000.01m;
        balance.OtherBenefitsPaid = 2_000.02m;
        balance.OtherTaxablePay = 3_000.03m;
        balance.DeMinimis = 4_000.04m;
        balance.OtherNonTaxable = 5_000.05m;
        balance.EmployeeContributions = 6_000.06m;
        balance.TaxWithheld = 7_000.07m;
        balance.DeMinimisLeaveDays = 2.5m;
        await Sut.AddNewAsync(balance);

        await using var reader = NewContext();
        var stored = await new PayrollOpeningBalanceRepository(reader).GetAsync(employee.Id, 2026);

        stored.Should().NotBeNull();
        stored!.Id.Should().Be(balance.Id);
        stored.ThroughDate.Should().Be(new DateOnly(2026, 3, 31));
        stored.BasicSalary.Should().Be(150_000.25m);
        stored.ThirteenthMonthPaid.Should().Be(1_000.01m);
        stored.OtherBenefitsPaid.Should().Be(2_000.02m);
        stored.OtherTaxablePay.Should().Be(3_000.03m);
        stored.DeMinimis.Should().Be(4_000.04m);
        stored.OtherNonTaxable.Should().Be(5_000.05m);
        stored.EmployeeContributions.Should().Be(6_000.06m);
        stored.TaxWithheld.Should().Be(7_000.07m);
        stored.DeMinimisLeaveDays.Should().Be(2.5m);
        stored.Employee.LastName.Should().Be("Santos");
    }

    [Fact]
    public async Task TheMoneyColumns_DefaultToZero()
    {
        var employee = await AnEmployeeAsync();

        await Context.Database.ExecuteSqlAsync(
            $"insert into payroll_opening_balances (id, employee_id, year, through_date, created_at, updated_at) values ({Guid.NewGuid()}, {employee.Id}, {2026}, {new DateOnly(2026, 3, 31)}, {DateTime.UtcNow}, {DateTime.UtcNow})");

        await using var reader = NewContext();
        var stored = await new PayrollOpeningBalanceRepository(reader).GetAsync(employee.Id, 2026);
        stored!.BasicSalary.Should().Be(0m);
        stored.TaxWithheld.Should().Be(0m);
        stored.DeMinimisLeaveDays.Should().Be(0m);
    }

    [Fact]
    public async Task ASecondBalanceForTheSameEmployeeAndYear_IsRefusedByTheDatabase()
    {
        var employee = await AnEmployeeAsync();
        await Sut.AddAsync(ABalance(employee));

        var act = async () => await new PayrollOpeningBalanceRepository(NewContext()).AddAsync(ABalance(employee));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AddNew_ASecondBalanceForTheSameEmployeeAndYear_SaysSheAlreadyHasOne()
    {
        var employee = await AnEmployeeAsync();
        await Sut.AddNewAsync(ABalance(employee));

        await using var other = NewContext();
        var repository = new PayrollOpeningBalanceRepository(other);
        var act = async () => await repository.AddNewAsync(ABalance(employee));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Santos already has an opening balance for 2026.");
        // The refused insert is not retried by the next save on that context.
        await other.SaveChangesAsync();
    }

    [Fact]
    public async Task TheSameEmployee_CanHaveABalanceForAnotherYear()
    {
        var employee = await AnEmployeeAsync();
        await Sut.AddNewAsync(ABalance(employee, 2025));
        await Sut.AddNewAsync(ABalance(employee, 2026));

        (await NewContext().Set<PayrollOpeningBalance>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task DeletingAnEmployeeWithABalance_IsRestricted()
    {
        var employee = await AnEmployeeAsync();
        await Sut.AddNewAsync(ABalance(employee));

        await using var writer = NewContext();
        writer.Employees.Remove(await writer.Employees.SingleAsync(e => e.Id == employee.Id));
        var act = async () => await writer.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetForYear_ReturnsThatYearsBalances_WithTheEmployee_ByName()
    {
        var santos = await AnEmployeeAsync("Santos");
        var cruz = await AnEmployeeAsync("Cruz", "Jose");
        await Sut.AddNewAsync(ABalance(santos));
        await Sut.AddNewAsync(ABalance(cruz));
        await Sut.AddNewAsync(ABalance(santos, 2025));

        await using var reader = NewContext();
        var balances = await new PayrollOpeningBalanceRepository(reader).GetForYearAsync(2026);

        balances.Select(b => b.Employee.LastName).Should().Equal("Cruz", "Santos");
        balances.Should().OnlyContain(b => b.Year == 2026);
    }

    [Fact]
    public async Task GetForEmployees_ReturnsOnlyTheirBalancesForTheYear()
    {
        var santos = await AnEmployeeAsync("Santos");
        var cruz = await AnEmployeeAsync("Cruz", "Jose");
        var reyes = await AnEmployeeAsync("Reyes", "Ana");
        await Sut.AddNewAsync(ABalance(santos));
        await Sut.AddNewAsync(ABalance(cruz));
        await Sut.AddNewAsync(ABalance(reyes));
        await Sut.AddNewAsync(ABalance(santos, 2025));

        await using var reader = NewContext();
        var balances = await new PayrollOpeningBalanceRepository(reader).GetForEmployeesAsync([santos.Id, cruz.Id], 2026);

        balances.Select(b => b.EmployeeId).Should().BeEquivalentTo([santos.Id, cruz.Id]);
        balances.Should().OnlyContain(b => b.Year == 2026);
    }

    [Fact]
    public async Task GetForEmployees_WithNoEmployees_IsEmpty()
    {
        (await Sut.GetForEmployeesAsync([], 2026)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_LoadsTheEmployee()
    {
        var employee = await AnEmployeeAsync();
        var balance = ABalance(employee);
        await Sut.AddNewAsync(balance);

        await using var reader = NewContext();
        var stored = await new PayrollOpeningBalanceRepository(reader).GetByIdAsync(balance.Id);

        stored!.Employee.LastName.Should().Be("Santos");
    }

    [Fact]
    public async Task Saving_StampsWhoChangedItAndWhen()
    {
        var employee = await AnEmployeeAsync();
        var balance = ABalance(employee);
        await Sut.AddNewAsync(balance);

        await using var reader = NewContext();
        var stored = await reader.Set<PayrollOpeningBalance>().SingleAsync();
        stored.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        stored.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task TheServiceOverPostgres_CreatesUpdatesAndDeletes_WithTheWarnings()
    {
        var employee = await AnEmployeeAsync();
        var before = ARun("PAY-2026-005", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20));
        var after = ARun("PAY-2026-007", new(2026, 4, 1), new(2026, 4, 15), new(2026, 4, 20));
        Context.PayrollRuns.AddRange(before, after);
        Context.PayrollRunEmployees.AddRange(AnEntry(before.Id, employee.Id), AnEntry(after.Id, employee.Id));
        await Context.SaveChangesAsync();

        var created = await Service(NewContext()).CreateAsync(new OpeningBalanceRequest(
            employee.Id, 2026, new DateOnly(2026, 3, 31), 150_000m, 0m, 0m, 0m, 0m, 0m, 6_000m, 4_500m, 0m));

        created.EmployeeName.Should().Be("Maria Santos");
        created.EmployeeNumber.Should().Be(employee.EmployeeNumber);
        created.Warnings.Should().Equal(
            "Maria Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-005 was paid on Mar 20, 2026.",
            "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.");

        var updated = await Service(NewContext()).UpdateAsync(created.Id, new OpeningBalanceRequest(
            employee.Id, 2026, new DateOnly(2026, 2, 28), 100_000m, 0m, 0m, 0m, 0m, 0m, 4_000m, 3_000m, 1m));

        updated.BasicSalary.Should().Be(100_000m);
        updated.Warnings.Should().Equal(
            "PAY-2026-005 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.",
            "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.");

        var listed = await Service(NewContext()).ListAsync(2026);
        listed.Should().ContainSingle().Which.ThroughDate.Should().Be(new DateOnly(2026, 2, 28));
        listed[0].Warnings.Should().BeEmpty();

        await Service(NewContext()).DeleteAsync(created.Id);
        (await Service(NewContext()).ListAsync(2026)).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAll_AddsTheNewBalances_AndSavesTheLoadedOnesChanges_Together()
    {
        var maria = await AnEmployeeAsync();
        var jose = await AnEmployeeAsync("Cruz", "Jose");
        await Sut.AddNewAsync(ABalance(jose));

        await using (var writer = NewContext())
        {
            var repository = new PayrollOpeningBalanceRepository(writer);
            var loaded = (await repository.GetForEmployeesForUpdateAsync([jose.Id], 2026)).Single();
            loaded.BasicSalary = 80_000m;
            await repository.SaveAllAsync([ABalance(maria)]);
        }

        await using var reader = NewContext();
        var stored = await reader.Set<PayrollOpeningBalance>().ToListAsync();
        stored.Should().HaveCount(2);
        stored.Single(b => b.EmployeeId == jose.Id).BasicSalary.Should().Be(80_000m);
        stored.Single(b => b.EmployeeId == maria.Id).Year.Should().Be(2026);
    }

    [Fact]
    public async Task SaveAll_WhenARivalAddedOneOfTheBalances_SavesNone_AndSaysToImportAgain()
    {
        var maria = await AnEmployeeAsync();
        var jose = await AnEmployeeAsync("Cruz", "Jose");
        await Sut.AddNewAsync(ABalance(maria));

        await using var other = NewContext();
        var repository = new PayrollOpeningBalanceRepository(other);
        var act = async () => await repository.SaveAllAsync([ABalance(jose), ABalance(maria)]);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Someone else added an opening balance for an employee in this file. Import it again.");
        (await NewContext().Set<PayrollOpeningBalance>().CountAsync()).Should().Be(1);
        // The refused inserts are not retried by the next save on that context.
        await other.SaveChangesAsync();
        (await NewContext().Set<PayrollOpeningBalance>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ForUpdate_LoadsTheEmployeesBalancesForTheYear_Tracked()
    {
        var maria = await AnEmployeeAsync();
        var jose = await AnEmployeeAsync("Cruz", "Jose");
        await Sut.AddNewAsync(ABalance(maria));
        await Sut.AddNewAsync(ABalance(maria, 2025));
        await Sut.AddNewAsync(ABalance(jose));

        await using var reader = NewContext();
        reader.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var loaded = await new PayrollOpeningBalanceRepository(reader).GetForEmployeesForUpdateAsync([maria.Id], 2026);

        var balance = loaded.Should().ContainSingle().Subject;
        balance.EmployeeId.Should().Be(maria.Id);
        balance.Year.Should().Be(2026);
        reader.Entry(balance).State.Should().Be(EntityState.Unchanged, "the import saves its changes to what it loaded");
        (await Sut.GetForEmployeesForUpdateAsync([], 2026)).Should().BeEmpty();
    }

    [Fact]
    public async Task EmployeesByNumber_AreFoundExactly()
    {
        var maria = await AnEmployeeAsync();
        var jose = await AnEmployeeAsync("Cruz", "Jose");
        await AnEmployeeAsync("Reyes", "Ana");

        var found = await new EmployeeRepository(NewContext())
            .GetByNumbersAsync([maria.EmployeeNumber, jose.EmployeeNumber.ToLowerInvariant(), "nobody"]);

        found.Select(e => e.Id).Should().Equal(maria.Id);
        (await new EmployeeRepository(NewContext()).GetByNumbersAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task TheImportOverPostgres_CreatesAndUpdates_OrSavesNothing()
    {
        var maria = await AnEmployeeAsync();
        var jose = await AnEmployeeAsync("Cruz", "Jose");
        await Sut.AddNewAsync(ABalance(jose));

        static Stream Csv(params string[] rows) => new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n",
            ["EmployeeNumber,Year,ThroughDate,BasicSalary,ThirteenthMonthPaid,OtherBenefitsPaid,OtherTaxablePay," +
             "DeMinimis,OtherNonTaxable,EmployeeContributions,TaxWithheld,DeMinimisLeaveDays", .. rows])));

        var refused = await Service(NewContext()).ImportAsync(Csv(
            $"{maria.EmployeeNumber},2026,2026-03-31,150000,0,0,0,0,0,6000,4500,0",
            $"{jose.EmployeeNumber},2026,2026-02-28,80000,0,0,0,0,0,90000,0,0"));

        refused.Errors.Should().Equal("Row 3: Contributions can't be more than the basic salary.");
        (await NewContext().Set<PayrollOpeningBalance>().CountAsync()).Should().Be(1);

        var imported = await Service(NewContext()).ImportAsync(Csv(
            $"{maria.EmployeeNumber},2026,2026-03-31,150000,0,0,0,0,0,6000,4500,0",
            $"{jose.EmployeeNumber},2026,2026-02-28,80000,0,0,0,0,0,3200,1500,1"));

        imported.Errors.Should().BeEmpty();
        imported.Created.Should().Be(1);
        imported.Updated.Should().Be(1);
        var listed = await Service(NewContext()).ListAsync(2026);
        listed.Select(b => (b.EmployeeId, b.BasicSalary, b.TaxWithheld)).Should().BeEquivalentTo(new[]
        {
            (maria.Id, 150_000m, 4_500m),
            (jose.Id, 80_000m, 1_500m),
        });
    }

    private static PayrollOpeningBalanceService Service(AppDbContext context) => new(
        new PayrollOpeningBalanceRepository(context), new EmployeeRepository(context), new PayrollRunRepository(context));
}
