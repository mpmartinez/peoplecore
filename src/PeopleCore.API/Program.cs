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
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = ["Admin", "HRManager", "Manager", "Employee", "PayrollService", "Service"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    var adminEmail = builder.Configuration["Seed:AdminEmail"] ?? "admin@peoplecore.local";
    var adminPassword = builder.Configuration["Seed:AdminPassword"];

    if (string.IsNullOrWhiteSpace(adminPassword) && app.Environment.IsDevelopment())
    {
        adminPassword = "Admin@123456";
        app.Logger.LogWarning(
            "Seeding {Email} with the well-known development password. Set Seed:AdminPassword to override.",
            adminEmail);
    }

    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        // Outside development, refuse to create a login nobody chose the password for.
        app.Logger.LogWarning(
            "Seed:AdminPassword is not configured; skipping the default admin account.");
    }
    else if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        var created = await userManager.CreateAsync(admin, adminPassword);
        if (created.Succeeded)
            await userManager.AddToRoleAsync(admin, "Admin");
        else
            app.Logger.LogError("Failed to seed the admin account: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
    }

    var dbContext = scope.ServiceProvider.GetRequiredService<PeopleCore.Infrastructure.Persistence.AppDbContext>();
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
