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
