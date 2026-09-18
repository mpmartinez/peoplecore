using FluentAssertions;
using Moq;
using PeopleCore.Application.Careers.Services;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Careers;

/// <summary>
/// The public careers page listed open jobs with no department or position: <c>GetOpenJobsAsync</c>
/// reads job postings through <c>IJobPostingRepository.GetAllAsync</c>, the unfiltered
/// <c>Repository&lt;T&gt;.GetAllAsync</c>, which never loaded JobPosting.Department or
/// JobPosting.Position - unlike <c>GetByIdAsync</c>, which already includes both and so was never
/// affected.
/// </summary>
public class CareersServiceDepartmentTests : DatabaseTestBase
{
    public CareersServiceDepartmentTests(PostgresFixture fixture) : base(fixture) { }

    private CareersService Service => new(
        new JobPostingRepository(NewContext()),
        new ApplicantRepository(NewContext()),
        Mock.Of<IStorageService>(),
        new DocumentStorageOptions());

    [Fact]
    public async Task GetOpenJobs_IncludesTheJobPostingsRealDepartmentName()
    {
        var company = ACompany();
        var engineering = new Department { Name = "Engineering", CompanyId = company.Id };
        Context.Companies.Add(company);
        Context.Departments.Add(engineering);
        Context.JobPostings.Add(new JobPosting
        {
            Title = "Backend Engineer", DepartmentId = engineering.Id, Status = JobPostingStatus.Open
        });
        await Context.SaveChangesAsync();

        var jobs = await Service.GetOpenJobsAsync();

        jobs.Should().ContainSingle().Which.DepartmentName.Should().Be("Engineering");
    }
}
