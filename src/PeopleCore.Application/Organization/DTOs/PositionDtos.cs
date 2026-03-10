namespace PeopleCore.Application.Organization.DTOs;

public record PositionDto(Guid Id, Guid DepartmentId, string DepartmentName, string Title, string? Level);
public record CreatePositionDto(Guid DepartmentId, string Title, string? Level);
public record UpdatePositionDto(string Title, string? Level);
