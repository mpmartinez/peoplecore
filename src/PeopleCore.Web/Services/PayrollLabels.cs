using System.Globalization;

namespace PeopleCore.Web.Services;

/// <summary>
/// Readable words for the payroll enum names the API sends as strings (PayrollRunStatus, LoanType),
/// so the final-pay summary, the separations list and the run pages say "For approval" and
/// "SSS loan" rather than "ForApproval" and "SSSLoan".
/// </summary>
public static class PayrollLabels
{
    public const string FinalPayRunType = "FinalPay";

    public static string RunStatusOf(string? status) => status switch
    {
        "Draft" => "Draft",
        "Processing" => "Processing",
        "ForApproval" => "For approval",
        "Approved" => "Approved",
        "Paid" => "Paid",
        null or "" => "",
        _ => status
    };

    /// <summary>The badge variant a run status gets, the same on every page.</summary>
    public static string RunStatusVariant(string? status) => status switch
    {
        "Approved" or "Paid" => "success",
        "Processing" or "ForApproval" => "warning",
        _ => "secondary"
    };

    public static string LoanTypeOf(string loanType) => loanType switch
    {
        "SSSLoan" => "SSS loan",
        "PagIbigLoan" => "Pag-IBIG loan",
        "CompanyLoan" => "Company loan",
        "CashAdvance" => "Cash advance",
        "CalamityLoan" => "Calamity loan",
        "Other" => "Other loan",
        _ => loanType
    };

    /// <summary>A peso amount the way every payroll page shows one: ₱1,234.50.</summary>
    public static string Peso(decimal amount) => "₱" + amount.ToString("N2", CultureInfo.InvariantCulture);
}
