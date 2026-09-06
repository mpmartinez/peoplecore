using System.Text;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Common.Interfaces;
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
        var storageProvider = configuration["Storage:Provider"] ?? "Minio";

        if (storageProvider.Equals("R2", StringComparison.OrdinalIgnoreCase))
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
}
