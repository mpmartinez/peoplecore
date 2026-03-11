namespace PeopleCore.Application.Organization.DTOs;

public record DepartmentDto(
    Guid Id,
    Guid CompanyId,
    Guid? ParentDepartmentId,
    string? ParentDepartmentName,
    string Name,
    string? Code,
    int SubDepartmentCount);

public record CreateDepartmentDto(
    Guid CompanyId,
    Guid? ParentDepartmentId,
    string Name,
    string? Code);

public record UpdateDepartmentDto(
    Guid? ParentDepartmentId,
    string Name,
    string? Code);
