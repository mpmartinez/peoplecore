namespace PeopleCore.API.Accounts;

/// <summary>How often a password reset may be asked for, by address and by caller.</summary>
public interface IResetRequestThrottle
{
    /// <summary>True if this request is within both limits, and counts it. False to ignore the request.</summary>
    bool TryRequest(string email, string ipAddress);
}
