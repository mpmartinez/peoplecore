namespace PeopleCore.DemoSeed.Seeding;

// Temporary: removed by Task 8 once every step has its real implementation.
public sealed partial class Seeder
{
    private partial Task RunReviewsAsync() => Task.CompletedTask;
    private partial Task RunRecruitmentAsync() => Task.CompletedTask;
    private partial Task<string> FinishAsync() => Task.FromResult(string.Empty);
}
