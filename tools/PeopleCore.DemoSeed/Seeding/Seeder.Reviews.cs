using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    /// <summary>
    /// The mid-year cycle stays open. Reviews the client owns are left at "self-evaluation submitted",
    /// so the client can finish them during the demo.
    /// </summary>
    private partial async Task RunReviewsAsync()
    {
        var admin = await AdminAsync();
        var cycleId = IdOf(await api.PostAsync("Create the mid-year cycle", "api/review-cycles", new
        {
            name = PerformancePlanner.CycleName, year = 2026, quarter = (int?)null,
            startDate = PerformancePlanner.CycleStart, endDate = PerformancePlanner.CycleEnd,
        }, admin));

        foreach (var review in plan.Reviews)
        {
            var person = plan.PersonNumber(review.PersonNumber);
            var step = $"Mid-year review of {person.EmployeeNumber}";

            var created = await api.PostAsync(step, "api/performance-reviews", new
            {
                employeeId = EmployeeId(person.Number), reviewCycleId = cycleId,
                reviewerId = EmployeeId(review.ReviewerNumber),
                kpiItems = review.Kpis.Select(k => new { description = k.Description, target = k.Target, weight = k.Weight }),
            }, await AdminAsync());

            var reviewId = IdOf(created);
            var kpiIds = created!["kpiItems"]!.AsArray()
                .ToDictionary(k => k!["description"]!.GetValue<string>(), k => Guid.Parse(k!["id"]!.GetValue<string>()));

            await api.PostAsync($"{step}: self-evaluation", $"api/performance-reviews/{reviewId}/self-evaluation", new
            {
                score = review.SelfScore, comments = review.SelfComment,
                kpiItems = review.Kpis.Select(k => new { id = kpiIds[k.Description], actual = k.Actual, score = k.SelfScore }),
            }, await TokenOfAsync(person.Number));

            if (review.AwaitsManager)
            {
                Count("reviews awaiting the manager");
                continue;
            }

            await api.PostAsync($"{step}: manager review", $"api/performance-reviews/{reviewId}/manager-review", new
            {
                score = review.ManagerScore, comments = review.ManagerComment,
                kpiItems = review.Kpis.Select(k => new { id = kpiIds[k.Description], actual = k.Actual, score = k.ManagerScore }),
            }, await TokenOfAsync(review.ReviewerNumber));
            Count("reviews completed");
        }

        Say($"Mid-year review: {plan.Reviews.Count} reviews.");
    }
}
