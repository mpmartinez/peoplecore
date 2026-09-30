using System.Globalization;

namespace PeopleCore.Web.Services;

/// <summary>
/// Readable words for maternity claims, kept in one place so the claims list, its forms and the
/// payroll pages say the same thing.
/// </summary>
public static class MaternityLabels
{
    /// <summary>
    /// What happens to the contributions on a cutoff the leave covers: the offset can take the
    /// basic, but the shares stay on the monthly basic, so the part the cutoff can't pay is
    /// deferred and collected later.
    /// </summary>
    public const string NetCashNote =
        "While her leave is covered by SSS, her contribution shares are deferred and collected from the advance or her next pay.";

    public static string StatusOf(MaternityClaimStatus status) => status switch
    {
        MaternityClaimStatus.Draft => "Draft",
        MaternityClaimStatus.Advanced => "Advanced",
        MaternityClaimStatus.Reimbursed => "Reimbursed",
        MaternityClaimStatus.Denied => "Denied",
        MaternityClaimStatus.Voided => "Voided",
        _ => status.ToString()
    };

    /// <summary>
    /// Advanced is money SSS still owes; Reimbursed is settled; Denied is money that won't come back;
    /// Voided was withdrawn and counts for nothing.
    /// </summary>
    public static string StatusVariant(MaternityClaimStatus status) => status switch
    {
        MaternityClaimStatus.Advanced => "warning",
        MaternityClaimStatus.Reimbursed => "success",
        MaternityClaimStatus.Denied => "destructive",
        MaternityClaimStatus.Voided => "outline",
        _ => "secondary"
    };

    /// <summary>The suggested allowance as a sentence, or why there is none.</summary>
    public static string Suggestion(SuggestedAllowanceDto suggestion)
    {
        var window = $"{Month(suggestion.WindowFrom)}–{Month(suggestion.WindowTo)}";

        if (suggestion.RatesOverridden)
            return "SSS rates are overridden in payroll settings, so no suggestion is possible; enter the SSS-approved allowance.";
        if (suggestion.DailyAllowance is not { } allowance)
            return $"No paid payroll found in the SSS window ({window}); enter the SSS-approved allowance.";

        var months = suggestion.MonthsFound == 1 ? "1 month" : $"{suggestion.MonthsFound} months";
        return $"Suggested: {PayrollLabels.Peso(allowance)} a day, from {months} of paid payroll in {window}.";
    }

    /// <summary>A date the way the claims pages show one: Aug 10, 2026.</summary>
    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>Days the way the claims pages show them: 105, or 52.5.</summary>
    public static string Days(decimal days) => days.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Month(DateOnly date) => date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
}
