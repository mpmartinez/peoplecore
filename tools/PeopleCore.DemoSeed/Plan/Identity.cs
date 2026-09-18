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
