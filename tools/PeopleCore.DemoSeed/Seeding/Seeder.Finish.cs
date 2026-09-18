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
