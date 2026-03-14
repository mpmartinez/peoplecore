using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.DTOs;

public record LeaveAccrualPolicyDto(
    Guid Id,
    Guid LeaveTypeId,
    string LeaveTypeName,
    int TenureMonthsMin,
    int? TenureMonthsMax,
    decimal DaysPerYear,
    string AccrualFrequency,
    bool IsActive);

public record CreateLeaveAccrualPolicyRequest(
    Guid LeaveTypeId,
    int TenureMonthsMin,
    int? TenureMonthsMax,
    decimal DaysPerYear,
    AccrualFrequency AccrualFrequency);

public record LeaveAccrualTransactionDto(
    Guid Id,
    string LeaveTypeName,
    DateOnly AccrualDate,
    decimal DaysAccrued,
    int PeriodYear,
    int PeriodMonth);
