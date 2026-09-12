using System.Text;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Minio;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Organization.Services;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Application.PayrollIntegration.Interfaces;
using PeopleCore.Application.PayrollIntegration.Services;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Application.Performance.Services;
using PeopleCore.Application.Analytics.Interfaces;
using PeopleCore.Application.Analytics.Services;
using PeopleCore.Application.Careers.Interfaces;
using PeopleCore.Application.Careers.Services;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Application.Recruitment.Services;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Infrastructure.Identity;
using PeopleCore.Infrastructure.BackgroundJobs;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;
using PeopleCore.Infrastructure.Storage;
using PeopleCore.Reports;

namespace PeopleCore.API.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default"))
                   .UseSnakeCaseNamingConvention());

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        var signingKey = ResolveJwtSigningKey(configuration);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidAudience = configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(signingKey)
            };
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Organization
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();

        // Storage (provider-selectable via Storage:Provider in appsettings.json)
        services.AddSingleton(ResolveDocumentStorageOptions(configuration));

        if (IsR2Provider(configuration))
        {
            var r2Config = configuration.GetSection("R2");
            var accountId = r2Config["AccountId"]!;
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                ForcePathStyle = true
            };
            services.AddSingleton<IAmazonS3>(new AmazonS3Client(
                r2Config["AccessKey"],
                r2Config["SecretKey"],
                s3Config));
            services.AddScoped<IStorageService, R2StorageService>();
        }
        else
        {
            var minioConfig = configuration.GetSection("Minio");
            services.AddSingleton<IMinioClient>(sp =>
                new MinioClient()
                    .WithEndpoint(minioConfig["Endpoint"])
                    .WithCredentials(minioConfig["AccessKey"], minioConfig["SecretKey"])
                    .WithSSL(bool.Parse(minioConfig["UseSSL"] ?? "false"))
                    .Build());
            services.AddScoped<IStorageService, MinioStorageService>();
        }

        // Employees
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();

        // Attendance
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IOvertimeRepository, OvertimeRepository>();
        services.AddScoped<IHolidayRepository, HolidayRepository>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IHolidayService, HolidayService>();

        // Leave
        services.AddScoped<ILeaveTypeRepository, LeaveTypeRepository>();
        services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
        services.AddScoped<ILeaveBalanceRepository, LeaveBalanceRepository>();
        services.AddScoped<ILeaveTypeService, LeaveTypeService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();
        services.AddScoped<ILeaveBalanceService, LeaveBalanceService>();
        services.AddScoped<ILeaveAccrualRepository, LeaveAccrualRepository>();
        services.AddScoped<ILeaveAccrualService, LeaveAccrualService>();
        services.AddHostedService<LeaveAccrualHostedService>();

        // Recruitment
        services.AddScoped<IJobPostingRepository, JobPostingRepository>();
        services.AddScoped<IApplicantRepository, ApplicantRepository>();
        services.AddScoped<IInterviewStageRepository, InterviewStageRepository>();
        services.AddScoped<IJobPostingService, JobPostingService>();
        services.AddScoped<IApplicantService, ApplicantService>();
        services.AddScoped<IInterviewService, InterviewService>();

        // Performance
        services.AddScoped<IReviewCycleRepository, ReviewCycleRepository>();
        services.AddScoped<IPerformanceReviewRepository, PerformanceReviewRepository>();
        services.AddScoped<IReviewCycleService, ReviewCycleService>();
        services.AddScoped<IPerformanceReviewService, PerformanceReviewService>();

        // Scheduling
        services.AddScoped<IShiftTemplateRepository, ShiftTemplateRepository>();
        services.AddScoped<IRotatingPatternRepository, RotatingPatternRepository>();
        services.AddScoped<IShiftAssignmentRepository, ShiftAssignmentRepository>();
        services.AddScoped<IShiftService, ShiftService>();

        // Careers
        services.AddScoped<ICareersService, CareersService>();

        // Caching
        services.AddMemoryCache();

        // Analytics
        services.AddScoped<IHRAnalyticsService, HRAnalyticsService>();
        services.AddScoped<IExecutiveAnalyticsService, ExecutiveAnalyticsService>();

        // Payroll
        services.AddScoped<IPayrollRunRepository, PayrollRunRepository>();
        services.AddScoped<IEmployeeCompensationRepository, EmployeeCompensationRepository>();
        services.AddScoped<IEmployeeAllowanceRepository, EmployeeAllowanceRepository>();
        services.AddScoped<IEmployeeLoanRepository, EmployeeLoanRepository>();
        services.AddScoped<IPayrollSettingsRepository, PayrollSettingsRepository>();
        services.AddScoped<PayrollComputationService>();
        services.AddScoped<IPayrollAttendanceBridge, PayrollAttendanceBridge>();
        services.AddScoped<IPayrollRunService, PayrollRunService>();
        services.AddScoped<IEmployeeCompensationService, EmployeeCompensationService>();
        services.AddScoped<IPayrollSettingsService, PayrollSettingsService>();
        services.AddScoped<IPayslipRenderer, PayslipRenderer>();
        services.AddScoped<IPayslipService, PayslipService>();
        services.AddScoped<IBir2316Service, Bir2316Service>();
        services.AddScoped<IBir2316Renderer, Bir2316Renderer>();

        // Payroll Export
        services.AddScoped<IPayrollExportService, PayrollExportService>();

        return services;
    }

    /// <summary>
    /// HMAC-SHA256 needs at least 256 bits of key material, and the key is a credential: it belongs
    /// in user-secrets, an environment variable, or a secrets store — never in a committed
    /// appsettings file. Fail at startup rather than signing tokens with a guessable placeholder.
    /// </summary>
    public static byte[] ResolveJwtSigningKey(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Jwt:Key is not configured. Set it out of source control, for example: " +
                "dotnet user-secrets set \"Jwt:Key\" \"<random value>\" --project src/PeopleCore.API, " +
                "or via the Jwt__Key environment variable.");

        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < 32)
            throw new InvalidOperationException(
                $"Jwt:Key must be at least 32 bytes for HMAC-SHA256; the configured value is {bytes.Length}.");

        return bytes;
    }

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

    /// <summary>
    /// Bucket names differ per deployment, so they are read from the active provider's section
    /// (<c>R2</c> or <c>Minio</c>) rather than hardcoded in the services that upload. Anything
    /// missing or blank falls back to the historical default, which keeps files already stored
    /// by existing deployments reachable.
    /// </summary>
    public static DocumentStorageOptions ResolveDocumentStorageOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(IsR2Provider(configuration) ? "R2" : "Minio");

        return new DocumentStorageOptions
        {
            BucketName = Configured(section["BucketName"], DocumentStorageOptions.DefaultBucketName),
            ResumesBucketName = Configured(section["ResumesBucketName"], DocumentStorageOptions.DefaultResumesBucketName)
        };

        static string Configured(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// The active provider's name, for the startup banner. Derived from the same predicate that
    /// selects the storage client, so what is logged cannot drift from what was registered.
    /// </summary>
    public static string ResolveStorageProviderName(IConfiguration configuration) =>
        IsR2Provider(configuration) ? "R2" : "Minio";

    /// <summary>
    /// Single source of truth for which provider is active. Both the storage client and the
    /// bucket names are selected from it: if the two ever disagreed, uploads would land in a
    /// bucket the other half of the application never looks in.
    /// </summary>
    private static bool IsR2Provider(IConfiguration configuration) =>
        (configuration["Storage:Provider"] ?? "Minio").Equals("R2", StringComparison.OrdinalIgnoreCase);
}
