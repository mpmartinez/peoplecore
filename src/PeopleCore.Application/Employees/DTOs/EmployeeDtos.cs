using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Employees.DTOs;

public record EmployeeDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    string FullName,
    DateOnly DateOfBirth,
    string Gender,
    string? CivilStatus,
    string WorkEmail,
    string? MobileNumber,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? PositionId,
    string? PositionTitle,
    Guid? ReportingManagerId,
    string? ReportingManagerName,
    EmploymentStatus EmploymentStatus,
    string EmploymentType,
    DateOnly HireDate,
    DateOnly? RegularizationDate,
    bool IsActive,
    bool Is13thMonthEligible);

public record CreateEmployeeDto(
    string EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    string Gender,
    string WorkEmail,
    string? MobileNumber,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    string EmploymentType,
    DateOnly HireDate);

public record UpdateEmployeeDto(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? CivilStatus,
    string? PersonalEmail,
    string? MobileNumber,
    string? Address,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? TeamId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    DateOnly? RegularizationDate,
    bool Is13thMonthEligible);

public record EmployeeFilterDto(
    string? Search,
    Guid? DepartmentId,
    EmploymentStatus? Status,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20);

public record GovernmentIdDto(Guid Id, GovernmentIdType IdType, string IdNumber);
public record UpsertGovernmentIdDto(GovernmentIdType IdType, string IdNumber);

public record EmergencyContactDto(Guid Id, string Name, string Relationship, string Phone, string? Address);
public record CreateEmergencyContactDto(string Name, string Relationship, string Phone, string? Address);

public record EmployeeDocumentDto(Guid Id, string DocumentType, string FileName, long? FileSizeBytes, DateTime UploadedAt);
