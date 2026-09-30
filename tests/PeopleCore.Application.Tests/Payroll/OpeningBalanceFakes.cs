using Moq;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>Opening balances for the tests that need them, read as the repository reads them.</summary>
internal static class OpeningBalanceFakes
{
    /// <summary>A repository holding <paramref name="balances"/>: each read returns the requested employees' for the year.</summary>
    public static Mock<IPayrollOpeningBalanceRepository> Holding(params PayrollOpeningBalance[] balances)
    {
        var repository = new Mock<IPayrollOpeningBalanceRepository>();
        repository
            .Setup(r => r.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, int year, CancellationToken _) =>
                balances.Where(b => ids.Contains(b.EmployeeId) && b.Year == year).ToList());
        repository
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid employeeId, int year, CancellationToken _) =>
                balances.SingleOrDefault(b => b.EmployeeId == employeeId && b.Year == year));
        return repository;
    }

    /// <summary>An opening balance for the employee and year, through Mar 31 unless set, with every figure zero unless set.</summary>
    public static PayrollOpeningBalance OpeningBalance(Guid employeeId, int year = 2026, decimal basicSalary = 0m,
        decimal thirteenthMonthPaid = 0m, decimal otherBenefitsPaid = 0m, decimal deMinimisLeaveDays = 0m,
        decimal otherTaxablePay = 0m, decimal deMinimis = 0m, decimal otherNonTaxable = 0m,
        decimal employeeContributions = 0m, decimal taxWithheld = 0m, DateOnly? throughDate = null) => new()
    {
        EmployeeId = employeeId,
        Year = year,
        ThroughDate = throughDate ?? new DateOnly(year, 3, 31),
        BasicSalary = basicSalary,
        ThirteenthMonthPaid = thirteenthMonthPaid,
        OtherBenefitsPaid = otherBenefitsPaid,
        OtherTaxablePay = otherTaxablePay,
        DeMinimis = deMinimis,
        OtherNonTaxable = otherNonTaxable,
        EmployeeContributions = employeeContributions,
        TaxWithheld = taxWithheld,
        DeMinimisLeaveDays = deMinimisLeaveDays,
    };
}
