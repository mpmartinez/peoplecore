using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.API.Extensions;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Application.Payroll.Services;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// The services that read "earlier this year" take the year-to-date source as an optional
/// constructor argument, falling back to the Paid runs alone, so a missing registration wouldn't
/// fail at startup: the app would quietly ignore every opening balance.
/// </summary>
public class PayrollYearToDateRegistrationTests
{
    private static ServiceProvider TheAppsServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=peoplecore;Username=postgres;Password=postgres",
            ["Jwt:Key"] = new string('k', 32),
            ["DataProtection:KeyEncryptionKey"] = new string('d', 32),
            ["Storage:Provider"] = "Minio",
            ["Minio:Endpoint"] = "localhost:9000",
            ["Minio:AccessKey"] = "minioadmin",
            ["Minio:SecretKey"] = "minioadmin",
            ["Minio:UseSSL"] = "false"
        }).Build();
        return new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }

    /// <summary>Reads a private field: the wiring isn't visible through the services' interfaces.</summary>
    private static object? Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    [Fact]
    public void TheServicesTheAppResolves_ReadTheOpeningBalances()
    {
        using var provider = TheAppsServices();
        using var scope = provider.CreateScope();

        // The wiring isn't visible through the services' interfaces, so this reads private fields by
        // reflection: each service's _yearToDate (PayrollRunService, FinalPayService and
        // GovernmentReportService fall back to a PayrollYearToDate over the Paid runs alone when
        // none is injected, so its type alone doesn't prove the registration) and that
        // PayrollYearToDate's _balances (null in the fallback, the opening-balance repository when
        // DI built it). Renaming either field fails this test rather than silently passing.
        object[] services =
        [
            scope.ServiceProvider.GetRequiredService<IPayrollRunService>(),
            scope.ServiceProvider.GetRequiredService<IFinalPayService>(),
            scope.ServiceProvider.GetRequiredService<IGovernmentReportService>(),
        ];
        foreach (var service in services)
        {
            var yearToDate = Field(service, "_yearToDate");
            yearToDate.Should().BeOfType<PayrollYearToDate>(because: $"{service.GetType().Name} reads earlier-this-year figures");
            Field(yearToDate!, "_balances").Should().BeAssignableTo<IPayrollOpeningBalanceRepository>(
                because: $"{service.GetType().Name}'s year-to-date adds the opening balances");
        }
    }

    [Fact]
    public void The2316TheAppResolves_ReadsTheOpeningBalances()
    {
        using var provider = TheAppsServices();
        using var scope = provider.CreateScope();

        // Bir2316Service takes the repository as an optional argument too (null: no balances), so
        // only its _balances field shows the registration reached it. The final pay's tax settle
        // and the 1604-C go through this same registration.
        var bir2316 = scope.ServiceProvider.GetRequiredService<IBir2316Service>();

        Field(bir2316, "_balances").Should().BeAssignableTo<IPayrollOpeningBalanceRepository>();
    }
}
