namespace PeopleCore.API.Accounts;

/// <summary>Claims PeopleCore adds to its own tokens. The web client reads them by these names too.</summary>
public static class AccountClaims
{
    /// <summary>The account's security stamp when the token was issued.</summary>
    public const string SecurityStamp = "sec_stamp";

    /// <summary>Present, with the value "true", while the account is on a temporary password.</summary>
    public const string MustChangePassword = "must_change_password";
}
