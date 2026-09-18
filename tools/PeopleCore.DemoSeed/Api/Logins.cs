using System.Security.Cryptography;

namespace PeopleCore.DemoSeed.Api;

/// <summary>
/// The administrator's session and one session per demo employee. Leave, overtime and
/// self-evaluations can only be filed by the employee concerned, and decided only by their manager,
/// so the seeder signs in as them. Passwords are random, held in memory for this run only, and
/// never printed.
/// </summary>
public sealed class Logins(ApiClient api)
{
    public const int Admin = 0;
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(60);

    private sealed record Session(string Email, string Password, string? UserId, string Token, DateTimeOffset IssuedAt)
    {
        public override string ToString() => $"Session({Email})";
    }

    private readonly Dictionary<int, Session> _sessions = new();

    public async Task AddAdminAsync(string email, string password) =>
        _sessions[Admin] = new Session(email, password, null, await api.SignInAsync(email, password), DateTimeOffset.UtcNow);

    public async Task CreateAsync(int personNumber, Guid employeeId, string email, string firstName, string lastName, string role)
    {
        var step = $"Create a login for DEMO-{personNumber:0000}";
        // Every account holds Employee already; only the extra role is named.
        string[] roles = role == "Employee" ? [] : [role];
        var created = await api.PostAsync(step, "api/users",
            new { email, firstName, lastName, employeeId, roles }, await TokenAsync(Admin));

        var userId = created!["account"]!["id"]!.GetValue<string>();
        var temporary = created["temporaryPassword"]!.GetValue<string>();
        var password = NewPassword();

        // A new account must replace its temporary password before it can do anything else.
        var temporaryToken = await api.SignInAsync(email, temporary);
        var session = await api.PostAsync($"First password change for DEMO-{personNumber:0000}", "api/auth/change-password",
            new { currentPassword = temporary, newPassword = password }, temporaryToken);

        _sessions[personNumber] = new Session(email, password, userId, session!["token"]!.GetValue<string>(), DateTimeOffset.UtcNow);
    }

    public async Task<string> TokenAsync(int key)
    {
        var session = _sessions[key];
        if (DateTimeOffset.UtcNow - session.IssuedAt < Refresh) return session.Token;

        var token = await api.SignInAsync(session.Email, session.Password);
        _sessions[key] = session with { Token = token, IssuedAt = DateTimeOffset.UtcNow };
        return token;
    }

    public string UserIdOf(int personNumber) => _sessions[personNumber].UserId!;

    /// <summary>Long, random, and always holding a digit, which the password policy requires.</summary>
    internal static string NewPassword() => "Demo" + RandomNumberGenerator.GetHexString(20, lowercase: true) + "7";
}
