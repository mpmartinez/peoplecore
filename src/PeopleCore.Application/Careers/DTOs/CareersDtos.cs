namespace PeopleCore.Application.Careers.DTOs;

public record PublicJobPostingDto(
    Guid Id, string Title, string Description, string Requirements,
    string? DepartmentName, string? PositionTitle, int Vacancies, DateTime PostedAt);

public record JobApplicationRequest(
    string FirstName, string LastName, string Email, string Phone,
    string ResumeBase64, string ResumeFileName);

public record JobApplicationResponse(Guid ApplicationId, string Message);
