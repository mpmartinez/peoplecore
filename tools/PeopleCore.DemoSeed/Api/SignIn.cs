namespace PeopleCore.DemoSeed.Api;

/// <summary>What a successful sign-in returns: the bearer token, the account's roles, and whether it must still replace a temporary password.</summary>
public sealed record SignIn(string Token, IReadOnlyList<string> Roles, bool MustChangePassword)
{
    public override string ToString() => $"SignIn(Roles = [{string.Join(", ", Roles)}], MustChangePassword = {MustChangePassword})";
}
