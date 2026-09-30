using System.Text;
using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// The opening-balance CSV: the template's header row, and an import that validates every row
/// first, refuses the whole file on any problem, and otherwise creates or updates each
/// (employee, year) in one save.
/// </summary>
public class OpeningBalanceImportTests
{
    private const string Header =
        "EmployeeNumber,Year,ThroughDate,BasicSalary,ThirteenthMonthPaid,OtherBenefitsPaid,OtherTaxablePay," +
        "DeMinimis,OtherNonTaxable,EmployeeContributions,TaxWithheld,DeMinimisLeaveDays";

    private readonly Mock<IPayrollOpeningBalanceRepository> _balances = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly PayrollOpeningBalanceService _sut;

    private static readonly Employee Maria = new() { EmployeeNumber = "E-001", FirstName = "Maria", LastName = "Santos" };
    private static readonly Employee Jose = new() { EmployeeNumber = "E-002", FirstName = "Jose", LastName = "Cruz" };

    private readonly List<PayrollOpeningBalance> _existing = [];
    private List<PayrollOpeningBalance>? _saved;
    private readonly List<PayrollRun> _paidRuns = [];

    public OpeningBalanceImportTests()
    {
        Employee[] everyone = [Maria, Jose];
        _employees.Setup(e => e.GetByNumbersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((IReadOnlyCollection<string> numbers, CancellationToken _) =>
                      everyone.Where(e => numbers.Contains(e.EmployeeNumber)).ToList());
        _balances.Setup(b => b.GetForEmployeesForUpdateAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((IReadOnlyCollection<Guid> ids, int year, CancellationToken _) =>
                     _existing.Where(b => b.Year == year && ids.Contains(b.EmployeeId)).ToList());
        _balances.Setup(b => b.SaveAllAsync(It.IsAny<IReadOnlyCollection<PayrollOpeningBalance>>(), It.IsAny<CancellationToken>()))
                 .Callback((IReadOnlyCollection<PayrollOpeningBalance> added, CancellationToken _) => _saved = added.ToList())
                 .Returns(Task.CompletedTask);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((int year, CancellationToken _) => _paidRuns.Where(r => r.PayDate.Year == year).ToList());
        _sut = new PayrollOpeningBalanceService(_balances.Object, _employees.Object, _runs.Object);
    }

    private static Stream Csv(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    private Task<OpeningBalanceImportResult> Import(params string[] lines) => _sut.ImportAsync(Csv(lines));

    private void NothingWasSaved() =>
        _balances.Verify(b => b.SaveAllAsync(It.IsAny<IReadOnlyCollection<PayrollOpeningBalance>>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ── The template ─────────────────────────────────────────────────────────

    [Fact]
    public void Template_IsTheHeaderRow_WithABomForExcel()
    {
        var bytes = OpeningBalanceCsv.Template();

        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Should().Be(Header + "\r\n");
    }

    [Fact]
    public void Template_IsNamedAndTypedForDownload()
    {
        OpeningBalanceCsv.TemplateFileName.Should().Be("opening-balances-template.csv");
        OpeningBalanceCsv.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public async Task Import_OfTheTemplateItself_HasNoRows()
    {
        var result = await _sut.ImportAsync(new MemoryStream(OpeningBalanceCsv.Template()));

        result.Errors.Should().Equal("The file has no rows.");
        NothingWasSaved();
    }

    // ── A valid file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_CreatesNewBalances_AndUpdatesExistingOnes_InOneSave()
    {
        var josesBalance = new PayrollOpeningBalance
        {
            EmployeeId = Jose.Id, Employee = Jose, Year = 2026, ThroughDate = new DateOnly(2026, 1, 31), BasicSalary = 1m,
        };
        _existing.Add(josesBalance);

        var result = await Import(
            Header,
            "E-001,2026,2026-03-31,150000,12500.50,1000,2000,3000,4000,6000.005,4500,2.5",
            "E-002,2026,2026-02-28,80000,,,,,,3200,1500,");

        result.Errors.Should().BeEmpty();
        result.Created.Should().Be(1);
        result.Updated.Should().Be(1);
        _balances.Verify(b => b.SaveAllAsync(It.IsAny<IReadOnlyCollection<PayrollOpeningBalance>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var marias = _saved.Should().ContainSingle().Subject;
        marias.EmployeeId.Should().Be(Maria.Id);
        marias.Year.Should().Be(2026);
        marias.ThroughDate.Should().Be(new DateOnly(2026, 3, 31));
        marias.BasicSalary.Should().Be(150_000m);
        marias.ThirteenthMonthPaid.Should().Be(12_500.50m);
        marias.OtherBenefitsPaid.Should().Be(1_000m);
        marias.OtherTaxablePay.Should().Be(2_000m);
        marias.DeMinimis.Should().Be(3_000m);
        marias.OtherNonTaxable.Should().Be(4_000m);
        marias.EmployeeContributions.Should().Be(6_000.01m, "figures are rounded to the centavo, like the form's");
        marias.TaxWithheld.Should().Be(4_500m);
        marias.DeMinimisLeaveDays.Should().Be(2.5m);

        // Updated in place: blank numeric cells are 0.
        josesBalance.ThroughDate.Should().Be(new DateOnly(2026, 2, 28));
        josesBalance.BasicSalary.Should().Be(80_000m);
        josesBalance.ThirteenthMonthPaid.Should().Be(0m);
        josesBalance.DeMinimisLeaveDays.Should().Be(0m);
        josesBalance.EmployeeContributions.Should().Be(3_200m);
        josesBalance.TaxWithheld.Should().Be(1_500m);
    }

    [Fact]
    public async Task Import_ReadsQuotedCells_TrimsThem_AndSkipsBlankLines()
    {
        var result = await Import(
            Header,
            "\" E-001 \",2026,2026-03-31,\"150000.00\",0,0,0,0,0,0,0,0",
            "",
            ",,,,,,,,,,,");

        result.Errors.Should().BeEmpty();
        result.Created.Should().Be(1);
        _saved.Should().ContainSingle().Which.EmployeeId.Should().Be(Maria.Id);
    }

    [Fact]
    public async Task Import_ToleratesTheHeadersCaseAndSpacing()
    {
        var result = await Import(
            " employeenumber ," + Header[(Header.IndexOf(',') + 1)..],
            "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_OfTheSameEmployeeForTwoYears_CreatesBoth()
    {
        var result = await Import(
            Header,
            "E-001,2025,2025-12-31,1,0,0,0,0,0,0,0,0",
            "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        result.Errors.Should().BeEmpty();
        result.Created.Should().Be(2);
        _saved!.Select(b => b.Year).Should().BeEquivalentTo(new[] { 2025, 2026 });
    }

    // ── An invalid file ──────────────────────────────────────────────────────

    [Fact]
    public async Task Import_WithProblems_SavesNothing_AndListsEveryRowsProblems()
    {
        var result = await Import(
            Header,
            "E-001,2026,2026-03-31,150000,0,0,0,0,0,6000,4500,0",      // row 2: fine
            "E-999,2026,2026-03-31,150000,0,0,0,0,0,6000,4500,0",      // row 3: nobody
            "E-002,2025,03/31/2025,1 000,0,0,0,0,0,abc,4500,0",        // row 4: date and two numbers
            "E-002,2026,2025-03-31,100,0,0,0,0,0,200,0,0",             // row 5: validation
            "E-001,2026,2026-04-30,150000,0,0,0,0,0,6000,4500,0",      // row 6: a duplicate of row 2
            ",2026,2026-03-31,0,0,0,0,0,0,0,0,0",                      // row 7: no employee number
            "E-002,,2026-03-31,0,0,0,0,0,0,0,0,0",                     // row 8: no year
            "E-002,twenty,2026-03-31,0,0,0,0,0,0,0,0,0",               // row 9: a year that isn't a number
            "E-002,2027,2027-03-31,-5,0,0,0,0,0,0,0,0",                // row 10: negative
            "E-002,2028,2028-03-31,100,0,0,0,0,0,200,0,0",             // row 11: contributions over the basic
            "E-002,2029,2029-03-31,0,0,0,0,0,0,0,0,11",                // row 12: leave days
            "E-002,2030,2030-03-31,10000000000,0,0,0,0,0,0,0,0",       // row 13: the cap
            "E-002,2031,2031-03-31,\"1,000.00\",0,0,0,0,0,0,0,0",      // row 14: a thousands separator
            "E-002,2032,2032-03-31,1,000.00,0,0,0,0,0,0,0,0");         // row 15: an unquoted one splits the cell

        result.Errors.Should().Equal(
            "Row 3: Unknown employee number E-999.",
            "Row 4: Enter a date as yyyy-MM-dd.",
            "Row 4: Enter BasicSalary as a number.",
            "Row 4: Enter EmployeeContributions as a number.",
            "Row 5: The through date must fall in 2026.",
            "Row 6: E-001 appears more than once for 2026.",
            "Row 7: Enter an employee number.",
            "Row 8: Enter a year.",
            "Row 9: Enter Year as a number.",
            "Row 10: Amounts can't be negative.",
            "Row 11: Contributions can't be more than the basic salary.",
            "Row 12: De minimis leave days must be between 0 and 10.",
            "Row 13: Enter an amount below ₱10,000,000,000.",
            "Row 14: Enter BasicSalary as a number.",
            "Row 15: The row has 13 cells; the header has 12.");
        result.Created.Should().Be(0);
        result.Updated.Should().Be(0);
        NothingWasSaved();
    }

    [Fact]
    public async Task Import_WithAProblem_DoesNotChangeAnExistingBalance()
    {
        var josesBalance = new PayrollOpeningBalance
        {
            EmployeeId = Jose.Id, Employee = Jose, Year = 2026, ThroughDate = new DateOnly(2026, 1, 31), BasicSalary = 1m,
        };
        _existing.Add(josesBalance);

        var result = await Import(
            Header,
            "E-002,2026,2026-02-28,80000,0,0,0,0,0,0,0,0",
            "E-999,2026,2026-02-28,80000,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal("Row 3: Unknown employee number E-999.");
        josesBalance.BasicSalary.Should().Be(1m);
        josesBalance.ThroughDate.Should().Be(new DateOnly(2026, 1, 31));
        NothingWasSaved();
    }

    [Fact]
    public async Task Import_CountsRowsFromTheHeader_IncludingBlankLines()
    {
        var result = await Import(
            Header,
            "",
            "E-999,2026,2026-03-31,0,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal("Row 3: Unknown employee number E-999.");
    }

    [Fact]
    public async Task Import_WithAShortRow_ReadsTheMissingCellsAsBlank()
    {
        var result = await Import(Header, "E-001,2026,2026-03-31,1000");

        result.Errors.Should().BeEmpty();
        _saved.Should().ContainSingle().Which.BasicSalary.Should().Be(1_000m);
    }

    [Theory]
    [InlineData("EmployeeNumber,Year,ThroughDate")]
    [InlineData("E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0")]
    [InlineData("")]
    public async Task Import_WithoutTheTemplatesHeader_IsRefused(string firstLine)
    {
        var result = await Import(firstLine, "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal("The first row must be the template's header.");
        NothingWasSaved();
    }

    [Fact]
    public async Task Import_OfAnEmptyFile_HasNoRows()
    {
        var result = await _sut.ImportAsync(new MemoryStream());

        result.Errors.Should().Equal("The file has no rows.");
        NothingWasSaved();
    }

    [Fact]
    public async Task Import_OfOnlyTheHeaderAndBlankLines_HasNoRows()
    {
        var result = await Import(Header, "", ",,,,,,,,,,,");

        result.Errors.Should().Equal("The file has no rows.");
        NothingWasSaved();
    }

    // ── Echoed values are neutralised ────────────────────────────────────────

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+1", "'+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-cmd", "'-cmd")]
    public async Task Import_NeutralisesAnEchoedEmployeeNumber(string number, string echoed)
    {
        var cell = "\"" + number.Replace("\"", "\"\"") + "\"";

        var result = await Import(Header, $"{cell},2026,2026-03-31,0,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal($"Row 2: Unknown employee number {echoed}.");
    }

    [Fact]
    public async Task Import_NeutralisesTheEmployeeNumberInADuplicate()
    {
        var formulaEmployee = new Employee { EmployeeNumber = "=1+1", FirstName = "Ana", LastName = "Reyes" };
        _employees.Setup(e => e.GetByNumbersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<Employee> { formulaEmployee });

        var result = await Import(
            Header,
            "=1+1,2026,2026-03-31,0,0,0,0,0,0,0,0,0",
            "=1+1,2026,2026-03-31,0,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal("Row 3: '=1+1 appears more than once for 2026.");
    }

    [Fact]
    public async Task Import_TruncatesALongEchoedValue_ToFiftyCharacters()
    {
        var number = new string('X', 60);

        var result = await Import(Header, $"{number},2026,2026-03-31,0,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal($"Row 2: Unknown employee number {new string('X', 50)}….");
    }

    [Fact]
    public async Task Import_KeepsAnEchoedValueOfExactlyFiftyCharacters()
    {
        var number = new string('X', 50);

        var result = await Import(Header, $"{number},2026,2026-03-31,0,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal($"Row 2: Unknown employee number {number}.");
    }

    // ── The error list's size ────────────────────────────────────────────────

    private static string[] UnknownRows(int count) =>
        [Header, .. Enumerable.Range(1, count).Select(i => $"N-{i},2026,2026-03-31,0,0,0,0,0,0,0,0,0")];

    [Fact]
    public async Task Import_ListsAtMostTwoHundredProblems_ThenSaysHowManyMore()
    {
        var result = await Import(UnknownRows(250));

        result.Errors.Should().HaveCount(201);
        result.Errors[0].Should().Be("Row 2: Unknown employee number N-1.");
        result.Errors[199].Should().Be("Row 201: Unknown employee number N-200.");
        result.Errors[200].Should().Be("…and 50 more problems.");
        NothingWasSaved();
    }

    [Fact]
    public async Task Import_WithExactlyTwoHundredProblems_ListsThemAll()
    {
        var result = await Import(UnknownRows(200));

        result.Errors.Should().HaveCount(200);
        result.Errors[^1].Should().Be("Row 201: Unknown employee number N-200.");
    }

    [Fact]
    public async Task Import_WithOneProblemOverTwoHundred_SaysOneMoreProblem()
    {
        var result = await Import(UnknownRows(201));

        result.Errors.Should().HaveCount(201);
        result.Errors[^1].Should().Be("…and 1 more problem.");
    }

    // ── Quotes and line endings ──────────────────────────────────────────────

    [Fact]
    public async Task Import_TreatsAQuoteInsideAnUnquotedCell_AsAnOrdinaryCharacter()
    {
        // Were the quote to open a quoted section, it would swallow the rest of the row's commas.
        var result = await Import(Header, "E\"X,2026,2026-03-31,0,0,0,0,0,0,0,0,0", "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal("Row 2: Unknown employee number E\"X.");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public async Task Import_ReadsEachLineEnding(string newline)
    {
        var text = string.Join(newline, Header, "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0",
            "E-002,2026,2026-03-31,2,0,0,0,0,0,0,0,0") + newline;

        var result = await _sut.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        result.Errors.Should().BeEmpty();
        result.Created.Should().Be(2);
    }

    [Fact]
    public async Task Import_ReadsTheLastRow_WithoutAFinalLineBreak()
    {
        var text = Header + "\r\nE-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0\r\nE-002,2026,2026-03-31,2,0,0,0,0,0,0,0,0";

        var result = await _sut.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        result.Errors.Should().BeEmpty();
        result.Created.Should().Be(2);
        _saved!.Select(b => b.BasicSalary).Should().Equal(1m, 2m);
    }

    [Fact]
    public async Task Import_KeepsALineBreakInsideAQuotedCell_InOneRow()
    {
        var result = await Import(
            Header,
            "\"E-\n001\",2026,2026-03-31,0,0,0,0,0,0,0,0,0",
            "E-999,2026,2026-03-31,0,0,0,0,0,0,0,0,0");

        result.Errors.Should().Equal(
            "Row 2: Unknown employee number E-\n001.",
            "Row 3: Unknown employee number E-999.");
    }

    [Fact]
    public async Task Import_OfAFileWithABom_ReadsItsHeaderAndRows()
    {
        var text = Header + "\r\nE-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var result = await _sut.ImportAsync(new MemoryStream(bytes));

        result.Errors.Should().BeEmpty();
        result.Created.Should().Be(1);
    }

    // ── Warnings ─────────────────────────────────────────────────────────────

    private void APaidRun(string runNumber, DateOnly payDate, params Employee[] employees)
    {
        var run = new PayrollRun
        {
            RunNumber = runNumber, PeriodStart = payDate.AddDays(-19), PeriodEnd = payDate.AddDays(-5), PayDate = payDate,
            Status = PayrollRunStatus.Paid,
        };
        foreach (var employee in employees)
            run.Employees.Add(new PayrollRunEmployee { PayrollRunId = run.Id, EmployeeId = employee.Id });
        _paidRuns.Add(run);
    }

    [Fact]
    public async Task Import_WarnsOfPaidRunsThatUsedOrOverlapTheFigures_ForCreatedAndUpdatedBalances()
    {
        _existing.Add(new PayrollOpeningBalance
        {
            EmployeeId = Jose.Id, Employee = Jose, Year = 2026, ThroughDate = new DateOnly(2026, 1, 31),
        });
        APaidRun("PAY-2026-005", new DateOnly(2026, 3, 20), Maria, Jose);
        APaidRun("PAY-2026-007", new DateOnly(2026, 4, 20), Maria, Jose);
        APaidRun("PAY-2025-020", new DateOnly(2025, 12, 20), Maria);

        var result = await Import(
            Header,
            "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0",   // created: 005 overlaps, 007 used the figures
            "E-002,2026,2026-02-28,1,0,0,0,0,0,0,0,0");  // updated: both used the figures

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Equal(
            "E-001: Maria Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-005 was paid on Mar 20, 2026.",
            "E-001 Maria Santos: PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue her 2316 to pick up the change.",
            "E-002 Jose Cruz: PAY-2026-005 used these figures; its 13th month and tax won't change. Reissue her 2316 to pick up the change.",
            "E-002 Jose Cruz: PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue her 2316 to pick up the change.");
    }

    [Fact]
    public async Task Import_LoadsThePaidRunsOncePerYear()
    {
        await Import(
            Header,
            "E-001,2025,2025-12-31,1,0,0,0,0,0,0,0,0",
            "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0",
            "E-002,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        _runs.Verify(r => r.GetPaidRunsInYearAsync(2025, It.IsAny<CancellationToken>()), Times.Once);
        _runs.Verify(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Import_WithoutPaidRuns_HasNoWarnings()
    {
        var result = await Import(Header, "E-001,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_WithProblems_HasNoWarnings_AndLoadsNoRuns()
    {
        var result = await Import(Header, "E-999,2026,2026-03-31,1,0,0,0,0,0,0,0,0");

        result.Warnings.Should().BeEmpty();
        _runs.Verify(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
