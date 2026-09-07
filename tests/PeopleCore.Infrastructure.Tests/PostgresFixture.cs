using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// A dedicated database on the shared m2net-postgres server, created for the run and dropped after.
/// <para>
/// <b>This suite is destructive.</b> It truncates every mapped table before every test. The server
/// it runs on hosts 33 databases - this project's own <c>peoplecore</c>, which holds live
/// development data, alongside other projects' <c>spms_pg</c>, <c>maritimeone</c>, <c>ias_db</c> and
/// <c>keycloak</c>. So the tests get their own database and never touch any of those, and
/// <see cref="ResetAsync"/> checks the connected database's name before issuing a single TRUNCATE.
/// A comment would not have been enough.
/// </para>
/// <para>
/// The schema is built by running the migrations rather than by <c>EnsureCreated</c>, so the tests
/// execute against the schema a deployment actually produces - and the migration chain gets its
/// first automated proof that it applies to an empty database.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>The only database this suite may ever connect to.</summary>
    private const string TestDatabase = "peoplecore_repotests";

    /// <summary>
    /// Defaults to the local m2net-postgres container. CI overrides the host and credentials with
    /// PEOPLECORE_TEST_POSTGRES, whose value is a connection string WITHOUT a Database - the
    /// database name is this class's to choose, and letting it be configured is how the guard
    /// above gets bypassed by accident.
    /// </summary>
    private static string ServerConnection =>
        Environment.GetEnvironmentVariable("PEOPLECORE_TEST_POSTGRES")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private string _truncateStatement = string.Empty;

    private static string ConnectionFor(string database) =>
        new NpgsqlConnectionStringBuilder(ServerConnection) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        // Dropped first as well as last: an aborted run leaves the database behind, and starting
        // from someone else's half-migrated schema is worse than starting from nothing.
        await DropTestDatabaseAsync();
        await ExecuteOnServerAsync($"CREATE DATABASE \"{TestDatabase}\";");

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        _truncateStatement = BuildTruncateStatement(context);
    }

    public AppDbContext CreateContext() => CreateContext(currentUser: null);

    public AppDbContext CreateContext(ICurrentUserService? currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionFor(TestDatabase))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, currentUser);
    }

    /// <summary>
    /// Empties every mapped table. Isolation is not optional: CountForYearAsync and
    /// GetPaidRunsInYearAsync do not filter by employee, so rows left by one test would change
    /// another test's count - surfacing as an order-dependent flake rather than an honest failure.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();

        // Read off the live connection, not off a constant, so that repointing the connection
        // string at a real database fails loudly here instead of emptying it.
        var connected = context.Database.GetDbConnection().Database;
        if (connected != TestDatabase)
        {
            throw new InvalidOperationException(
                $"Refusing to truncate '{connected}'. This suite empties every table in the "
                + $"database it connects to and must only ever run against '{TestDatabase}'.");
        }

        await context.Database.ExecuteSqlRawAsync(_truncateStatement);
    }

    /// <summary>
    /// Built from the model, never hardcoded - a hardcoded list stops truncating a table the day
    /// someone adds an entity, quietly reintroducing the cross-test bleed it was written to stop.
    /// One TRUNCATE over every table with CASCADE, so foreign keys impose no ordering.
    /// </summary>
    private static string BuildTruncateStatement(AppDbContext context)
    {
        var tables = context.Model.GetEntityTypes()
            .Select(entity => new { Schema = entity.GetSchema() ?? "public", Table = entity.GetTableName() })
            .Where(t => t.Table is not null)
            .Select(t => $"\"{t.Schema}\".\"{t.Table}\"")
            .Distinct()
            .ToList();

        return $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;";
    }

    // WITH (FORCE) terminates any session still holding the database open - without it, a leaked
    // connection from a failed test makes the drop hang. Requires PostgreSQL 13 or later; the
    // server is 18.1.
    private static Task DropTestDatabaseAsync()
        => ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{TestDatabase}\" WITH (FORCE);");

    private static async Task ExecuteOnServerAsync(string sql)
    {
        // CREATE DATABASE and DROP DATABASE cannot run inside a transaction, and EF opens one for
        // ExecuteSqlRaw, so these go through Npgsql directly against the maintenance database.
        await using var connection = new NpgsqlConnection(ConnectionFor("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await DropTestDatabaseAsync();
    }
}

/// <summary>
/// Every database test class joins this collection. xUnit runs the classes in a collection
/// sequentially, which is required here - they share one database, and two classes truncating it
/// concurrently would delete each other's rows mid-test.
/// </summary>
[CollectionDefinition(DatabaseCollection.Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
