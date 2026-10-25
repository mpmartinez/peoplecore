using Npgsql;

namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// The database the end-to-end run owns: created empty before the API starts, dropped after.
/// <para>
/// It lives on the same shared m2net-postgres server as the repository tests, alongside this
/// project's own <c>peoplecore</c> and other projects' databases, so the name is fixed here and
/// never configurable - the same rule, for the same reason, as <c>PostgresFixture</c>. The API
/// builds the schema itself by running its migrations at startup, exactly as a fresh deployment
/// does, so the run also proves that a brand-new database comes up with an administrator who can
/// sign in.
/// </para>
/// </summary>
internal static class E2EDatabase
{
    /// <summary>The only database this suite may ever create, use or drop.</summary>
    public const string Name = "peoplecore_e2e";

    /// <summary>
    /// Defaults to the local m2net-postgres container. CI overrides the host and credentials with
    /// PEOPLECORE_TEST_POSTGRES, a connection string WITHOUT a Database - shared with the
    /// repository tests, which pick their own database name the same way.
    /// </summary>
    private static string ServerConnection =>
        Environment.GetEnvironmentVariable("PEOPLECORE_TEST_POSTGRES")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    public static string ConnectionString => ConnectionFor(Name);

    private static string ConnectionFor(string database) =>
        new NpgsqlConnectionStringBuilder(ServerConnection) { Database = database }.ConnectionString;

    /// <summary>
    /// Dropped first as well as created: an aborted run leaves the database behind, and the API
    /// would otherwise start against the previous run's rows and its already-seeded admin.
    /// </summary>
    public static async Task RecreateAsync()
    {
        await DropAsync();
        await ExecuteOnServerAsync($"CREATE DATABASE \"{Name}\";");
    }

    // WITH (FORCE) because the API's connection pool may still hold sessions open when the run
    // ends; without it the drop waits on them. PostgreSQL 13 or later.
    public static Task DropAsync()
    {
        NpgsqlConnection.ClearAllPools();
        return ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE);");
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        // CREATE and DROP DATABASE cannot run inside a transaction, so they go straight through
        // Npgsql against the maintenance database.
        await using var connection = new NpgsqlConnection(ConnectionFor("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
