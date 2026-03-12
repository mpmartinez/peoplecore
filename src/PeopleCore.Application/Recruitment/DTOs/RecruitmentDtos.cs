using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Recruitment.DTOs;

public record JobPostingDto(
    Guid Id, string Title, Guid? DepartmentId, string? DepartmentName,
    Guid? PositionId, string? PositionTitle,
    string? Description, string? Requirements, int Vacancies,
    string Status, DateTime? PostedAt, DateTime? ClosedAt);

public record CreateJobPostingDto(
    string Title, Guid? DepartmentId, Guid? PositionId,
    string? Description, string? Requirements, int Vacancies);

public record UpdateJobPostingDto(
    string Title, Guid? DepartmentId, Guid? PositionId,
    string? Description, string? Requirements, int Vacancies, string Status);

public record ApplicantDto(
    Guid Id, Guid JobPostingId, string JobPostingTitle,
    string FirstName, string LastName, string Email, string? Phone,
    ApplicantStatus Status, Guid? ConvertedEmployeeId, DateTime AppliedAt);

public record CreateApplicantDto(
    Guid JobPostingId, string FirstName, string LastName,
    string Email, string? Phone);

public record UpdateApplicantStatusDto(ApplicantStatus Status);

public record ConvertToEmployeeDto(
    string EmployeeNumber,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    DateOnly HireDate);

public record InterviewStageDto(
    Guid Id, Guid ApplicantId, string ApplicantName,
    string StageName, DateTime? ScheduledAt,
    Guid? InterviewerId, string? InterviewerName,
    string? Outcome, string? Notes);

public record CreateInterviewStageDto(
    Guid ApplicantId, string StageName,
    DateTime? ScheduledAt, Guid? InterviewerId);

public record UpdateInterviewStageDto(
    DateTime? ScheduledAt, Guid? InterviewerId,
    string? Outcome, string? Notes);
