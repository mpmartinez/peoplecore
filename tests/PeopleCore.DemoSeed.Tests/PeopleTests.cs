using System.Text.RegularExpressions;
using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// Twenty fictional people. Their names are common Filipino names; everything that could identify a
/// real person - IDs, phone numbers, emails - is made up but shaped like the real thing.
/// </summary>
public class PeopleTests
{
    private static IReadOnlyList<Person> People(int seed = 20260918) => PeopleBuilder.Build(new Random(seed));

    [Fact]
    public void ThereAreTwenty_NumberedDemo0001ToDemo0020()
    {
        People().Select(p => p.EmployeeNumber).Should().Equal(
            Enumerable.Range(1, 20).Select(n => $"DEMO-{n:0000}"));
    }

    [Fact]
    public void TheDepartments_HaveTheAgreedHeadcount()
    {
        People().GroupBy(p => p.Department).ToDictionary(g => g.Key, g => g.Count()).Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                ["Executive"] = 1, ["Human Resources"] = 2, ["Finance"] = 3,
                ["Operations"] = 7, ["Sales"] = 4, ["IT"] = 3,
            });
    }

    [Fact]
    public void TheHrManager_IsDemo0002_AndHoldsTheHrManagerRole()
    {
        var hr = People().Single(p => p.EmployeeNumber == "DEMO-0002");

        hr.Title.Should().Be("HR Manager");
        hr.Role.Should().Be("HRManager");
    }

    [Fact]
    public void EveryoneButTheGeneralManager_ReportsToSomeoneListedBeforeThem()
    {
        var people = People();

        people[0].ManagerNumber.Should().BeNull();
        foreach (var person in people.Skip(1))
            person.ManagerNumber.Should().NotBeNull().And.BeLessThan(person.Number,
                "managers are created first, so an employee can name one on creation");
    }

    [Fact]
    public void Heads_AreManagers_AndEveryoneElseAnEmployee()
    {
        var people = People();
        var managerNumbers = people.Where(p => p.ManagerNumber is not null).Select(p => p.ManagerNumber!.Value).ToHashSet();

        foreach (var person in people.Where(p => p.Role != "HRManager"))
            person.Role.Should().Be(managerNumbers.Contains(person.Number) ? "Manager" : "Employee",
                "whoever has direct reports must be able to approve their requests");
    }

    [Fact]
    public void Names_AreUnique_AndDrawnFromTheLists()
    {
        var people = People();

        people.Select(p => p.FullName).Should().OnlyHaveUniqueItems();
        people.Should().OnlyContain(p => Identity.Surnames.Contains(p.LastName));
        people.Should().OnlyContain(p =>
            (p.Gender == "Male" ? Identity.MaleFirstNames : Identity.FemaleFirstNames).Contains(p.FirstName));
    }

    [Fact]
    public void GovernmentIds_MobilesAndEmails_HaveTheRealShapes()
    {
        foreach (var p in People())
        {
            p.Ids.Sss.Should().MatchRegex(@"^\d{2}-\d{7}-\d$");
            p.Ids.PhilHealth.Should().MatchRegex(@"^\d{2}-\d{9}-\d$");
            p.Ids.PagIbig.Should().MatchRegex(@"^\d{4}-\d{4}-\d{4}$");
            p.Ids.Tin.Should().MatchRegex(@"^\d{3}-\d{3}-\d{3}-000$");
            p.Mobile.Should().MatchRegex(@"^09\d{9}$");
            p.Contact.Phone.Should().MatchRegex(@"^09\d{9}$");
            p.Email.Should().MatchRegex(@"^[a-z]+\.[a-z]+(\d+)?@bayanihantrading\.example$");
        }
    }

    [Fact]
    public void SalariesAndHireDates_AreBelievable()
    {
        var people = People();

        people.Should().OnlyContain(p => p.MonthlySalary >= 18_000m && p.MonthlySalary <= 150_000m);
        people[0].MonthlySalary.Should().Be(people.Max(p => p.MonthlySalary), "the General Manager earns the most");
        people.Count(p => p.HireDate.Year == 2026).Should().Be(2, "two people join during the demo year");
        people.Should().OnlyContain(p => p.HireDate.Year >= 2018);
        people.Should().OnlyContain(p => p.BirthDate < p.HireDate.AddYears(-20), "nobody joins younger than 20");
    }

    [Fact]
    public void TheSameSeed_GivesTheSamePeople_AndAnotherSeedDoesNot()
    {
        People(7).Should().BeEquivalentTo(People(7));
        People(7).Select(p => p.FullName).Should().NotEqual(People(8).Select(p => p.FullName));
    }
}
