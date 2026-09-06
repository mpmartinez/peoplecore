using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// An employee's pay basis. Deliberately separate from any HR-facing employee DTO - PeopleCore
/// serves employee data to the Manager and Employee roles through ESS, and compensation must
/// never travel in an HR DTO (see EmployeeCompensation's own remarks).
/// </summary>
public record EmployeeCompensationDto(
    Guid Id,
    Guid EmployeeId,
    decimal BasicSalary,
    PayFrequency PayFrequency,
    string TaxCode,
    int Dependents);

public record UpsertCompensationRequest(
    decimal BasicSalary,
    PayFrequency PayFrequency,
    string TaxCode,
    int Dependents);
