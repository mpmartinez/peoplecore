using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeopleCore.API;
using PeopleCore.API.Extensions;
using PeopleCore.API.Middleware;
using PeopleCore.Application.Common.Options;
using PeopleCore.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

// The Blazor client's DTOs (see PeopleCore.Web/Services/ApiClient.cs) declare enum-valued
// fields such as EmploymentStatus, EmploymentType, PayFrequency, and PayrollRunStatus as
// `string`, not as their underlying numeric enum. Serializing enums as strings aligns the
// server with what every client DTO already expects. It's also more resilient over time:
// a numeric enum value silently changes meaning if a new member is ever inserted in the
// middle of the enum, whereas a string value keeps its meaning regardless of member order.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddResponseCaching();
builder.Services.AddInfrastructure(builder.Configuration);

// Set once at boot rather than per request. PayZen set it inside three separate controller
// actions; three copies of a global setting is three places to forget one, and the failure
// surfaces at PDF generation rather than at start-up.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:5002")
              .AllowAnyHeader()
              .AllowAnyMethod());

    options.AddPolicy("CareersPortal", policy =>
    {
        var origins = builder.Configuration.GetSection("CareersPortal:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// The careers apply endpoint is public and unauthenticated, so throttle it per client IP.
builder.Services.AddRateLimiter(options =>
{
    var permitPerHour = builder.Configuration.GetValue<int?>("CareersPortal:MaxApplicationsPerHourPerIp") ?? 3;

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.CareersApply, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitPerHour,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Name the buckets this process will actually use, once, at boot. Bucket names are
// configuration rather than consts now, and the two providers disagree about what a wrong
// name does: R2 rejects an absent bucket with NoSuchBucket, while MinIO creates it on first
// upload. So on MinIO a typo does not fail - it quietly starts a new, empty bucket, and the
// files already uploaded under the old name simply stop being found. This line turns that
// into something an operator can spot in the boot log instead of discovering it from a
// download that 404s.
var storageOptions = app.Services.GetRequiredService<DocumentStorageOptions>();
app.Logger.LogInformation(
    "Object storage: provider {Provider}, employee documents in {DocumentsBucket}, resumes in {ResumesBucket}.",
    ServiceExtensions.ResolveStorageProviderName(builder.Configuration),
    storageOptions.BucketName,
    storageOptions.ResumesBucketName);

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Traefik terminates TLS and forwards plain HTTP, so without this Request.Scheme reads "http"
// (CreatedAtAction would emit an http:// Location from an https:// service) and
// Connection.RemoteIpAddress reads Traefik's container IP - collapsing the careers-apply rate
// limiter's per-IP partition into one shared bucket for every caller. Clearing KnownIPNetworks
// and KnownProxies is safe here specifically because docker-compose.dokploy.yml publishes no host
// ports: the container is reachable only from dokploy-network, never directly from the internet,
// so there is no untrusted path these headers could arrive from.
var forwardedHeaderOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaderOptions.KnownIPNetworks.Clear();
forwardedHeaderOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaderOptions);

app.UseCors();
app.UseResponseCaching();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness only - deliberately does not touch the database. Neon scales compute to zero, and a
// cold start can outlast the health check's timeout; a DB-backed probe would have Docker restart
// a container whose only problem is that its database was asleep.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapControllers();

// Seed roles and default admin on first run
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PeopleCore.Infrastructure.Persistence.AppDbContext>();

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

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = ["Admin", "HRManager", "Manager", "Employee", "PayrollService", "Service"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

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

    if (!await dbContext.Companies.AnyAsync())
    {
        dbContext.Companies.Add(new PeopleCore.Domain.Entities.Organization.Company { Name = "My Company" });
        await dbContext.SaveChangesAsync();
    }

    // Payroll cannot compute without statutory rates. Exactly one settings row is seeded:
    // PayrollRun carries no CompanyId yet, so the engine resolves a single row, and
    // PayrollSettingsRepository throws if it ever finds more than one. Per-company settings
    // arrive with the CompanyId that a later phase adds to PayrollRun.
    if (!await dbContext.PayrollSettings.AnyAsync())
    {
        var company = await dbContext.Companies.OrderBy(c => c.Name).FirstOrDefaultAsync();
        if (company is not null)
        {
            dbContext.PayrollSettings.Add(new PeopleCore.Domain.Entities.Payroll.PayrollSettings { CompanyId = company.Id });
            await dbContext.SaveChangesAsync();
        }
    }
}

app.Run();
