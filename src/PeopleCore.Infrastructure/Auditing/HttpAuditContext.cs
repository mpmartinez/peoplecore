using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PeopleCore.Application.Common.Auditing;

namespace PeopleCore.Infrastructure.Auditing;

/// <summary>
/// The audit context of the current HTTP request. Outside a request - a hosted job, the startup
/// seeder - everything reads null except what the job sets in <see cref="SystemActor"/>.
/// The client IP is <c>RemoteIpAddress</c>, which the forwarded-headers middleware has already
/// replaced with the caller's address behind the proxy.
/// </summary>
public class HttpAuditContext : IAuditContext
{
    private const int MaxUserAgentLength = 500;

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAuditContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext? Http => _httpContextAccessor.HttpContext;

    public string? UserId => Http?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserEmail => Http?.User?.FindFirstValue(ClaimTypes.Email);
    public string? IpAddress => Http?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var agent = Http?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(agent)) return null;
            return agent.Length > MaxUserAgentLength ? agent[..MaxUserAgentLength] : agent;
        }
    }

    public string? CorrelationId => Http?.TraceIdentifier;
    public string? HttpMethod => Http?.Request.Method;
    public string? Path => Http?.Request.Path.Value;

    public string? SystemActor { get; set; }
}
