using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PeopleCore.API.Extensions;
using PeopleCore.Application.Common.Options;
using Xunit;

namespace PeopleCore.Application.Tests.Storage;

/// <summary>
/// Bucket names are deployment configuration, not code. These tests pin how they are resolved
/// from the active provider's section, and — most importantly — pin the historical defaults:
/// changing a default silently orphans every object already stored under the old name.
/// </summary>
public class DocumentStorageOptionsResolutionTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Resolve_MinioProvider_ReadsBothBucketNamesFromMinioSection()
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "Minio"),
            ("Minio:BucketName", "minio-documents"),
            ("Minio:ResumesBucketName", "minio-resumes"),
            ("R2:BucketName", "r2-documents"),
            ("R2:ResumesBucketName", "r2-resumes"));

        var options = ServiceExtensions.ResolveDocumentStorageOptions(configuration);

        options.BucketName.Should().Be("minio-documents");
        options.ResumesBucketName.Should().Be("minio-resumes");
    }

    [Fact]
    public void Resolve_R2Provider_ReadsBothBucketNamesFromR2Section()
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "R2"),
            ("Minio:BucketName", "minio-documents"),
            ("Minio:ResumesBucketName", "minio-resumes"),
            ("R2:BucketName", "r2-documents"),
            ("R2:ResumesBucketName", "r2-resumes"));

        var options = ServiceExtensions.ResolveDocumentStorageOptions(configuration);

        options.BucketName.Should().Be("r2-documents");
        options.ResumesBucketName.Should().Be("r2-resumes");
    }

    [Theory]
    [InlineData("r2")]
    [InlineData("R2")]
    public void Resolve_R2ProviderName_IsCaseInsensitive(string provider)
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", provider),
            ("R2:ResumesBucketName", "r2-resumes"));

        var options = ServiceExtensions.ResolveDocumentStorageOptions(configuration);

        options.ResumesBucketName.Should().Be("r2-resumes");
    }

    [Fact]
    public void Resolve_NoProviderConfigured_FallsBackToMinioSection()
    {
        var configuration = BuildConfiguration(
            ("Minio:ResumesBucketName", "minio-resumes"),
            ("R2:ResumesBucketName", "r2-resumes"));

        var options = ServiceExtensions.ResolveDocumentStorageOptions(configuration);

        options.ResumesBucketName.Should().Be("minio-resumes");
    }

    [Fact]
    public void Resolve_ResumesKeyAbsent_FallsBackToHistoricalDefault()
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "Minio"),
            ("Minio:BucketName", "minio-documents"));

        var options = ServiceExtensions.ResolveDocumentStorageOptions(configuration);

        options.ResumesBucketName.Should().Be("resumes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ResumesKeyBlank_FallsBackToHistoricalDefault(string configured)
    {
        var configuration = BuildConfiguration(
            ("Storage:Provider", "Minio"),
            ("Minio:ResumesBucketName", configured));

        var options = ServiceExtensions.ResolveDocumentStorageOptions(configuration);

        options.ResumesBucketName.Should().Be("resumes");
    }

    [Fact]
    public void Resolve_EmptyConfiguration_ReturnsBothHistoricalDefaults()
    {
        var options = ServiceExtensions.ResolveDocumentStorageOptions(BuildConfiguration());

        options.BucketName.Should().Be("peoplecore-documents");
        options.ResumesBucketName.Should().Be("resumes");
    }

    /// <summary>
    /// Resumes live in their own bucket, separate from employee documents. If these two ever
    /// collapse onto one name, existing deployments lose track of already-uploaded files.
    /// </summary>
    [Fact]
    public void Defaults_ResumesAndDocumentBuckets_AreDistinctAndUnchanged()
    {
        var options = new DocumentStorageOptions();

        options.BucketName.Should().Be("peoplecore-documents");
        options.ResumesBucketName.Should().Be("resumes");
        options.ResumesBucketName.Should().NotBe(options.BucketName);
    }

    [Theory]
    [InlineData("Minio")]
    [InlineData("R2")]
    public void AppSettings_DeclaresResumesBucketName_DefaultingToResumes(string section)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindAppSettingsPath()));

        var value = document.RootElement
            .GetProperty(section)
            .GetProperty("ResumesBucketName")
            .GetString();

        value.Should().Be("resumes");
    }

    [Fact]
    public void ProviderName_DefaultsToMinio_WhenNotConfigured()
    {
        ServiceExtensions.ResolveStorageProviderName(BuildConfiguration()).Should().Be("Minio");
    }

    [Theory]
    [InlineData("R2", "R2")]
    [InlineData("r2", "R2")]
    [InlineData("Minio", "Minio")]
    [InlineData("minio", "Minio")]
    [InlineData("something-else", "Minio")]
    public void ProviderName_NormalisesTheConfiguredValue(string configured, string expected)
    {
        var configuration = BuildConfiguration(("Storage:Provider", configured));

        ServiceExtensions.ResolveStorageProviderName(configuration).Should().Be(expected);
    }

    private static string FindAppSettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PeopleCore.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must run inside the PeopleCore repository");
        return Path.Combine(directory!.FullName, "src", "PeopleCore.API", "appsettings.json");
    }
}
