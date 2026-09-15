namespace PeopleCore.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? EmployeeId { get; }
    string? Email { get; }

    /// <summary>The caller's role names, for display. Never decide access with these - see <see cref="HasPermission"/>.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>True when the caller's token grants <paramref name="key"/> (a <c>Permissions</c> key).</summary>
    bool HasPermission(string key);
}
