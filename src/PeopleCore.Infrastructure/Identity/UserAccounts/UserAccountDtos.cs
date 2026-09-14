namespace PeopleCore.Infrastructure.Identity.UserAccounts;

/// <summary>A sign-in account as the Users page lists it, with the employee record it is linked to, if any.</summary>
public record UserAccountDto(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    Guid? EmployeeId,
    string? EmployeeNumber,
    string? EmployeeName);

public record CreateUserAccountRequest(
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string>? Roles,
    Guid? EmployeeId);

/// <summary>
/// Replaces the account's email, names, roles and employee link. <see cref="NewPassword"/> is the
/// one optional field: left blank, the password is not touched.
/// </summary>
public record UpdateUserAccountRequest(
    string? Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string>? Roles,
    Guid? EmployeeId,
    string? NewPassword);

public enum UserAccountFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>What a change to an account came to: the account as saved, or why nothing was saved.</summary>
public sealed record UserAccountResult(UserAccountDto? User, UserAccountFailure Failure, string? Message)
{
    public bool Succeeded => Failure == UserAccountFailure.None;

    public static UserAccountResult Ok(UserAccountDto? user) => new(user, UserAccountFailure.None, null);
    public static UserAccountResult NotFound() => new(null, UserAccountFailure.NotFound, "The account could not be found.");
    public static UserAccountResult Invalid(string message) => new(null, UserAccountFailure.Invalid, message);
    public static UserAccountResult Conflict(string message) => new(null, UserAccountFailure.Conflict, message);
}
