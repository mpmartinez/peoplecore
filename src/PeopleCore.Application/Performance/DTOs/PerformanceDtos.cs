using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Performance.DTOs;

public record ReviewCycleDto(
    Guid Id, string Name, int Year, int? Quarter,
    DateOnly StartDate, DateOnly EndDate, ReviewStatus Status);

public record CreateReviewCycleDto(
    string Name, int Year, int? Quarter,
    DateOnly StartDate, DateOnly EndDate);

public record PerformanceReviewDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid ReviewCycleId, string ReviewCycleName,
    Guid ReviewerId, string ReviewerName,
    decimal? SelfEvaluationScore, decimal? ManagerScore, decimal? FinalScore,
    string? SelfEvaluationComments, string? ManagerComments,
    ReviewStatus Status, DateTime? SubmittedAt, DateTime? CompletedAt,
    IReadOnlyList<KpiItemDto> KpiItems);

public record CreatePerformanceReviewDto(
    Guid EmployeeId, Guid ReviewCycleId, Guid ReviewerId,
    IReadOnlyList<CreateKpiItemDto> KpiItems);

public record SubmitSelfEvaluationDto(
    decimal Score, string? Comments,
    IReadOnlyList<UpdateKpiItemDto> KpiItems);

public record SubmitManagerReviewDto(
    decimal Score, string? Comments,
    IReadOnlyList<UpdateKpiItemDto> KpiItems);

public record KpiItemDto(
    Guid Id, string Description, string? Target,
    string? Actual, decimal Weight, decimal? Score);

public record CreateKpiItemDto(
    string Description, string? Target, decimal Weight);

public record UpdateKpiItemDto(
    Guid Id, string? Actual, decimal? Score);
