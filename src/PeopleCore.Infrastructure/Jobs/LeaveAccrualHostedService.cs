using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Jobs;

public class LeaveAccrualHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LeaveAccrualHostedService> _logger;

    public LeaveAccrualHostedService(IServiceProvider services, ILogger<LeaveAccrualHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            if (now.Day == 1 && now.Hour == 0)
                await RunAccrualAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunAccrualAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running monthly leave accrual for {Month}/{Year}",
            DateTime.UtcNow.Month, DateTime.UtcNow.Year);

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var leaveTypes = await context.LeaveTypes.Where(lt => lt.IsActive).ToListAsync(ct);
        var employees = await context.Employees.Where(e => e.IsActive).ToListAsync(ct);
        var year = DateTime.UtcNow.Year;
        var monthlyAccrual = 1m / 12m;

        foreach (var employee in employees)
        {
            foreach (var leaveType in leaveTypes)
            {
                var balance = await context.LeaveBalances
                    .FirstOrDefaultAsync(b => b.EmployeeId == employee.Id &&
                                              b.LeaveTypeId == leaveType.Id &&
                                              b.Year == year, ct);
                if (balance is null)
                {
                    balance = new Domain.Entities.Leave.LeaveBalance
                    {
                        EmployeeId = employee.Id,
                        LeaveTypeId = leaveType.Id,
                        Year = year,
                        TotalDays = leaveType.MaxDaysPerYear * monthlyAccrual,
                        UsedDays = 0
                    };
                    await context.LeaveBalances.AddAsync(balance, ct);
                }
                else
                {
                    balance.TotalDays += leaveType.MaxDaysPerYear * monthlyAccrual;
                    if (balance.TotalDays > leaveType.MaxDaysPerYear)
                        balance.TotalDays = leaveType.MaxDaysPerYear;
                    balance.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("Leave accrual complete.");
    }
}
