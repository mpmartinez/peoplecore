using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.API.Extensions;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Application.Payroll.Services;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// PayrollRunService takes its maternity calculator as an optional constructor argument, so a
/// missing registration wouldn't fail at startup: the app would quietly pay no maternity offset.
/// </summary>
public class MaternityPayRegistrationTests
{
    [Fact]
    public void ThePayrollRunServiceTheAppResolves_HasTheMaternityCalculator()
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
        using var provider = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IPayrollRunService>();

        var calculator = typeof(PayrollRunService)
            .GetField("_maternityPay", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service);
        calculator.Should().BeOfType<MaternityPayCalculator>();

        // A final pay offsets and advances maternity through the same calculator.
        var finalPay = scope.ServiceProvider.GetRequiredService<PeopleCore.Application.Payroll.FinalPay.IFinalPayService>();
        typeof(PeopleCore.Application.Payroll.FinalPay.FinalPayService)
            .GetField("_maternityPay", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(finalPay).Should().BeOfType<MaternityPayCalculator>();
    }
}
