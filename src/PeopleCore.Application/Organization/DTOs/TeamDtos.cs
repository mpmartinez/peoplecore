namespace PeopleCore.Application.Organization.DTOs;

public record TeamDto(Guid Id, Guid DepartmentId, string DepartmentName, string Name);
public record CreateTeamDto(Guid DepartmentId, string Name);
public record UpdateTeamDto(string Name);
