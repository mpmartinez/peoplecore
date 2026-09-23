using FluentAssertions;
using PeopleCore.Web.Services;

namespace PeopleCore.Web.Tests.Services;

public class PayrollLabelsTests
{
    [Theory]
    [InlineData("Draft", "Draft")]
    [InlineData("Processing", "Processing")]
    [InlineData("ForApproval", "For approval")]
    [InlineData("Approved", "Approved")]
    [InlineData("Paid", "Paid")]
    public void RunStatuses_ReadAsWords(string status, string label) =>
        PayrollLabels.RunStatusOf(status).Should().Be(label);

    [Theory]
    [InlineData("Draft", "secondary")]
    [InlineData("Processing", "warning")]
    [InlineData("ForApproval", "warning")]
    [InlineData("Approved", "success")]
    [InlineData("Paid", "success")]
    public void EveryPageColoursARunStatusTheSameWay(string status, string variant) =>
        PayrollLabels.RunStatusVariant(status).Should().Be(variant);

    [Theory]
    [InlineData("SSSLoan", "SSS loan")]
    [InlineData("PagIbigLoan", "Pag-IBIG loan")]
    [InlineData("CashAdvance", "Cash advance")]
    public void LoanTypes_ReadAsWords(string loanType, string label) =>
        PayrollLabels.LoanTypeOf(loanType).Should().Be(label);
}
