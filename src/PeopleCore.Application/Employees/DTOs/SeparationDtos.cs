using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Employees.DTOs;

public record RecordSeparationRequest(
    Guid EmployeeId,
    SeparationType Type,
    AuthorizedCause? AuthorizedCause,
    DateOnly NoticeDate,
    DateOnly LastWorkingDay,
    string? Reason);

public record SeparationDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    string? Position,
    SeparationType Type,
    AuthorizedCause? AuthorizedCause,
    DateOnly NoticeDate,
    DateOnly LastWorkingDay,
    string? Reason,
    SeparationStatus Status,
    string RecordedBy,
    string? SeparatedBy,
    DateTime? SeparatedAt,
    DateOnly FinalPayDueBy,
    bool FinalPayOverdue,
    int ClearedCount,
    int ClearanceCount,
    IReadOnlyList<ClearanceItemDto> ClearanceItems,
    Guid? FinalPayRunId,
    string? FinalPayRunNumber,
    PayrollRunStatus? FinalPayStatus);

public record ClearanceItemDto(
    Guid Id,
    string Name,
    string? ClearedBy,
    DateTime? ClearedAt,
    string? Note,
    string? LastUndoneBy,
    DateTime? LastUndoneAt);

public record ClearItemRequest(string? Note);

public record AddClearanceItemRequest(string Name);
