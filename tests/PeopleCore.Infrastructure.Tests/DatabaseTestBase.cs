using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Tests;

[Collection(DatabaseCollection.Name)]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    protected DatabaseTestBase(PostgresFixture fixture) => _fixture = fixture;

    protected AppDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        Context = _fixture.CreateContext();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();

    /// <summary>
    /// A second context over the same database. Used to read back what a repository wrote without
    /// the first context's change tracker answering from memory instead of from SQL.
    /// </summary>
    protected AppDbContext NewContext() => _fixture.CreateContext();

    protected AppDbContext NewContext(ICurrentUserService? currentUser) => _fixture.CreateContext(currentUser);

    // ── Seed helpers ─────────────────────────────────────────────────────────
    // Only the columns the schema requires are set. EmployeeNumber is unique and WorkEmail is
    // required, so both are made distinct per call - two employees in one test would otherwise
    // collide on the unique index rather than failing on the behaviour under test.

    protected static Employee AnEmployee(string lastName = "Dela Cruz", string firstName = "Juan")
        => new()
        {
            EmployeeNumber = "E" + Guid.NewGuid().ToString("N")[..8],
            FirstName = firstName,
            LastName = lastName,
            WorkEmail = Guid.NewGuid().ToString("N") + "@example.com",
            HireDate = new DateOnly(2020, 1, 1),
        };

    protected static Company ACompany(string name = "PeopleCore Inc.")
        => new() { Name = name };

    protected static PayrollRun ARun(
        string runNumber,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly payDate,
        PayrollRunStatus status = PayrollRunStatus.Paid)
        => new()
        {
            RunNumber = runNumber,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = payDate,
            Frequency = PayFrequency.SemiMonthly,
            Status = status,
        };

    protected static PayrollRunEmployee AnEntry(Guid runId, Guid employeeId, decimal regularPay = 30_000m)
        => new()
        {
            PayrollRunId = runId,
            EmployeeId = employeeId,
            RegularPay = regularPay,
        };
}
