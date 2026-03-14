using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.Infrastructure.BackgroundJobs;

public class LeaveAccrualHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveAccrualHostedService> _logger;

    public LeaveAccrualHostedService(IServiceScopeFactory scopeFactory, ILogger<LeaveAccrualHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Catch up current month on startup
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ILeaveAccrualService>();
            var now = DateTime.UtcNow;
            await service.RunAccrualsAsync(now.Year, now.Month, stoppingToken);
            _logger.LogInformation("Leave accrual catch-up completed for {Year}-{Month}", now.Year, now.Month);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Leave accrual catch-up failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            var delay = nextMonth - now;

            _logger.LogInformation("Leave accrual job scheduled for {NextRun}", nextMonth);
            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ILeaveAccrualService>();
                await service.RunAccrualsAsync(nextMonth.Year, nextMonth.Month, stoppingToken);
                _logger.LogInformation("Leave accrual completed for {Year}-{Month}", nextMonth.Year, nextMonth.Month);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Leave accrual job failed for {Year}-{Month}", nextMonth.Year, nextMonth.Month);
            }
        }
    }
}
