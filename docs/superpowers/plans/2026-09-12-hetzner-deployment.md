# PeopleCore Hetzner Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Package the PeopleCore API and Blazor WebAssembly client as two container images and deploy them to the existing Hetzner box behind Dokploy's Traefik, with Neon Postgres and Cloudflare R2.

**Architecture:** One multi-stage `Dockerfile` produces an `api` image (ASP.NET Core on :8080) and a `web` image (nginx serving the published Blazor WASM on :80). GitHub Actions pushes both to GHCR; Dokploy runs `docker-compose.dokploy.yml`, and Traefik terminates TLS for `peoplecore.m2netsolutions.com` and splits by path — `/api` and `/health` to the API, everything else to nginx. Three startup changes to the API make this deployable at all: it applies its migrations, answers a health route, and refuses to start without an administrator account.

**Tech Stack:** .NET 10, Blazor WebAssembly, Tailwind CSS 3.4 (built by npm during `dotnet publish`), EF Core 10 + Npgsql, nginx:alpine, Docker, GitHub Actions, GHCR, Dokploy, Traefik, Neon Postgres, Cloudflare R2.

**Spec:** `docs/superpowers/specs/2026-09-12-hetzner-deployment-design.md`

## Global Constraints

- Hostname is `peoplecore.m2netsolutions.com`. It appears in the compose file, in nginx's `server_name`, and in `appsettings.Production.json`; all three must match.
- Images are `ghcr.io/mpmartinez/peoplecore-api` and `ghcr.io/mpmartinez/peoplecore-web`. The repository owner is `mpmartinez`; the default branch is `main`, not `master`.
- Target framework is `net10.0` throughout. Base images are `mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0` and `nginx:alpine`.
- The API listens on `8080`; nginx listens on `80`. Neither terminates TLS — Traefik does.
- Configuration reaches the container as environment variables using ASP.NET's double-underscore form (`Seed__AdminPassword` maps to `Seed:AdminPassword`).
- The Neon connection string must use the **pooled** endpoint (host ending `-pooler`) and must end with `No Reset On Close=true`. Npgsql's server-side prepared statements break against Neon's transaction-mode PgBouncer without it.
- Never commit a real secret. `.env` is already gitignored (`.gitignore:12`, `*.env`); `.env.example` holds placeholders only.
- Existing conventions: tests are xunit + FluentAssertions + Moq; comments explain *why*, not *what*.
- Do not modify `.github/workflows/ci.yml`. It stays the correctness gate.

---

## File Structure

**Created:**

| Path | Responsibility |
|---|---|
| `Dockerfile` | Shared build stage; `api` and `web` runtime targets |
| `.dockerignore` | Keeps local `bin/`, `obj/` and `node_modules/` out of the build context |
| `nginx.conf` | Serves the Blazor WASM on plain HTTP behind Traefik; caching rules |
| `docker-compose.dokploy.yml` | The two services, their environment, and Traefik routing labels |
| `.env.example` | Documents every variable Dokploy needs, with placeholder values |
| `.github/workflows/docker-publish.yml` | Builds both targets and pushes to GHCR |
| `src/PeopleCore.Web/wwwroot/appsettings.Production.json` | Production `ApiBaseUrl` for the client |
| `docs/deployment.md` | The manual runbook: Neon, R2, DNS, Dokploy, first-deploy checks |
| `tests/PeopleCore.Application.Tests/Api/SeedAdminPasswordTests.cs` | The four admin-password cases |

**Modified:**

| Path | Change |
|---|---|
| `src/PeopleCore.API/Extensions/ServiceExtensions.cs` | Add `ResolveSeedAdminPassword` and `DevelopmentAdminPassword` |
| `src/PeopleCore.API/Program.cs:86-130` | Migration block, admin-seeding rewrite, `/health` route |

---

## Task 1: Refuse to start without an administrator account

Today a Production deploy with no `Seed:AdminPassword` logs a warning and comes up with no account anyone can log in with. Move the decision out of `Program.cs`'s top-level statements — where nothing can test it — into a static method beside `ResolveJwtSigningKey`, which already sets the precedent of throwing at startup for a missing credential.

**Files:**
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (append to the class, after `ResolveJwtSigningKey`)
- Modify: `src/PeopleCore.API/Program.cs:97-123`
- Test: `tests/PeopleCore.Application.Tests/Api/SeedAdminPasswordTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public const string PeopleCore.API.Extensions.ServiceExtensions.DevelopmentAdminPassword = "Admin@123456"`
  - `public static string? PeopleCore.API.Extensions.ServiceExtensions.ResolveSeedAdminPassword(IConfiguration configuration, IHostEnvironment environment, bool adminExists)` — returns the password to seed with, or `null` when there is nothing to seed. Throws `InvalidOperationException` when no admin exists, none is configured, and the environment is not Development.

- [ ] **Step 1: Write the failing tests**

Create `tests/PeopleCore.Application.Tests/Api/SeedAdminPasswordTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using PeopleCore.API.Extensions;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

public class SeedAdminPasswordTests
{
    private static IConfiguration Configuration(string? seedAdminPassword)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Seed:AdminPassword"]).Returns(seedAdminPassword);
        return config.Object;
    }

    private static IHostEnvironment Environment(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }

    [Fact]
    public void Throws_when_production_has_no_password_and_no_admin_exists()
    {
        // The case this guard exists for: the deploy would come up healthy, serve the client,
        // answer /health, and have no account anyone could log in with.
        var act = () => ServiceExtensions.ResolveSeedAdminPassword(
            Configuration(null), Environment("Production"), adminExists: false);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Seed__AdminPassword*");
    }

    [Fact]
    public void Returns_null_when_production_has_no_password_but_an_admin_already_exists()
    {
        // Nothing to seed, so no credential is needed. Throwing here would take a running
        // deployment down the moment the variable was pruned from its environment.
        var password = ServiceExtensions.ResolveSeedAdminPassword(
            Configuration(null), Environment("Production"), adminExists: true);

        password.Should().BeNull();
    }

    [Fact]
    public void Returns_the_configured_password_in_production()
    {
        var password = ServiceExtensions.ResolveSeedAdminPassword(
            Configuration("S3cret-From-Dokploy"), Environment("Production"), adminExists: false);

        password.Should().Be("S3cret-From-Dokploy");
    }

    [Fact]
    public void Falls_back_to_the_well_known_password_in_development()
    {
        // `dotnet run` against a local Postgres has to keep working with no configuration.
        var password = ServiceExtensions.ResolveSeedAdminPassword(
            Configuration(null), Environment("Development"), adminExists: false);

        password.Should().Be(ServiceExtensions.DevelopmentAdminPassword);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "FullyQualifiedName~SeedAdminPasswordTests"
```

Expected: the build fails with `CS0117: 'ServiceExtensions' does not contain a definition for 'ResolveSeedAdminPassword'` (and the same for `DevelopmentAdminPassword`).

If instead the build fails with `CS0246: The type or namespace name 'IHostEnvironment' could not be found`, the test project is not picking up the ASP.NET shared framework through its `PeopleCore.API` reference. Add this to `tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj` and re-run:

```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [ ] **Step 3: Add the using directive**

In `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, the `using` block currently starts with `using System.Text;` and `using Amazon.S3;`. Add:

```csharp
using Microsoft.Extensions.Hosting;
```

This supplies both `IHostEnvironment` and the `IsDevelopment()` extension method.

- [ ] **Step 4: Write the implementation**

Append to the `ServiceExtensions` class in `src/PeopleCore.API/Extensions/ServiceExtensions.cs`, immediately after the closing brace of `ResolveJwtSigningKey`:

```csharp
    /// <summary>
    /// The password Development seeds the administrator with when none is configured. Public so
    /// that the warning in Program.cs and the test that pins this behaviour both name one value.
    /// </summary>
    public const string DevelopmentAdminPassword = "Admin@123456";

    /// <summary>
    /// Resolves the password to seed the administrator account with, or <c>null</c> when there is
    /// nothing to seed.
    /// </summary>
    /// <remarks>
    /// This used to log a warning and carry on, which meant a Production deploy missing
    /// Seed:AdminPassword came up healthy — serving the client, answering /health — with no
    /// account anyone could log in with. The only signal was a line in the container log.
    /// Refuse to start instead, the way <see cref="ResolveJwtSigningKey"/> already does for a
    /// missing Jwt:Key.
    ///
    /// The throw is conditional on <paramref name="adminExists"/>. An unconditional one would
    /// take a working deployment down the moment somebody pruned the variable from its
    /// environment — an outage traded for a misconfiguration that costs nothing while an
    /// administrator is already in the database.
    /// </remarks>
    public static string? ResolveSeedAdminPassword(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool adminExists)
    {
        var password = configuration["Seed:AdminPassword"];

        if (!string.IsNullOrWhiteSpace(password))
            return password;

        if (adminExists)
            return null;

        if (environment.IsDevelopment())
            return DevelopmentAdminPassword;

        throw new InvalidOperationException(
            "Seed:AdminPassword is not configured and no administrator account exists, so this " +
            "deployment would start with no way to log in. Set it out of source control, for " +
            "example via the Seed__AdminPassword environment variable.");
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "FullyQualifiedName~SeedAdminPasswordTests"
```

Expected: `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 6: Call it from Program.cs**

In `src/PeopleCore.API/Program.cs`, replace lines 97-123 — from `var adminEmail = ...` through the closing brace of the `else if (await userManager.FindByEmailAsync(adminEmail) is null)` block — with:

```csharp
    // Docker Compose substitutes an empty string for an unset SEED_ADMIN_EMAIL, not an absent
    // key, so "??" never sees a null to fall back on. IsNullOrWhiteSpace catches that case too.
    var configuredAdminEmail = builder.Configuration["Seed:AdminEmail"];
    var adminEmail = string.IsNullOrWhiteSpace(configuredAdminEmail)
        ? "admin@peoplecore.local"
        : configuredAdminEmail;
    var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
    var adminPassword = ServiceExtensions.ResolveSeedAdminPassword(
        builder.Configuration, app.Environment, adminExists: existingAdmin is not null);

    if (existingAdmin is null && adminPassword is not null)
    {
        if (adminPassword == ServiceExtensions.DevelopmentAdminPassword)
            app.Logger.LogWarning(
                "Seeding {Email} with the well-known development password. Set Seed:AdminPassword to override.",
                adminEmail);

        var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        var created = await userManager.CreateAsync(admin, adminPassword);
        if (created.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
        else
        {
            // Logging this and continuing is exactly the failure mode this guard exists to
            // prevent: a configured-but-rejected password (e.g. Identity's RequireDigit) would
            // still leave the container healthy, serving /health, with no account anyone could
            // log in with. The Identity errors say precisely what was wrong - e.g. "Passwords
            // must have at least one digit" - which is what an operator needs to fix it.
            throw new InvalidOperationException(
                $"Failed to seed the admin account {adminEmail}: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));
        }
    }
```

`Program.cs:11` already has `using PeopleCore.API.Extensions;`, so `ServiceExtensions` resolves unqualified — no new using directive is needed.

**Later broadened** (final whole-branch review): the email fallback and the failure branch
above were revised after this task originally landed — `CreateAsync` failing (e.g. a
configured password Identity rejects) used to log and continue, leaving the same
no-login-possible end state this task exists to prevent. It now throws. See the code for
the current version; this block is kept here as the historical record of Task 1.

- [ ] **Step 7: Verify the whole solution still builds and the suite is green**

```bash
dotnet build PeopleCore.slnx --configuration Release
```

Expected: `Build succeeded`, 0 errors.

```bash
dotnet test PeopleCore.slnx --no-build --configuration Release
```

Expected: all tests pass. `PeopleCore.Infrastructure.Tests` needs the `PEOPLECORE_TEST_POSTGRES` environment variable pointing at a local Postgres — the same one `ci.yml` sets. If it is not available locally, run only the Application tests and note it.

- [ ] **Step 8: Commit**

```bash
git add src/PeopleCore.API/Extensions/ServiceExtensions.cs src/PeopleCore.API/Program.cs tests/PeopleCore.Application.Tests/Api/SeedAdminPasswordTests.cs
git commit -m "feat(api): refuse to start when no admin account can be seeded

A Production deploy missing Seed:AdminPassword logged a warning and came
up healthy with no account anyone could log in with. Throw instead,
matching what ResolveJwtSigningKey already does for a missing Jwt:Key.

Conditional on the account being absent: an unconditional throw would
take a running deployment down the moment the variable was pruned from
its environment, trading a silent failure for an outage."
```

---

## Task 2: Apply migrations at startup

The API has 11 migrations under `src/PeopleCore.Infrastructure/Persistence/Migrations` and calls neither `Migrate` nor `EnsureCreated` anywhere outside the test fixture. A fresh Neon database would come up empty and every seeding statement below would fail. This is the procedure SPMS.Training uses in `DataSeeder.SeedAsync`, adopted as-is.

**Files:**
- Modify: `src/PeopleCore.API/Program.cs:86-130`

**Interfaces:**
- Consumes: nothing. (Task 1 touches lines 97-123 of the same file; apply Task 1 first so the line numbers below hold.)
- Produces: nothing consumed by later tasks. Task 4's `HEALTHCHECK` start period assumes migrations run at boot.

- [ ] **Step 1: Move the `AppDbContext` resolution to the top of the seeding scope**

In `src/PeopleCore.API/Program.cs`, delete this line — it sits immediately after the admin-seeding block and immediately before the `if (!await dbContext.Companies.AnyAsync())` check. (Do not go by line number: Task 1 rewrote the block above it and shifted everything after.)

```csharp
    var dbContext = scope.ServiceProvider.GetRequiredService<PeopleCore.Infrastructure.Persistence.AppDbContext>();
```

Then insert it as the first statement inside `using (var scope = app.Services.CreateScope())`, before `var roleManager = ...`:

```csharp
    var dbContext = scope.ServiceProvider.GetRequiredService<PeopleCore.Infrastructure.Persistence.AppDbContext>();
```

Everything that follows in the scope touches tables, so the schema has to exist before any of it runs.

- [ ] **Step 2: Add the migration block**

Immediately after the `dbContext` line inserted in Step 1, and before `var roleManager = ...`:

```csharp
    // Apply pending migrations. The catch is SPMS.Training's: a transaction-mode connection
    // pooler — Neon's PgBouncer, Supabase's Supavisor — can interrupt the migration lock and
    // surface as the context being disposed mid-flight. It is a fallback, not a substitute. If
    // migrations genuinely did not apply, startup continues only as far as the seeding below,
    // which fails loudly against a schema that is not there.
    try
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            app.Logger.LogInformation("Applying {Count} pending migration(s)...", pending.Count);
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            app.Logger.LogInformation("Database is up to date - no pending migrations");
        }
    }
    catch (ObjectDisposedException)
    {
        app.Logger.LogWarning(
            "MigrateAsync failed (connection pooler limitation) - verifying database connectivity...");
        if (!await dbContext.Database.CanConnectAsync())
            throw new InvalidOperationException("Cannot connect to database");
        app.Logger.LogInformation("Database connection verified successfully");
    }
```

- [ ] **Step 3: Verify it builds**

```bash
dotnet build PeopleCore.slnx --configuration Release
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Verify migrations actually apply against an empty database**

Create a throwaway database and point the API at it:

```bash
psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS peoplecore_migratecheck;" -c "CREATE DATABASE peoplecore_migratecheck;"
```

```bash
ConnectionStrings__Default="Host=localhost;Database=peoplecore_migratecheck;Username=postgres;Password=postgres" Jwt__Key="$(openssl rand -base64 32)" dotnet run --project src/PeopleCore.API
```

Expected in the console: `Applying 11 pending migration(s)...`, then the application starts and listens. Stop it with Ctrl-C, then confirm the schema landed:

```bash
psql -h localhost -U postgres -d peoplecore_migratecheck -c "\dt" | head -20
```

Expected: a table list including `employees`, `AspNetUsers` and `__EFMigrationsHistory`. Note the casing: `UseSnakeCaseNamingConvention` rewrites the domain entities, but ASP.NET Identity's tables and EF's own history table keep their Pascal-case names.

Start it a second time against the same database and expect `Database is up to date - no pending migrations`, proving the block is idempotent.

Then drop it:

```bash
psql -h localhost -U postgres -c "DROP DATABASE peoplecore_migratecheck;"
```

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.API/Program.cs
git commit -m "feat(api): apply pending migrations at startup

11 migrations existed and nothing outside the test fixture applied them,
so a fresh database came up empty and every seeding statement below
failed. Adopts the procedure SPMS.Training uses, ObjectDisposedException
fallback included: a transaction-mode pooler can interrupt the migration
lock, and Neon's PgBouncer is one."
```

---

## Task 3: Health endpoint

Both the container `HEALTHCHECK` in Task 4 and the Traefik `/health` path rule in Task 5 need a route that exists. The API has none.

**Files:**
- Modify: `src/PeopleCore.API/Program.cs:84`

**Interfaces:**
- Consumes: nothing.
- Produces: `GET /health` returning `200 OK` with body `{"status":"healthy"}`. Task 4's `HEALTHCHECK` and Task 5's Traefik router both address this path.

- [ ] **Step 1: Add the route**

In `src/PeopleCore.API/Program.cs`, immediately before `app.MapControllers();`:

```csharp
// Liveness only - deliberately does not touch the database. Neon scales compute to zero, and a
// cold start can outlast the health check's timeout; a DB-backed probe would have Docker restart
// a container whose only problem is that its database was asleep.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
```

It is anonymous without further ceremony: PeopleCore applies `[Authorize]` per controller rather than through a global filter.

- [ ] **Step 2: Verify it builds**

```bash
dotnet build PeopleCore.slnx --configuration Release
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Verify the endpoint answers**

Start the API against a local Postgres:

```bash
ConnectionStrings__Default="Host=localhost;Database=peoplecore;Username=postgres;Password=postgres" Jwt__Key="$(openssl rand -base64 32)" dotnet run --project src/PeopleCore.API
```

In a second shell:

```bash
curl -i http://localhost:5180/health
```

Expected: `HTTP/1.1 200 OK` and the body `{"status":"healthy"}`. Confirm no bearer token was needed. Stop the API.

- [ ] **Step 4: Commit**

```bash
git add src/PeopleCore.API/Program.cs
git commit -m "feat(api): add a /health liveness endpoint

Needed by the container HEALTHCHECK and by the Traefik path rule that
routes /health to the API container. Liveness only: Neon scales compute
to zero, so a database-backed probe would restart a container whose only
problem is that its database was asleep."
```

---

## Task 4: Container images

One `Dockerfile`, two runtime targets. Two departures from SPMS.Training's, both forced: Node has to be in the build stage because `PeopleCore.Web.csproj` shells out to npm during `dotnet publish`, and LibreOffice is dropped because nothing in PeopleCore invokes `soffice`.

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `nginx.conf`
- Create: `src/PeopleCore.Web/wwwroot/appsettings.Production.json`

**Interfaces:**
- Consumes: `GET /health` from Task 3 (the `HEALTHCHECK` target).
- Produces: two build targets, `api` and `web`, referenced by name in Task 5's compose file and Task 6's workflow. The `api` image listens on `8080`, the `web` image on `80`.

- [ ] **Step 1: Write `.dockerignore`**

Create `.dockerignore` at the repository root:

```
# Build output. A local obj/ carries absolute paths and a project.assets.json from the host,
# which poisons the container's restore.
**/bin/
**/obj/

# Reinstalled inside the image against the lockfile.
**/node_modules/

.git/
.github/
.claude/
docs/
**/TestResults/
*.user
.env
```

`tests/` is deliberately not excluded: the restore targets the whole solution, so both test `.csproj` files have to reach the build context.

- [ ] **Step 2: Write the production client configuration**

Create `src/PeopleCore.Web/wwwroot/appsettings.Production.json`:

```json
{
  "ApiBaseUrl": "https://peoplecore.m2netsolutions.com"
}
```

`WebAssemblyHostBuilder.CreateDefault` loads `appsettings.json` then `appsettings.{Environment}.json` from `wwwroot`. A standalone Blazor WASM app takes its environment from the `blazor-environment` header its host sends; nginx sends none, so it falls back to Production. The existing `appsettings.json` keeps `http://localhost:5180` for local development.

- [ ] **Step 3: Write `nginx.conf`**

Create `nginx.conf` at the repository root:

```nginx
# Serves the published Blazor WASM behind Dokploy's Traefik. Traefik terminates TLS and routes
# peoplecore.m2netsolutions.com/api and /health to the API container; everything else arrives
# here on plain HTTP.
events { worker_connections 1024; }

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;

    # `dotnet publish` writes a .gz next to every framework asset, compressed at a quality no
    # on-the-fly compressor would spend the CPU on. gzip_static hands that file over directly
    # instead of re-gzipping the payload per request. Dynamic gzip stays on for index.html and
    # anything else without a prebuilt pair.
    #
    # The .br files publish writes alongside are left unused: stock nginx has no brotli module,
    # and faking it with a try_files/add_header pair does not work - add_header cannot tell which
    # file try_files actually picked, so it would claim Content-Encoding: br on plain responses
    # too, and the gzip filter would then re-encode the brotli bytes on top.
    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/javascript application/wasm;
    gzip_min_length 1000;
    gzip_static on;
    gzip_vary on;

    server {
        listen 80;
        server_name peoplecore.m2netsolutions.com;
        root /usr/share/nginx/html;
        index index.html;

        # Blazor WASM SPA fallback
        location / {
            try_files $uri $uri/ /index.html;
        }

        # The one non-fingerprinted entry point: it names the fingerprinted bundles
        # (blazor.webassembly.<hash>.js and friends) by URL. Falling through to the bare
        # location / above would leave index.html to browser heuristic freshness, so a
        # returning browser could serve a stale copy that references a bundle the current
        # deploy no longer ships - _framework has no try_files, so that 404s instead of
        # falling back to HTML, and the app is blank until a hard reload.
        location = /index.html {
            add_header Cache-Control "no-cache";
        }

        # The service worker is how a new deploy reaches a browser that already has the old one,
        # so it is the one file that must never be cached. Note PeopleCore names it sw.js.
        location = /sw.js {
            add_header Cache-Control "no-cache, no-store, must-revalidate";
            add_header Pragma "no-cache";
            add_header Expires "0";
        }

        # Carries ApiBaseUrl. A stale copy points the client at the wrong API, which fails in a
        # way nobody would connect back to a cache header.
        location ~* ^/appsettings(\..+)?\.json$ {
            add_header Cache-Control "public, no-cache";
        }

        # Content-fingerprinted by the .NET publish, so a year is safe here - and only here.
        location ^~ /_framework/ {
            add_header Cache-Control "public, max-age=31536000, immutable";
        }

        # NOT fingerprinted: index.html links css/tailwind.css, css/app.css and js/theme.js by
        # fixed name. A year-long cache would mean a deploy's CSS and JS could not reach a
        # returning browser at all. Revalidating costs one conditional request that answers 304.
        location ~* ^/(css|js)/ {
            add_header Cache-Control "public, no-cache";
        }

        location ~* \.(webmanifest|png|ico|svg|jpg|jpeg|gif)$ {
            add_header Cache-Control "public, max-age=31536000";
        }
    }
}
```

- [ ] **Step 4: Write the `Dockerfile`**

Create `Dockerfile` at the repository root:

```dockerfile
# PeopleCore
# Multi-stage build for the API and the Blazor WebAssembly client.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# PeopleCore.Web.csproj's BuildTailwindCss target runs `npm ci` and `npm run build:css` from
# inside `dotnet publish`, so Node has to be on PATH before the publish below - not after it.
# (SPMS.Training's Dockerfile has no Node, which is why this one is not a copy of it.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Directory.Build.props first: it sets M2NetCoreProject, which the ProjectReference conditions
# in PeopleCore.Application and PeopleCore.Domain read at restore time.
COPY Directory.Build.props PeopleCore.slnx ./

# Each .csproj by its own path - Docker COPY has no ** glob. All nine are needed because the
# restore below targets the whole solution.
COPY src/M2NET.Core/M2NET.Core.csproj src/M2NET.Core/
COPY src/PeopleCore.Domain/PeopleCore.Domain.csproj src/PeopleCore.Domain/
COPY src/PeopleCore.Application/PeopleCore.Application.csproj src/PeopleCore.Application/
COPY src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj src/PeopleCore.Infrastructure/
COPY src/PeopleCore.Reports/PeopleCore.Reports.csproj src/PeopleCore.Reports/
COPY src/PeopleCore.API/PeopleCore.API.csproj src/PeopleCore.API/
COPY src/PeopleCore.Web/PeopleCore.Web.csproj src/PeopleCore.Web/
COPY tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj tests/PeopleCore.Application.Tests/
COPY tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj tests/PeopleCore.Infrastructure.Tests/

RUN dotnet restore PeopleCore.slnx

# npm install as its own layer, keyed on the lockfile, so an ordinary source change rebuilds the
# stylesheet without reinstalling Tailwind. The csproj skips its own `npm ci` when node_modules
# already exists - which, after this line, it does.
COPY src/PeopleCore.Web/package.json src/PeopleCore.Web/package-lock.json src/PeopleCore.Web/
RUN npm ci --prefix src/PeopleCore.Web --no-audit --no-fund

COPY src/ src/

RUN dotnet publish src/PeopleCore.API/PeopleCore.API.csproj -c Release -o /app/api --no-restore
RUN dotnet publish src/PeopleCore.Web/PeopleCore.Web.csproj -c Release -o /app/web --no-restore


# ─── API ──────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app

# curl is for the HEALTHCHECK. The font packages are for PeopleCore.Reports: QuestPDF and
# PDFsharp render payslips and BIR 2316, and nothing in the code calls
# FontManager.RegisterFont, so glyphs resolve from system fonts. Without these the PDFs come
# out blank or substituted rather than failing, which is why a payslip is the first thing to
# check after a deploy.
#
# No LibreOffice. SPMS.Training installs libreoffice-writer and -draw to rasterise certificate
# templates; nothing in PeopleCore invokes soffice, and the two packages cost ~700 MB.
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl \
        fontconfig \
        fonts-liberation \
        fonts-dejavu-core \
        libicu-dev \
        locales \
    && locale-gen en_US.UTF-8 \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LANG=en_US.UTF-8 \
    LC_ALL=en_US.UTF-8 \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/api .

EXPOSE 8080

# A generous start period: the container applies 11 migrations against Neon at boot, and Neon
# scales compute to zero, so the first request can wait on a cold start.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PeopleCore.API.dll"]


# ─── Web ──────────────────────────────────────────────────────────────────────
FROM nginx:alpine AS web
WORKDIR /usr/share/nginx/html

COPY --from=build /app/web/wwwroot .

COPY nginx.conf /etc/nginx/nginx.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
```

Note the `api` target does **not** copy the Blazor output into its own `wwwroot`. SPMS.Training does, but under this compose the web container serves those files and the copy would be dead weight.

**Later removed** (final whole-branch review): the symlink block this step originally wrote
was deleted outright. `PeopleCore.Web.csproj` sets
`<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>`, which rewrites the `#[.{fingerprint}]` placeholder in `index.html` at publish time, so the
published page already points straight at `_framework/blazor.webassembly.<hash>.js` —
verified by inspecting the built image. The plain name survives only as an unused
import-map key. Training needs the symlink because it does not set that csproj property;
PeopleCore does. The `blazor.webassembly.js` block in the Dockerfile above is the historical
record of Task 4; the code no longer has it.

- [ ] **Step 5: Build both images**

```bash
docker build --target api -t peoplecore-api:local .
```

Expected: `Successfully tagged peoplecore-api:local`. Watch for the `npm ci` layer succeeding and `dotnet publish` completing without a Tailwind error.

```bash
docker build --target web -t peoplecore-web:local .
```

Expected: `Successfully tagged peoplecore-web:local`.

- [ ] **Step 6: Verify the web image serves the client**

```bash
docker run --rm -d --name peoplecore-web-check -p 8081:80 peoplecore-web:local
```

```bash
curl -s http://localhost:8081/ | head -5
```

Expected: the opening of `index.html`, including `<title>PeopleCore HRMS</title>`.

```bash
curl -s http://localhost:8081/appsettings.Production.json
```

Expected: `{"ApiBaseUrl": "https://peoplecore.m2netsolutions.com"}` — confirming Step 2's file made it into the published output.

```bash
docker exec peoplecore-web-check sh -c 'ls _framework/blazor.webassembly.*.js'
curl -sI http://localhost:8081/_framework/blazor.webassembly.<hash>.js | head -3   # use the real fingerprinted name from the ls above
```

Expected: `HTTP/1.1 200 OK` for the fingerprinted name directly. There is no plain-name
symlink to fall back on (see the note above Step 5) — the published `index.html` already
points straight at the fingerprinted URL via `OverrideHtmlAssetPlaceholders`, so this check
confirms the file the page actually requests, not a compatibility shim.

```bash
curl -sI http://localhost:8081/css/tailwind.css | grep -i cache-control
```

Expected: `Cache-Control: public, no-cache`. Confirm the stylesheet is non-empty — it is generated at build time and gitignored, so an empty response means the Tailwind step silently produced nothing:

```bash
curl -s http://localhost:8081/css/tailwind.css | wc -c
```

Expected: a five- or six-figure byte count, not `0`.

```bash
docker stop peoplecore-web-check
```

- [ ] **Step 7: Verify the API image boots and answers /health**

Postgres runs locally as the container `m2net-postgres` (`postgres:18-alpine3.23`, user `postgres`, password `postgres`). Container-name DNS does not work on Docker's default bridge, so put both containers on a user-defined network first — this attaches `m2net-postgres` to an additional network without disturbing its existing bridge connectivity or published ports:

```bash
docker network create peoplecore-check
docker network connect peoplecore-check m2net-postgres
```

The API container then reaches the database at `Host=m2net-postgres`:

```bash
docker run --rm -d --name peoplecore-api-check --network peoplecore-check -p 8082:8080 \
  -e ConnectionStrings__Default="Host=m2net-postgres;Database=peoplecore;Username=postgres;Password=postgres" \
  -e Jwt__Key="$(openssl rand -base64 32)" \
  -e Seed__AdminPassword="Local@123456" \
  peoplecore-api:local
```

```bash
curl -s http://localhost:8082/health
```

Expected: `{"status":"healthy"}`.

```bash
docker logs peoplecore-api-check | grep -i "migration"
```

Expected: either `Applying N pending migration(s)...` or `Database is up to date - no pending migrations`.

Now confirm Task 1's guard fires in a container. Stop the running one and start another without `Seed__AdminPassword` against an empty database:

```bash
docker stop peoplecore-api-check
docker exec -e PGPASSWORD=postgres m2net-postgres psql -U postgres -c "DROP DATABASE IF EXISTS peoplecore_guardcheck;" -c "CREATE DATABASE peoplecore_guardcheck;"
docker run --rm --name peoplecore-guard-check --network peoplecore-check \
  -e ConnectionStrings__Default="Host=m2net-postgres;Database=peoplecore_guardcheck;Username=postgres;Password=postgres" \
  -e Jwt__Key="$(openssl rand -base64 32)" \
  peoplecore-api:local
```

Expected: the container exits non-zero with an `InvalidOperationException` naming `Seed__AdminPassword`. This is the behaviour that keeps a misconfigured deploy from coming up loginless.

Then clean up the throwaway database and the scratch network:

```bash
docker exec -e PGPASSWORD=postgres m2net-postgres psql -U postgres -c "DROP DATABASE peoplecore_guardcheck;"
docker network disconnect peoplecore-check m2net-postgres
docker network rm peoplecore-check
```

- [ ] **Step 8: Commit**

```bash
git add Dockerfile .dockerignore nginx.conf src/PeopleCore.Web/wwwroot/appsettings.Production.json
git commit -m "build: containerise the API and the Blazor client

Two targets off one multi-stage build, after SPMS.Training's shape, with
the two differences PeopleCore forces. Node goes in the build stage
because PeopleCore.Web.csproj shells out to npm for Tailwind during
dotnet publish. LibreOffice comes out because nothing here invokes
soffice and it costs ~700 MB.

One baked nginx.conf, no runtime override: training bind-mounts a second
config only to paper over a 443/TLS one left from the DigitalOcean
droplet, and PeopleCore has no such history."
```

---

## Task 5: Compose file and environment template

**Files:**
- Create: `docker-compose.dokploy.yml`
- Create: `.env.example`

**Interfaces:**
- Consumes: the `api` and `web` images from Task 4; `GET /health` from Task 3.
- Produces: service names `peoplecore-api` and `peoplecore-web`, and the variable names Task 7's runbook tells the operator to fill in.

- [ ] **Step 1: Write `docker-compose.dokploy.yml`**

Create `docker-compose.dokploy.yml` at the repository root:

```yaml
# PeopleCore — Dokploy stack for the Hetzner box, behind Dokploy's Traefik.
#
# ─── Setup in Dokploy ─────────────────────────────────────────────────────────
#  1. Create a "Compose" service pointing at this file.
#  2. In the service's *Environment* tab, set every variable listed in .env.example.
#  3. Point peoplecore.m2netsolutions.com DNS at the Hetzner box, then deploy —
#     Traefik issues the Let's Encrypt certificate automatically over HTTP-01.
#
# ─── Routing ──────────────────────────────────────────────────────────────────
#  Traefik terminates TLS for peoplecore.m2netsolutions.com and splits by path:
#    /api + /health  -> api  (higher priority)
#    everything else -> web  (nginx serving the Blazor WASM on plain HTTP)
#  SPMS.Training is untouched; it keeps its own compose service on the same box.
services:
  peoplecore-api:
    image: ghcr.io/mpmartinez/peoplecore-api:latest
    pull_policy: always
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__Default: ${PEOPLECORE_DB_CONNECTION}
      Jwt__Key: ${JWT_KEY}
      Jwt__Issuer: peoplecore-api
      Jwt__Audience: peoplecore-web
      AllowedOrigins: https://peoplecore.m2netsolutions.com
      Seed__AdminEmail: ${SEED_ADMIN_EMAIL}
      Seed__AdminPassword: ${SEED_ADMIN_PASSWORD}
      Storage__Provider: R2
      R2__AccountId: ${R2_ACCOUNT_ID}
      R2__AccessKey: ${R2_ACCESS_KEY}
      R2__SecretKey: ${R2_SECRET_KEY}
      R2__BucketName: ${R2_BUCKET_NAME}
    # Neon's hostname has to resolve from inside the container; the box's own resolver has
    # been unreliable for this on the training stack.
    dns:
      - 8.8.8.8
      - 8.8.4.4
    networks:
      - dokploy-network
    labels:
      - "traefik.enable=true"
      - "traefik.docker.network=dokploy-network"
      - "traefik.http.routers.peoplecore-api.rule=Host(`peoplecore.m2netsolutions.com`) && (PathPrefix(`/api`) || PathPrefix(`/health`))"
      - "traefik.http.routers.peoplecore-api.entrypoints=websecure"
      - "traefik.http.routers.peoplecore-api.tls=true"
      - "traefik.http.routers.peoplecore-api.tls.certresolver=letsencrypt"
      - "traefik.http.routers.peoplecore-api.priority=100"
      - "traefik.http.services.peoplecore-api.loadbalancer.server.port=8080"

  peoplecore-web:
    image: ghcr.io/mpmartinez/peoplecore-web:latest
    pull_policy: always
    restart: unless-stopped
    depends_on:
      - peoplecore-api
    networks:
      - dokploy-network
    labels:
      - "traefik.enable=true"
      - "traefik.docker.network=dokploy-network"
      - "traefik.http.middlewares.peoplecore-compress.compress=true"
      - "traefik.http.routers.peoplecore-web.rule=Host(`peoplecore.m2netsolutions.com`)"
      - "traefik.http.routers.peoplecore-web.entrypoints=websecure"
      - "traefik.http.routers.peoplecore-web.tls=true"
      - "traefik.http.routers.peoplecore-web.tls.certresolver=letsencrypt"
      - "traefik.http.routers.peoplecore-web.priority=10"
      - "traefik.http.routers.peoplecore-web.middlewares=peoplecore-compress"
      - "traefik.http.services.peoplecore-web.loadbalancer.server.port=80"
      # HTTP -> HTTPS redirect
      - "traefik.http.routers.peoplecore-web-http.rule=Host(`peoplecore.m2netsolutions.com`)"
      - "traefik.http.routers.peoplecore-web-http.entrypoints=web"
      - "traefik.http.routers.peoplecore-web-http.middlewares=peoplecore-redirect"
      - "traefik.http.middlewares.peoplecore-redirect.redirectscheme.scheme=https"

networks:
  dokploy-network:
    external: true
```

There is no named volume. SPMS.Training mounts one because its `FileStorage__UploadPath` writes to disk; PeopleCore's documents go to R2, so the container holds no durable state.

- [ ] **Step 2: Write `.env.example`**

Create `.env.example` at the repository root:

```bash
# Copy to ".env" alongside docker-compose.dokploy.yml and fill in real values, or paste the
# same keys into the Dokploy service's Environment tab. Docker Compose loads .env
# automatically; .env is gitignored, this file is not.
#
#   cp .env.example .env   # then edit .env

# Neon Postgres — pooled endpoint (host ends in "-pooler"), database "peoplecore".
# "SSL Mode=VerifyFull" authenticates the server, not just encrypts the channel: Neon serves
# publicly-trusted certificates, so there is no reason to fall back to "Trust Server
# Certificate=true" on a connection that crosses the public internet carrying the whole HRMS
# and payroll dataset. Keep "No Reset On Close=true": Neon's PgBouncer runs in transaction
# mode and Npgsql's server-side prepared statements break against it without that flag.
PEOPLECORE_DB_CONNECTION=Host=ep-CHANGE_ME-pooler.c-2.ap-southeast-1.aws.neon.tech;Database=peoplecore;Username=neondb_owner;Password=CHANGE_ME;SSL Mode=VerifyFull;No Reset On Close=true

# JWT signing key — at least 32 bytes. Generate: openssl rand -base64 32
# The API refuses to start if this is missing or shorter.
JWT_KEY=CHANGE_ME

# The administrator account seeded on first run. The API refuses to start when no admin
# exists in the database and SEED_ADMIN_PASSWORD is unset — that combination would come up
# healthy with no way to log in.
SEED_ADMIN_EMAIL=admin@m2netsolutions.com
SEED_ADMIN_PASSWORD=CHANGE_ME

# Cloudflare R2 — bucket for employee documents. The bucket stays private; the API issues
# time-limited presigned URLs. The account id is the one in the R2 endpoint hostname.
R2_ACCOUNT_ID=CHANGE_ME
R2_ACCESS_KEY=CHANGE_ME
R2_SECRET_KEY=CHANGE_ME
# The application currently ignores this value - EmployeeDocumentService hardcodes the bucket
# name as a const - and always uses "peoplecore-documents". Create the R2 bucket under that
# exact name; a differently-named bucket fails with NoSuchBucket regardless of what this is
# set to. (Wiring this variable through is queued as separate work.)
R2_BUCKET_NAME=peoplecore-documents
```

**Later revised** (final whole-branch review): the connection string above moved from
`SSL Mode=Require;Trust Server Certificate=true` to `SSL Mode=VerifyFull`, and the
`R2_BUCKET_NAME` comment noting it is currently inert was added. The current `.env.example`
is the source of truth; this block is Task 5's historical record.

- [ ] **Step 3: Verify the compose file parses and every variable resolves**

```bash
cp .env.example .env
docker compose -f docker-compose.dokploy.yml config
```

Expected: the fully resolved YAML, with no `WARN[0000] The "X" variable is not set` lines. Every `${...}` in the compose file must have a matching key in `.env.example` — a warning here means one is missing.

Confirm the two routers cannot both win:

```bash
docker compose -f docker-compose.dokploy.yml config | grep -E "priority|rule="
```

Expected: the api router at `priority=100` with the `/api`/`/health` rule, the web router at `priority=10` with the bare host rule.

Then remove the local `.env` so it cannot be committed or shadow anything later:

```bash
rm .env
```

- [ ] **Step 4: Commit**

```bash
git add docker-compose.dokploy.yml .env.example
git commit -m "build: add the Dokploy compose stack for peoplecore.m2netsolutions.com

Two services on the existing dokploy-network, Traefik terminating TLS
and splitting by path - /api and /health to the API at priority 100,
everything else to nginx at 10. No named volume: documents go to R2, so
neither container holds durable state."
```

---

## Task 6: Publish images to GHCR

**Files:**
- Create: `.github/workflows/docker-publish.yml`

**Interfaces:**
- Consumes: the `api` and `web` targets from Task 4's `Dockerfile`.
- Produces: `ghcr.io/mpmartinez/peoplecore-api:latest` and `ghcr.io/mpmartinez/peoplecore-web:latest`, which Task 5's compose file pulls.

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/docker-publish.yml`:

```yaml
name: Build and Push Docker Images

on:
  push:
    branches:
      - main
  pull_request:
    branches:
      - main
  workflow_dispatch:

env:
  REGISTRY: ghcr.io
  IMAGE_NAME_API: ${{ github.repository_owner }}/peoplecore-api
  IMAGE_NAME_WEB: ${{ github.repository_owner }}/peoplecore-web

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata for API
        id: meta-api
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME_API }}
          tags: |
            type=ref,event=branch
            type=ref,event=pr
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=raw,value=latest,enable={{is_default_branch}}

      - name: Extract metadata for Web
        id: meta-web
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME_WEB }}
          tags: |
            type=ref,event=branch
            type=ref,event=pr
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=raw,value=latest,enable={{is_default_branch}}

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build and push API image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./Dockerfile
          target: api
          push: ${{ github.event_name != 'pull_request' }}
          tags: ${{ steps.meta-api.outputs.tags }}
          labels: ${{ steps.meta-api.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Build and push Web image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./Dockerfile
          target: web
          push: ${{ github.event_name != 'pull_request' }}
          tags: ${{ steps.meta-web.outputs.tags }}
          labels: ${{ steps.meta-web.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

# Deployment is Dokploy's job. It watches GHCR and redeploys because
# docker-compose.dokploy.yml sets pull_policy: always. CI's responsibility ends at pushing
# :latest. Correctness is ci.yml's job, and this workflow does not duplicate it.
```

Pull requests build both targets without pushing, so a Dockerfile that no longer builds fails the PR rather than the deploy.

- [ ] **Step 2: Verify the workflow is valid YAML and references real targets**

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/docker-publish.yml')); print('valid YAML')"
```

Expected: `valid YAML`.

```bash
grep -n "target:" .github/workflows/docker-publish.yml
grep -n "^FROM .* AS " Dockerfile
```

Expected: the two `target:` values (`api`, `web`) each appear as an `AS <name>` in the Dockerfile.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/docker-publish.yml
git commit -m "ci: build and push the two images to GHCR

Keyed on main, not training's master. Pull requests build both targets
without pushing, so a broken Dockerfile fails the PR rather than the
deploy. Deployment itself stays Dokploy's job via pull_policy: always."
```

- [ ] **Step 4: Confirm the workflow runs green after push**

Once the branch is pushed and merged to `main`:

```bash
gh run list --workflow=docker-publish.yml --limit 3
```

Expected: the most recent run `completed success`. Then confirm both packages exist:

```bash
gh api "user/packages?package_type=container" --jq '.[].name'
```

Expected: the list includes `peoplecore-api` and `peoplecore-web`.

---

## Task 7: Deployment runbook

The remaining steps need a human with console access to Neon, Cloudflare, DNS and Dokploy. Write them down rather than leaving them in a chat log.

**Files:**
- Create: `docs/deployment.md`

**Interfaces:**
- Consumes: the variable names from Task 5's `.env.example`, the image names from Task 6, and `GET /health` from Task 3.
- Produces: nothing consumed by code.

- [ ] **Step 1: Write `docs/deployment.md`**

Create `docs/deployment.md`:

````markdown
# Deploying PeopleCore

PeopleCore runs on the Hetzner box that already hosts SPMS.Training, under the same Dokploy
installation and behind the same Traefik. Postgres is Neon; employee documents are on
Cloudflare R2.

- Design and rationale: `docs/superpowers/specs/2026-09-12-hetzner-deployment-design.md`
- Stack definition: `docker-compose.dokploy.yml`
- Variables: `.env.example`

## How a deploy happens

Push to `main` → `.github/workflows/docker-publish.yml` builds `ghcr.io/mpmartinez/peoplecore-api:latest`
and `...-web:latest` → Dokploy pulls them, because the compose file sets `pull_policy: always`.

Traefik terminates TLS for `peoplecore.m2netsolutions.com` and splits by path: `/api` and
`/health` reach the API container, everything else reaches nginx serving the Blazor client.

## First-time setup

These are one-off and manual.

### 1. Neon

Create a database named `peoplecore` in the existing Neon project — the same project that
holds `training`. The role stays `neondb_owner`.

Copy the **pooled** connection string (the host ends in `-pooler`) and append
`SSL Mode=VerifyFull;No Reset On Close=true`. Neon serves publicly-trusted certificates, so
`VerifyFull` authenticates the server rather than merely encrypting the channel — this
connection crosses the public internet carrying the whole HRMS and payroll dataset, so
`Trust Server Certificate=true` (which disables that validation) has no place here. Keep
`No Reset On Close=true` exactly as it is: Neon's PgBouncer runs in transaction mode, and
Npgsql's server-side prepared statements break against it without that flag. The result:

```
Host=ep-<id>-pooler.<region>.aws.neon.tech;Database=peoplecore;Username=neondb_owner;Password=<secret>;SSL Mode=VerifyFull;No Reset On Close=true
```

The API applies its 11 migrations itself on first boot. There is nothing to run by hand.

### 2. Cloudflare R2

1. Create a bucket named `peoplecore-documents`.
2. Create an R2 API token scoped to that bucket with **Object Read & Write**.
3. Record the account id, the access key id, and the secret access key.

Leave the bucket **private**. The API hands out time-limited presigned URLs
(`R2StorageService.GetPresignedUrlAsync`), so no public access binding is needed — adding
one would expose every document to anyone who guessed a key.

### 3. DNS

Point `peoplecore.m2netsolutions.com` at the Hetzner box with an A record.

Do this **before** the first deploy. Traefik issues the certificate over HTTP-01, which
means the name has to resolve to the box before it can be validated.

### 4. GHCR visibility

The two packages default to private on their first push. Either make them public, or add a
registry credential in Dokploy. Dokploy cannot pull an image it cannot see, and the failure
reads as a generic pull error.

### 5. Dokploy

Create a **Compose** service pointing at `docker-compose.dokploy.yml`, then fill in the
Environment tab from `.env.example`:

| Variable | Source |
|---|---|
| `PEOPLECORE_DB_CONNECTION` | Step 1 |
| `JWT_KEY` | `openssl rand -base64 32` |
| `SEED_ADMIN_EMAIL` | the address that should own the first admin account |
| `SEED_ADMIN_PASSWORD` | chosen now; the API refuses to start without it on a fresh database |
| `R2_ACCOUNT_ID`, `R2_ACCESS_KEY`, `R2_SECRET_KEY`, `R2_BUCKET_NAME` | Step 2 |

Then deploy.

## After the first deploy

Work through all five. The first two prove the stack; the last three cover the things no
amount of configuration review can confirm.

1. **Health** — `curl https://peoplecore.m2netsolutions.com/health` returns
   `{"status":"healthy"}`.
2. **Migrations** — the API container log shows `Applying 11 pending migration(s)...`
   followed by the seeding lines.
3. **Login** — sign in as `SEED_ADMIN_EMAIL` with `SEED_ADMIN_PASSWORD`.
4. **Generate a payslip PDF.** This is the one behaviour the deployment cannot prove by
   construction. Nothing in the code calls `FontManager.RegisterFont`, so QuestPDF and
   PDFsharp resolve glyphs from the container's system fonts — and missing fonts degrade to
   blank or substituted text rather than throwing. A payslip that renders correctly is the
   only evidence that the font packages in the `api` image are doing their job.
5. **Upload an employee document and download it again.** Exercises the R2 round trip and
   the presigned URL, which no other check touches.

## Troubleshooting

**The API container crash-loops with `InvalidOperationException` naming `Seed__AdminPassword`.**
Working as designed: no administrator exists in the database and no password is configured,
so the deployment would have come up with no way to log in. Set `SEED_ADMIN_PASSWORD` and
redeploy.

**The API container crash-loops with `InvalidOperationException` naming `Jwt:Key`.**
`JWT_KEY` is unset or shorter than 32 bytes.

**The log shows `MigrateAsync failed (connection pooler limitation)`.**
The pooler interrupted the migration lock. If the database was already migrated, this is
harmless and the next line confirms connectivity. On a *fresh* database it means the schema
is not there, and the seeding immediately afterwards will fail — redeploy to retry.

**The very first deploy logs an EF Core `fail:` line about `__EFMigrationsHistory`.**
Expected once, on a genuinely empty database, and not an incident. `GetPendingMigrationsAsync`
finds out which migrations have run by querying the history table — which does not exist yet
the first time — and EF logs that failed probe at error level before creating it. The
`Applying N pending migration(s)...` line immediately after is the one that matters. It does
not recur on later deploys.

**The client loads but every API call fails.**
Check `appsettings.Production.json` in the web image matches the deployed hostname, and that
the api router still outranks the web router — if the web router's priority ever meets or
exceeds 100, it swallows `/api` and the client gets `index.html` back instead of JSON.

**A deploy's CSS or JS changes do not reach a returning browser.**
`nginx.conf` serves `/css/` and `/js/` with `no-cache` precisely to prevent this. If it
regresses, check that those paths did not pick up the `_framework` immutable rule — only
`_framework` is content-fingerprinted and safe to cache for a year.

**The first deploy fails with a TLS or certificate-validation error from Npgsql.**
That is `SSL Mode=VerifyFull` in `PEOPLECORE_DB_CONNECTION` — the one part of the connection
string not yet exercised against real Neon. Confirm the string was copied from Neon's
pooled endpoint unmodified; a hand-edited host or a non-Neon Postgres in front of it is the
usual cause of a certificate Npgsql won't validate.
````

**Later revised** (final whole-branch review): the connection string in Step 1 above moved
to `SSL Mode=VerifyFull`, and the TLS troubleshooting entry was added. The current
`docs/deployment.md` is the source of truth; this block is Task 7's historical record.

- [ ] **Step 2: Verify the runbook is accurate against the files it describes**

Every variable named in the runbook must exist in `.env.example`:

```bash
grep -oE "\`[A-Z][A-Z0-9_]*\`" docs/deployment.md | tr -d '`' | sort -u | while read v; do
  grep -q "^$v=" .env.example || echo "NOT IN .env.example: $v"
done
```

Expected: no output other than lines for non-variable names in backticks (`Object`, `Host`, etc.) — check each reported name by eye and confirm it is prose, not a variable.

Confirm the migration count the runbook quotes is still right:

```bash
ls src/PeopleCore.Infrastructure/Persistence/Migrations/*.cs | grep -vc "Designer\|ModelSnapshot"
```

Expected: `11`. If the number has moved because a migration was added during this work, update the two places `docs/deployment.md` says 11.

- [ ] **Step 3: Commit**

```bash
git add docs/deployment.md
git commit -m "docs: add the PeopleCore deployment runbook

The Neon, R2, DNS, GHCR and Dokploy steps need console access and cannot
be automated from here, so they are written down rather than left in a
chat log. Includes the five post-deploy checks - a payslip PDF and an R2
round trip among them, being the two things no configuration review can
confirm."
```

---

## Done

After Task 7, `main` builds two images on every push, Dokploy deploys them, and
`docs/deployment.md` carries the manual steps.

What remains is **not** code and is the operator's to do, in this order: Neon database →
R2 bucket → DNS → GHCR visibility → Dokploy service. Then the five post-deploy checks.
