namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// The administrator the API seeds into the end-to-end database on its first start. These exist
/// only in <see cref="E2EDatabase"/>, which is created and dropped by every run, so they are not
/// secrets - they are passed to the API as Seed:AdminEmail and Seed:AdminPassword so that the run
/// exercises the same seeding path a fresh deployment does, rather than the Development default.
/// </summary>
public static class E2ECredentials
{
    public const string AdminEmail = "e2e-admin@peoplecore.test";

    // Meets Identity's default rules (upper, lower, digit, symbol, length), which the API's
    // startup seeding enforces by refusing to start.
    public const string AdminPassword = "E2e-Admin-Passw0rd!";
}
