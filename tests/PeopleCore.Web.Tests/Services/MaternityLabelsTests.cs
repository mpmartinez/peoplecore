using FluentAssertions;
using PeopleCore.Web.Services;

namespace PeopleCore.Web.Tests.Services;

public class MaternityLabelsTests
{
    [Theory]
    [InlineData(MaternityClaimStatus.Draft, "Draft", "secondary")]
    [InlineData(MaternityClaimStatus.Advanced, "Advanced", "warning")]
    [InlineData(MaternityClaimStatus.Reimbursed, "Reimbursed", "success")]
    [InlineData(MaternityClaimStatus.Denied, "Denied", "destructive")]
    public void EachStatus_HasAWord_AndABadge(MaternityClaimStatus status, string label, string variant)
    {
        MaternityLabels.StatusOf(status).Should().Be(label);
        MaternityLabels.StatusVariant(status).Should().Be(variant);
    }

    [Theory]
    [InlineData(666.67, 6, false, "Suggested: ₱666.67 a day, from 6 months of paid payroll in Apr 2025–Mar 2026.")]
    [InlineData(1234.5, 1, false, "Suggested: ₱1,234.50 a day, from 1 month of paid payroll in Apr 2025–Mar 2026.")]
    [InlineData(null, 0, false, "No paid payroll found in the SSS window (Apr 2025–Mar 2026); enter the SSS-approved allowance.")]
    [InlineData(null, 0, true, "SSS rates are overridden in payroll settings, so no suggestion is possible; enter the SSS-approved allowance.")]
    public void TheSuggestion_ReadsAsASentence(double? allowance, int months, bool overridden, string expected)
    {
        var suggestion = new SuggestedAllowanceDto((decimal?)allowance, months, new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31), overridden);

        MaternityLabels.Suggestion(suggestion).Should().Be(expected);
    }
}
