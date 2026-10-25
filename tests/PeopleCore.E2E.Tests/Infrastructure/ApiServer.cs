using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// The real API - Program.cs, its middleware, migrations and startup seeding - on a real Kestrel
/// port, because the browser has to reach it over the network. WebApplicationFactory's default
/// in-memory TestServer is only reachable through the HttpClient it hands out.
/// </summary>
internal sealed class ApiServer(string webOrigin) : WebApplicationFactory<Program>
{
    /// <summary>
    /// Its own environment rather than Development: Development would load the developer's user
    /// secrets (which may point at a real database) and fall back to the well-known seed password.
    /// Everything the API needs is supplied below instead, the way a deployment supplies it.
    /// </summary>
    private const string Environment = "E2E";

    public string BaseUrl { get; private set; } = string.Empty;

    public void Start()
    {
        // Port 0: the OS picks a free one, so parallel runs and a developer's own API on 5180
        // never collide. The address actually bound is read back once the server is listening.
        UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        StartServer();

        BaseUrl = Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single()
            .TrimEnd('/');
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        // UseSetting, not ConfigureAppConfiguration: Program.cs reads Jwt:Key and the connection
        // string while registering services, before Build, and only host settings are visible
        // that early under minimal hosting.
        builder.UseSetting("ConnectionStrings:Default", E2EDatabase.ConnectionString);
        builder.UseSetting("Jwt:Key", "peoplecore-e2e-signing-key-not-a-secret-0123456789");
        builder.UseSetting("DataProtection:KeyEncryptionKey", "peoplecore-e2e-key-ring-key-not-a-secret-012345");
        builder.UseSetting("Seed:AdminEmail", E2ECredentials.AdminEmail);
        builder.UseSetting("Seed:AdminPassword", E2ECredentials.AdminPassword);
        builder.UseSetting("AllowedOrigins", webOrigin);

        // The suite never uploads, so no object store is needed; MinIO's client does not connect
        // until it is used. Pointing it at a port nothing listens on makes an accidental upload
        // fail fast instead of landing in a developer's local MinIO.
        builder.UseSetting("Minio:Endpoint", "127.0.0.1:1");

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}
