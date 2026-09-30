using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollOpeningBalanceServiceTests
{
    private readonly Mock<IPayrollOpeningBalanceRepository> _balances = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly PayrollOpeningBalanceService _sut;

    private static readonly Employee Maria = new()
    {
        EmployeeNumber = "E-001", FirstName = "Maria", MiddleName = "Reyes", LastName = "Santos"
    };

    private static readonly Employee Jose = new() { EmployeeNumber = "E-002", FirstName = "Jose", LastName = "Cruz" };

    private readonly List<PayrollRun> _paidRuns = [];

    public PayrollOpeningBalanceServiceTests()
    {
        _employees.Setup(e => e.GetByIdAsync(Maria.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Maria);
        _employees.Setup(e => e.GetByIdAsync(Jose.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Jose);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((int year, CancellationToken _) => _paidRuns.Where(r => r.PayDate.Year == year).ToList());
        _balances.Setup(b => b.AddNewAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((PayrollOpeningBalance b, CancellationToken _) => b);
        _sut = new PayrollOpeningBalanceService(_balances.Object, _employees.Object, _runs.Object);
    }

    private static OpeningBalanceRequest ARequest(Employee? employee = null, int year = 2026, DateOnly? through = null,
        decimal basic = 150_000m, decimal thirteenth = 0m, decimal otherBenefits = 0m, decimal otherTaxable = 0m,
        decimal deMinimis = 0m, decimal otherNonTaxable = 0m, decimal contributions = 0m, decimal tax = 0m,
        decimal leaveDays = 0m)
        => new((employee ?? Maria).Id, year, through ?? new DateOnly(2026, 3, 31), basic, thirteenth, otherBenefits,
            otherTaxable, deMinimis, otherNonTaxable, contributions, tax, leaveDays);

    private PayrollOpeningBalance ABalance(Employee? employee = null, int year = 2026, DateOnly? through = null)
    {
        var who = employee ?? Maria;
        var balance = new PayrollOpeningBalance
        {
            EmployeeId = who.Id, Employee = who, Year = year, ThroughDate = through ?? new DateOnly(2026, 3, 31),
            BasicSalary = 150_000m, TaxWithheld = 4_500m,
        };
        _balances.Setup(b => b.GetByIdAsync(balance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        return balance;
    }

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

    // ── Validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Create_WithoutAYear_IsRefused(int year)
    {
        var act = () => _sut.CreateAsync(ARequest(year: year));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter a year.");
    }

    [Theory]
    [InlineData(2025, 12, 31)]
    [InlineData(2027, 1, 1)]
    public async Task Create_WithAThroughDateOutsideTheYear_IsRefused(int y, int m, int d)
    {
        var act = () => _sut.CreateAsync(ARequest(through: new DateOnly(y, m, d)));

        await act.Should().ThrowAsync<DomainException>().WithMessage("The through date must fall in 2026.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Create_WithANegativeAmount_IsRefused(int which)
    {
        var amounts = new decimal[8];
        amounts[which] = -0.01m;
        var request = new OpeningBalanceRequest(Maria.Id, 2026, new DateOnly(2026, 3, 31),
            amounts[0], amounts[1], amounts[2], amounts[3], amounts[4], amounts[5], amounts[6], amounts[7], 0m);

        var act = () => _sut.CreateAsync(request);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Amounts can't be negative.");
    }

    [Fact]
    public async Task Create_WithContributionsOverTheBasicSalary_IsRefused()
    {
        var act = () => _sut.CreateAsync(ARequest(basic: 6_000m, contributions: 6_000.01m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Contributions can't be more than the basic salary.");
        _balances.Verify(b => b.AddNewAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithContributionsEqualToTheBasicSalary_IsAccepted()
    {
        var dto = await _sut.CreateAsync(ARequest(basic: 6_000m, contributions: 6_000m));

        dto.EmployeeContributions.Should().Be(6_000m);
    }

    [Fact]
    public async Task Update_WithContributionsOverTheBasicSalary_IsRefused()
    {
        var balance = ABalance();

        var act = () => _sut.UpdateAsync(balance.Id, ARequest(basic: 1_000m, contributions: 2_000m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Contributions can't be more than the basic salary.");
        _balances.Verify(b => b.UpdateAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(10.01)]
    public async Task Create_WithLeaveDaysOutsideZeroToTen_IsRefused(decimal days)
    {
        var act = () => _sut.CreateAsync(ARequest(leaveDays: days));

        await act.Should().ThrowAsync<DomainException>().WithMessage("De minimis leave days must be between 0 and 10.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Create_WithLeaveDaysAtTheLimits_IsAccepted(decimal days)
    {
        var dto = await _sut.CreateAsync(ARequest(leaveDays: days));

        dto.DeMinimisLeaveDays.Should().Be(days);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Create_WithAnAmountOfTenBillionOrMore_IsRefused(int which)
    {
        // Past numeric(18,2)'s reach would fail the save as a 500; a typo this big is refused first.
        var amounts = new decimal[8];
        amounts[which] = 10_000_000_000m;
        amounts[0] = Math.Max(amounts[0], amounts[6]); // contributions can't pass the basic
        var request = new OpeningBalanceRequest(Maria.Id, 2026, new DateOnly(2026, 3, 31),
            amounts[0], amounts[1], amounts[2], amounts[3], amounts[4], amounts[5], amounts[6], amounts[7], 0m);

        var act = () => _sut.CreateAsync(request);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter an amount below ₱10,000,000,000.");
        _balances.Verify(b => b.AddNewAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithAnAmountJustBelowTenBillion_IsAccepted()
    {
        var dto = await _sut.CreateAsync(ARequest(basic: 9_999_999_999.99m));

        dto.BasicSalary.Should().Be(9_999_999_999.99m);
    }

    [Fact]
    public async Task Update_WithAnAmountOfTenBillionOrMore_IsRefused()
    {
        var balance = ABalance();

        var act = () => _sut.UpdateAsync(balance.Id, ARequest(tax: 12_000_000_000m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter an amount below ₱10,000,000,000.");
        _balances.Verify(b => b.UpdateAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ForAnUnknownEmployee_IsNotFound()
    {
        var act = () => _sut.CreateAsync(ARequest() with { EmployeeId = Guid.NewGuid() });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Create_WhenSheAlreadyHasABalanceForTheYear_IsRefused()
    {
        var existing = ABalance();
        _balances.Setup(b => b.GetAsync(Maria.Id, 2026, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var act = () => _sut.CreateAsync(ARequest());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Maria Reyes Santos already has an opening balance for 2026.");
        _balances.Verify(b => b.AddNewAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_SavesEveryFigure_AndAnswersWithTheEmployee()
    {
        PayrollOpeningBalance? added = null;
        _balances.Setup(b => b.AddNewAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()))
                 .Callback((PayrollOpeningBalance b, CancellationToken _) => added = b)
                 .ReturnsAsync((PayrollOpeningBalance b, CancellationToken _) => b);

        var dto = await _sut.CreateAsync(new OpeningBalanceRequest(Maria.Id, 2026, new DateOnly(2026, 3, 31),
            150_000m, 1_000m, 2_000m, 3_000m, 4_000m, 5_000m, 6_000m, 7_000m, 2.5m));

        added.Should().NotBeNull();
        added!.EmployeeId.Should().Be(Maria.Id);
        added.Year.Should().Be(2026);
        added.ThroughDate.Should().Be(new DateOnly(2026, 3, 31));
        added.BasicSalary.Should().Be(150_000m);
        added.ThirteenthMonthPaid.Should().Be(1_000m);
        added.OtherBenefitsPaid.Should().Be(2_000m);
        added.OtherTaxablePay.Should().Be(3_000m);
        added.DeMinimis.Should().Be(4_000m);
        added.OtherNonTaxable.Should().Be(5_000m);
        added.EmployeeContributions.Should().Be(6_000m);
        added.TaxWithheld.Should().Be(7_000m);
        added.DeMinimisLeaveDays.Should().Be(2.5m);

        dto.Id.Should().Be(added.Id);
        dto.EmployeeName.Should().Be("Maria Reyes Santos");
        dto.EmployeeNumber.Should().Be("E-001");
        dto.TaxWithheld.Should().Be(7_000m);
        dto.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_RoundsFiguresToTheCentavo()
    {
        var dto = await _sut.CreateAsync(ARequest(basic: 100.005m, leaveDays: 1.255m));

        dto.BasicSalary.Should().Be(100.01m);
        dto.DeMinimisLeaveDays.Should().Be(1.26m);
    }

    // ── The edit warning ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenAPaidRunOfHersInTheYearCameAfterTheThroughDate_WarnsItWontChange()
    {
        APaidRun("PAY-2026-007", new DateOnly(2026, 4, 15), Maria);
        APaidRun("PAY-2026-008", new DateOnly(2026, 4, 30), Maria, Jose);
        APaidRun("PAY-2026-009", new DateOnly(2026, 5, 15), Jose);   // not hers
        APaidRun("PAY-2027-001", new DateOnly(2027, 1, 15), Maria);  // another year

        var dto = await _sut.CreateAsync(ARequest());

        dto.Warnings.Should().Equal(
            "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.",
            "PAY-2026-008 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.");
    }

    [Fact]
    public async Task Update_WhenAPaidRunOfHersCameAfterTheThroughDate_WarnsItWontChange()
    {
        var balance = ABalance();
        APaidRun("PAY-2026-007", new DateOnly(2026, 4, 15), Maria);

        var dto = await _sut.UpdateAsync(balance.Id, ARequest(tax: 5_000m));

        dto.Warnings.Should().Equal(
            "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.");
    }

    [Fact]
    public async Task Get_DoesNotCarryTheEditWarning()
    {
        var balance = ABalance();
        APaidRun("PAY-2026-007", new DateOnly(2026, 4, 15), Maria);

        var dto = await _sut.GetAsync(balance.Id);

        dto.Warnings.Should().BeEmpty();
    }

    // ── The double-count warning ─────────────────────────────────────────────

    [Fact]
    public async Task List_WarnsOfAPaidRunOfHersOnOrBeforeTheThroughDate()
    {
        var maria = ABalance(Maria);
        var jose = ABalance(Jose);
        _balances.Setup(b => b.GetForYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([maria, jose]);
        APaidRun("PAY-2026-005", new DateOnly(2026, 3, 15), Maria);
        APaidRun("PAY-2026-006", new DateOnly(2026, 3, 31), Maria, Jose);  // on the through date counts
        APaidRun("PAY-2026-007", new DateOnly(2026, 4, 15), Maria, Jose);  // after: no double count

        var list = await _sut.ListAsync(2026);

        list.Should().HaveCount(2);
        list[0].Warnings.Should().Equal(
            "Maria Reyes Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-005 was paid on Mar 15, 2026.",
            "Maria Reyes Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-006 was paid on Mar 31, 2026.");
        list[1].Warnings.Should().Equal(
            "Jose Cruz's opening balance already covers pay through Mar 31, 2026; PAY-2026-006 was paid on Mar 31, 2026.");
        _runs.Verify(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_WithoutAYear_IsRefused()
    {
        var act = () => _sut.ListAsync(0);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Enter a year.");
    }

    [Fact]
    public async Task Get_CarriesTheDoubleCountWarning()
    {
        var balance = ABalance();
        APaidRun("PAY-2026-005", new DateOnly(2026, 3, 15), Maria);

        var dto = await _sut.GetAsync(balance.Id);

        dto.Warnings.Should().Equal(
            "Maria Reyes Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-005 was paid on Mar 15, 2026.");
    }

    [Fact]
    public async Task Update_CarriesTheDoubleCountWarningBeforeTheEditWarning()
    {
        var balance = ABalance();
        APaidRun("PAY-2026-005", new DateOnly(2026, 3, 15), Maria);
        APaidRun("PAY-2026-007", new DateOnly(2026, 4, 15), Maria);

        var dto = await _sut.UpdateAsync(balance.Id, ARequest());

        dto.Warnings.Should().Equal(
            "Maria Reyes Santos's opening balance already covers pay through Mar 31, 2026; PAY-2026-005 was paid on Mar 15, 2026.",
            "PAY-2026-007 used these figures; its 13th month and tax won't change. Reissue the 2316 to pick up the change.");
    }

    // ── Get, update, delete ──────────────────────────────────────────────────

    [Fact]
    public async Task Get_AnUnknownBalance_IsNotFound()
    {
        var act = () => _sut.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_SavesTheNewFigures()
    {
        var balance = ABalance();

        var dto = await _sut.UpdateAsync(balance.Id, new OpeningBalanceRequest(Maria.Id, 2026, new DateOnly(2026, 4, 30),
            200_000m, 1_000m, 2_000m, 3_000m, 4_000m, 5_000m, 6_000m, 7_000m, 3m));

        balance.ThroughDate.Should().Be(new DateOnly(2026, 4, 30));
        balance.BasicSalary.Should().Be(200_000m);
        balance.ThirteenthMonthPaid.Should().Be(1_000m);
        balance.OtherBenefitsPaid.Should().Be(2_000m);
        balance.OtherTaxablePay.Should().Be(3_000m);
        balance.DeMinimis.Should().Be(4_000m);
        balance.OtherNonTaxable.Should().Be(5_000m);
        balance.EmployeeContributions.Should().Be(6_000m);
        balance.TaxWithheld.Should().Be(7_000m);
        balance.DeMinimisLeaveDays.Should().Be(3m);
        _balances.Verify(b => b.UpdateAsync(balance, It.IsAny<CancellationToken>()), Times.Once);
        dto.BasicSalary.Should().Be(200_000m);
        dto.EmployeeName.Should().Be("Maria Reyes Santos");
    }

    [Fact]
    public async Task Update_ToAnotherEmployee_IsRefused()
    {
        var balance = ABalance();

        var act = () => _sut.UpdateAsync(balance.Id, ARequest(Jose));

        await act.Should().ThrowAsync<DomainException>().WithMessage("An opening balance's employee and year can't change.");
        _balances.Verify(b => b.UpdateAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ToAnotherYear_IsRefused()
    {
        var balance = ABalance();

        var act = () => _sut.UpdateAsync(balance.Id, ARequest(year: 2025, through: new DateOnly(2025, 3, 31)));

        await act.Should().ThrowAsync<DomainException>().WithMessage("An opening balance's employee and year can't change.");
    }

    [Fact]
    public async Task Update_IsValidatedLikeCreate()
    {
        var balance = ABalance();

        var act = () => _sut.UpdateAsync(balance.Id, ARequest(tax: -1m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Amounts can't be negative.");
        _balances.Verify(b => b.UpdateAsync(It.IsAny<PayrollOpeningBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_AnUnknownBalance_IsNotFound()
    {
        var act = () => _sut.UpdateAsync(Guid.NewGuid(), ARequest());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Delete_RemovesTheBalance()
    {
        var balance = ABalance();

        await _sut.DeleteAsync(balance.Id);

        _balances.Verify(b => b.DeleteAsync(balance, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_AnUnknownBalance_IsNotFound()
    {
        var act = () => _sut.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
