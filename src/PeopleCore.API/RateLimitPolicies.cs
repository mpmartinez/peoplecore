namespace PeopleCore.API;

public static class RateLimitPolicies
{
    /// <summary>Per-IP throttle for the public, unauthenticated careers application endpoint.</summary>
    public const string CareersApply = "careers-apply";
}
