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
DemoPlan plan;
try
{
    plan = DemoPlan.Build(seed, today);
}
catch (ArgumentOutOfRangeException)
{
    Console.Error.WriteLine($"The demo calendar covers February to October 2026; today is {today:d MMMM yyyy}. Nothing was changed.");
    return 64;
}

using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
Console.WriteLine($"Seeding the demo company into {new Uri(url).Host}, history to {today:d MMMM yyyy}.");

var seeder = new Seeder(new ApiClient(http), plan, Console.Out);
try
{
    var result = await seeder.RunAsync(email, password);

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
catch (PreflightRefusedException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
catch (SeedException e)
{
    return Stopped(e.Message);
}
catch (Exception e)
{
    // Anything else (a timeout, a dropped connection, a response of an unexpected shape) is reported
    // the same way, by type and message: a stack trace tells the operator nothing they can act on.
    return Stopped($"{e.GetType().Name}: {e.Message}");
}

int Stopped(string reason)
{
    var error = Console.Error;
    error.WriteLine();
    error.WriteLine($"Stopped. {reason}");

    if (!seeder.WroteAnything)
    {
        error.WriteLine("Nothing was changed.");
        return 1;
    }

    error.WriteLine();
    error.WriteLine("Records created before the failure remain on the site:");
    if (seeder.CountsSoFar.Count == 0) error.WriteLine("  (none counted yet)");
    foreach (var (what, count) in seeder.CountsSoFar)
        error.WriteLine($"  {what,-32}{count,6}");

    if (seeder.LoginCleanup is { } cleanup)
    {
        error.WriteLine();
        error.WriteLine($"Demo logins switched off after the failure: {cleanup.SwitchedOff} of the {cleanup.Created} created.");
        foreach (var problem in cleanup.Errors)
            error.WriteLine($"  Could not switch off: {problem}");
    }

    error.WriteLine();
    error.WriteLine("A rerun is refused while any DEMO- employee exists on the site, and the API has no way to");
    error.WriteLine("delete them. Cleaning up needs direct work on the database.");
    return 1;
}
