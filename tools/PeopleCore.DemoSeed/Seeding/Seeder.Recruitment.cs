using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private partial async Task RunRecruitmentAsync()
    {
        var admin = await AdminAsync();
        var postingIds = new List<Guid>();

        foreach (var posting in RecruitmentPlanner.Postings)
        {
            var id = IdOf(await api.PostAsync($"Post {posting.Title}", "api/job-postings", new
            {
                title = posting.Title, departmentId = _departmentIds[posting.Department], positionId = (Guid?)null,
                description = posting.Description, requirements = posting.Requirements, vacancies = posting.Vacancies,
            }, admin));
            await api.PutAsync($"Publish {posting.Title}", $"api/job-postings/{id}/publish", null, admin);
            postingIds.Add(id);
            Count("job postings");
        }

        foreach (var applicant in plan.Applicants)
        {
            var posting = RecruitmentPlanner.Postings[applicant.PostingIndex];
            var step = $"Applicant {applicant.FirstName} {applicant.LastName}";

            var id = IdOf(await api.PostAsync(step, "api/applicants", new
            {
                jobPostingId = postingIds[applicant.PostingIndex], firstName = applicant.FirstName,
                lastName = applicant.LastName, email = applicant.Email, phone = applicant.Phone,
            }, admin));

            if (applicant.Status != "Applied")
                await api.PutAsync($"{step}: {applicant.Status}", $"api/applicants/{id}/status", new { status = applicant.Status }, admin);

            if (applicant.InterviewAt is { } at)
            {
                // The department head interviews. Each department's head is its first person reporting to the General Manager.
                var interviewer = plan.People.First(p => p.Department == posting.Department && p.ManagerNumber == 1);

                // ScheduledAt is stored in the database, and Npgsql refuses a DateTime that isn't
                // UTC-kind. The plan's InterviewAt already holds the intended Philippine wall-clock
                // time (the app stores Philippine wall-clock time labelled UTC), so this only marks
                // its Kind - it does not shift the value.
                var scheduledAt = DateTime.SpecifyKind(at, DateTimeKind.Utc);

                await api.PostAsync($"{step}: interview", "api/interviews", new
                {
                    applicantId = id, stageName = applicant.InterviewStage, scheduledAt,
                    interviewerId = EmployeeId(interviewer.Number),
                }, admin);
                Count("interviews scheduled");
            }

            Count("applicants");
        }

        for (var i = 0; i < RecruitmentPlanner.Postings.Count; i++)
            if (RecruitmentPlanner.Postings[i].CloseAfterApplicants)
                await api.PutAsync($"Close {RecruitmentPlanner.Postings[i].Title}", $"api/job-postings/{postingIds[i]}/close", null, admin);

        Say($"Recruitment: {RecruitmentPlanner.Postings.Count} postings, {plan.Applicants.Count} applicants.");
    }
}
