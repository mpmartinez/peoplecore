using M2NET.Core.Enums;
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
    Gender Gender,
    CivilStatus? CivilStatus,
    string WorkEmail,
    string? MobileNumber,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? PositionId,
    string? PositionTitle,
    Guid? ReportingManagerId,
    string? ReportingManagerName,
    Guid? TeamId,
    EmploymentStatus EmploymentStatus,
    EmploymentType EmploymentType,
    DateOnly HireDate,
    DateOnly? RegularizationDate,
    bool IsActive,
    bool Is13thMonthEligible,
    // The day they left, for an employee who has; the record-separation form uses it for those
    // who left without a separation being recorded.
    DateOnly? SeparationDate = null);

/// <summary>
/// One row of the company directory, which every signed-in user may list. Deliberately a subset of
/// <see cref="EmployeeDto"/>: nothing personal (date of birth, gender, civil status, mobile) and
/// nothing about the employment terms (type, hire or regularization date, 13th-month eligibility).
/// <see cref="SeparationDate"/> is filled in only for callers who record separations, whose form
/// lists people who left without one; it is null for everyone else.
/// </summary>
public record EmployeeDirectoryEntryDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string FullName,
    string WorkEmail,
    string? DepartmentName,
    string? PositionTitle,
    EmploymentStatus EmploymentStatus,
    bool IsActive,
    DateOnly? SeparationDate = null)
{
    public static EmployeeDirectoryEntryDto From(EmployeeDto e, bool includeSeparationDate = false) => new(
        e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.FullName, e.WorkEmail,
        e.DepartmentName, e.PositionTitle, e.EmploymentStatus, e.IsActive,
        includeSeparationDate ? e.SeparationDate : null);
}

public record CreateEmployeeDto(
    string EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    Gender Gender,
    string WorkEmail,
    string? MobileNumber,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    EmploymentType EmploymentType,
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
