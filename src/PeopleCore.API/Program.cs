using Microsoft.AspNetCore.Authentication.JwtBearer;
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

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseResponseCaching();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
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
        var pending = await dbContext.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            app.Logger.LogInformation("Applying {Count} pending migration(s)...", pending.Count());
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

    var adminEmail = builder.Configuration["Seed:AdminEmail"] ?? "admin@peoplecore.local";
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
            await userManager.AddToRoleAsync(admin, "Admin");
        else
            app.Logger.LogError("Failed to seed the admin account: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
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
