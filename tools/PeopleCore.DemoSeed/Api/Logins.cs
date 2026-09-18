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
    private readonly Dictionary<int, string> _createdUserIds = new();

    /// <summary>
    /// Every demo login created so far, by person number, recorded as soon as the account exists, so
    /// a run that stops even halfway through creating a login can still switch it off.
    /// </summary>
    public IReadOnlyDictionary<int, string> CreatedUserIds => _createdUserIds;

    /// <summary>Signs the administrator in, and returns the sign-in so the caller can check its roles.</summary>
    public async Task<SignIn> AddAdminAsync(string email, string password)
    {
        var signIn = await api.SignInAsync(email, password);
        _sessions[Admin] = new Session(email, password, null, signIn.Token, DateTimeOffset.UtcNow);
        return signIn;
    }

    public async Task CreateAsync(int personNumber, Guid employeeId, string email, string firstName, string lastName, string role)
    {
        var step = $"Create a login for DEMO-{personNumber:0000}";
        // Every account holds Employee already; only the extra role is named.
        string[] roles = role == "Employee" ? [] : [role];
        var created = await api.PostAsync(step, "api/users",
            new { email, firstName, lastName, employeeId, roles }, await TokenAsync(Admin));

        var userId = ApiClient.RequireString(created, step, "POST", "api/users", "account", "id");
        _createdUserIds[personNumber] = userId;
        var temporary = ApiClient.RequireString(created, step, "POST", "api/users", "temporaryPassword");
        var password = NewPassword();

        // A new account must replace its temporary password before it can do anything else.
        var temporaryToken = (await api.SignInAsync(email, temporary)).Token;
        var changeStep = $"First password change for DEMO-{personNumber:0000}";
        var session = await api.PostAsync(changeStep, "api/auth/change-password",
            new { currentPassword = temporary, newPassword = password }, temporaryToken);
        var token = ApiClient.RequireString(session, changeStep, "POST", "api/auth/change-password", "token");

        _sessions[personNumber] = new Session(email, password, userId, token, DateTimeOffset.UtcNow);
    }

    public async Task<string> TokenAsync(int key)
    {
        var session = _sessions[key];
        if (DateTimeOffset.UtcNow - session.IssuedAt < Refresh) return session.Token;

        var token = (await api.SignInAsync(session.Email, session.Password)).Token;
        _sessions[key] = session with { Token = token, IssuedAt = DateTimeOffset.UtcNow };
        return token;
    }

    public string UserIdOf(int personNumber) => _sessions[personNumber].UserId!;

    /// <summary>Long, random, and always holding a digit, which the password policy requires.</summary>
    internal static string NewPassword() => "Demo" + RandomNumberGenerator.GetHexString(20, lowercase: true) + "7";
}
