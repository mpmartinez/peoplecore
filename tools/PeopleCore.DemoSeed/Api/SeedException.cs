namespace PeopleCore.DemoSeed.Api;

/// <summary>A request the API refused. The run stops here; the message says what was being done.</summary>
public sealed class SeedException(string step, string method, string path, int status, string apiMessage)
    : Exception($"{step}: {method} {path} returned {status}: {apiMessage}")
{
    public string Step { get; } = step;
    public string Method { get; } = method;
    public string Path { get; } = path;
    public int Status { get; } = status;
    public string ApiMessage { get; } = apiMessage;
}

/// <summary>A preflight check refused the site or the account, before anything was written. Nothing was changed.</summary>
public class PreflightRefusedException(string message) : Exception(message);

/// <summary>The site already holds the demo company. Nothing was changed.</summary>
public sealed class AlreadySeededException(string message) : PreflightRefusedException(message);
