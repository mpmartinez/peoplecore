using FluentAssertions;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Domain.Entities.Employees;
using M2NET.Core.Enums;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

/// <summary>
/// Employee documents live in their own configurable bucket, separate from applicant resumes.
/// </summary>
public class EmployeeDocumentServiceBucketTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IStorageService> _storage = new();

    private EmployeeDocumentService CreateSut(DocumentStorageOptions options) =>
        new(_employeeRepo.Object, _storage.Object, options);

    private Guid ArrangeEmployee()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP-001",
            FirstName = "Juan",
            LastName = "dela Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            WorkEmail = "juan@company.com",
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = EmploymentType.Regular,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true
        };

        _employeeRepo.Setup(r => r.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(employee);

        return employee.Id;
    }

    [Fact]
    public async Task Upload_UploadsDocumentToConfiguredBucket()
    {
        var employeeId = ArrangeEmployee();
        var sut = CreateSut(new DocumentStorageOptions { BucketName = "tenant-documents" });

        using var stream = new MemoryStream("file"u8.ToArray());
        await sut.UploadDocumentAsync(employeeId, DocumentType.Contract, "contract.pdf", stream, "application/pdf");

        _storage.Verify(s => s.UploadAsync(
            "tenant-documents", It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Pins the unchanged default so existing documents stay reachable.</summary>
    [Fact]
    public async Task Upload_WithDefaultOptions_UploadsDocumentToPeopleCoreDocumentsBucket()
    {
        var employeeId = ArrangeEmployee();
        var sut = CreateSut(new DocumentStorageOptions());

        using var stream = new MemoryStream("file"u8.ToArray());
        await sut.UploadDocumentAsync(employeeId, DocumentType.Contract, "contract.pdf", stream, "application/pdf");

        _storage.Verify(s => s.UploadAsync(
            "peoplecore-documents", It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_RemovesDocumentFromConfiguredBucket()
    {
        var employeeId = ArrangeEmployee();
        var sut = CreateSut(new DocumentStorageOptions { BucketName = "tenant-documents" });

        using var stream = new MemoryStream("file"u8.ToArray());
        var uploaded = await sut.UploadDocumentAsync(
            employeeId, DocumentType.Contract, "contract.pdf", stream, "application/pdf");

        await sut.DeleteDocumentAsync(employeeId, uploaded.Id);

        _storage.Verify(s => s.DeleteAsync(
            "tenant-documents", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDownloadUrl_SignsAgainstConfiguredBucket()
    {
        var employeeId = ArrangeEmployee();
        var sut = CreateSut(new DocumentStorageOptions { BucketName = "tenant-documents" });

        using var stream = new MemoryStream("file"u8.ToArray());
        var uploaded = await sut.UploadDocumentAsync(
            employeeId, DocumentType.Contract, "contract.pdf", stream, "application/pdf");

        _storage.Setup(s => s.GetPresignedUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://signed.example/doc");

        var url = await sut.GetDownloadUrlAsync(employeeId, uploaded.Id);

        url.Should().Be("https://signed.example/doc");
        _storage.Verify(s => s.GetPresignedUrlAsync(
            "tenant-documents", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
