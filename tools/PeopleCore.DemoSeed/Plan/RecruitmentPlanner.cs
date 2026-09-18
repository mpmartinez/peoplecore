namespace PeopleCore.DemoSeed.Plan;

public record PostingPlan(string Title, string Department, string Description, string Requirements, int Vacancies, bool CloseAfterApplicants);

public record ApplicantPlan(
    int PostingIndex, string FirstName, string LastName, string Email, string Phone,
    string Status, string? InterviewStage, DateTime? InterviewAt);

/// <summary>
/// Three openings with twelve applicants between them, spread over every status the pipeline
/// has. The closed posting has already hired someone. The applicant is not converted into an
/// employee, so the company stays at twenty.
/// </summary>
public static class RecruitmentPlanner
{
    public const string ApplicantDomain = "applicant.example";

    public static readonly IReadOnlyList<PostingPlan> Postings =
    [
        new("Warehouse Staff", "Operations",
            "Receive, store and dispatch goods at our Valenzuela warehouse.",
            "High school graduate; able to lift 25 kg; forklift experience a plus.", 2, false),
        new("Account Executive", "Sales",
            "Grow and look after corporate accounts in Metro Manila.",
            "College graduate; two years of B2B sales; own vehicle preferred.", 1, false),
        new("Junior Accountant", "Finance",
            "Support month-end close, payables and statutory filings.",
            "BS Accountancy; CPA or board taker; familiar with BIR forms.", 1, true),
    ];

    private static readonly (int Posting, string Status)[] Layout =
    [
        (0, "Applied"), (0, "Applied"), (0, "Screening"), (0, "Interview"), (0, "Interview"), (0, "Rejected"),
        (1, "Applied"), (1, "Screening"), (1, "Interview"), (1, "Offer"),
        (2, "Hired"), (2, "Rejected"),
    ];

    public static IReadOnlyList<ApplicantPlan> Applicants(IReadOnlyList<Person> people, DateOnly today, Random rng)
    {
        var taken = people.Select(p => p.FullName).ToHashSet();
        var applicants = new List<ApplicantPlan>();

        foreach (var (posting, status) in Layout)
        {
            string first, last;
            do
            {
                first = Identity.Pick(rng, rng.Next(2) == 0 ? Identity.MaleFirstNames : Identity.FemaleFirstNames);
                last = Identity.Pick(rng, Identity.Surnames);
            } while (!taken.Add($"{first} {last}"));

            var interview = status == "Interview"
                ? today.AddDays(rng.Next(2, 11)).ToDateTime(new TimeOnly(10, 0))
                : (DateTime?)null;

            applicants.Add(new ApplicantPlan(
                posting, first, last, $"{Identity.EmailLocalPart(first, last)}@{ApplicantDomain}", Identity.Mobile(rng),
                status, interview is null ? null : (rng.Next(2) == 0 ? "Initial Interview" : "Final Interview"), interview));
        }

        return applicants;
    }
}
