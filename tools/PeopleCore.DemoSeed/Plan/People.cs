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
