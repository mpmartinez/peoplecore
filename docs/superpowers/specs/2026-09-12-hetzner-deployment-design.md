# PeopleCore on Hetzner — deployment design

**Date:** 2026-09-12
**Status:** Approved

Deploy the PeopleCore API and Blazor WebAssembly client to the existing Hetzner box that
already runs SPMS.Training, using the same Dokploy + Traefik + GHCR arrangement. Postgres
is a new database in the existing Neon project; employee documents go to a new Cloudflare
R2 bucket.

## Decisions taken before design

| Question | Answer |
|---|---|
| Hostname | `peoplecore.m2netsolutions.com` |
| Host | The Hetzner box already running training, same Dokploy instance and `dokploy-network` |
| Database | New database `peoplecore` inside the existing Neon project |
| R2 bucket | Does not exist yet; created by hand in the Cloudflare dashboard |
| Migration procedure | The same procedure training uses (see §4) |
| Nginx | One baked config, no runtime override (deviation from training, approved) |

## 1. Container build

A single multi-stage `Dockerfile` at the repository root with a shared `build` stage and
two runtime targets, `api` and `web` — the shape SPMS.Training uses.

Two deliberate differences from training's Dockerfile:

**Node 22 in the build stage.** `src/PeopleCore.Web/PeopleCore.Web.csproj` defines a
`BuildTailwindCss` target that runs `npm ci` and `npm run build:css` before static web
assets are collected. `dotnet publish` of the Web project therefore cannot run without
Node on PATH. Training's SDK image has no Node, so its Dockerfile cannot be copied
verbatim. `package.json` and `package-lock.json` are copied and `npm ci` run as their own
layer before the source copy, so an ordinary source change does not reinstall Tailwind.

**No LibreOffice.** Training installs `libreoffice-writer` and `libreoffice-draw` to
rasterise certificate templates. Nothing in PeopleCore invokes `soffice` — a case-insensitive
grep for `soffice` or `libreoffice` over `src` returns nothing — and the two packages cost
roughly 700 MB in the runtime image.

`fontconfig`, `fonts-liberation` and `fonts-dejavu-core` are kept. `PeopleCore.Reports`
renders payslips and BIR 2316 with QuestPDF and PDFsharp, and no code path calls
`FontManager.RegisterFont`, so glyphs resolve from system fonts. A runtime image without
them produces blank or substituted text rather than an error, which is why §8 makes a
payslip the first post-deploy check.

Stage outline:

```
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
  # Node 22 (nodesource) for the Tailwind build
  COPY Directory.Build.props PeopleCore.slnx ./       # props defines M2NetCoreProject, which the
                                                      # ProjectReference conditions read at restore
  # Each .csproj copied by its own explicit path — Docker COPY has no ** glob. All nine
  # (seven under src/, two under tests/) are needed: the restore targets the whole slnx.
  RUN dotnet restore PeopleCore.slnx
  COPY src/PeopleCore.Web/package*.json src/PeopleCore.Web/
  RUN npm ci --prefix src/PeopleCore.Web
  COPY src/ src/
  RUN dotnet publish src/PeopleCore.API -c Release -o /app/api --no-restore
  RUN dotnet publish src/PeopleCore.Web -c Release -o /app/web --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
  # curl, fontconfig, fonts-liberation, fonts-dejavu-core, libicu, locales
  ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false, LANG/LC_ALL=en_US.UTF-8
  COPY --from=build /app/api .
  ENV ASPNETCORE_URLS=http://+:8080, ASPNETCORE_ENVIRONMENT=Production
  HEALTHCHECK curl -f http://localhost:8080/health
  ENTRYPOINT ["dotnet", "PeopleCore.API.dll"]

FROM nginx:alpine AS web
  COPY --from=build /app/web/wwwroot .
  # symlink blazor.webassembly.<hash>.js -> blazor.webassembly.js
  COPY nginx.conf /etc/nginx/nginx.conf
  EXPOSE 80
```

The `blazor.webassembly.js` symlink is carried over from training unchanged. .NET 10
fingerprints the framework JS filename, `index.html` references it through the
`#[.{fingerprint}]` placeholder, and import maps do not apply to classic script tags.

The API image does **not** copy the Blazor output into its own `wwwroot`. Training does,
but under the Dokploy compose the web container serves those files and the copy is dead
weight.

## 2. Nginx

One `nginx.conf`, baked into the `web` image, serving plain HTTP on `:80` behind Traefik.
Training instead bakes a 443/TLS config and bind-mounts `nginx.dokploy.conf` over it at
runtime; that override exists only because training's baked config is a leftover from the
DigitalOcean droplet that also fronted IAS. PeopleCore has no such history, so the second
file would be a workaround with no cause.

Caching rules are taken from `nginx.dokploy.conf`, which reasons them out in comments:

- SPA fallback: `try_files $uri $uri/ /index.html`
- `gzip_static on` — `dotnet publish` writes a `.gz` beside each framework asset,
  compressed harder than any on-the-fly compressor would pay for. `.br` files are left
  unused; stock nginx has no brotli module.
- `/_framework/` — `max-age=31536000, immutable` (content-fingerprinted by the publish)
- `/css/` and `/js/` — `no-cache`. `index.html` links `css/tailwind.css`, `css/app.css` and
  `js/theme.js` by fixed name, so a long cache here would keep a deploy's CSS from ever
  reaching a returning browser.
- `sw.js` — `no-cache, no-store, must-revalidate`. Note PeopleCore's service worker is
  named `sw.js`, not training's `service-worker.js`.
- Images and `.webmanifest` — `max-age=31536000`

PeopleCore's `sw.js` is a pass-through worker that caches nothing, so training's
`BUILD_VERSION` stamping (which drives its "new version available" prompt) has no
counterpart here and is omitted.

## 3. Compose and routing

`docker-compose.dokploy.yml` at the repository root, structured like training's: two
services on the external `dokploy-network`, Traefik terminating TLS for
`peoplecore.m2netsolutions.com` and splitting by path.

| Router | Rule | Priority | Target |
|---|---|---|---|
| `peoplecore-api` | host matches and path starts with `/api` or `/health` | 100 | `peoplecore-api:8080` |
| `peoplecore-web` | host matches | 10 | `peoplecore-web:80` |
| `peoplecore-web-http` | same host on the `web` entrypoint | — | redirect to https |

Both TLS routers use `certresolver=letsencrypt`; a `compress` middleware is attached to the
web router. Images are `ghcr.io/mpmartinez/peoplecore-api:latest` and
`ghcr.io/mpmartinez/peoplecore-web:latest` with `pull_policy: always` and
`restart: unless-stopped`.

No named volume. Training mounts `spms-training-uploads:/app/uploads` because its
`FileStorage__UploadPath` writes to disk; PeopleCore's documents go to R2, so the
container holds no durable state.

Environment variables, set in the Dokploy service's Environment tab:

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://+:8080` |
| `ConnectionStrings__Default` | Neon pooled string (see §4) |
| `Jwt__Key` | secret, at least 32 bytes; `openssl rand -base64 32` |
| `Jwt__Issuer` | `peoplecore-api` |
| `Jwt__Audience` | `peoplecore-web` |
| `AllowedOrigins` | `https://peoplecore.m2netsolutions.com` |
| `Seed__AdminEmail` | chosen admin address |
| `Seed__AdminPassword` | secret |
| `Storage__Provider` | `R2` |
| `R2__AccountId` | Cloudflare account id |
| `R2__AccessKey` / `R2__SecretKey` | R2 API token pair |
| `R2__BucketName` | `peoplecore-documents` |

`AllowedOrigins` is a single string in PeopleCore — `Program.cs` reads it with
`builder.Configuration["AllowedOrigins"]` and passes it to `policy.WithOrigins(...)` —
unlike training's indexed `AllowedOrigins__0` array. The flat env var is correct here.

`Seed__AdminPassword` is required: without it `Program.cs` logs
"Seed:AdminPassword is not configured; skipping the default admin account" and the
deployment comes up with no way to log in.

A `.env.example` at the root documents every variable with the same commentary, following
training's file.

## 4. Migrations

The API has 23 migrations under `src/PeopleCore.Infrastructure/Persistence/Migrations` and
currently calls neither `Migrate` nor `EnsureCreated` anywhere outside the test fixture. A
fresh Neon database would come up empty.

Adopt training's procedure verbatim (`DataSeeder.SeedAsync`), placed at the head of the
existing seeding scope in `PeopleCore.API/Program.cs`:

```csharp
try
{
    var pending = await dbContext.Database.GetPendingMigrationsAsync();
    if (pending.Any())
    {
        logger.LogInformation("Applying {Count} pending migration(s)...", pending.Count());
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        logger.LogInformation("Database is up to date - no pending migrations");
    }
}
catch (ObjectDisposedException)
{
    logger.LogWarning("MigrateAsync failed (connection pooler limitation) - verifying database connectivity...");
    if (!await dbContext.Database.CanConnectAsync())
        throw new InvalidOperationException("Cannot connect to database");
    logger.LogInformation("Database connection verified successfully");
}
```

The `ObjectDisposedException` catch is what training uses to survive a transaction-mode
connection pooler interrupting the migration lock. It is a fallback, not a substitute: if
migrations did not apply, the run continues only when the database is reachable, and the
existing seeding immediately afterwards will fail loudly against a schema that is missing.

This block runs inside the scope `Program.cs` already opens, before the role, admin,
company and payroll-settings seeding — all of which touch tables and so require the schema
to exist first. The `AppDbContext` is currently resolved partway down that block; it moves
to the top.

The connection string uses Neon's **pooled** endpoint (host ending `-pooler`) and must end
with `No Reset On Close=true`. Neon's PgBouncer runs in transaction mode and Npgsql's
server-side prepared statements break against it without that flag. Shape:

```
Host=ep-<id>-pooler.<region>.aws.neon.tech;Database=peoplecore;Username=neondb_owner;Password=<secret>;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true
```

## 5. Health endpoint

PeopleCore exposes no health route. Both the Docker `HEALTHCHECK` and the Traefik
`/health` path rule need one.

Add a minimal `app.MapGet("/health", ...)` returning 200 in `Program.cs`, before
`app.MapControllers()`. It is anonymous by default — PeopleCore applies `[Authorize]` per
controller rather than through a global filter.

It deliberately does **not** probe the database. Neon scales compute to zero and a cold
start can outlast the health check's timeout; a DB-backed check would have Docker restart
a container whose only problem is that its database was asleep.

## 6. Client configuration

Add `src/PeopleCore.Web/wwwroot/appsettings.Production.json`:

```json
{ "ApiBaseUrl": "https://peoplecore.m2netsolutions.com" }
```

`WebAssemblyHostBuilder.CreateDefault` loads `appsettings.json` then
`appsettings.{Environment}.json` from `wwwroot`. A standalone Blazor WASM app takes its
environment from the `blazor-environment` header the host sends; nginx sends none, so it
falls back to Production. The existing `appsettings.json` keeps its
`http://localhost:5180` value for local development. This is exactly how training carries
its two files.

Because the API and the client share a host and Traefik splits by path, this base URL is
same-origin in production and the CORS policy is belt-and-braces rather than load-bearing.

## 7. CI

New `.github/workflows/docker-publish.yml`, modelled on training's:

- Triggers on push to `main`, on pull requests, and `workflow_dispatch`
- Logs in to `ghcr.io` with `secrets.GITHUB_TOKEN` and `permissions: packages: write`
- `docker/metadata-action` tags both images; `latest` only on the default branch
- Builds `target: api` and `target: web` from the same Dockerfile with
  `cache-from/to: type=gha`
- Pushes only when the event is not a pull request — PRs build to verify, do not publish

Deployment ends at the push. Dokploy watches GHCR and redeploys because the compose file
sets `pull_policy: always`.

Note training's workflow keys on `master`; PeopleCore's default branch is `main`, which is
what `ci.yml` already uses.

The existing `ci.yml` (build, test against a Postgres service, vulnerable-package audit) is
not modified. It remains the correctness gate; `docker-publish.yml` only packages.

## 8. Manual steps

These need a human with console access and are documented in a new `docs/deployment.md`
rather than automated:

1. **Neon** — create database `peoplecore` in the existing project. The role stays
   `neondb_owner`. Copy the pooled connection string.
2. **Cloudflare R2** — create bucket `peoplecore-documents`; create an R2 API token scoped
   to it (Object Read & Write); record the account id, access key id and secret. The bucket
   stays private — `R2StorageService.GetPresignedUrlAsync` issues time-limited URLs, so no
   public access binding is needed.
3. **DNS** — `peoplecore.m2netsolutions.com` A record to the Hetzner box's IP. Traefik
   issues the certificate over HTTP-01 on first request, so DNS must resolve before the
   first deploy.
4. **Dokploy** — new Compose service pointing at `docker-compose.dokploy.yml`, with the
   §3 variables filled into the Environment tab.
5. **GHCR visibility** — the two new packages default to private on first push; Dokploy
   needs either public packages or a registry credential.

### First-deploy verification

- `https://peoplecore.m2netsolutions.com/health` returns 200
- API logs show "Applying 23 pending migration(s)" then the seeding lines
- Log in as the seeded admin
- **Generate a payslip PDF.** This is the one behaviour the deployment cannot prove by
  construction: no code registers fonts, so QuestPDF and PDFsharp resolve glyphs from the
  container's system fonts, and missing fonts degrade to blank or substituted text rather
  than throwing.
- Upload an employee document and download it back, confirming the R2 round trip and the
  presigned URL.

## 9. Files

Created:

- `Dockerfile`
- `.dockerignore`
- `nginx.conf`
- `docker-compose.dokploy.yml`
- `.env.example`
- `.github/workflows/docker-publish.yml`
- `src/PeopleCore.Web/wwwroot/appsettings.Production.json`
- `docs/deployment.md`

Modified:

- `src/PeopleCore.API/Program.cs` — migration block at the head of the seeding scope; the
  `/health` endpoint

No change to `appsettings.json`: the `Storage`, `R2` and `ConnectionStrings` sections
already exist with development defaults, and production overrides arrive as environment
variables.

## Out of scope

- Moving IAS or training off their current hosts
- Log aggregation, metrics or uptime alerting beyond Docker's `HEALTHCHECK`
- A staging environment
- Database backups beyond Neon's own retention
- `CareersPortal__AllowedOrigins`, which stays at its placeholder until a careers site
  exists
