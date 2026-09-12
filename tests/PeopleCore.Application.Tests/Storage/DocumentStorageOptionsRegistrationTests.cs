using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.API.Extensions;
using PeopleCore.Application.Careers.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Employees.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Storage;

/// <summary>
/// The services that upload now take <see cref="DocumentStorageOptions"/> by constructor
/// injection, so a missing registration is a startup crash rather than a compile error.
/// </summary>
public class DocumentStorageOptionsRegistrationTests
{
    private static ServiceProvider BuildProvider(params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=peoplecore;Username=postgres;Password=postgres",
            ["Jwt:Key"] = new string('k', 32),
            ["Storage:Provider"] = "Minio",
            ["Minio:Endpoint"] = "localhost:9000",
            ["Minio:AccessKey"] = "minioadmin",
            ["Minio:SecretKey"] = "minioadmin",
            ["Minio:UseSSL"] = "false"
        };

        foreach (var (key, value) in overrides)
            settings[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddInfrastructure_RegistersResolvedDocumentStorageOptions()
    {
        using var provider = BuildProvider(("Minio:ResumesBucketName", "tenant-resumes"));

        var options = provider.GetService<DocumentStorageOptions>();

        options.Should().NotBeNull();
        options!.ResumesBucketName.Should().Be("tenant-resumes");
    }

    [Fact]
    public void AddInfrastructure_UploadingServices_AreResolvable()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ICareersService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IEmployeeDocumentService>().Should().NotBeNull();
    }
}
