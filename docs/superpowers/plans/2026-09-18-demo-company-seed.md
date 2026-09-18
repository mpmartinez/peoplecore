# Demo Company Seed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A console program that fills a PeopleCore site, through its HTTP API, with a fictional 20-person Filipino company and its history from 1 January 2026 to the day it runs, so a client can try every report.

**Architecture:** The program has three layers.
- A **plan** layer is pure and deterministic. From a seed and a date it builds everything that should exist: the people, holidays, pay periods, leave, overtime, attendance, reviews and applicants. It is fully unit-tested.
- An **API** layer wraps `HttpClient`. It signs in, sends JSON, uploads CSV, and turns any failure into a `SeedException` naming the step.
- A **seeder** layer walks the plan in calendar order and calls the API as the administrator, or as whichever employee the rule requires.

**Tech Stack:** .NET 10 console app, `System.Net.Http.Json`, `System.Text.Json.Nodes`, xUnit and FluentAssertions for the tests. There are no new NuGet packages beyond what the test projects already use.

## Global Constraints

- The program lives in `tools/PeopleCore.DemoSeed`. Its tests live in `tests/PeopleCore.DemoSeed.Tests`. Both are added to `PeopleCore.slnx`.
- **It talks to PeopleCore only over HTTP.** It never references `src/` projects and never touches a database.
- **Every person is fictional.** Names are common Filipino first names and surnames. Government IDs, mobile numbers and emails are fake but correctly shaped.
- Email domain: `bayanihantrading.example`. `.example` is reserved by RFC 2606 and can never be a real domain.
- Employee numbers: `DEMO-0001` to `DEMO-0020`.
- The company has 20 people in six departments: Executive 1, Human Resources 2, Finance 3, Operations 7, Sales 4, IT 3.
- The history starts on **2026-01-01** and runs to the day before the run date. "Today" is the run date.
- Holidays come from Proclamation No. 1006 (2026), January to September. The two Eid holidays are proclaimed separately, so they are left out.
- Schedule: Monday to Friday, 08:00–17:00, 60-minute break, from `max(hire date, 2026-01-01)`.
- Payroll is semi-monthly, with periods 1–15 and 16–end of month. Every **completed** half-month gets a run. Every run except the latest is approved and marked paid. The latest is left as computed, awaiting approval.
- Leave types: Vacation Leave (`VL`) and Sick Leave (`SL`), each 15 days a year, accruing monthly.
- The client's account is the HR Manager persona (`DEMO-0002`), holding the seeded `HRManager` role. The other 19 logins are deactivated at the end.
- **Configuration comes from environment variables**:
  - `PEOPLECORE_URL`
  - `PEOPLECORE_ADMIN_EMAIL`
  - `PEOPLECORE_ADMIN_PASSWORD`
  - `PEOPLECORE_SEED` (optional, default `20260918`)
- **Never print or log a password or a token.** The only exception is the HR Manager's temporary password, printed once in the final summary.
- The program stops on the first failed request. It prints the step, method, path, status and the API's message, then exits with code 1.
- It refuses to run if any employee number starting `DEMO-` already exists.

## File map

```
tools/PeopleCore.DemoSeed/
  PeopleCore.DemoSeed.csproj
  Program.cs                      entry point: env vars, run, summary, exit code
  Plan/
    Calendar.cs                   holidays, work days, pay periods
    Roster.cs                     the 20 fixed roles (department, title, salary, manager, hire date)
    Identity.cs                   names, birthdays, IDs, mobiles, contacts drawn from the seed
    People.cs                     Person record + PeopleBuilder combining Roster and Identity
    LeavePlanner.cs               leave filings that never exceed the accrued balance
    OvertimePlanner.cs            overtime filings
    AttendancePlanner.cs          attendance rows for one month
    PerformancePlanner.cs         the mid-year review plan
    RecruitmentPlanner.cs         postings and applicants
    DemoPlan.cs                   DemoPlan record + DemoPlan.Build(seed, today)
  Api/
    SeedException.cs
    ApiClient.cs                  JSON/CSV requests, sign-in, error mapping
    Logins.cs                     create login + first password change, token refresh
  Seeding/
    Seeder.cs                     orchestration, state, summary
    Seeder.Organization.cs        guard, company, departments, positions, teams, employees, pay, IDs
    Seeder.Setup.cs               holidays, shift, assignments, logins, leave types and policies
    Seeder.Months.cs              the month loop: accruals, leave, overtime, attendance, payroll
    Seeder.Reviews.cs             performance cycle and reviews
    Seeder.Recruitment.cs         postings, applicants, interviews
    Seeder.Finish.cs              deactivate logins, reset the client's password
tests/PeopleCore.DemoSeed.Tests/
  PeopleCore.DemoSeed.Tests.csproj
  CalendarTests.cs
  PeopleTests.cs
  LeavePlannerTests.cs
  OvertimePlannerTests.cs
  AttendancePlannerTests.cs
  PerformanceAndRecruitmentTests.cs
  DemoPlanTests.cs
  ApiClientTests.cs
```

---

### Task 1: Projects and the calendar

**Files:**
- Create: `tools/PeopleCore.DemoSeed/PeopleCore.DemoSeed.csproj`
- Create: `tools/PeopleCore.DemoSeed/Program.cs` (temporary stub, replaced in Task 8)
- Create: `tools/PeopleCore.DemoSeed/Plan/Calendar.cs`
- Create: `tests/PeopleCore.DemoSeed.Tests/PeopleCore.DemoSeed.Tests.csproj`
- Create: `tests/PeopleCore.DemoSeed.Tests/CalendarTests.cs`
- Modify: `PeopleCore.slnx`

**Interfaces:**
- Produces, in namespace `PeopleCore.DemoSeed.Plan`:
  - `record Holiday(DateOnly Date, string Name, bool IsRegular)`
  - `record PayPeriod(DateOnly Start, DateOnly End, DateOnly PayDate)`
  - `static class Calendar`
    - `DateOnly Start` (2026-01-01)
    - `IReadOnlyList<Holiday> Holidays`
    - `bool IsWorkDay(DateOnly date)`
    - `IEnumerable<DateOnly> WorkDays(DateOnly from, DateOnly to)` (inclusive)
    - `IReadOnlyList<PayPeriod> CompletedPayPeriods(DateOnly today)`

- [ ] **Step 1: Create the two projects**

`tools/PeopleCore.DemoSeed/PeopleCore.DemoSeed.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>PeopleCore.DemoSeed</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="PeopleCore.DemoSeed.Tests" />
  </ItemGroup>

</Project>
```

For `tests/PeopleCore.DemoSeed.Tests/PeopleCore.DemoSeed.Tests.csproj`, copy the `<PackageReference>` items **with their exact versions** from `tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj`: xUnit, xunit runner, `Microsoft.NET.Test.Sdk`, FluentAssertions and coverlet if present. Leave out Moq. Then:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <!-- the same xunit / test-sdk / FluentAssertions references and versions as PeopleCore.Application.Tests -->
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\tools\PeopleCore.DemoSeed\PeopleCore.DemoSeed.csproj" />
  </ItemGroup>

</Project>
```

Replace the comment with the real `PackageReference` lines.

A stub `tools/PeopleCore.DemoSeed/Program.cs`, so the project builds:

```csharp
return 0;
```

Add both projects to `PeopleCore.slnx`. Add a new `<Folder Name="/tools/">` containing `tools/PeopleCore.DemoSeed/PeopleCore.DemoSeed.csproj`, and add the test project to the existing `/tests/` folder.

- [ ] **Step 2: Write the failing calendar tests**

`tests/PeopleCore.DemoSeed.Tests/CalendarTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Which days are worked and which half-months get paid.</summary>
public class CalendarTests
{
    [Fact]
    public void TheHolidays_AreTheProclaimed2026Dates_JanuaryToSeptember()
    {
        Calendar.Holidays.Select(h => (h.Date, h.IsRegular)).Should().Equal(
            (new DateOnly(2026, 1, 1), true),
            (new DateOnly(2026, 2, 17), false),
            (new DateOnly(2026, 4, 2), true),
            (new DateOnly(2026, 4, 3), true),
            (new DateOnly(2026, 4, 4), false),
            (new DateOnly(2026, 4, 9), true),
            (new DateOnly(2026, 5, 1), true),
            (new DateOnly(2026, 6, 12), true),
            (new DateOnly(2026, 8, 21), false),
            (new DateOnly(2026, 8, 31), true));
    }

    [Fact]
    public void Weekends_AndHolidays_AreNotWorkDays()
    {
        Calendar.IsWorkDay(new DateOnly(2026, 1, 1)).Should().BeFalse();   // New Year's Day, a Thursday
        Calendar.IsWorkDay(new DateOnly(2026, 1, 3)).Should().BeFalse();   // Saturday
        Calendar.IsWorkDay(new DateOnly(2026, 1, 4)).Should().BeFalse();   // Sunday
        Calendar.IsWorkDay(new DateOnly(2026, 1, 2)).Should().BeTrue();    // Friday
    }

    [Fact]
    public void WorkDays_InJanuary2026_AreTwentyOne()
    {
        Calendar.WorkDays(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).Should().HaveCount(21);
    }

    [Fact]
    public void PayPeriods_TileTheYear_UpToTheLastCompletedHalfMonth()
    {
        var periods = Calendar.CompletedPayPeriods(new DateOnly(2026, 9, 18));

        periods.Should().HaveCount(17);
        periods[0].Should().Be(new PayPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15)));
        periods[1].Should().Be(new PayPeriod(new DateOnly(2026, 1, 16), new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31)));
        periods[3].End.Should().Be(new DateOnly(2026, 2, 28));
        periods[^1].Should().Be(new PayPeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15)));

        for (var i = 1; i < periods.Count; i++)
            periods[i].Start.Should().Be(periods[i - 1].End.AddDays(1), "periods leave no gap and do not overlap");
    }

    [Fact]
    public void AHalfMonthEndingToday_IsNotYetComplete()
    {
        Calendar.CompletedPayPeriods(new DateOnly(2026, 9, 15)).Should().HaveCount(16);
        Calendar.CompletedPayPeriods(new DateOnly(2026, 9, 16)).Should().HaveCount(17);
    }
}
```

The spec speaks of "18 runs", but that was an estimate. The rule is "every completed half-month". On 18 September that gives 17 periods, from 1–15 January to 1–15 September. The test pins the rule.

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: FAIL to compile, because `Calendar` does not exist.

- [ ] **Step 4: Write the calendar**

`tools/PeopleCore.DemoSeed/Plan/Calendar.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

public record Holiday(DateOnly Date, string Name, bool IsRegular);

public record PayPeriod(DateOnly Start, DateOnly End, DateOnly PayDate);

/// <summary>
/// The demo year. Holidays are those of Proclamation No. 1006 (2026) that fall before October.
/// Eidul Fitr and Eidul Adha are proclaimed separately each year and are left out rather than guessed.
/// </summary>
public static class Calendar
{
    public static readonly DateOnly Start = new(2026, 1, 1);

    public static readonly IReadOnlyList<Holiday> Holidays =
    [
        new(new DateOnly(2026, 1, 1), "New Year's Day", true),
        new(new DateOnly(2026, 2, 17), "Chinese New Year", false),
        new(new DateOnly(2026, 4, 2), "Maundy Thursday", true),
        new(new DateOnly(2026, 4, 3), "Good Friday", true),
        new(new DateOnly(2026, 4, 4), "Black Saturday", false),
        new(new DateOnly(2026, 4, 9), "Araw ng Kagitingan", true),
        new(new DateOnly(2026, 5, 1), "Labor Day", true),
        new(new DateOnly(2026, 6, 12), "Independence Day", true),
        new(new DateOnly(2026, 8, 21), "Ninoy Aquino Day", false),
        new(new DateOnly(2026, 8, 31), "National Heroes Day", true),
    ];

    private static readonly HashSet<DateOnly> HolidayDates = Holidays.Select(h => h.Date).ToHashSet();

    public static bool IsWorkDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !HolidayDates.Contains(date);

    public static IEnumerable<DateOnly> WorkDays(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
            if (IsWorkDay(day)) yield return day;
    }

    /// <summary>Semi-monthly periods from 1 January whose last day is before <paramref name="today"/>.</summary>
    public static IReadOnlyList<PayPeriod> CompletedPayPeriods(DateOnly today)
    {
        var periods = new List<PayPeriod>();
        for (var month = Start; month < today; month = month.AddMonths(1))
        {
            var firstHalfEnd = new DateOnly(month.Year, month.Month, 15);
            var secondHalfEnd = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

            if (firstHalfEnd < today) periods.Add(new PayPeriod(month, firstHalfEnd, firstHalfEnd));
            if (secondHalfEnd < today) periods.Add(new PayPeriod(firstHalfEnd.AddDays(1), secondHalfEnd, secondHalfEnd));
        }
        return periods;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: PASS, 5 tests.

Then: `dotnet build PeopleCore.slnx --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add tools/PeopleCore.DemoSeed tests/PeopleCore.DemoSeed.Tests PeopleCore.slnx
git commit -m "feat(demo): a calendar of 2026 work days, holidays and pay periods"
```

---

### Task 2: The people

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Plan/Roster.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/Identity.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/People.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/PeopleTests.cs`

**Interfaces:**
- Consumes: `Calendar` (Task 1).
- Produces, in namespace `PeopleCore.DemoSeed.Plan`:
  - `record GovernmentIds(string Sss, string PhilHealth, string PagIbig, string Tin)`
  - `record EmergencyContact(string Name, string Relationship, string Phone)`
  - `record Person(int Number, string EmployeeNumber, string FirstName, string? MiddleName, string LastName, string Gender, DateOnly BirthDate, string CivilStatus, string Mobile, string Email, string Department, string Title, string? Team, int? ManagerNumber, decimal MonthlySalary, DateOnly HireDate, string EmploymentStatus, string Role, string TaxCode, int Dependents, GovernmentIds Ids, EmergencyContact Contact)`
    - with `string FullName => $"{FirstName} {LastName}"`
    - with `DateOnly ActiveFrom => HireDate > Calendar.Start ? HireDate : Calendar.Start`
  - `static class PeopleBuilder { IReadOnlyList<Person> Build(Random rng); }`
  - `static class Roster`, with `record Seat(...)` and `IReadOnlyList<Seat> Seats`
  - `static class Identity` with `static string Pick(Random rng, IReadOnlyList<string> from)`, plus the name lists `MaleFirstNames`, `FemaleFirstNames` and `Surnames`, which the recruitment planner reuses.
- Constants: `Person.Role` is one of `"Manager"`, `"HRManager"` or `"Employee"`, the seeded role names. `DEMO-0002` is the HR Manager.

- [ ] **Step 1: Write the failing tests**

`tests/PeopleCore.DemoSeed.Tests/PeopleTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo --filter "FullyQualifiedName~PeopleTests"`
Expected: FAIL to compile.

- [ ] **Step 3: The roster**

`tools/PeopleCore.DemoSeed/Plan/Roster.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

/// <summary>
/// The twenty jobs in Bayanihan Trading, in creation order: every manager comes before the people who
/// report to them. Salaries are monthly pesos. Hire dates are fixed so the hiring trend has a shape.
/// </summary>
public static class Roster
{
    public record Seat(
        string Department, string Title, string? Team, int? ManagerNumber, decimal MonthlySalary,
        DateOnly HireDate, string EmploymentStatus);

    public static readonly IReadOnlyList<Seat> Seats =
    [
        /*  1 */ new("Executive", "General Manager", null, null, 150_000m, new(2018, 3, 5), "Regular"),
        /*  2 */ new("Human Resources", "HR Manager", null, 1, 85_000m, new(2019, 6, 17), "Regular"),
        /*  3 */ new("Human Resources", "HR Officer", null, 2, 32_000m, new(2022, 2, 1), "Regular"),
        /*  4 */ new("Finance", "Finance Manager", null, 1, 95_000m, new(2018, 9, 3), "Regular"),
        /*  5 */ new("Finance", "Senior Accountant", null, 4, 45_000m, new(2020, 1, 13), "Regular"),
        /*  6 */ new("Finance", "Accountant", null, 4, 30_000m, new(2023, 7, 10), "Regular"),
        /*  7 */ new("Operations", "Operations Manager", null, 1, 90_000m, new(2018, 5, 21), "Regular"),
        /*  8 */ new("Operations", "Warehouse Supervisor", "Warehouse", 7, 38_000m, new(2019, 11, 4), "Regular"),
        /*  9 */ new("Operations", "Inventory Clerk", "Warehouse", 8, 21_000m, new(2021, 4, 5), "Regular"),
        /* 10 */ new("Operations", "Warehouse Staff", "Warehouse", 8, 18_000m, new(2022, 8, 15), "Regular"),
        /* 11 */ new("Operations", "Warehouse Staff", "Warehouse", 8, 18_000m, new(2024, 3, 11), "Regular"),
        /* 12 */ new("Operations", "Logistics Coordinator", "Logistics", 7, 26_000m, new(2023, 1, 16), "Regular"),
        /* 13 */ new("Operations", "Delivery Driver", "Logistics", 12, 19_000m, new(2026, 2, 2), "Probationary"),
        /* 14 */ new("Sales", "Sales Manager", null, 1, 88_000m, new(2019, 2, 18), "Regular"),
        /* 15 */ new("Sales", "Account Executive", "Metro Manila", 14, 28_000m, new(2021, 10, 4), "Regular"),
        /* 16 */ new("Sales", "Account Executive", "Metro Manila", 14, 28_000m, new(2023, 5, 22), "Regular"),
        /* 17 */ new("Sales", "Account Executive", "Provincial", 14, 26_000m, new(2026, 6, 1), "Probationary"),
        /* 18 */ new("IT", "IT Lead", null, 1, 80_000m, new(2020, 8, 10), "Regular"),
        /* 19 */ new("IT", "Software Developer", null, 18, 55_000m, new(2022, 10, 3), "Regular"),
        /* 20 */ new("IT", "Software Developer", null, 18, 42_000m, new(2024, 9, 9), "Regular"),
    ];
}
```

- [ ] **Step 4: The identities**

`tools/PeopleCore.DemoSeed/Plan/Identity.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

/// <summary>
/// Common Filipino names, combined at random into people who do not exist, and made-up numbers in
/// the shapes the government agencies use. Nothing here belongs to a real person.
/// </summary>
public static class Identity
{
    public static readonly IReadOnlyList<string> MaleFirstNames =
    [
        "Jose", "Juan", "Mark", "John Paul", "Christian", "Michael", "Carlo", "Rafael", "Miguel", "Paolo",
        "Angelo", "Jerome", "Joshua", "Kenneth", "Ramon", "Emmanuel", "Adrian", "Gabriel", "Nestor", "Rodel",
    ];

    public static readonly IReadOnlyList<string> FemaleFirstNames =
    [
        "Maria", "Ana", "Kristine", "Angelica", "Jasmine", "Maricel", "Rowena", "Camille", "Patricia", "Jennifer",
        "Nicole", "Katherine", "Liza", "Mary Grace", "Joy", "Rosalie", "Charmaine", "Divina", "Aileen", "Bea",
    ];

    public static readonly IReadOnlyList<string> Surnames =
    [
        "Santos", "Reyes", "Cruz", "Bautista", "Ocampo", "Garcia", "Mendoza", "Torres", "Tomas", "Andrada",
        "Castillo", "Flores", "Villanueva", "Ramos", "Castro", "Rivera", "Aquino", "Navarro", "Salazar", "Mercado",
        "Dela Cruz", "De Leon", "Pascual", "Gonzales", "Aguilar", "Manalo", "Dizon", "Soriano", "Lopez", "Fernandez",
    ];

    private static readonly string[] Relationships = ["Spouse", "Mother", "Father", "Sister", "Brother"];

    public static string Pick(Random rng, IReadOnlyList<string> from) => from[rng.Next(from.Count)];

    public static string Digits(Random rng, int count) =>
        string.Concat(Enumerable.Range(0, count).Select(_ => rng.Next(10)));

    public static string Mobile(Random rng) => "09" + Digits(rng, 9);

    public static GovernmentIds GovernmentIds(Random rng) => new(
        Sss: $"{Digits(rng, 2)}-{Digits(rng, 7)}-{Digits(rng, 1)}",
        PhilHealth: $"{Digits(rng, 2)}-{Digits(rng, 9)}-{Digits(rng, 1)}",
        PagIbig: $"{Digits(rng, 4)}-{Digits(rng, 4)}-{Digits(rng, 4)}",
        Tin: $"{Digits(rng, 3)}-{Digits(rng, 3)}-{Digits(rng, 3)}-000");

    public static EmergencyContact Contact(Random rng, string lastName)
    {
        var relationship = Pick(rng, Relationships);
        var female = relationship is "Mother" or "Sister" || (relationship == "Spouse" && rng.Next(2) == 0);
        var first = Pick(rng, female ? FemaleFirstNames : MaleFirstNames);
        return new EmergencyContact($"{first} {lastName}", relationship, Mobile(rng));
    }

    /// <summary>"Mary Grace Dela Cruz" becomes "marygrace.delacruz".</summary>
    public static string EmailLocalPart(string firstName, string lastName) =>
        $"{Squash(firstName)}.{Squash(lastName)}";

    private static string Squash(string name) =>
        new(name.ToLowerInvariant().Where(char.IsAsciiLetterLower).ToArray());
}
```

- [ ] **Step 5: The people**

`tools/PeopleCore.DemoSeed/Plan/People.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

public record GovernmentIds(string Sss, string PhilHealth, string PagIbig, string Tin);

public record EmergencyContact(string Name, string Relationship, string Phone);

public record Person(
    int Number, string EmployeeNumber, string FirstName, string? MiddleName, string LastName, string Gender,
    DateOnly BirthDate, string CivilStatus, string Mobile, string Email, string Department, string Title,
    string? Team, int? ManagerNumber, decimal MonthlySalary, DateOnly HireDate, string EmploymentStatus,
    string Role, string TaxCode, int Dependents, GovernmentIds Ids, EmergencyContact Contact)
{
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>The first day this person appears in the demo year.</summary>
    public DateOnly ActiveFrom => HireDate > Calendar.Start ? HireDate : Calendar.Start;
}

public static class PeopleBuilder
{
    public const string EmailDomain = "bayanihantrading.example";

    public static IReadOnlyList<Person> Build(Random rng)
    {
        var seats = Roster.Seats;
        var managers = seats.Where(s => s.ManagerNumber is not null).Select(s => s.ManagerNumber!.Value).ToHashSet();
        var usedNames = new HashSet<string>();
        var usedEmails = new HashSet<string>();
        var people = new List<Person>();

        for (var i = 0; i < seats.Count; i++)
        {
            var seat = seats[i];
            var number = i + 1;
            var gender = rng.Next(2) == 0 ? "Male" : "Female";

            string first, last;
            do
            {
                first = Identity.Pick(rng, gender == "Male" ? Identity.MaleFirstNames : Identity.FemaleFirstNames);
                last = Identity.Pick(rng, Identity.Surnames);
            } while (!usedNames.Add($"{first} {last}"));

            var middle = Identity.Pick(rng, Identity.Surnames);
            var age = seat.MonthlySalary >= 80_000m ? rng.Next(38, 55) : rng.Next(23, 40);
            var birth = new DateOnly(2026 - age, rng.Next(1, 13), rng.Next(1, 29));
            if (birth > seat.HireDate.AddYears(-21)) birth = seat.HireDate.AddYears(-21 - rng.Next(0, 5));

            var married = age >= 30 && rng.Next(3) != 0;
            var dependents = married ? rng.Next(0, 4) : 0;

            var local = Identity.EmailLocalPart(first, last);
            var email = $"{local}@{EmailDomain}";
            for (var n = 2; !usedEmails.Add(email); n++) email = $"{local}{n}@{EmailDomain}";

            var role = seat.Title == "HR Manager" ? "HRManager" : managers.Contains(number) ? "Manager" : "Employee";

            people.Add(new Person(
                number, $"DEMO-{number:0000}", first, middle == last ? null : middle, last, gender, birth,
                married ? "Married" : "Single", Identity.Mobile(rng), email, seat.Department, seat.Title, seat.Team,
                seat.ManagerNumber, seat.MonthlySalary, seat.HireDate, seat.EmploymentStatus, role,
                married ? "ME" : "S", dependents, Identity.GovernmentIds(rng), Identity.Contact(rng, last)));
        }

        return people;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: PASS, 14 tests.

If `SalariesAndHireDates_AreBelievable` fails on the birth-date rule, check the clamp in step 5 rather than loosening the test.

- [ ] **Step 7: Commit**

```bash
git add tools/PeopleCore.DemoSeed/Plan tests/PeopleCore.DemoSeed.Tests/PeopleTests.cs
git commit -m "feat(demo): twenty fictional people with Filipino names and made-up IDs"
```

---
### Task 3: Leave and overtime plans

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Plan/Picking.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/LeavePlanner.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/OvertimePlanner.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/LeavePlannerTests.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/OvertimePlannerTests.cs`

**Interfaces:**
- Consumes: `Calendar`, `Person`, `PeopleBuilder` (Tasks 1–2).
- Produces, in namespace `PeopleCore.DemoSeed.Plan`:
  - `enum Decision { Approved, Rejected, Pending }`
  - `record LeaveFiling(int PersonNumber, string TypeCode, DateOnly Start, DateOnly End, Decision Decision, int FiledInMonth)`
    - `int Days` is the number of work days from `Start` to `End`.
  - `static class LeavePlanner`
    - `decimal DaysPerMonth = 1.25m`
    - `IReadOnlyList<int> PendingFor` = persons 3, 5, 10 and 15
    - `decimal Accrued(Person person, int month)`
    - `IReadOnlyList<LeaveFiling> Plan(IReadOnlyList<Person> people, DateOnly today, Random rng)`
  - `record OvertimeFiling(int PersonNumber, DateOnly Date, TimeOnly Start, TimeOnly End, string Reason, Decision Decision)`
  - `static class OvertimePlanner`
    - `int ApprovedCount = 24`
    - `int PendingPerson = 3`
    - `IReadOnlyList<OvertimeFiling> Plan(IReadOnlyList<Person> people, DateOnly today, IReadOnlyList<LeaveFiling> leave, Random rng)`
  - `static class Picking`
    - `DateOnly? RandomWorkDay(Random rng, DateOnly from, DateOnly to)`
    - `IReadOnlyList<DateOnly> ConsecutiveWorkDays(DateOnly start, int count)`

The API's rules shape these plans:
- **Balance check at filing.** A leave request is refused when the balance can't cover it. Balances only grow through monthly accruals of 15 ÷ 12 = 1.25 days per type.
  - For the check, a filing counts every day already planned for that type, whatever its date, against what has accrued by the filing's own month. That is stricter than the real balance, so it can never over-draw.
  - People hired in 2026 are assumed to start accruing the month after they join.
- **No weekends or holidays in a filing.** A filing never spans one, so however the API counts days, it gets the same number.
- **Pending requests fall to the client.** They are for people whose requests the client, the HR Manager, may decide:
  - The client holds `approvals.all`, so any pending **leave** is theirs to decide.
  - Pending **overtime** must come from a direct report of the HR Manager, which is person 3, the HR Officer. The overtime rules accept only the direct manager's decision.

- [ ] **Step 1: Write the failing tests**

`tests/PeopleCore.DemoSeed.Tests/LeavePlannerTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// Leave the API will accept: work days only, never overlapping, never more than has accrued. A few
/// requests are left pending for the client to decide.
/// </summary>
public class LeavePlannerTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static (IReadOnlyList<Person> People, IReadOnlyList<LeaveFiling> Leave) Planned(int seed = 20260918)
    {
        var rng = new Random(seed);
        var people = PeopleBuilder.Build(rng);
        return (people, LeavePlanner.Plan(people, Today, rng));
    }

    [Fact]
    public void Filings_CoverOnlyWorkDays_AndDoNotOverlapForOnePerson()
    {
        var (_, leave) = Planned();

        foreach (var filing in leave)
        {
            var calendarDays = filing.End.DayNumber - filing.Start.DayNumber + 1;
            filing.Days.Should().Be(calendarDays, "a filing never spans a weekend or holiday");
        }

        foreach (var person in leave.GroupBy(f => f.PersonNumber))
        {
            var days = person.SelectMany(f => Calendar.WorkDays(f.Start, f.End)).ToList();
            days.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void NobodyTakesLeaveBeforeTheyStarted()
    {
        var (people, leave) = Planned();

        leave.Should().OnlyContain(f => f.Start >= people[f.PersonNumber - 1].ActiveFrom);
    }

    [Fact]
    public void InDateOrder_NoFilingEverExceedsWhatHasAccrued()
    {
        var (people, leave) = Planned();

        foreach (var group in leave.Where(f => f.Decision != Decision.Rejected).GroupBy(f => (f.PersonNumber, f.TypeCode)))
        {
            var person = people[group.Key.PersonNumber - 1];
            decimal used = 0;
            foreach (var filing in group.OrderBy(f => f.FiledInMonth).ThenBy(f => f.Start))
            {
                used += filing.Days;
                used.Should().BeLessThanOrEqualTo(LeavePlanner.Accrued(person, filing.FiledInMonth));
            }
        }
    }

    [Fact]
    public void DecidedFilings_AreInThePast_AndFiledInTheirOwnMonth()
    {
        var (_, leave) = Planned();

        foreach (var filing in leave.Where(f => f.Decision != Decision.Pending))
        {
            filing.End.Should().BeBefore(Today);
            filing.FiledInMonth.Should().Be(filing.Start.Month);
        }
    }

    [Fact]
    public void PendingFilings_AreFiledThisMonth_ForLaterDates_ByTheChosenPeople()
    {
        var (_, leave) = Planned();
        var pending = leave.Where(f => f.Decision == Decision.Pending).ToList();

        pending.Select(f => f.PersonNumber).Should().BeEquivalentTo(LeavePlanner.PendingFor);
        pending.Should().OnlyContain(f => f.FiledInMonth == Today.Month && f.Start > Today);
    }

    [Fact]
    public void TwoFilings_AreRejected()
    {
        Planned().Leave.Count(f => f.Decision == Decision.Rejected).Should().Be(2);
    }

    [Fact]
    public void EachPerson_FilesAFewTimes()
    {
        var (people, leave) = Planned();

        foreach (var person in people.Where(p => p.HireDate.Year < 2026))
            leave.Count(f => f.PersonNumber == person.Number && f.Decision != Decision.Pending).Should().BeInRange(2, 6);
    }

    [Fact]
    public void Accrual_StartsTheMonthAfterA2026Hire()
    {
        var (people, _) = Planned();
        var driver = people.Single(p => p.EmployeeNumber == "DEMO-0013"); // hired 2 February 2026

        LeavePlanner.Accrued(driver, 2).Should().Be(0);
        LeavePlanner.Accrued(driver, 3).Should().Be(1.25m);
        LeavePlanner.Accrued(people[0], 1).Should().Be(1.25m);
        LeavePlanner.Accrued(people[0], 9).Should().Be(11.25m);
    }
}
```

`tests/PeopleCore.DemoSeed.Tests/OvertimePlannerTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Evening overtime in Operations and IT, plus two for the client to decide.</summary>
public class OvertimePlannerTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static (IReadOnlyList<Person> People, IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime) Planned()
    {
        var rng = new Random(20260918);
        var people = PeopleBuilder.Build(rng);
        var leave = LeavePlanner.Plan(people, Today, rng);
        return (people, leave, OvertimePlanner.Plan(people, Today, leave, rng));
    }

    [Fact]
    public void TwentyFourAreApproved_FromOperationsAndItStaffWithAManager()
    {
        var (people, _, overtime) = Planned();
        var approved = overtime.Where(o => o.Decision == Decision.Approved).ToList();

        approved.Should().HaveCount(OvertimePlanner.ApprovedCount);
        approved.Should().OnlyContain(o =>
            (people[o.PersonNumber - 1].Department == "Operations" || people[o.PersonNumber - 1].Department == "IT")
            && people[o.PersonNumber - 1].ManagerNumber != null);
    }

    [Fact]
    public void TwoArePending_FromTheHrOfficer_OnPastWorkDaysThisMonth()
    {
        var (_, _, overtime) = Planned();
        var pending = overtime.Where(o => o.Decision == Decision.Pending).ToList();

        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(o => o.PersonNumber == OvertimePlanner.PendingPerson
            && o.Date < Today && o.Date.Month == Today.Month && Calendar.IsWorkDay(o.Date));
    }

    [Fact]
    public void Overtime_IsOnWorkDays_InTheEvening_NeverOnLeave_AndOncePerPersonPerDay()
    {
        var (people, leave, overtime) = Planned();
        var onLeave = leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => (l.PersonNumber, d))).ToHashSet();

        foreach (var o in overtime)
        {
            Calendar.IsWorkDay(o.Date).Should().BeTrue();
            o.Date.Should().BeOnOrAfter(people[o.PersonNumber - 1].ActiveFrom);
            o.Start.Should().Be(new TimeOnly(17, 0));
            (o.End.ToTimeSpan() - o.Start.ToTimeSpan()).TotalHours.Should().BeInRange(2, 4);
            onLeave.Should().NotContain((o.PersonNumber, o.Date));
        }

        overtime.Select(o => (o.PersonNumber, o.Date)).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo --filter "FullyQualifiedName~LeavePlanner|FullyQualifiedName~OvertimePlanner"`
Expected: FAIL to compile.

- [ ] **Step 3: Picking helpers**

`tools/PeopleCore.DemoSeed/Plan/Picking.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

public static class Picking
{
    /// <summary>A random work day in [from, to], or null when there is none.</summary>
    public static DateOnly? RandomWorkDay(Random rng, DateOnly from, DateOnly to)
    {
        var days = Calendar.WorkDays(from, to).ToList();
        return days.Count == 0 ? null : days[rng.Next(days.Count)];
    }

    /// <summary>Up to <paramref name="count"/> work days from <paramref name="start"/>, stopping at the first day off.</summary>
    public static IReadOnlyList<DateOnly> ConsecutiveWorkDays(DateOnly start, int count)
    {
        var days = new List<DateOnly>();
        for (var day = start; days.Count < count && Calendar.IsWorkDay(day); day = day.AddDays(1))
            days.Add(day);
        return days;
    }
}
```

- [ ] **Step 4: The leave planner**

`tools/PeopleCore.DemoSeed/Plan/LeavePlanner.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

public enum Decision { Approved, Rejected, Pending }

public record LeaveFiling(int PersonNumber, string TypeCode, DateOnly Start, DateOnly End, Decision Decision, int FiledInMonth)
{
    public int Days => Calendar.WorkDays(Start, End).Count();
}

/// <summary>
/// Vacation and sick leave that the API will accept. The balance check counts every day planned
/// for a type, whatever its date, against what has accrued by the filing's own month. That is
/// stricter than the real balance, so a filing can never be refused for lack of days.
/// </summary>
public static class LeavePlanner
{
    public const decimal DaysPerMonth = 1.25m;

    /// <summary>
    /// People who leave a request pending for the client. The client holds approvals.all, so any of
    /// these is theirs to decide.
    /// </summary>
    public static readonly IReadOnlyList<int> PendingFor = [3, 5, 10, 15];

    /// <summary>
    /// Days of one type accrued by the end of <paramref name="month"/>. People already on staff accrue
    /// from January. People hired in 2026 are assumed to start the month after they join.
    /// </summary>
    public static decimal Accrued(Person person, int month)
    {
        var firstMonth = person.HireDate.Year < 2026 ? 1 : person.HireDate.Month + 1;
        return month < firstMonth ? 0 : (month - firstMonth + 1) * DaysPerMonth;
    }

    public static IReadOnlyList<LeaveFiling> Plan(IReadOnlyList<Person> people, DateOnly today, Random rng)
    {
        var filings = new List<LeaveFiling>();

        foreach (var person in people)
        {
            var taken = new HashSet<DateOnly>();
            var used = new Dictionary<string, decimal> { ["VL"] = 0, ["SL"] = 0 };
            var wanted = person.HireDate.Year == 2026 ? rng.Next(1, 3) : rng.Next(2, 7);
            var mine = 0;
            // Someone who will leave a request pending keeps two vacation days back for it.
            var reserve = PendingFor.Contains(person.Number) ? 2 : 0;

            for (var attempt = 0; mine < wanted && attempt < 300; attempt++)
            {
                var type = rng.Next(3) == 0 ? "SL" : "VL";
                var start = Picking.RandomWorkDay(rng, person.ActiveFrom, today.AddDays(-1));
                if (start is null) break;

                var days = Picking.ConsecutiveWorkDays(start.Value, type == "SL" ? rng.Next(1, 3) : rng.Next(1, 4));
                if (days.Any(taken.Contains) || days[^1] >= today) continue;
                if (used[type] + days.Count + (type == "VL" ? reserve : 0) > Accrued(person, start.Value.Month)) continue;

                used[type] += days.Count;
                taken.UnionWith(days);
                filings.Add(new LeaveFiling(person.Number, type, days[0], days[^1], Decision.Approved, start.Value.Month));
                mine++;
            }

            if (!PendingFor.Contains(person.Number)) continue;

            for (var attempt = 0; attempt < 300; attempt++)
            {
                var start = Picking.RandomWorkDay(rng, today.AddDays(3), today.AddDays(14));
                if (start is null) break;

                var days = Picking.ConsecutiveWorkDays(start.Value, rng.Next(1, 3));
                if (days.Any(taken.Contains) || used["VL"] + days.Count > Accrued(person, today.Month)) continue;

                used["VL"] += days.Count;
                taken.UnionWith(days);
                filings.Add(new LeaveFiling(person.Number, "VL", days[0], days[^1], Decision.Pending, today.Month));
                break;
            }
        }

        // Two decided filings are turned down. They are picked from people who filed at least three
        // times, so nobody's whole year is refusals.
        var candidates = filings
            .Select((f, i) => (f, i))
            .Where(x => x.f.Decision == Decision.Approved && filings.Count(o => o.PersonNumber == x.f.PersonNumber) >= 3)
            .Select(x => x.i)
            .ToList();
        foreach (var index in candidates.OrderBy(_ => rng.Next()).Take(2))
            filings[index] = filings[index] with { Decision = Decision.Rejected };

        return filings.OrderBy(f => f.FiledInMonth).ThenBy(f => f.Start).ThenBy(f => f.PersonNumber).ToList();
    }
}
```

- [ ] **Step 5: The overtime planner**

`tools/PeopleCore.DemoSeed/Plan/OvertimePlanner.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

public record OvertimeFiling(int PersonNumber, DateOnly Date, TimeOnly Start, TimeOnly End, string Reason, Decision Decision);

/// <summary>
/// Evening overtime from 17:00. Approved requests come from Operations and IT staff, since only
/// someone with a manager can have overtime approved. Two pending requests come from the HR Officer,
/// whose manager is the client: the API lets only the direct manager decide overtime.
/// </summary>
public static class OvertimePlanner
{
    public const int ApprovedCount = 24;
    public const int PendingPerson = 3;

    private static readonly string[] Reasons =
    [
        "Month-end inventory count", "Urgent delivery schedule", "System maintenance window",
        "Client deliverable deadline", "Payroll cut-off support", "Warehouse restocking",
    ];

    public static IReadOnlyList<OvertimeFiling> Plan(
        IReadOnlyList<Person> people, DateOnly today, IReadOnlyList<LeaveFiling> leave, Random rng)
    {
        var onLeave = leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => (l.PersonNumber, d)))
            .ToHashSet();
        var eligible = people
            .Where(p => p.Department is "Operations" or "IT" && p.ManagerNumber is not null)
            .ToList();
        var filings = new List<OvertimeFiling>();
        var used = new HashSet<(int, DateOnly)>();

        while (filings.Count < ApprovedCount)
        {
            var person = eligible[rng.Next(eligible.Count)];
            var date = Picking.RandomWorkDay(rng, person.ActiveFrom, today.AddDays(-1));
            if (date is null || onLeave.Contains((person.Number, date.Value)) || !used.Add((person.Number, date.Value))) continue;

            filings.Add(Evening(person.Number, date.Value, rng, Decision.Approved));
        }

        // The HR Officer's two most recent work days this month, worked late and awaiting the client.
        var recent = Calendar.WorkDays(new DateOnly(today.Year, today.Month, 1), today.AddDays(-1))
            .Where(d => !onLeave.Contains((PendingPerson, d)) && !used.Contains((PendingPerson, d)))
            .TakeLast(2);
        foreach (var date in recent)
            filings.Add(Evening(PendingPerson, date, rng, Decision.Pending));

        return filings.OrderBy(f => f.Date).ThenBy(f => f.PersonNumber).ToList();
    }

    private static OvertimeFiling Evening(int person, DateOnly date, Random rng, Decision decision)
    {
        var start = new TimeOnly(17, 0);
        return new OvertimeFiling(person, date, start, start.AddHours(rng.Next(2, 5)), Reasons[rng.Next(Reasons.Length)], decision);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: PASS, including the 11 new tests.

If `TwoArePending_FromTheHrOfficer…` fails because the run date is too early in the month to have two past work days, don't loosen the test. The rehearsal date and the production date are both late in September.

- [ ] **Step 7: Commit**

```bash
git add tools/PeopleCore.DemoSeed/Plan tests/PeopleCore.DemoSeed.Tests
git commit -m "feat(demo): plan leave and overtime the API will accept"
```

---

### Task 4: Attendance, reviews, applicants, and the whole plan

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Plan/AttendancePlanner.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/PerformancePlanner.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/RecruitmentPlanner.cs`
- Create: `tools/PeopleCore.DemoSeed/Plan/DemoPlan.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/AttendancePlannerTests.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/PerformanceAndRecruitmentTests.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/DemoPlanTests.cs`

**Interfaces:**
- Consumes everything from Tasks 1–3.
- Produces, in namespace `PeopleCore.DemoSeed.Plan`:
  - `record AttendanceRow(string EmployeeNumber, DateOnly Date, TimeOnly TimeIn, TimeOnly TimeOut)`
  - `static class AttendancePlanner`
    - `IReadOnlyList<AttendanceRow> ForMonth(IReadOnlyList<Person> people, int month, DateOnly today, IReadOnlyList<LeaveFiling> leave, IReadOnlyList<OvertimeFiling> overtime, int seed)`
    - `string ToCsv(IEnumerable<AttendanceRow> rows)`
  - `record KpiPlan(string Description, string Target, decimal Weight, string Actual, decimal SelfScore, decimal ManagerScore)`
  - `record ReviewPlan(int PersonNumber, int ReviewerNumber, IReadOnlyList<KpiPlan> Kpis, decimal SelfScore, string SelfComment, decimal? ManagerScore, string? ManagerComment)`
    - `bool AwaitsManager => ManagerScore is null`
  - `static class PerformancePlanner`
    - `const string CycleName = "2026 Mid-Year Review"`
    - `DateOnly CycleStart` (2026-01-01) and `DateOnly CycleEnd` (2026-06-30)
    - `IReadOnlyList<ReviewPlan> Plan(IReadOnlyList<Person> people, Random rng)`
  - `record PostingPlan(string Title, string Department, string Description, string Requirements, int Vacancies, bool CloseAfterApplicants)`
  - `record ApplicantPlan(int PostingIndex, string FirstName, string LastName, string Email, string Phone, string Status, string? InterviewStage, DateTime? InterviewAt)`
  - `static class RecruitmentPlanner`
    - `IReadOnlyList<PostingPlan> Postings`
    - `IReadOnlyList<ApplicantPlan> Applicants(IReadOnlyList<Person> people, DateOnly today, Random rng)`
  - `record DemoPlan(int Seed, DateOnly Today, IReadOnlyList<Person> People, IReadOnlyList<PayPeriod> PayPeriods, IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime, IReadOnlyList<ReviewPlan> Reviews, IReadOnlyList<ApplicantPlan> Applicants)`
    - `static DemoPlan Build(int seed, DateOnly today)`
    - `IReadOnlyList<int> Months`: January to today's month
    - `IReadOnlyList<AttendanceRow> AttendanceFor(int month)`
    - `Person PersonNumber(int number)`

- [ ] **Step 1: Write the failing tests**

`tests/PeopleCore.DemoSeed.Tests/AttendancePlannerTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Clock-ins that look like a real office: mostly on time, some late, now and then absent.</summary>
public class AttendancePlannerTests
{
    private static readonly DemoPlan Plan = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));

    [Fact]
    public void Rows_FallOnWorkDays_OnlyWhileEmployed_AndBeforeToday()
    {
        foreach (var month in Plan.Months)
        foreach (var row in Plan.AttendanceFor(month))
        {
            Calendar.IsWorkDay(row.Date).Should().BeTrue();
            row.Date.Month.Should().Be(month);
            row.Date.Should().BeBefore(Plan.Today);
            row.Date.Should().BeOnOrAfter(Plan.People.Single(p => p.EmployeeNumber == row.EmployeeNumber).ActiveFrom);
        }
    }

    [Fact]
    public void NobodyClocksInOnApprovedLeave()
    {
        var onLeave = Plan.Leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => ($"DEMO-{l.PersonNumber:0000}", d)))
            .ToHashSet();

        Plan.Months.SelectMany(Plan.AttendanceFor)
            .Should().OnlyContain(r => !onLeave.Contains((r.EmployeeNumber, r.Date)));
    }

    [Fact]
    public void OvertimeDays_ClockOutAtOrAfterTheOvertimeEnd()
    {
        var rows = Plan.Months.SelectMany(Plan.AttendanceFor).ToDictionary(r => (r.EmployeeNumber, r.Date));

        foreach (var o in Plan.Overtime)
            rows[($"DEMO-{o.PersonNumber:0000}", o.Date)].TimeOut.Should().BeOnOrAfter(o.End);
    }

    [Fact]
    public void AMonth_HasSomeLates_AndSomeAbsences()
    {
        var march = Plan.AttendanceFor(3);
        var expected = Plan.People.Sum(p => Calendar.WorkDays(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))
            .Count(d => d >= p.ActiveFrom));

        march.Count(r => r.TimeIn > new TimeOnly(8, 0)).Should().BeGreaterThan(0);
        march.Count.Should().BeLessThan(expected, "someone is absent or on leave");
    }

    [Fact]
    public void TheCsv_HasTheImportHeader_AndOneLinePerRow()
    {
        var rows = Plan.AttendanceFor(1).Take(2).ToList();

        var csv = AttendancePlanner.ToCsv(rows).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        csv[0].Should().Be("employee_number,date,time_in,time_out");
        csv[1].Should().MatchRegex(@"^DEMO-\d{4},2026-01-\d{2},\d{2}:\d{2},\d{2}:\d{2}$");
        csv.Should().HaveCount(3);
    }

    [Fact]
    public void TheSameMonth_IsTheSameEveryTime()
    {
        DemoPlan.Build(20260918, Plan.Today).AttendanceFor(5).Should().Equal(Plan.AttendanceFor(5));
    }
}
```

`tests/PeopleCore.DemoSeed.Tests/PerformanceAndRecruitmentTests.cs`:

```csharp
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
```

`tests/PeopleCore.DemoSeed.Tests/DemoPlanTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

public class DemoPlanTests
{
    [Fact]
    public void TheSameSeedAndDay_BuildTheSamePlan()
    {
        var a = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));
        var b = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));

        b.People.Should().BeEquivalentTo(a.People);
        b.Leave.Should().Equal(a.Leave);
        b.Overtime.Should().Equal(a.Overtime);
        b.Applicants.Should().Equal(a.Applicants);
    }

    [Fact]
    public void Months_RunFromJanuaryToTodaysMonth()
    {
        DemoPlan.Build(1, new DateOnly(2026, 9, 18)).Months.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Theory]
    [InlineData(2026, 1, 20)]
    [InlineData(2027, 3, 1)]
    public void ADayTheCalendarCannotCover_IsRefused(int year, int month, int day)
    {
        var act = () => DemoPlan.Build(1, new DateOnly(year, month, day));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: FAIL to compile.

- [ ] **Step 3: The attendance planner**

`tools/PeopleCore.DemoSeed/Plan/AttendancePlanner.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace PeopleCore.DemoSeed.Plan;

public record AttendanceRow(string EmployeeNumber, DateOnly Date, TimeOnly TimeIn, TimeOnly TimeOut);

/// <summary>
/// One month of clock-ins in the shape api/attendance/import reads. About one day in fifty is an
/// absence, one in ten a late arrival, one in thirty an early exit. A day of approved leave has no
/// row. An overtime day clocks out after the overtime ends. Each month has its own random stream,
/// so building a month never depends on having built another.
/// </summary>
public static class AttendancePlanner
{
    public static IReadOnlyList<AttendanceRow> ForMonth(
        IReadOnlyList<Person> people, int month, DateOnly today,
        IReadOnlyList<LeaveFiling> leave, IReadOnlyList<OvertimeFiling> overtime, int seed)
    {
        var rng = new Random(unchecked(seed * 100 + month));
        var first = new DateOnly(2026, month, 1);
        var monthEnd = first.AddMonths(1).AddDays(-1);
        var last = monthEnd < today ? monthEnd : today.AddDays(-1);

        var onLeave = leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => (l.PersonNumber, d)))
            .ToHashSet();
        var overtimeEnds = overtime.ToDictionary(o => (o.PersonNumber, o.Date), o => o.End);
        var rows = new List<AttendanceRow>();

        foreach (var person in people)
        {
            var from = person.ActiveFrom > first ? person.ActiveFrom : first;
            foreach (var day in Calendar.WorkDays(from, last))
            {
                var roll = rng.NextDouble();
                if (onLeave.Contains((person.Number, day))) continue;

                var worksLate = overtimeEnds.TryGetValue((person.Number, day), out var overtimeEnd);
                if (!worksLate && roll < 0.02) continue;

                var timeIn = roll < 0.12
                    ? new TimeOnly(8, 5).AddMinutes(rng.Next(0, 26))
                    : new TimeOnly(7, 40).AddMinutes(rng.Next(0, 21));
                var timeOut = worksLate
                    ? overtimeEnd.AddMinutes(rng.Next(0, 11))
                    : rng.NextDouble() < 0.03
                        ? new TimeOnly(16, 0).AddMinutes(rng.Next(0, 46))
                        : new TimeOnly(17, 0).AddMinutes(rng.Next(0, 26));

                rows.Add(new AttendanceRow(person.EmployeeNumber, day, timeIn, timeOut));
            }
        }

        return rows;
    }

    public static string ToCsv(IEnumerable<AttendanceRow> rows)
    {
        var csv = new StringBuilder("employee_number,date,time_in,time_out\n");
        foreach (var row in rows)
            csv.Append(row.EmployeeNumber).Append(',')
               .Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
               .Append(row.TimeIn.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(',')
               .Append(row.TimeOut.ToString("HH:mm", CultureInfo.InvariantCulture)).Append('\n');
        return csv.ToString();
    }
}
```

- [ ] **Step 4: The performance planner**

`tools/PeopleCore.DemoSeed/Plan/PerformancePlanner.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

public record KpiPlan(string Description, string Target, decimal Weight, string Actual, decimal SelfScore, decimal ManagerScore);

public record ReviewPlan(
    int PersonNumber, int ReviewerNumber, IReadOnlyList<KpiPlan> Kpis,
    decimal SelfScore, string SelfComment, decimal? ManagerScore, string? ManagerComment)
{
    public bool AwaitsManager => ManagerScore is null;
}

/// <summary>
/// The first-half review, scored 1 to 5. Each person is reviewed by their manager. The General
/// Manager has none, so the HR Manager reviews them. Reviews the HR Manager owns are left awaiting
/// the manager's part, because the HR Manager is the client, who can then finish them in the demo.
/// </summary>
public static class PerformancePlanner
{
    public const string CycleName = "2026 Mid-Year Review";
    public static readonly DateOnly CycleStart = new(2026, 1, 1);
    public static readonly DateOnly CycleEnd = new(2026, 6, 30);
    private static readonly DateOnly OnStaffBy = new(2026, 3, 31);

    private static readonly Dictionary<string, (string Description, string Target, string Actual)[]> Kpis = new()
    {
        ["Executive"] = [("Revenue growth", "12% year on year", "10.5%"), ("Operating margin", "18%", "17.2%"), ("Key hires filled", "4", "3")],
        ["Human Resources"] = [("Time to fill", "30 days", "34 days"), ("Payroll accuracy", "100%", "99.8%"), ("Training hours per employee", "16", "14")],
        ["Finance"] = [("Month-end close", "5 working days", "5 days"), ("Receivables over 60 days", "Under 10%", "8%"), ("Audit findings", "0 major", "0 major")],
        ["Operations"] = [("On-time deliveries", "95%", "93%"), ("Inventory accuracy", "99%", "98.6%"), ("Safety incidents", "0", "0")],
        ["Sales"] = [("Sales against quota", "100%", "96%"), ("New accounts", "12", "11"), ("Collection rate", "95%", "94%")],
        ["IT"] = [("System uptime", "99.5%", "99.7%"), ("Tickets resolved within SLA", "90%", "92%"), ("Projects delivered", "3", "3")],
    };

    private static readonly decimal[] Weights = [40, 30, 30];

    private static readonly string[] SelfComments =
    [
        "Met most targets; want to improve on turnaround time.", "A strong half. I took on extra work during peak season.",
        "Learned a lot this half and ready for more responsibility.", "Some targets slipped in March; recovered by June.",
    ];

    private static readonly string[] ManagerComments =
    [
        "Reliable and consistent. Keep it up.", "Good progress; focus on the targets that slipped.",
        "Exceeded expectations on key deliverables.", "Solid half. Ready for a stretch assignment.",
    ];

    public static IReadOnlyList<ReviewPlan> Plan(IReadOnlyList<Person> people, Random rng)
    {
        var reviews = new List<ReviewPlan>();
        foreach (var person in people.Where(p => p.HireDate <= OnStaffBy))
        {
            var reviewer = person.ManagerNumber ?? 2;
            var kpis = Kpis[person.Department]
                .Select((k, i) => new KpiPlan(k.Description, k.Target, Weights[i], k.Actual, Score(rng), Score(rng)))
                .ToList();
            var self = Weighted(kpis, k => k.SelfScore);
            var awaits = reviewer == 2;

            reviews.Add(new ReviewPlan(
                person.Number, reviewer, kpis, self, SelfComments[rng.Next(SelfComments.Length)],
                awaits ? null : Weighted(kpis, k => k.ManagerScore),
                awaits ? null : ManagerComments[rng.Next(ManagerComments.Length)]));
        }
        return reviews;
    }

    private static decimal Score(Random rng) => Math.Round(3.0m + (decimal)rng.NextDouble() * 1.8m, 1);

    private static decimal Weighted(IReadOnlyList<KpiPlan> kpis, Func<KpiPlan, decimal> score) =>
        Math.Round(kpis.Sum(k => score(k) * k.Weight) / 100m, 1);
}
```

- [ ] **Step 5: The recruitment planner**

`tools/PeopleCore.DemoSeed/Plan/RecruitmentPlanner.cs`:

```csharp
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
```

- [ ] **Step 6: The whole plan**

`tools/PeopleCore.DemoSeed/Plan/DemoPlan.cs`:

```csharp
namespace PeopleCore.DemoSeed.Plan;

/// <summary>
/// Everything the seeder will create, decided before the first request is sent. The same seed and
/// the same day always give the same plan.
/// </summary>
public record DemoPlan(
    int Seed, DateOnly Today, IReadOnlyList<Person> People, IReadOnlyList<PayPeriod> PayPeriods,
    IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime,
    IReadOnlyList<ReviewPlan> Reviews, IReadOnlyList<ApplicantPlan> Applicants)
{
    /// <summary>
    /// The calendar knows only 2026's holidays, and a demo needs at least one completed month of
    /// history, so the plan can be built only between February and December 2026.
    /// </summary>
    public static DemoPlan Build(int seed, DateOnly today)
    {
        if (today < new DateOnly(2026, 2, 1) || today.Year != 2026)
            throw new ArgumentOutOfRangeException(nameof(today), today,
                "The demo calendar covers 2026 only, and needs at least one full month of history.");

        var rng = new Random(seed);
        var people = PeopleBuilder.Build(rng);
        var leave = LeavePlanner.Plan(people, today, rng);
        var overtime = OvertimePlanner.Plan(people, today, leave, rng);
        var reviews = PerformancePlanner.Plan(people, rng);
        var applicants = RecruitmentPlanner.Applicants(people, today, rng);

        return new DemoPlan(seed, today, people, Calendar.CompletedPayPeriods(today), leave, overtime, reviews, applicants);
    }

    public IReadOnlyList<int> Months => Enumerable.Range(1, Today.Month).ToList();

    public IReadOnlyList<AttendanceRow> AttendanceFor(int month) =>
        AttendancePlanner.ForMonth(People, month, Today, Leave, Overtime, Seed);

    public Person PersonNumber(int number) => People[number - 1];
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: PASS, including the 14 new tests.

- [ ] **Step 8: Commit**

```bash
git add tools/PeopleCore.DemoSeed/Plan tests/PeopleCore.DemoSeed.Tests
git commit -m "feat(demo): plan attendance, the mid-year review and recruitment"
```

---
### Task 5: The API client and the logins

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Api/SeedException.cs`
- Create: `tools/PeopleCore.DemoSeed/Api/ApiClient.cs`
- Create: `tools/PeopleCore.DemoSeed/Api/Logins.cs`
- Test: `tests/PeopleCore.DemoSeed.Tests/ApiClientTests.cs`

**Interfaces:**
- Produces, in namespace `PeopleCore.DemoSeed.Api`:
  - `class SeedException : Exception`, with `Step`, `Method`, `Path`, `Status` and `ApiMessage`. The message reads `"{step}: {method} {path} returned {status}: {apiMessage}"`.
  - `class AlreadySeededException : Exception`
  - `sealed class ApiClient(HttpClient http)`:
    - `Task<JsonNode?> GetAsync(string step, string path, string token)`
    - `Task<JsonNode?> PostAsync(string step, string path, object? body, string token)`
    - `Task<JsonNode?> PutAsync(string step, string path, object? body, string token)`
    - `Task<JsonNode?> PostCsvAsync(string step, string path, string csv, string token)` sends multipart form field `file`, named `attendance.csv`.
    - `Task<string> SignInAsync(string email, string password)` returns the token.
    - `static string ReadMessage(string body)` is internal, for the tests.
  - `sealed class Logins(ApiClient api)`:
    - `const int Admin = 0`
    - `Task AddAdminAsync(string email, string password)`
    - `Task CreateAsync(int personNumber, Guid employeeId, string email, string firstName, string lastName, string role)` creates the login, signs in with the temporary password, and changes it to a random one the program keeps in memory.
    - `Task<string> TokenAsync(int key)` re-signs in when the token is more than 60 minutes old.
    - `string UserIdOf(int personNumber)`
    - `static string NewPassword()` is internal, for the tests.
- Wire format: JSON uses `JsonSerializerDefaults.Web` (camelCase). `DateOnly` serialises as `yyyy-MM-dd` and `TimeOnly` as `HH:mm:ss`. The API reads enums as their names, so enum fields are sent as strings such as `"Male"` and `"SemiMonthly"`.

- [ ] **Step 1: Write the failing tests**

`tests/PeopleCore.DemoSeed.Tests/ApiClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using PeopleCore.DemoSeed.Api;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// The client stops on the first refused request, and says which step, which call, and what the
/// API said. It never repeats a password.
/// </summary>
public class ApiClientTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (ApiClient Client, Handler Handler) Client(HttpStatusCode status, string body)
    {
        var handler = new Handler(status, body);
        return (new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }), handler);
    }

    [Fact]
    public async Task ASuccessfulCall_ReturnsTheJson_AndSendsTheBearerToken()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """{"id":"abc"}""");

        var node = await client.PostAsync("Create thing", "api/things", new { name = "x" }, "tok");

        node!["id"]!.GetValue<string>().Should().Be("abc");
        handler.Last!.Headers.Authorization!.ToString().Should().Be("Bearer tok");
        handler.LastBody.Should().Be("""{"name":"x"}""");
    }

    [Fact]
    public async Task DatesAndTimes_TravelInTheApisFormat()
    {
        var (client, handler) = Client(HttpStatusCode.OK, "{}");

        await client.PostAsync("s", "api/x", new { day = new DateOnly(2026, 3, 9), at = new TimeOnly(8, 0) }, "t");

        handler.LastBody.Should().Be("""{"day":"2026-03-09","at":"08:00:00"}""");
    }

    [Fact]
    public async Task ARefusal_Throws_NamingTheStepCallStatusAndTheApisDetail()
    {
        var (client, _) = Client(HttpStatusCode.BadRequest, """{"title":"Nope","detail":"Insufficient leave balance.","status":400}""");

        var act = () => client.PostAsync("File leave for DEMO-0004", "api/leave-requests", new { }, "t");

        (await act.Should().ThrowAsync<SeedException>()).Which.Message
            .Should().Be("File leave for DEMO-0004: POST api/leave-requests returned 400: Insufficient leave balance.");
    }

    [Theory]
    [InlineData("""{"message":"Invalid credentials."}""", "Invalid credentials.")]
    [InlineData("""{"errors":{"Email":["The Email field is required."]}}""", "Email: The Email field is required.")]
    [InlineData("plain text failure", "plain text failure")]
    [InlineData("", "(no body)")]
    public void TheApisMessage_IsFoundWhereverItIs(string body, string expected)
    {
        ApiClient.ReadMessage(body).Should().Be(expected);
    }

    [Fact]
    public async Task AFailedSignIn_DoesNotRepeatThePassword()
    {
        var (client, _) = Client(HttpStatusCode.Unauthorized, """{"message":"Invalid credentials."}""");

        var act = () => client.SignInAsync("admin@example.test", "Sup3rSecret!");

        (await act.Should().ThrowAsync<SeedException>()).Which.Message.Should().NotContain("Sup3rSecret!");
    }

    [Fact]
    public async Task ACsvUpload_IsAMultipartFileNamedFile()
    {
        var (client, handler) = Client(HttpStatusCode.OK, "{}");

        await client.PostCsvAsync("Import", "api/attendance/import", "employee_number,date,time_in,time_out\n", "t");

        handler.Last!.Content!.Headers.ContentType!.MediaType.Should().Be("multipart/form-data");
        handler.LastBody.Should().Contain("name=file").And.Contain("filename=attendance.csv");
    }

    [Fact]
    public void GeneratedPasswords_MeetThePolicy_AndDiffer()
    {
        var a = Logins.NewPassword();
        var b = Logins.NewPassword();

        a.Length.Should().BeGreaterThanOrEqualTo(12);
        a.Should().MatchRegex(@"\d");
        a.Should().NotBe(b);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo --filter "FullyQualifiedName~ApiClientTests"`
Expected: FAIL to compile.

- [ ] **Step 3: The exceptions**

`tools/PeopleCore.DemoSeed/Api/SeedException.cs`:

```csharp
namespace PeopleCore.DemoSeed.Api;

/// <summary>A request the API refused. The run stops here; the message says what was being done.</summary>
public sealed class SeedException(string step, string method, string path, int status, string apiMessage)
    : Exception($"{step}: {method} {path} returned {status}: {apiMessage}")
{
    public string Step { get; } = step;
    public string Method { get; } = method;
    public string Path { get; } = path;
    public int Status { get; } = status;
    public string ApiMessage { get; } = apiMessage;
}

/// <summary>The site already holds the demo company. Nothing was changed.</summary>
public sealed class AlreadySeededException(string message) : Exception(message);
```

- [ ] **Step 4: The client**

`tools/PeopleCore.DemoSeed/Api/ApiClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PeopleCore.DemoSeed.Api;

/// <summary>
/// JSON over HTTP to one PeopleCore site. Every call names the step it belongs to, so a refusal
/// says what the seeder was doing when it happened. Request bodies are never repeated in an error:
/// some of them carry passwords.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<JsonNode?> GetAsync(string step, string path, string token) =>
        SendAsync(step, HttpMethod.Get, path, null, token);

    public Task<JsonNode?> PostAsync(string step, string path, object? body, string token) =>
        SendAsync(step, HttpMethod.Post, path, body is null ? null : JsonContent.Create(body, options: Json), token);

    public Task<JsonNode?> PutAsync(string step, string path, object? body, string token) =>
        SendAsync(step, HttpMethod.Put, path, body is null ? null : JsonContent.Create(body, options: Json), token);

    public Task<JsonNode?> PostCsvAsync(string step, string path, string csv, string token)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", "attendance.csv" } };
        return SendAsync(step, HttpMethod.Post, path, form, token);
    }

    public async Task<string> SignInAsync(string email, string password)
    {
        var node = await SendAsync($"Sign in as {email}", HttpMethod.Post, "api/auth/login",
            JsonContent.Create(new { email, password }, options: Json), token: null);
        return node?["token"]?.GetValue<string>()
            ?? throw new SeedException($"Sign in as {email}", "POST", "api/auth/login", 200, "No token in the response.");
    }

    private async Task<JsonNode?> SendAsync(string step, HttpMethod method, string path, HttpContent? content, string? token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new SeedException(step, method.Method, path, (int)response.StatusCode, ReadMessage(body));

        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    /// <summary>The most useful sentence in an error body: ProblemDetails detail, a message, validation errors, or the text itself.</summary>
    internal static string ReadMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";
        try
        {
            if (JsonNode.Parse(body) is JsonObject json)
            {
                if (json["detail"]?.GetValue<string>() is { Length: > 0 } detail) return detail;
                if (json["message"]?.GetValue<string>() is { Length: > 0 } message) return message;
                if (json["errors"] is JsonObject errors)
                    return string.Join("; ", errors.Select(e =>
                        $"{e.Key}: {string.Join(" ", e.Value!.AsArray().Select(v => v!.GetValue<string>()))}"));
                if (json["title"]?.GetValue<string>() is { Length: > 0 } title) return title;
            }
        }
        catch (JsonException)
        {
        }
        return body.Length > 300 ? body[..300] : body;
    }
}
```

- [ ] **Step 5: The logins**

`tools/PeopleCore.DemoSeed/Api/Logins.cs`:

```csharp
using System.Security.Cryptography;

namespace PeopleCore.DemoSeed.Api;

/// <summary>
/// The administrator's session and one session per demo employee. Leave, overtime and
/// self-evaluations can only be filed by the employee concerned, and decided only by their manager,
/// so the seeder signs in as them. Passwords are random, held in memory for this run only, and
/// never printed.
/// </summary>
public sealed class Logins(ApiClient api)
{
    public const int Admin = 0;
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(60);

    private sealed record Session(string Email, string Password, string? UserId, string Token, DateTimeOffset IssuedAt);

    private readonly Dictionary<int, Session> _sessions = new();

    public async Task AddAdminAsync(string email, string password) =>
        _sessions[Admin] = new Session(email, password, null, await api.SignInAsync(email, password), DateTimeOffset.UtcNow);

    public async Task CreateAsync(int personNumber, Guid employeeId, string email, string firstName, string lastName, string role)
    {
        var step = $"Create a login for DEMO-{personNumber:0000}";
        // Every account holds Employee already; only the extra role is named.
        string[] roles = role == "Employee" ? [] : [role];
        var created = await api.PostAsync(step, "api/users",
            new { email, firstName, lastName, employeeId, roles }, await TokenAsync(Admin));

        var userId = created!["account"]!["id"]!.GetValue<string>();
        var temporary = created["temporaryPassword"]!.GetValue<string>();
        var password = NewPassword();

        // A new account must replace its temporary password before it can do anything else.
        var temporaryToken = await api.SignInAsync(email, temporary);
        var session = await api.PostAsync($"First password change for DEMO-{personNumber:0000}", "api/auth/change-password",
            new { currentPassword = temporary, newPassword = password }, temporaryToken);

        _sessions[personNumber] = new Session(email, password, userId, session!["token"]!.GetValue<string>(), DateTimeOffset.UtcNow);
    }

    public async Task<string> TokenAsync(int key)
    {
        var session = _sessions[key];
        if (DateTimeOffset.UtcNow - session.IssuedAt < Refresh) return session.Token;

        var token = await api.SignInAsync(session.Email, session.Password);
        _sessions[key] = session with { Token = token, IssuedAt = DateTimeOffset.UtcNow };
        return token;
    }

    public string UserIdOf(int personNumber) => _sessions[personNumber].UserId!;

    /// <summary>Long, random, and always holding a digit, which the password policy requires.</summary>
    internal static string NewPassword() => "Demo" + RandomNumberGenerator.GetHexString(20, lowercase: true) + "7";
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/PeopleCore.DemoSeed.Tests --nologo`
Expected: PASS, including the 10 new tests.

The key order in the JSON body assertions follows the anonymous object's declaration order. If the serialiser escapes `"` differently in the error body, compare against the actual output, but keep the assertions' intent.

- [ ] **Step 7: Commit**

```bash
git add tools/PeopleCore.DemoSeed/Api tests/PeopleCore.DemoSeed.Tests/ApiClientTests.cs
git commit -m "feat(demo): an API client that names the step it failed on"
```

---

### Task 6: Seeding the organisation, people and setup

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.cs`
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Organization.cs`
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Setup.cs`

**Interfaces:**
- Consumes: `DemoPlan` and everything in `Plan` (Tasks 1–4), plus `ApiClient`, `Logins`, `SeedException` and `AlreadySeededException` (Task 5).
- Produces, in namespace `PeopleCore.DemoSeed.Seeding`:
  - `sealed partial class Seeder(ApiClient api, DemoPlan plan, TextWriter log)`
    - `Task<SeedResult> RunAsync(string adminEmail, string adminPassword)`
    - `SeedResult` is a record `(IReadOnlyDictionary<string, int> Counts, string ClientEmail, string ClientTemporaryPassword)`.
  - Private helpers later tasks use:
    - `Task<string> AdminAsync()`
    - `Task<string> TokenOfAsync(int personNumber)`
    - `Guid EmployeeId(int personNumber)`
    - `void Count(string what, int n = 1)`
    - `void Say(string line)`
  - Private state later tasks use:
    - `Dictionary<int, Guid> _employeeIds`
    - `Dictionary<string, Guid> _departmentIds` (keyed by department name)
    - `Dictionary<string, Guid> _leaveTypeIds` (keyed by `"VL"` or `"SL"`)
- Partial methods defined in later tasks and called from `RunAsync`:
  - `RunMonthAsync(int month)` (Task 7)
  - `RunReviewsAsync()` (Task 8)
  - `RunRecruitmentAsync()` (Task 8)
  - `FinishAsync()` (Task 8)

  Until then, Task 6 adds temporary no-op bodies in `Seeder.cs`, **marked for removal in Task 8**, so the project builds.

This task has no unit tests. The seeder is a straight sequence of API calls, and Task 9's rehearsal against a real API is its test. The build must stay clean, and every earlier test must stay green.

**Before writing the preflight, read these controllers** to get each list endpoint's exact response shape:
- `src/PeopleCore.API/Controllers/Employees/EmployeesController.cs`: the `GetAll` result is a `PagedResult` with `items` and `totalCount`.
- `src/PeopleCore.API/Controllers/Attendance/HolidaysController.cs`: its GET route and whether it returns a list or a paged result.
- `src/PeopleCore.API/Controllers/Leave/LeaveController.cs` `GetLeaveTypes`.
- `src/PeopleCore.API/Controllers/Leave/LeaveAccrualPoliciesController.cs`: its GET route and fields, including `leaveTypeId`, `daysPerYear`, `accrualFrequency`, `tenureMonthsMin` and `isActive`, if present.

Where the code below reads a field whose name you had to confirm, use the confirmed name.

- [ ] **Step 1: The seeder's core**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.cs`:

```csharp
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public record SeedResult(IReadOnlyDictionary<string, int> Counts, string ClientEmail, string ClientTemporaryPassword);

/// <summary>
/// Walks the plan through the API in calendar order. Each month does its accruals first, then the
/// leave and overtime filed that month, then the attendance, then the payroll for that month. That
/// way every payroll run sees the same history a real company's would.
/// </summary>
public sealed partial class Seeder(ApiClient api, DemoPlan plan, TextWriter log)
{
    /// <summary>The client's persona: the HR Manager.</summary>
    public const int ClientPerson = 2;

    private readonly Logins _logins = new(api);
    private readonly Dictionary<int, Guid> _employeeIds = new();
    private readonly Dictionary<string, Guid> _departmentIds = new();
    private readonly Dictionary<(string Department, string Title), Guid> _positionIds = new();
    private readonly Dictionary<(string Department, string Team), Guid> _teamIds = new();
    private readonly Dictionary<string, Guid> _leaveTypeIds = new();
    private readonly SortedDictionary<string, int> _counts = new();

    public async Task<SeedResult> RunAsync(string adminEmail, string adminPassword)
    {
        await _logins.AddAdminAsync(adminEmail, adminPassword);
        Say("Signed in as the administrator.");

        await PreflightAsync();
        await CreateOrganizationAsync();
        await CreateEmployeesAsync();
        await CreateHolidaysAndScheduleAsync();
        await CreateLoginsAsync();
        await CreateLeaveSetupAsync();

        foreach (var month in plan.Months)
            await RunMonthAsync(month);

        await RunReviewsAsync();
        await RunRecruitmentAsync();
        var temporaryPassword = await FinishAsync();

        return new SeedResult(_counts, plan.PersonNumber(ClientPerson).Email, temporaryPassword);
    }

    private Task<string> AdminAsync() => _logins.TokenAsync(Logins.Admin);

    private Task<string> TokenOfAsync(int personNumber) => _logins.TokenAsync(personNumber);

    private Guid EmployeeId(int personNumber) => _employeeIds[personNumber];

    private void Count(string what, int n = 1) => _counts[what] = _counts.GetValueOrDefault(what) + n;

    private void Say(string line) => log.WriteLine(line);

    private static Guid IdOf(System.Text.Json.Nodes.JsonNode? node) => Guid.Parse(node!["id"]!.GetValue<string>());

    // Implemented in Seeder.Months.cs, Seeder.Reviews.cs, Seeder.Recruitment.cs and Seeder.Finish.cs.
    private partial Task RunMonthAsync(int month);
    private partial Task RunReviewsAsync();
    private partial Task RunRecruitmentAsync();
    private partial Task<string> FinishAsync();
}
```

Never print a password, or an email address alongside one.

C# partial methods with return values need both a declaration and an implementation. So, while Tasks 7 and 8 are unwritten, add a file `Seeding/Seeder.Pending.cs` with these implementations:

```csharp
namespace PeopleCore.DemoSeed.Seeding;

// Temporary: removed by Task 8 once every step has its real implementation.
public sealed partial class Seeder
{
    private partial Task RunMonthAsync(int month) => Task.CompletedTask;
    private partial Task RunReviewsAsync() => Task.CompletedTask;
    private partial Task RunRecruitmentAsync() => Task.CompletedTask;
    private partial Task<string> FinishAsync() => Task.FromResult(string.Empty);
}
```

Task 7 deletes the `RunMonthAsync` line from it, and Task 8 deletes the file.

- [ ] **Step 2: Preflight, organisation and employees**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.Organization.cs`:

```csharp
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private static readonly string[] Cities =
    [
        "Quezon City", "Makati City", "Pasig City", "Taguig City", "Mandaluyong City",
        "Caloocan City", "Marikina City", "Valenzuela City", "Parañaque City", "Las Piñas City",
    ];

    /// <summary>
    /// Refuses before changing anything if the site already holds the demo, or holds leave setup
    /// the demo's leave plan could not live with.
    /// </summary>
    private async Task PreflightAsync()
    {
        const string step = "Check for an earlier demo";
        for (var page = 1; ; page++)
        {
            var result = await api.GetAsync(step, $"api/employees?page={page}&pageSize=100", await AdminAsync());
            var items = result!["items"]!.AsArray();
            if (items.Any(e => e!["employeeNumber"]!.GetValue<string>().StartsWith("DEMO-", StringComparison.Ordinal)))
                throw new AlreadySeededException("This site already has DEMO- employees. Nothing was changed.");
            if (page * 100 >= result["totalCount"]!.GetValue<int>()) break;
        }

        await CheckLeaveSetupAsync();
        Say("Preflight passed: no earlier demo on this site.");
    }

    private async Task CreateOrganizationAsync()
    {
        var companies = (await api.GetAsync("Find the company", "api/companies", await AdminAsync()))!.AsArray();
        if (companies.Count == 0)
            throw new SeedException("Find the company", "GET", "api/companies", 200, "The site has no company record.");
        var companyId = IdOf(companies[0]);

        foreach (var department in plan.People.Select(p => p.Department).Distinct())
        {
            var code = department switch
            {
                "Human Resources" => "HR", "Operations" => "OPS", "Executive" => "EXEC",
                "Finance" => "FIN", "Sales" => "SLS", _ => department.ToUpperInvariant(),
            };
            _departmentIds[department] = IdOf(await api.PostAsync($"Create the {department} department", "api/departments",
                new { companyId, parentDepartmentId = (Guid?)null, name = department, code }, await AdminAsync()));
            Count("departments");
        }

        foreach (var (department, title) in plan.People.Select(p => (p.Department, p.Title)).Distinct())
        {
            _positionIds[(department, title)] = IdOf(await api.PostAsync($"Create the {title} position", "api/positions",
                new { departmentId = _departmentIds[department], title, level = (string?)null }, await AdminAsync()));
            Count("positions");
        }

        foreach (var (department, team) in plan.People.Where(p => p.Team is not null).Select(p => (p.Department, p.Team!)).Distinct())
        {
            _teamIds[(department, team)] = IdOf(await api.PostAsync($"Create the {team} team", "api/teams",
                new { departmentId = _departmentIds[department], name = team }, await AdminAsync()));
            Count("teams");
        }

        Say($"Organisation: {_departmentIds.Count} departments, {_positionIds.Count} positions, {_teamIds.Count} teams.");
    }

    private async Task CreateEmployeesAsync()
    {
        var rng = new Random(plan.Seed);
        foreach (var person in plan.People)
        {
            var step = $"Create {person.EmployeeNumber}";
            var admin = await AdminAsync();
            var departmentId = _departmentIds[person.Department];
            var positionId = _positionIds[(person.Department, person.Title)];
            Guid? managerId = person.ManagerNumber is { } manager ? EmployeeId(manager) : null;
            var employmentType = person.EmploymentStatus == "Probationary" ? "Probationary" : "Regular";

            var id = IdOf(await api.PostAsync(step, "api/employees", new
            {
                employeeNumber = person.EmployeeNumber,
                firstName = person.FirstName, middleName = person.MiddleName, lastName = person.LastName,
                dateOfBirth = person.BirthDate, gender = person.Gender,
                workEmail = person.Email, mobileNumber = person.Mobile,
                departmentId, positionId, reportingManagerId = managerId,
                employmentStatus = person.EmploymentStatus, employmentType,
                hireDate = person.HireDate,
            }, admin));
            _employeeIds[person.Number] = id;

            // Civil status, team, address and regularisation can only be set by an update.
            await api.PutAsync($"{step}: details", $"api/employees/{id}", new
            {
                firstName = person.FirstName, middleName = person.MiddleName, lastName = person.LastName,
                civilStatus = person.CivilStatus, personalEmail = (string?)null, mobileNumber = person.Mobile,
                address = $"{Cities[rng.Next(Cities.Length)]}, Metro Manila",
                departmentId, positionId,
                teamId = person.Team is null ? (Guid?)null : _teamIds[(person.Department, person.Team)],
                reportingManagerId = managerId,
                employmentStatus = person.EmploymentStatus,
                regularizationDate = person.EmploymentStatus == "Regular" ? person.HireDate.AddMonths(6) : (DateOnly?)null,
                is13thMonthEligible = true,
            }, admin);

            foreach (var (idType, idNumber) in new[]
                     {
                         ("SSS", person.Ids.Sss), ("PhilHealth", person.Ids.PhilHealth),
                         ("PagIbig", person.Ids.PagIbig), ("TIN", person.Ids.Tin),
                     })
                await api.PutAsync($"{step}: {idType}", $"api/employees/{id}/government-ids", new { idType, idNumber }, admin);

            await api.PostAsync($"{step}: emergency contact", $"api/employees/{id}/emergency-contacts", new
            {
                name = person.Contact.Name, relationship = person.Contact.Relationship,
                phone = person.Contact.Phone, address = (string?)null,
            }, admin);

            await api.PutAsync($"{step}: pay", $"api/employee-compensation/{id}", new
            {
                basicSalary = person.MonthlySalary, payFrequency = "SemiMonthly",
                taxCode = person.TaxCode, dependents = person.Dependents,
            }, admin);

            Count("employees");
        }

        Say($"Employees: {_employeeIds.Count}, each with pay, government IDs and an emergency contact.");
    }
}
```

Check `UpdateEmployeeDto` in `src/PeopleCore.Application/Employees/DTOs/EmployeeDtos.cs` against the update body above. Its fields are `FirstName`, `MiddleName`, `LastName`, `CivilStatus`, `PersonalEmail`, `MobileNumber`, `Address`, `DepartmentId`, `PositionId`, `TeamId`, `ReportingManagerId`, `EmploymentStatus`, `RegularizationDate` and `Is13thMonthEligible`. Send them all: the update replaces the record's values.

- [ ] **Step 3: Holidays, schedule, logins, leave setup**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.Setup.cs`:

```csharp
using System.Text.Json.Nodes;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private static readonly (string Code, string Name)[] LeaveTypes = [("VL", "Vacation Leave"), ("SL", "Sick Leave")];

    private async Task CreateHolidaysAndScheduleAsync()
    {
        var admin = await AdminAsync();

        // A site may already carry this year's holidays. Adding a second copy of a date could count it twice.
        var existing = await ExistingHolidayDatesAsync(admin);
        foreach (var holiday in Calendar.Holidays.Where(h => !existing.Contains(h.Date)))
        {
            await api.PostAsync($"Add the holiday {holiday.Name}", "api/holidays", new
            {
                name = holiday.Name, holidayDate = holiday.Date,
                holidayType = holiday.IsRegular ? "RegularHoliday" : "SpecialNonWorking", isRecurring = false,
            }, admin);
            Count("holidays");
        }

        var shiftId = IdOf(await api.PostAsync("Create the day shift", "api/shift-templates", new
        {
            name = "Day Shift 8-5", startTime = new TimeOnly(8, 0), endTime = new TimeOnly(17, 0),
            breakMinutes = 60, isNightShift = false,
        }, admin));

        foreach (var person in plan.People)
        {
            await api.PostAsync($"Assign the day shift to {person.EmployeeNumber}", "api/shift-assignments", new
            {
                employeeId = EmployeeId(person.Number), shiftTemplateId = shiftId, rotatingPatternId = (Guid?)null,
                patternStartDate = (DateOnly?)null, effectiveFrom = person.ActiveFrom, effectiveTo = (DateOnly?)null,
            }, admin);
            Count("shift assignments");
        }

        Say("Holidays and the Monday-to-Friday day shift are in place.");
    }

    /// <summary>
    /// Reads the holidays already on the site. Match the route and shape of HolidaysController's GET.
    /// This version assumes a plain array of objects carrying holidayDate.
    /// </summary>
    private async Task<HashSet<DateOnly>> ExistingHolidayDatesAsync(string admin)
    {
        var node = await api.GetAsync("Read existing holidays", "api/holidays", admin);
        var items = node is JsonArray array ? array : node?["items"]?.AsArray() ?? [];
        return items.Select(h => DateOnly.Parse(h!["holidayDate"]!.GetValue<string>()[..10])).ToHashSet();
    }

    private async Task CreateLoginsAsync()
    {
        foreach (var person in plan.People)
        {
            await _logins.CreateAsync(person.Number, EmployeeId(person.Number), person.Email,
                person.FirstName, person.LastName, person.Role);
            Count("logins");
        }
        Say($"Logins: {plan.People.Count}, one per employee.");
    }

    /// <summary>
    /// The leave plan assumes 15 days a year per type, accruing monthly for everyone. An existing
    /// VL or SL type is reused. Its accrual policy must match that assumption, or the run stops before
    /// anything is created.
    /// </summary>
    private async Task CheckLeaveSetupAsync()
    {
        var admin = await AdminAsync();
        var types = (await api.GetAsync("Read leave types", "api/leave-types", admin))!.AsArray();
        var policies = await api.GetAsync("Read accrual policies", "api/leave-accrual-policies", admin);
        var policyList = policies is JsonArray array ? array : policies?["items"]?.AsArray() ?? [];

        foreach (var (code, _) in LeaveTypes)
        {
            var type = types.FirstOrDefault(t => string.Equals(t!["code"]!.GetValue<string>(), code, StringComparison.OrdinalIgnoreCase));
            if (type is null) continue;

            _leaveTypeIds[code] = IdOf(type);
            var theirs = policyList.Where(p => p!["leaveTypeId"]!.GetValue<string>() == type["id"]!.GetValue<string>()).ToList();
            var usable = theirs.Any(p =>
                p!["accrualFrequency"]!.GetValue<string>() == "Monthly"
                && p["daysPerYear"]!.GetValue<decimal>() >= 15
                && p["tenureMonthsMin"]!.GetValue<int>() == 0);

            if (theirs.Count > 0 && !usable)
                throw new SeedException("Check leave setup", "GET", "api/leave-accrual-policies", 200,
                    $"The existing {code} accrual policy is not monthly at 15 days a year from day one, so the demo's leave would be refused. Nothing was changed.");
        }
    }

    private async Task CreateLeaveSetupAsync()
    {
        var admin = await AdminAsync();
        foreach (var (code, name) in LeaveTypes)
        {
            if (!_leaveTypeIds.ContainsKey(code))
            {
                _leaveTypeIds[code] = IdOf(await api.PostAsync($"Create {name}", "api/leave-types", new
                {
                    name, code, maxDaysPerYear = 15m, isPaid = true, isCarryOver = false,
                    carryOverMaxDays = (decimal?)null, genderRestriction = (string?)null, requiresDocument = false,
                }, admin));
                Count("leave types");
            }

            var policies = await api.GetAsync("Read accrual policies", "api/leave-accrual-policies", admin);
            var policyList = policies is JsonArray array ? array : policies?["items"]?.AsArray() ?? [];
            var hasPolicy = policyList.Any(p => p!["leaveTypeId"]!.GetValue<string>() == _leaveTypeIds[code].ToString());
            if (hasPolicy) continue;

            await api.PostAsync($"Create the {code} accrual policy", "api/leave-accrual-policies", new
            {
                leaveTypeId = _leaveTypeIds[code], tenureMonthsMin = 0, tenureMonthsMax = (int?)null,
                daysPerYear = 15m, accrualFrequency = "Monthly",
            }, admin);
            Count("accrual policies");
        }
        Say("Leave: Vacation and Sick Leave at 15 days a year, accruing monthly.");
    }
}
```

The preflight reads `api/leave-types` and `api/leave-accrual-policies`. Confirm the field names (`code`, `id`, `leaveTypeId`, `accrualFrequency`, `daysPerYear`, `tenureMonthsMin`) against the DTOs those endpoints return, and use the real ones. If a list endpoint is paged, read `items`. The helper already handles both shapes.

- [ ] **Step 4: Build and run every test**

```bash
dotnet build PeopleCore.slnx --nologo
dotnet test tests/PeopleCore.DemoSeed.Tests --nologo
```

Expected: 0 warnings, 0 errors, and every test green.

- [ ] **Step 5: Commit**

```bash
git add tools/PeopleCore.DemoSeed/Seeding
git commit -m "feat(demo): seed the organisation, the twenty people, their pay and their logins"
```

---

### Task 7: The month loop

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Months.cs`
- Modify: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Pending.cs` (remove the `RunMonthAsync` line)

**Interfaces:**
- Consumes: Task 6's `Seeder` helpers and state, plus `DemoPlan.Leave`, `Overtime`, `PayPeriods` and `AttendanceFor`.
- Produces: `private partial Task RunMonthAsync(int month)`.

Order within a month, and why:
1. **Accruals**, so this month's leave has a balance to draw on.
2. **Leave**, filed by the employee, then decided by their manager. The General Manager's leave is decided by the HR Manager, who holds `approvals.all`.
3. **Overtime**, filed by the employee, then decided by their direct manager. The API accepts nobody else.
4. **Attendance**, imported as one CSV. It already leaves out approved leave days.
5. **Payroll**, for each completed half-month ending this month. It is created and computed, then approved and paid, except for the plan's last period, which is left awaiting approval.

A run takes its attendance snapshot when it is created, so steps 2 to 4 must come before step 5.

- [ ] **Step 1: Write the loop**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.Months.cs`:

```csharp
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private static readonly Dictionary<string, string[]> LeaveReasons = new()
    {
        ["VL"] = ["Family trip to the province", "Personal errands", "Child's school event", "Short vacation"],
        ["SL"] = ["Fever and flu", "Medical check-up", "Dental appointment", "Migraine"],
    };

    private const string RejectionReason = "Too many of the team are already off that week. Please pick other dates.";

    private partial async Task RunMonthAsync(int month)
    {
        var rng = new Random(unchecked(plan.Seed * 31 + month));
        var admin = await AdminAsync();

        await api.PostAsync($"Run {month:00}/2026 leave accruals", $"api/leave-accruals/run-manual?year=2026&month={month}", null, admin);

        foreach (var filing in plan.Leave.Where(f => f.FiledInMonth == month))
            await FileLeaveAsync(filing, rng);

        foreach (var filing in plan.Overtime.Where(o => o.Date.Month == month))
            await FileOvertimeAsync(filing);

        var rows = plan.AttendanceFor(month);
        if (rows.Count > 0)
        {
            await api.PostCsvAsync($"Import {month:00}/2026 attendance", "api/attendance/import",
                AttendancePlanner.ToCsv(rows), await AdminAsync());
            Count("attendance days", rows.Count);
        }

        foreach (var period in plan.PayPeriods.Where(p => p.End.Month == month))
            await RunPayrollAsync(period);

        Say($"{new DateOnly(2026, month, 1):MMMM}: done.");
    }

    private async Task FileLeaveAsync(LeaveFiling filing, Random rng)
    {
        var person = plan.PersonNumber(filing.PersonNumber);
        var step = $"{person.EmployeeNumber} files {filing.TypeCode} for {filing.Start:yyyy-MM-dd}";
        var reasons = LeaveReasons[filing.TypeCode];

        var id = IdOf(await api.PostAsync(step, "api/leave-requests", new
        {
            employeeId = EmployeeId(person.Number), leaveTypeId = _leaveTypeIds[filing.TypeCode],
            startDate = filing.Start, endDate = filing.End, reason = reasons[rng.Next(reasons.Length)],
        }, await TokenOfAsync(person.Number)));

        var approver = await TokenOfAsync(person.ManagerNumber ?? ClientPerson);
        switch (filing.Decision)
        {
            case Decision.Approved:
                await api.PutAsync($"{step}: approve", $"api/leave-requests/{id}/approve", null, approver);
                Count("leave approved");
                break;
            case Decision.Rejected:
                await api.PutAsync($"{step}: reject", $"api/leave-requests/{id}/reject", new { rejectionReason = RejectionReason }, approver);
                Count("leave rejected");
                break;
            default:
                Count("leave pending");
                break;
        }
    }

    private async Task FileOvertimeAsync(OvertimeFiling filing)
    {
        var person = plan.PersonNumber(filing.PersonNumber);
        var step = $"{person.EmployeeNumber} files overtime for {filing.Date:yyyy-MM-dd}";

        var id = IdOf(await api.PostAsync(step, "api/overtime-requests", new
        {
            employeeId = EmployeeId(person.Number), overtimeDate = filing.Date,
            startTime = filing.Date.ToDateTime(filing.Start), endTime = filing.Date.ToDateTime(filing.End),
            reason = filing.Reason,
        }, await TokenOfAsync(person.Number)));

        if (filing.Decision == Decision.Approved)
        {
            await api.PutAsync($"{step}: approve", $"api/overtime-requests/{id}/approve", null,
                await TokenOfAsync(person.ManagerNumber!.Value));
            Count("overtime approved");
        }
        else
        {
            Count("overtime pending");
        }
    }

    private async Task RunPayrollAsync(PayPeriod period)
    {
        var step = $"Payroll {period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd}";
        var admin = await AdminAsync();
        var employees = plan.People.Where(p => p.HireDate <= period.End)
            .Select(p => new { employeeId = EmployeeId(p.Number) }).ToList();

        var id = IdOf(await api.PostAsync(step, "api/payroll-runs", new
        {
            periodStart = period.Start, periodEnd = period.End, payDate = period.PayDate,
            frequency = "SemiMonthly", employees,
        }, admin));

        if (period == plan.PayPeriods[^1])
        {
            Count("payroll runs awaiting approval");
            return;
        }

        await api.PutAsync($"{step}: approve", $"api/payroll-runs/{id}/approve", null, admin);
        await api.PutAsync($"{step}: mark paid", $"api/payroll-runs/{id}/mark-paid", null, admin);
        Count("payroll runs paid");
    }
}
```

Then delete the `RunMonthAsync` line from `Seeder.Pending.cs`.

Check `CreateOvertimeRequestDto` (`src/PeopleCore.Application/Attendance/DTOs/AttendanceDtos.cs`) for the exact type of `StartTime`/`EndTime`. They are `DateTime`, which the code above sends. If the API rejects an unzoned `DateTime`, send `DateTime.SpecifyKind(..., DateTimeKind.Unspecified)` values. That is what `ToDateTime` returns already.

- [ ] **Step 2: Build and test**

```bash
dotnet build PeopleCore.slnx --nologo
dotnet test tests/PeopleCore.DemoSeed.Tests --nologo
```

Expected: 0 warnings, 0 errors, and every test green.

- [ ] **Step 3: Commit**

```bash
git add tools/PeopleCore.DemoSeed/Seeding
git commit -m "feat(demo): walk the year month by month through leave, overtime, attendance and payroll"
```

---

### Task 8: Reviews, recruitment, finishing, and the entry point

**Files:**
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Reviews.cs`
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Recruitment.cs`
- Create: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Finish.cs`
- Delete: `tools/PeopleCore.DemoSeed/Seeding/Seeder.Pending.cs`
- Modify: `tools/PeopleCore.DemoSeed/Program.cs`
- Create: `tools/PeopleCore.DemoSeed/README.md`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `RunReviewsAsync`, `RunRecruitmentAsync` and `FinishAsync`, which returns the client's temporary password.
  - The runnable program.

- [ ] **Step 1: The review cycle**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.Reviews.cs`:

```csharp
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
```

Confirm `PerformanceReviewDto` in `src/PeopleCore.Application/Performance/DTOs/PerformanceDtos.cs` carries `KpiItems` with `Id` and `Description`. The create endpoint must return that DTO. If it returns only an id, fetch `GET api/performance-reviews/{id}` for the KPI ids instead.

- [ ] **Step 2: Recruitment**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.Recruitment.cs`:

```csharp
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
                await api.PostAsync($"{step}: interview", "api/interviews", new
                {
                    applicantId = id, stageName = applicant.InterviewStage, scheduledAt = at,
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
```

- [ ] **Step 3: Finishing**

`tools/PeopleCore.DemoSeed/Seeding/Seeder.Finish.cs`:

```csharp
namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    /// <summary>
    /// The nineteen helper logins are switched off, and their history stays. The client's login gets
    /// a fresh temporary password. The one the seeder used is thrown away with this process.
    /// </summary>
    private partial async Task<string> FinishAsync()
    {
        var admin = await AdminAsync();
        foreach (var person in plan.People.Where(p => p.Number != ClientPerson))
        {
            await api.PostAsync($"Deactivate the login of {person.EmployeeNumber}",
                $"api/users/{_logins.UserIdOf(person.Number)}/deactivate", null, admin);
            Count("logins deactivated");
        }

        var reset = await api.PostAsync("Reset the client's password",
            $"api/users/{_logins.UserIdOf(ClientPerson)}/reset-password", null, admin);
        return reset!["temporaryPassword"]!.GetValue<string>();
    }
}
```

Delete `tools/PeopleCore.DemoSeed/Seeding/Seeder.Pending.cs`.

- [ ] **Step 4: The entry point**

Replace `tools/PeopleCore.DemoSeed/Program.cs`:

```csharp
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;
using PeopleCore.DemoSeed.Seeding;

var url = Environment.GetEnvironmentVariable("PEOPLECORE_URL");
var email = Environment.GetEnvironmentVariable("PEOPLECORE_ADMIN_EMAIL");
var password = Environment.GetEnvironmentVariable("PEOPLECORE_ADMIN_PASSWORD");
var seed = int.TryParse(Environment.GetEnvironmentVariable("PEOPLECORE_SEED"), out var s) ? s : 20260918;

if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("Set PEOPLECORE_URL, PEOPLECORE_ADMIN_EMAIL and PEOPLECORE_ADMIN_PASSWORD.");
    return 64;
}

// The demo's days are Philippine days, whatever time zone this machine runs in.
var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
var plan = DemoPlan.Build(seed, today);

using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
Console.WriteLine($"Seeding the demo company into {new Uri(url).Host}, history to {today:d MMMM yyyy}.");

try
{
    var result = await new Seeder(new ApiClient(http), plan, Console.Out).RunAsync(email, password);

    Console.WriteLine();
    Console.WriteLine("Done.");
    foreach (var (what, count) in result.Counts)
        Console.WriteLine($"  {what,-32}{count,6}");
    Console.WriteLine();
    Console.WriteLine("The client's account:");
    Console.WriteLine($"  Email:              {result.ClientEmail}");
    Console.WriteLine($"  Temporary password: {result.ClientTemporaryPassword}");
    Console.WriteLine("  They set their own password when they first sign in.");
    return 0;
}
catch (AlreadySeededException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
catch (SeedException e)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Stopped. {e.Message}");
    Console.Error.WriteLine("Records created before this step remain on the site.");
    return 1;
}
```

- [ ] **Step 5: A README for whoever runs it**

`tools/PeopleCore.DemoSeed/README.md`:

````markdown
# Demo company seed

Fills a PeopleCore site with Bayanihan Trading: 20 fictional employees and their history from
1 January 2026 to today. It covers attendance, leave, overtime, semi-monthly payroll, a mid-year
review and recruitment, so every report has real numbers. Everything goes through the API, so
payroll is computed by the real engine.

## Run it

```bash
PEOPLECORE_URL=https://peoplecore.m2netsolutions.com \
PEOPLECORE_ADMIN_EMAIL=you@example.com \
PEOPLECORE_ADMIN_PASSWORD='your admin password' \
dotnet run --project tools/PeopleCore.DemoSeed
```

In PowerShell, set each variable with `$env:PEOPLECORE_URL = "..."` first.

It takes a few minutes. When it finishes, it prints the client's email and a temporary password.
The client signs in with those and chooses their own password.

## What to know

- It **refuses to run twice**. If any employee number starting `DEMO-` exists, it stops before
  changing anything.
- It **stops at the first refused request**, and names the step. Records created before that step
  stay on the site. The API offers no undo, so rehearse against a local API before running it
  against production.
- The 19 other employees' logins exist only to file their history. They are deactivated at the end.
- Everyone in it is fictional. The names are common Filipino names, and the IDs, phone numbers and
  emails (on `.example` domains) are made up.
- `PEOPLECORE_SEED` (optional) changes who the twenty people are. The default always builds the
  same company.
````

- [ ] **Step 6: Build and test**

```bash
dotnet build PeopleCore.slnx --nologo
dotnet test tests/PeopleCore.DemoSeed.Tests --nologo
```

Expected: 0 warnings, 0 errors, and every test green.

- [ ] **Step 7: Commit**

```bash
git add tools/PeopleCore.DemoSeed
git commit -m "feat(demo): reviews, recruitment, the client's account, and a runnable program"
```

---

### Task 9: Rehearsal against a local API

**Files:** none unless a check fails. The controller runs this task.

- [ ] **Step 1: Start a fresh API**

Start the API with `preview_start`, using a launch entry that isn't committed. Point it at a new database, `peoplecore_demo_rehearsal`, with `--Seed:AdminPassword=SmokeAdmin2026`, `--DataProtection:KeyEncryptionKey=<40 random characters>` and `--Jwt:Key` if needed. Never use the developer's `peoplecore` database.

- [ ] **Step 2: Run the seeder**

```bash
PEOPLECORE_URL=http://localhost:5180 PEOPLECORE_ADMIN_EMAIL=admin@peoplecore.local PEOPLECORE_ADMIN_PASSWORD=SmokeAdmin2026 dotnet run --project tools/PeopleCore.DemoSeed
```

Expected: exit code 0, and a summary with these counts, for a run between 16 and 30 September. Earlier or later dates shift the payroll count:
- 20 employees
- 20 logins
- 19 logins deactivated
- 16 payroll runs paid
- 1 payroll run awaiting approval
- 4 leave requests pending
- 2 overtime requests pending
- 2 reviews awaiting the manager
- 12 applicants

- [ ] **Step 3: Check the result through the API**

Sign in as the admin, then check each of these:

1. `GET api/payroll-runs`: 17 runs. 16 are `Paid`. The latest is not paid and has computed totals.
2. `GET api/reports/payslip/{latestPaidRunId}/{an employee id}`: non-zero gross pay, SSS, PhilHealth, Pag-IBIG and withholding tax.
3. `GET api/reports/2316/years/{an employee id}` includes 2026.
4. `GET api/analytics/hr/headcount` and `GET api/analytics/executive/workforce-summary` return non-empty results. Take the exact routes from the two analytics controllers.
5. `GET api/attendance/summary` for March shows some lates and some absences.
6. **The client's account:**
   - Sign in with the printed temporary password. The account must change it.
   - Change it, then list pending leave requests: there are 4.
   - List pending overtime: there are 2.
   - Approve one leave request.
7. Signing in as a deactivated employee, such as `DEMO-0010`'s email, is refused.
8. **Run the seeder a second time.** It must exit with code 2 and "This site already has DEMO- employees. Nothing was changed.", and the employee count must not change.

- [ ] **Step 4: Look at it**

Open the local web app and sign in as the client. Take screenshots of:
- the dashboard
- the payroll register for a paid run
- one payslip
- the HR analytics page
- the leave approvals list

- [ ] **Step 5: Tidy up**

Stop the API and the web app, and revert the launch entry. `git status --short` must be empty.

---

## Spec coverage

| Spec requirement | Task |
|---|---|
| 20 fictional people, Filipino names, fake-but-shaped IDs and contacts | 2 |
| Departments, heads, managers, salaries, hire dates | 2, 6 |
| 2026 holidays and the day shift | 1, 6 |
| Monthly accruals, VL and SL at 15 days a year | 3, 6, 7 |
| Leave filed and decided, some rejected, a few pending | 3, 7 |
| Overtime, mostly Operations and IT, two pending for the client | 3, 7 |
| Attendance with lates, absences and undertime, none on leave, holidays or weekends | 4, 7 |
| Semi-monthly payroll computed by the engine, all paid except the latest | 1, 7 |
| Mid-year review, some awaiting the client | 4, 8 |
| Three postings, twelve applicants, interviews | 4, 8 |
| One login per employee, 19 deactivated, the client's password reset and printed | 5, 6, 8 |
| Guard against a second run, and stop at the first refused request | 5, 6, 8 |
| No passwords printed except the client's temporary one | 5, 8 |
| Unit tests for the generators | 1–5 |
| Rehearsal against a local API, second run refused | 9 |
