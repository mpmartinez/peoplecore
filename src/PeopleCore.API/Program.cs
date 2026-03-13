using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text.Json;
using PeopleCore.API.Extensions;
using PeopleCore.API.Middleware;
using PeopleCore.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:5002")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed roles and default admin on first run
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = ["Admin", "HRManager", "Manager", "Employee", "PayrollService"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    const string adminEmail = "admin@peoplecore.local";
    const string adminPassword = "Admin@123456";
    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        await userManager.CreateAsync(admin, adminPassword);
        await userManager.AddToRoleAsync(admin, "Admin");
    }

    var dbContext = scope.ServiceProvider.GetRequiredService<PeopleCore.Infrastructure.Persistence.AppDbContext>();
    if (!await dbContext.Companies.AnyAsync())
    {
        dbContext.Companies.Add(new PeopleCore.Domain.Entities.Organization.Company { Name = "My Company" });
        await dbContext.SaveChangesAsync();
    }
}

app.Run();
