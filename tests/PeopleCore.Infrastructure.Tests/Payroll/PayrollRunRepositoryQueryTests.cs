using FluentAssertions;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollRunRepositoryQueryTests : DatabaseTestBase
{
    public PayrollRunRepositoryQueryTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(Context);

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_AttributesIncomeByPayDateNotPeriodStart()
    {
        // The 2316's central rule, and this design's deliberate departure from the PayZen source:
        // BIR taxes compensation in the year it is PAID. A 26 December to 10 January period paid
        // on 10 January is 2027 income, not 2026 income. Until now this was asserted only against
        // mocks, which returned whatever the test handed them - so the SQL translation of
        // PayDate.Year was never exercised at all.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var decemberPeriodPaidInJanuary = ARun(
            "PAY-2026-024",
            periodStart: new DateOnly(2026, 12, 26),
            periodEnd: new DateOnly(2027, 1, 10),
            payDate: new DateOnly(2027, 1, 10));
        var novemberRun = ARun(
            "PAY-2026-022",
            periodStart: new DateOnly(2026, 11, 1),
            periodEnd: new DateOnly(2026, 11, 15),
            payDate: new DateOnly(2026, 11, 20));

        Context.PayrollRuns.AddRange(decemberPeriodPaidInJanuary, novemberRun);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(decemberPeriodPaidInJanuary.Id, employee.Id),
            AnEntry(novemberRun.Id, employee.Id));
        await Context.SaveChangesAsync();

        var in2026 = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2026);
        var in2027 = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2027);

        in2026.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-022");
        in2027.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-024");
    }

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_ExcludesRunsThatAreNotPaid()
    {
        // A computed-but-unapproved run is not income yet, and ComputeAsync resets a run to Draft
        // on every recompute - so including one would let a tax certificate move after issue.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var paid = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var draft = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20),
            status: PayrollRunStatus.Draft);
        var approved = ARun("PAY-2026-003", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20),
            status: PayrollRunStatus.Approved);

        Context.PayrollRuns.AddRange(paid, draft, approved);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(paid.Id, employee.Id),
            AnEntry(draft.Id, employee.Id),
            AnEntry(approved.Id, employee.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2026);

        result.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-001");
    }

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_ExcludesAnotherEmployeesRuns()
    {
        var mine = AnEmployee(lastName: "Santos");
        var theirs = AnEmployee(lastName: "Reyes");
        Context.Employees.AddRange(mine, theirs);

        var myRun = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var theirRun = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));

        Context.PayrollRuns.AddRange(myRun, theirRun);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(myRun.Id, mine.Id),
            AnEntry(theirRun.Id, theirs.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsForEmployeeInYearAsync(mine.Id, 2026);

        result.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-001");
    }

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_IsOrderedByPayDate()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var later = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));
        var earlier = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));

        Context.PayrollRuns.AddRange(later, earlier);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(later.Id, employee.Id),
            AnEntry(earlier.Id, employee.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2026);

        result.Select(r => r.RunNumber).Should().ContainInOrder("PAY-2026-001", "PAY-2026-002");
    }

    [Fact]
    public async Task CountForYear_CountsByPeriodStartNotPayDate()
    {
        // CountForYearAsync is deliberately different from the queries above: it numbers runs
        // (PAY-{year}-{sequence}), which follows the period, not the pay date. Pinning the
        // difference stops someone "fixing" the inconsistency and renumbering every run.
        var straddling = ARun("PAY-2026-024", new(2026, 12, 26), new(2027, 1, 10), new(2027, 1, 10));
        var ordinary = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));

        Context.PayrollRuns.AddRange(straddling, ordinary);
        await Context.SaveChangesAsync();

        (await Sut.CountForYearAsync(2026)).Should().Be(2);
        (await Sut.CountForYearAsync(2027)).Should().Be(0);
    }

    [Fact]
    public async Task GetPaidYearsForEmployee_IsDistinctAndDescending()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var a2026 = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var b2026 = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));
        var a2027 = ARun("PAY-2027-001", new(2027, 1, 1), new(2027, 1, 15), new(2027, 1, 20));

        Context.PayrollRuns.AddRange(a2026, b2026, a2027);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(a2026.Id, employee.Id),
            AnEntry(b2026.Id, employee.Id),
            AnEntry(a2027.Id, employee.Id));
        await Context.SaveChangesAsync();

        var years = await Sut.GetPaidYearsForEmployeeAsync(employee.Id);

        years.Should().Equal(2027, 2026);
    }

    [Fact]
    public async Task GetEmployeeIdsWithPaidRunsInYear_DeduplicatesAndOrdersByName()
    {
        // SelectMany into entries, Distinct over an anonymous type carrying the employee's name,
        // order by last then first, then project the id back out. A non-obvious translation, and
        // an employee in two paid runs must appear once.
        var santos = AnEmployee(lastName: "Santos", firstName: "Ana");
        var cruz = AnEmployee(lastName: "Cruz", firstName: "Bea");
        Context.Employees.AddRange(santos, cruz);

        var january = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var february = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));

        Context.PayrollRuns.AddRange(january, february);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(january.Id, santos.Id),
            AnEntry(january.Id, cruz.Id),
            AnEntry(february.Id, santos.Id));
        await Context.SaveChangesAsync();

        var ids = await Sut.GetEmployeeIdsWithPaidRunsInYearAsync(2026);

        ids.Should().Equal(cruz.Id, santos.Id);
    }

    [Fact]
    public async Task GetWithEntries_LoadsBothIncludeChains()
    {
        // AsSplitQuery issues several round trips; this proves entries arrive with their employee
        // AND their loan deduction lines, not just whichever chain the last query happened to run.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);

        var entry = AnEntry(run.Id, employee.Id);
        Context.PayrollRunEmployees.Add(entry);
        Context.Set<PayrollLoanDeduction>().Add(new()
        {
            PayrollRunEmployeeId = entry.Id,
            EmployeeLoanId = Guid.NewGuid(),
            LoanType = "SSSLoan",
            Amount = 1_500m,
        });
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var loaded = await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id);

        loaded.Should().NotBeNull();
        var loadedEntry = loaded!.Employees.Should().ContainSingle().Subject;
        loadedEntry.Employee.Should().NotBeNull();
        loadedEntry.Employee!.LastName.Should().Be("Dela Cruz");
        loadedEntry.LoanDeductionLines.Should().ContainSingle().Which.Amount.Should().Be(1_500m);
    }

    [Fact]
    public async Task GetPaged_PagesAndReportsTheUnpagedTotal()
    {
        for (var i = 1; i <= 5; i++)
        {
            Context.PayrollRuns.Add(ARun(
                $"PAY-2026-{i:D3}", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20)));
            await Context.SaveChangesAsync();
        }

        var (items, total) = await Sut.GetPagedAsync(page: 2, pageSize: 2);

        total.Should().Be(5, "the count must ignore paging");
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPaidRunsInYear_ReturnsEveryEmployeesPaidRunsOrderedByPayDate()
    {
        // The company-wide counterpart of GetPaidRunsForEmployeeInYearAsync, used by the
        // generate-all path. It takes no employee id, so it is one of the global queries that
        // makes per-test truncation mandatory rather than tidy.
        var santos = AnEmployee(lastName: "Santos");
        var reyes = AnEmployee(lastName: "Reyes");
        Context.Employees.AddRange(santos, reyes);

        var february = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));
        var january = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var draft = ARun("PAY-2026-003", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20),
            status: PayrollRunStatus.Draft);
        var nextYear = ARun("PAY-2027-001", new(2027, 1, 1), new(2027, 1, 15), new(2027, 1, 20));

        Context.PayrollRuns.AddRange(february, january, draft, nextYear);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(february.Id, santos.Id),
            AnEntry(january.Id, reyes.Id),
            AnEntry(draft.Id, santos.Id),
            AnEntry(nextYear.Id, santos.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsInYearAsync(2026);

        result.Select(r => r.RunNumber).Should().Equal("PAY-2026-001", "PAY-2026-002");
    }

    [Fact]
    public async Task GetRunsForEmployee_IsOrderedByPayDateDescending()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var earlier = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var later = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));

        Context.PayrollRuns.AddRange(earlier, later);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(earlier.Id, employee.Id),
            AnEntry(later.Id, employee.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetRunsForEmployeeAsync(employee.Id);

        result.Select(r => r.RunNumber).Should().ContainInOrder("PAY-2026-002", "PAY-2026-001");
    }
}
