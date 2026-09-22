using System.Globalization;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Employees.Coe;

/// <summary>The Certificate of Employment request body - every field optional, so HR gets sensible defaults for free.</summary>
public record CoeRequest(string? Purpose, string? SignatoryName, string? SignatoryTitle, bool IncludeSalary);

/// <summary>
/// Everything <see cref="CoeContent.Build"/> needs to word a certificate, gathered by
/// <see cref="ICoeService"/> from the employee, company and compensation records. Kept separate
/// from those entities so the wording stays pure and unit-testable without a database.
/// </summary>
public record CoeFacts(
    string FullName,
    string? Position,
    DateOnly HireDate,
    DateOnly? LastWorkingDay,
    string CompanyName,
    string? CompanyAddress,
    byte[]? Logo,
    decimal? MonthlyBasicSalary,
    string? Gender,
    string DefaultSignatoryName);

/// <summary>The worded, ready-to-print content of a Certificate of Employment. <see cref="CoeDocument"/> (in PeopleCore.Reports) lays this out on a page.</summary>
public record CoeContent(
    string CompanyName,
    string? CompanyAddress,
    byte[]? Logo,
    string Title,
    IReadOnlyList<string> Paragraphs,
    string DateLine,
    string SignatoryName,
    string SignatoryTitle)
{
    private const string DateFormat = "MMMM d, yyyy";

    /// <summary>
    /// Words the certificate. Pure: no I/O, no clock reads beyond the <paramref name="today"/>
    /// it is handed, so every sentence here is exercised by <c>CoeContentTests</c> without a
    /// database or a renderer.
    /// </summary>
    public static CoeContent Build(CoeFacts facts, CoeRequest request, DateOnly today)
    {
        if (request.IncludeSalary && facts.MonthlyBasicSalary is null)
            throw new DomainException($"{facts.FullName} has no salary on record, so the salary line can't be added.");

        var isFormer = facts.LastWorkingDay is not null;
        var verb = isFormer ? "was employed" : "has been employed";
        var toDate = isFormer ? Format(facts.LastWorkingDay!.Value) : "present";
        var positionClause = string.IsNullOrWhiteSpace(facts.Position) ? "" : $" as {facts.Position}";

        var paragraphs = new List<string>
        {
            $"This is to certify that {facts.FullName} {verb} by {facts.CompanyName}{positionClause} " +
            $"from {Format(facts.HireDate)} to {toDate}."
        };

        if (request.IncludeSalary)
        {
            var pronoun = facts.Gender switch
            {
                "Male" => "He",
                "Female" => "She",
                _ => "They"
            };
            // A current employee still earns it, so the sentence stays present tense (agreeing
            // with the pronoun); a former employee's pay stopped with their last working day.
            var salaryVerb = isFormer ? "received" : pronoun == "They" ? "receive" : "receives";
            var amount = facts.MonthlyBasicSalary!.Value.ToString("#,##0.00", CultureInfo.InvariantCulture);
            paragraphs.Add($"{pronoun} {salaryVerb} a monthly basic salary of ₱{amount}.");
        }

        var purpose = string.IsNullOrWhiteSpace(request.Purpose)
            ? "This certification is issued upon the request of the employee for whatever legal purpose it may serve."
            : request.Purpose.Trim();
        paragraphs.Add(purpose);

        return new CoeContent(
            facts.CompanyName,
            facts.CompanyAddress,
            facts.Logo,
            "CERTIFICATE OF EMPLOYMENT",
            paragraphs,
            $"Issued on {Format(today)}.",
            request.SignatoryName ?? facts.DefaultSignatoryName,
            request.SignatoryTitle ?? "HR Manager");
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
