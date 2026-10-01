using FluentAssertions;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A final pay goes through the same engine as a regular run, so its entry stores the taxable part
/// of its 13th month too.
/// </summary>
public partial class FinalPayServiceTests
{
    [Fact]
    public async Task CreateAsync_TheWorkedExample_StoresNothingTaxable()
    {
        // 13th month (36,500 February + 15,600 this period) / 12 = 4,341.67, nothing used earlier:
        // all within the 90,000.
        await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.ThirteenthMonth.Should().Be(4_341.67m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(0m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(4_341.67m);
    }

    [Fact]
    public async Task CreateAsync_StoresThe13thMonthsTaxablePart_PastTheExemptionTheOpeningBalancesUsed()
    {
        // Before PeopleCore she was paid 1,100,000 of basic and an 88,000 13th month.
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, basicSalary: 1_100_000m, thirteenthMonthPaid: 88_000m));

        await sut.CreateAsync(_separation.Id, Request());

        // 13th month: (1,100,000 + 36,500 + 15,600) / 12 = 1,152,100 / 12 = 96,008.33, less the
        // 88,000 paid = 8,008.33. 90,000 - 88,000 = 2,000 of the exemption left: 2,000 exempt,
        // 8,008.33 - 2,000 = 6,008.33 taxable.
        SavedEntry.ThirteenthMonth.Should().Be(8_008.33m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(6_008.33m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(2_000m);
    }
}
