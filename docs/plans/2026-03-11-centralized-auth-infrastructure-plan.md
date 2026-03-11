# Centralized Auth & Infrastructure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Set up Keycloak SSO with HA across Digital Ocean + Hetzner, PostgreSQL HA via Supabase + Neon, Cloudflare R2 storage, and wire PeopleCore (Blazor WASM + ASP.NET Core) to Keycloak.

**Architecture:** Keycloak on two VPS nodes behind Cloudflare DNS failover, backed by Supabase PostgreSQL with Neon logical replica. Blazor WASM uses OIDC via Microsoft.AspNetCore.Components.WebAssembly.Authentication. API validates JWT locally using cached Keycloak public key.

**Tech Stack:** Keycloak 26.x, PostgreSQL 16, Cloudflare R2, ASP.NET Core 10, Blazor WASM 10, AWSSDK.S3, Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Components.WebAssembly.Authentication

---

## Phase 1: Keycloak on Digital Ocean (Primary)

### Task 1: Provision Digital Ocean VPS

**Step 1: Create Droplet**
- Region: SGP1 (Singapore) or nearest to users
- Size: 2 vCPU / 2GB RAM / 50GB SSD (~$18/mo)
- OS: Ubuntu 24.04 LTS
- Enable backups: Yes
- Add SSH key

**Step 2: Point DNS to droplet**

In Cloudflare DNS:
```
Type:    A
Name:    auth
Content: <DO_DROPLET_IP>
Proxy:   DNS only (grey cloud) ← must NOT be proxied
TTL:     60
```

**Step 3: SSH in and install Docker**
```bash
ssh root@<DO_DROPLET_IP>
curl -fsSL https://get.docker.com | sh
systemctl enable docker && systemctl start docker
apt-get install -y docker-compose-plugin certbot
```

---

### Task 2: Configure Supabase Database for Keycloak

**Step 1: Create Supabase project**
- supabase.com → New Project → Region: Southeast Asia → Plan: Pro

**Step 2: Create keycloak database user**
```sql
-- Run in Supabase SQL Editor
CREATE USER keycloak_user WITH PASSWORD 'strong_password_here';
CREATE DATABASE keycloak OWNER keycloak_user;
GRANT ALL PRIVILEGES ON DATABASE keycloak TO keycloak_user;
```

**Step 3: Note direct connection string (NOT pooled — Keycloak needs direct)**
```
jdbc:postgresql://db.<project-ref>.supabase.co:5432/keycloak
User: keycloak_user
Pass: strong_password_here
```

---

### Task 3: Deploy Keycloak on Digital Ocean VPS

**Step 1: Create directories on VPS**
```bash
mkdir -p /opt/keycloak/conf
cd /opt/keycloak
```

**Step 2: Obtain SSL certificate**
```bash
certbot certonly --standalone -d auth.yourdomain.com
cp /etc/letsencrypt/live/auth.yourdomain.com/fullchain.pem /opt/keycloak/conf/server.crt
cp /etc/letsencrypt/live/auth.yourdomain.com/privkey.pem /opt/keycloak/conf/server.key
chmod 644 /opt/keycloak/conf/server.crt
chmod 600 /opt/keycloak/conf/server.key
```

**Step 3: Create /opt/keycloak/docker-compose.yml**
```yaml
version: '3.8'

services:
  keycloak:
    image: quay.io/keycloak/keycloak:26.1
    command: start
    environment:
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://db.<project-ref>.supabase.co:5432/keycloak
      KC_DB_USERNAME: keycloak_user
      KC_DB_PASSWORD: strong_password_here
      KC_HOSTNAME: auth.yourdomain.com
      KC_HOSTNAME_STRICT: "true"
      KC_HTTP_ENABLED: "false"
      KC_HTTPS_PORT: 8443
      KC_HTTPS_CERTIFICATE_FILE: /opt/keycloak/conf/server.crt
      KC_HTTPS_CERTIFICATE_KEY_FILE: /opt/keycloak/conf/server.key
      KC_BOOTSTRAP_ADMIN_USERNAME: admin
      KC_BOOTSTRAP_ADMIN_PASSWORD: <strong-admin-password>
      KC_PROXY_HEADERS: xforwarded
      KC_LOG_LEVEL: INFO
    ports:
      - "8443:8443"
    volumes:
      - /opt/keycloak/conf:/opt/keycloak/conf:ro
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "curl -f https://localhost:8443/health/ready -k || exit 1"]
      interval: 30s
      timeout: 10s
      retries: 3
```

**Step 4: Start Keycloak**
```bash
cd /opt/keycloak
docker compose up -d
docker compose logs -f keycloak
```
Expected: `Keycloak 26.1 on JVM (powered by Quarkus) started`

**Step 5: Open firewall**
```bash
ufw allow 8443/tcp
ufw allow 22/tcp
ufw enable
```

**Step 6: Auto-renew SSL certs**
```bash
echo "0 0 * * * certbot renew --quiet && \
  cp /etc/letsencrypt/live/auth.yourdomain.com/fullchain.pem /opt/keycloak/conf/server.crt && \
  cp /etc/letsencrypt/live/auth.yourdomain.com/privkey.pem /opt/keycloak/conf/server.key && \
  docker compose -f /opt/keycloak/docker-compose.yml restart keycloak" | crontab -
```

---

### Task 4: Cloudflare Health Check + Failover

**Step 1: Create health check**

Cloudflare → Traffic → Health Checks → Create:
```
Name:     keycloak-primary
URL:      https://auth.yourdomain.com/health/ready
Interval: 60s
Retries:  2
Expected: 200
```

**Step 2: Create alert notification**

Cloudflare → Notifications → Create:
```
Event:    Health Check status change
Alert to: your-email@domain.com
```

---

## Phase 2: Keycloak Standby on Hetzner

### Task 5: Provision Hetzner VPS

**Step 1: Create server in Hetzner Cloud**
- Type: CPX21 (3 vCPU / 4GB RAM, ~$7/mo)
- Location: Nuremberg or Helsinki
- OS: Ubuntu 24.04

**Step 2: Install Docker (same as Task 1 Step 3)**
```bash
ssh root@<HETZNER_IP>
curl -fsSL https://get.docker.com | sh
systemctl enable docker && systemctl start docker
apt-get install -y docker-compose-plugin certbot
```

**Step 3: Deploy same docker-compose.yml**

Copy `/opt/keycloak/docker-compose.yml` from DO.
Both nodes point to the **same Supabase DB** — state is shared automatically.

```bash
mkdir -p /opt/keycloak/conf
cd /opt/keycloak
# paste docker-compose.yml — same config, same DB connection string
certbot certonly --standalone -d auth.yourdomain.com
cp /etc/letsencrypt/live/auth.yourdomain.com/fullchain.pem /opt/keycloak/conf/server.crt
cp /etc/letsencrypt/live/auth.yourdomain.com/privkey.pem /opt/keycloak/conf/server.key
docker compose up -d
```

**Step 4: Add Hetzner IP to Cloudflare DNS**
```
Type:     A
Name:     auth
Content:  <HETZNER_IP>
Proxy:    DNS only
TTL:      60
```

Cloudflare → Load Balancing → create pool with both IPs, health check on `/health/ready`.
Route traffic to Hetzner automatically when DO is unhealthy.

---

## Phase 3: Keycloak Realm & Client Configuration

### Task 6: Create Realm and Clients

**Step 1: Open Admin Console**

Navigate to: `https://auth.yourdomain.com/admin`
Login with admin credentials set in docker-compose.

**Step 2: Create realm per agency**

Left sidebar → realm dropdown → Create Realm:
```
Realm name: agency-a
Enabled:    ON
```

**Step 3: Configure token lifetimes**

Realm Settings → Tokens:
```
Access Token Lifespan:  15 minutes
Refresh Token Lifespan: 8 hours
SSO Session Idle:       8 hours
SSO Session Max:        12 hours
```

**Step 4: Create a client per subscribed app**

Clients → Create Client → for `hrms`:
```
Client ID:        hrms
Client Type:      OpenID Connect
Standard Flow:    ON
Direct Access:    OFF

Valid Redirect URIs:
  https://hrms.yourdomain.com/*
  http://localhost:5000/*

Web Origins:
  https://hrms.yourdomain.com
  http://localhost:5000
```

Repeat for each app the agency subscribes to:
`payroll`, `accounting`, `spmsv2`, `allotment`, `training`

**Step 5: Create roles**

Clients → hrms → Roles → Add Role:
```
hrms.admin
hrms.viewer
hrms.hr_officer
```

Realm Roles → Add:
```
platform.admin   ← cross-app admin
platform.user    ← standard user
```

**Step 6: Create test user**

Users → Add User:
```
Username:       testuser
Email:          testuser@agency-a.com
Email Verified: ON
```

Credentials → Set Password (Temporary: OFF)
Role Mappings → assign `hrms.admin`, `platform.user`

---

## Phase 4: ASP.NET Core API Integration

### Task 7: Configure JWT Bearer in PeopleCore.API

**Files:**
- Modify: `src/PeopleCore.API/Program.cs`
- Modify: `src/PeopleCore.API/appsettings.json`
- Create: `src/PeopleCore.API/appsettings.Development.json`

**Step 1: Add Keycloak config to appsettings.json**
```json
{
  "Keycloak": {
    "Authority": "https://auth.yourdomain.com/realms/agency-a",
    "Audience": "hrms",
    "RequireHttpsMetadata": true
  }
}
```

**Step 2: Create appsettings.Development.json**
```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/agency-a",
    "Audience": "hrms",
    "RequireHttpsMetadata": false
  }
}
```

**Step 3: Add JWT Bearer to Program.cs**

`Microsoft.AspNetCore.Authentication.JwtBearer` is already in the csproj.

Add before `builder.Build()`:
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var kc = builder.Configuration.GetSection("Keycloak");
        options.Authority = kc["Authority"];
        options.Audience  = kc["Audience"];
        options.RequireHttpsMetadata = bool.Parse(kc["RequireHttpsMetadata"] ?? "true");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer   = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.FromSeconds(30)
        };

        // Map Keycloak resource_access roles to .NET ClaimTypes.Role
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                var resourceAccess = ctx.Principal?
                    .FindFirst("resource_access")?.Value;

                if (resourceAccess is not null)
                {
                    try
                    {
                        var audience = kc["Audience"]!;
                        using var doc = JsonDocument.Parse(resourceAccess);
                        if (doc.RootElement.TryGetProperty(audience, out var appRoles)
                            && appRoles.TryGetProperty("roles", out var roles))
                        {
                            var identity = ctx.Principal!.Identity as ClaimsIdentity;
                            foreach (var role in roles.EnumerateArray())
                                identity?.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
                        }
                    }
                    catch { /* malformed token — leave roles empty */ }
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
```

Add after `app.Build()`:
```csharp
app.UseAuthentication();
app.UseAuthorization();
```

**Step 4: Protect controllers**

Add `[Authorize]` to any controller that needs protection:
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmployeesController : ControllerBase { ... }
```

Role-based authorization:
```csharp
[Authorize(Roles = "hrms.admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(Guid id) { ... }
```

**Step 5: Build to verify no compile errors**
```bash
dotnet build src/PeopleCore.API/
```
Expected: Build succeeded, 0 errors.

**Step 6: Commit**
```bash
git add src/PeopleCore.API/Program.cs \
        src/PeopleCore.API/appsettings.json \
        src/PeopleCore.API/appsettings.Development.json
git commit -m "feat: configure Keycloak JWT bearer authentication in PeopleCore API"
```

---

## Phase 5: Blazor WASM Integration

### Task 8: Configure OIDC in Blazor WASM

**Files:**
- Modify: `src/PeopleCore.Web/PeopleCore.Web.csproj`
- Modify: `src/PeopleCore.Web/Program.cs`
- Modify: `src/PeopleCore.Web/wwwroot/appsettings.json`
- Modify: `src/PeopleCore.Web/App.razor`
- Create: `src/PeopleCore.Web/Pages/Authentication.razor`
- Create: `src/PeopleCore.Web/Shared/RedirectToLogin.razor`

**Step 1: Add authentication package to csproj**
```xml
<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Authentication"
                  Version="10.0.3" />
```

```bash
dotnet restore src/PeopleCore.Web/
```

**Step 2: Add Keycloak config to wwwroot/appsettings.json**
```json
{
  "Keycloak": {
    "Authority": "https://auth.yourdomain.com/realms/agency-a",
    "ClientId": "hrms",
    "ResponseType": "code",
    "PostLogoutRedirectUri": "/"
  }
}
```

**Step 3: Configure OIDC in Program.cs**
```csharp
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using PeopleCore.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var kc = builder.Configuration.GetSection("Keycloak");

builder.Services.AddOidcAuthentication(options =>
{
    options.ProviderOptions.Authority       = kc["Authority"];
    options.ProviderOptions.ClientId        = kc["ClientId"];
    options.ProviderOptions.ResponseType    = kc["ResponseType"] ?? "code";
    options.ProviderOptions.PostLogoutRedirectUri = kc["PostLogoutRedirectUri"] ?? "/";
    options.ProviderOptions.DefaultScopes.Clear();
    options.ProviderOptions.DefaultScopes.Add("openid");
    options.ProviderOptions.DefaultScopes.Add("profile");
    options.ProviderOptions.DefaultScopes.Add("email");
});

// Authenticated HTTP client — auto-attaches Bearer token
builder.Services.AddHttpClient("API", client =>
    client.BaseAddress = new Uri("https://api.yourdomain.com"))
    .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("API"));

await builder.Build().RunAsync();
```

**Step 4: Create Authentication.razor**

File: `src/PeopleCore.Web/Pages/Authentication.razor`
```razor
@page "/authentication/{action}"
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

<RemoteAuthenticatorView Action="@Action" />

@code {
    [Parameter] public string? Action { get; set; }
}
```

**Step 5: Create RedirectToLogin.razor**

File: `src/PeopleCore.Web/Shared/RedirectToLogin.razor`
```razor
@inject NavigationManager Navigation
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

@code {
    protected override void OnInitialized() =>
        Navigation.NavigateToLogin("authentication/login");
}
```

**Step 6: Update App.razor to require auth**
```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication

<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData"
                                DefaultLayout="@typeof(Layout.MainLayout)">
                <NotAuthorized>
                    @if (context.User.Identity?.IsAuthenticated != true)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        <p>You are not authorized to access this page.</p>
                    }
                </NotAuthorized>
            </AuthorizeRouteView>
            <FocusOnNavigate RouteData="@routeData" Selector="h1" />
        </Found>
        <NotFound>
            <PageTitle>Not found</PageTitle>
            <p>Sorry, there's nothing at this address.</p>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

**Step 7: Add login/logout to MainLayout**

In `src/PeopleCore.Web/Layout/MainLayout.razor`, add inside the layout:
```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.WebAssembly.Authentication
@inject NavigationManager Navigation

<AuthorizeView>
    <Authorized>
        <span>@context.User.Identity?.Name</span>
        <button @onclick="@(() => Navigation.NavigateToLogout("authentication/logout"))">
            Log out
        </button>
    </Authorized>
</AuthorizeView>
```

**Step 8: Build to verify**
```bash
dotnet build src/PeopleCore.Web/
```
Expected: Build succeeded, 0 errors.

**Step 9: Commit**
```bash
git add src/PeopleCore.Web/
git commit -m "feat: configure Keycloak OIDC authentication in Blazor WASM"
```

---

## Phase 6: Cloudflare R2 Storage

### Task 9: Create R2 Bucket and Wire to Infrastructure

**Files:**
- Modify: `src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj`
- Create: `src/PeopleCore.Infrastructure/Storage/IStorageService.cs`
- Create: `src/PeopleCore.Infrastructure/Storage/StorageKeyBuilder.cs`
- Create: `src/PeopleCore.Infrastructure/Storage/R2StorageService.cs`
- Modify: `src/PeopleCore.API/appsettings.json`

**Step 1: Create R2 bucket**

Cloudflare Dashboard → R2 → Create Bucket:
```
Name:     manning-agency-files
Location: Auto
```

R2 → Manage API Tokens → Create Token:
```
Permission: Object Read & Write
Bucket:     manning-agency-files
```

Note:
```
Account ID:       <cloudflare-account-id>
Access Key ID:    <r2-key-id>
Secret Access Key: <r2-secret>
Endpoint:         https://<account-id>.r2.cloudflarestorage.com
```

**Step 2: Add AWSSDK.S3 package**
```xml
<!-- src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj -->
<PackageReference Include="AWSSDK.S3" Version="3.7.410.6" />
```

```bash
dotnet restore src/PeopleCore.Infrastructure/
```

**Step 3: Add R2 config to appsettings.json**
```json
{
  "Storage": {
    "R2": {
      "Endpoint":        "https://<account-id>.r2.cloudflarestorage.com",
      "AccessKeyId":     "<r2-key-id>",
      "SecretAccessKey": "<r2-secret>",
      "BucketName":      "manning-agency-files"
    }
  }
}
```

**Step 4: Create IStorageService**

File: `src/PeopleCore.Infrastructure/Storage/IStorageService.cs`
```csharp
namespace PeopleCore.Infrastructure.Storage;

public interface IStorageService
{
    Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default);
}
```

**Step 5: Create StorageKeyBuilder**

File: `src/PeopleCore.Infrastructure/Storage/StorageKeyBuilder.cs`
```csharp
namespace PeopleCore.Infrastructure.Storage;

public static class StorageKeyBuilder
{
    /// <summary>tenantId/module/fileName</summary>
    public static string Build(string tenantId, string module, string fileName)
        => $"{tenantId}/{module}/{fileName}";

    /// <summary>tenantId/module/subPath/fileName</summary>
    public static string Build(string tenantId, string module, string subPath, string fileName)
        => $"{tenantId}/{module}/{subPath}/{fileName}";
}
```

**Step 6: Create R2StorageService**

File: `src/PeopleCore.Infrastructure/Storage/R2StorageService.cs`
```csharp
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace PeopleCore.Infrastructure.Storage;

public sealed class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2StorageService(IAmazonS3 s3, IConfiguration config)
    {
        _s3     = s3;
        _bucket = config["Storage:R2:BucketName"]!;
    }

    public async Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = _bucket,
            Key         = key,
            InputStream = content,
            ContentType = contentType
        }, ct);

        return key;
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var response = await _s3.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
        => await _s3.DeleteObjectAsync(_bucket, key, ct);

    public Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key        = key,
            Expires    = DateTime.UtcNow.Add(expiry)
        });
        return Task.FromResult(url);
    }
}
```

**Step 7: Register in DI**

In your infrastructure registration (e.g., `src/PeopleCore.Infrastructure/DependencyInjection.cs`):
```csharp
var r2 = configuration.GetSection("Storage:R2");
services.AddSingleton<IAmazonS3>(new AmazonS3Client(
    r2["AccessKeyId"],
    r2["SecretAccessKey"],
    new AmazonS3Config
    {
        ServiceURL    = r2["Endpoint"],
        ForcePathStyle = true
    }
));
services.AddScoped<IStorageService, R2StorageService>();
```

**Step 8: Build**
```bash
dotnet build src/PeopleCore.Infrastructure/
```
Expected: Build succeeded, 0 errors.

**Step 9: Commit**
```bash
git add src/PeopleCore.Infrastructure/Storage/
git commit -m "feat: add Cloudflare R2 storage service with tenant-scoped key builder"
```

---

## Phase 7: Blazor WASM Performance

### Task 10: Enable PWA + IL Trimming + Brotli

**Files:**
- Modify: `src/PeopleCore.Web/PeopleCore.Web.csproj`
- Create: `src/PeopleCore.Web/wwwroot/service-worker.js`
- Create: `src/PeopleCore.Web/wwwroot/service-worker.published.js`

**Step 1: Update csproj**
```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>partial</TrimMode>
</PropertyGroup>

<ItemGroup>
  <ServiceWorker Include="wwwroot\service-worker.js"
                 PublishedContent="wwwroot\service-worker.published.js" />
</ItemGroup>
```

**Step 2: Create wwwroot/service-worker.js (dev — no caching)**
```javascript
// Development: always fetch from network
self.addEventListener('fetch', () => { });
```

**Step 3: Create wwwroot/service-worker.published.js (production caching)**
```javascript
self.importScripts('./service-worker-assets.js');
self.addEventListener('install',  e => e.waitUntil(onInstall(e)));
self.addEventListener('activate', e => e.waitUntil(onActivate(e)));
self.addEventListener('fetch',    e => e.respondWith(onFetch(e)));

const cachePrefix = 'pc-cache-';
const cacheName   = `${cachePrefix}${self.assetsManifest.version}`;
const include = [/\.dll$/, /\.wasm/, /\.html/, /\.js$/, /\.css$/, /\.json$/];
const exclude = [/^service-worker\.js$/];

async function onInstall(e) {
    const requests = self.assetsManifest.assets
        .filter(a => include.some(p => p.test(a.url)))
        .filter(a => !exclude.some(p => p.test(a.url)))
        .map(a => new Request(a.url, { integrity: a.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await cache.addAll(requests);
}

async function onActivate(e) {
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(k => k.startsWith(cachePrefix) && k !== cacheName)
        .map(k => caches.delete(k)));
}

async function onFetch(e) {
    if (e.request.method !== 'GET') return fetch(e.request);
    const req   = e.request.mode === 'navigate' ? 'index.html' : e.request;
    const cache = await caches.open(cacheName);
    return (await cache.match(req)) ?? fetch(e.request);
}
```

**Step 4: Configure Brotli on your web server (nginx)**
```nginx
server {
    listen 443 ssl;
    server_name app.yourdomain.com;

    root  /var/www/peoplecore;
    index index.html;

    brotli            on;
    brotli_comp_level 6;
    brotli_types      text/plain text/css application/javascript
                      application/wasm application/octet-stream;

    location / { try_files $uri $uri/ /index.html; }

    location /_framework/ {
        add_header Cache-Control "public, max-age=31536000, immutable";
    }

    location /service-worker.js {
        add_header Cache-Control "no-store, no-cache";
    }
}
```

**Step 5: Publish and check size**
```bash
dotnet publish src/PeopleCore.Web/ -c Release -o ./publish/web
du -sh ./publish/web/wwwroot/_framework/
```

**Step 6: Commit**
```bash
git add src/PeopleCore.Web/
git commit -m "feat: enable PWA service worker, IL trimming, Brotli for Blazor WASM"
```

---

## Phase 8: Neon PostgreSQL Replica

### Task 11: Set Up Neon as Logical Replica

**Step 1: Create Neon project**
- neon.tech → New Project → Region: match Supabase region → Postgres 16

**Step 2: Note Neon connection string**
```
postgresql://user:pass@ep-xxx.region.aws.neon.tech/neondb?sslmode=require
```

**Step 3: Verify Supabase has logical replication on**
```sql
-- Run in Supabase SQL Editor
SHOW wal_level;
-- Expected: logical
```

**Step 4: Create publication on Supabase**
```sql
CREATE PUBLICATION hrms_pub FOR ALL TABLES;
```

**Step 5: Create subscription on Neon**
```sql
-- Run in Neon SQL Editor
CREATE SUBSCRIPTION hrms_sub
  CONNECTION 'postgresql://keycloak_user:password@db.<ref>.supabase.co:5432/keycloak'
  PUBLICATION hrms_pub;
```

**Step 6: Verify replication**
```sql
-- On Supabase: insert a test row into any table
-- On Neon: confirm it appears within a few seconds
SELECT * FROM employees ORDER BY created_at DESC LIMIT 1;
```

---

## Phase 9: Agency Onboarding Automation

### Task 12: Realm Provisioning Script

**Files:**
- Create: `tools/provision-realm.sh`

**Step 1: Create script**

File: `tools/provision-realm.sh`
```bash
#!/bin/bash
# Usage: KC_ADMIN_PASSWORD=xxx ./provision-realm.sh <realm> <apps-csv>
# Example: KC_ADMIN_PASSWORD=xxx ./provision-realm.sh agency-b "payroll,hrms"

set -e
REALM=$1
APPS=$2
KC_URL="https://auth.yourdomain.com"

# Get admin token
TOKEN=$(curl -s -X POST "$KC_URL/realms/master/protocol/openid-connect/token" \
  -d "client_id=admin-cli&username=admin&password=$KC_ADMIN_PASSWORD&grant_type=password" \
  | jq -r '.access_token')

# Create realm
curl -sf -X POST "$KC_URL/admin/realms" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"realm\":\"$REALM\",\"enabled\":true,\"accessTokenLifespan\":900,\"ssoSessionIdleTimeout\":28800}"

echo "✓ Realm '$REALM' created"

# Create client per app
IFS=',' read -ra APP_LIST <<< "$APPS"
for APP in "${APP_LIST[@]}"; do
  curl -sf -X POST "$KC_URL/admin/realms/$REALM/clients" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{
      \"clientId\":\"$APP\",
      \"enabled\":true,
      \"standardFlowEnabled\":true,
      \"redirectUris\":[\"https://$APP.yourdomain.com/*\",\"http://localhost:5000/*\"],
      \"webOrigins\":[\"https://$APP.yourdomain.com\",\"http://localhost:5000\"]
    }"
  echo "  ✓ Client '$APP' added to realm '$REALM'"
done

echo ""
echo "Done. Admin: $KC_URL/admin/master/console/#/$REALM"
echo "Login: $KC_URL/realms/$REALM/account"
```

**Step 2: Make executable and test**
```bash
chmod +x tools/provision-realm.sh
KC_ADMIN_PASSWORD=yourpassword ./tools/provision-realm.sh agency-test "hrms,payroll"
```
Expected: realm and clients appear in Keycloak admin console.

**Step 3: Commit**
```bash
git add tools/provision-realm.sh
git commit -m "feat: add Keycloak realm provisioning script for agency onboarding"
```

---

## Summary & Cost

### Completion Checklist
```
Phase 1: Primary Keycloak
  ☐ Task 1: Digital Ocean VPS provisioned
  ☐ Task 2: Supabase DB for Keycloak
  ☐ Task 3: Keycloak deployed with Docker + SSL

Phase 2: HA Standby
  ☐ Task 4: Cloudflare health check + failover
  ☐ Task 5: Keycloak on Hetzner (same DB)

Phase 3: Keycloak Config
  ☐ Task 6: Realm, clients, roles, test user

Phase 4: API
  ☐ Task 7: JWT Bearer in PeopleCore.API

Phase 5: Blazor WASM
  ☐ Task 8: OIDC auth + protected routes

Phase 6: Storage
  ☐ Task 9: Cloudflare R2 + IStorageService

Phase 7: Performance
  ☐ Task 10: PWA + Brotli + IL Trimming

Phase 8: DB Replica
  ☐ Task 11: Neon logical replica from Supabase

Phase 9: Automation
  ☐ Task 12: Agency provisioning script
```

### Monthly Cost
| Infrastructure | Provider | Cost |
|---|---|---|
| Keycloak Primary | Digital Ocean 2vCPU/2GB | $18 |
| Keycloak Standby | Hetzner CPX21 | $7 |
| PostgreSQL Primary | Supabase Pro | $25 |
| PostgreSQL Replica | Neon | $0 |
| DNS + CDN + WAF | Cloudflare | $0 |
| R2 Storage ~50GB | Cloudflare R2 | ~$1 |
| **Total** | | **~$51/month** |
