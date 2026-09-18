using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

public class PerformanceAndRecruitmentTests
{
    private static readonly DemoPlan Plan = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));

    [Fact]
    public void EveryoneOnStaffByMarch_IsReviewed_ByTheirManager_OrTheGmByHr()
    {
        var expected = Plan.People.Where(p => p.HireDate <= new DateOnly(2026, 3, 31)).Select(p => p.Number);

        Plan.Reviews.Select(r => r.PersonNumber).Should().BeEquivalentTo(expected);
        foreach (var review in Plan.Reviews)
            review.ReviewerNumber.Should().Be(Plan.PersonNumber(review.PersonNumber).ManagerNumber ?? 2);
    }

    [Fact]
    public void TheReviewsLeftForTheManager_AreExactlyThoseTheClientReviews()
    {
        Plan.Reviews.Where(r => r.AwaitsManager).Should().NotBeEmpty()
            .And.OnlyContain(r => r.ReviewerNumber == 2);
        Plan.Reviews.Where(r => r.ReviewerNumber == 2).Should().OnlyContain(r => r.AwaitsManager);
    }

    [Fact]
    public void Kpis_WeighToOneHundred_AndScoresSitOnAOneToFiveScale()
    {
        foreach (var review in Plan.Reviews)
        {
            review.Kpis.Sum(k => k.Weight).Should().Be(100);
            review.SelfScore.Should().BeInRange(1, 5);
            review.Kpis.Should().OnlyContain(k => k.SelfScore >= 1 && k.SelfScore <= 5 && k.ManagerScore >= 1 && k.ManagerScore <= 5);
        }
    }

    [Fact]
    public void ThreePostings_TwelveApplicants_EveryStatusRepresented()
    {
        RecruitmentPlanner.Postings.Should().HaveCount(3);
        RecruitmentPlanner.Postings.Count(p => p.CloseAfterApplicants).Should().Be(1);
        Plan.Applicants.Should().HaveCount(12);
        Plan.Applicants.Select(a => a.Status).Distinct().Should().BeEquivalentTo(
            ["Applied", "Screening", "Interview", "Offer", "Hired", "Rejected"]);
    }

    [Fact]
    public void Applicants_AreNotEmployees_AndInterviewsAreComing()
    {
        var employees = Plan.People.Select(p => p.FullName).ToHashSet();

        Plan.Applicants.Should().OnlyContain(a => !employees.Contains($"{a.FirstName} {a.LastName}"));
        Plan.Applicants.Select(a => a.Email).Should().OnlyHaveUniqueItems();
        Plan.Applicants.Where(a => a.Status == "Interview").Should().OnlyContain(a =>
            a.InterviewAt > Plan.Today.ToDateTime(TimeOnly.MinValue) && a.InterviewStage != null);
    }
}
