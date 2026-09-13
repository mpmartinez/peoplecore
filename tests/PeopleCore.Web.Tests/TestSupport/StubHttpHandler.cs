using System.Net;
using System.Text;

namespace PeopleCore.Web.Tests.TestSupport;

/// <summary>
/// Answers HTTP requests from a list of canned responses, so ApiClient - and the pages that
/// inject it - can be exercised without a running API. Routes match on method plus the path and
/// query as the client built them; a request nothing matches gets a 404, the same thing the real
/// API would say about an endpoint it does not have, rather than an exception that would read as
/// a network failure.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string PathAndQuery, Func<HttpResponseMessage> Respond)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Request bodies, read eagerly: HttpClient disposes the content once SendAsync returns.</summary>
    public List<string?> RequestBodies { get; } = [];

    public StubHttpHandler On(HttpMethod method, string pathAndQuery, HttpStatusCode status, string? json = null) =>
        On(method, pathAndQuery, () => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
        });

    public StubHttpHandler On(HttpMethod method, string pathAndQuery, Func<HttpResponseMessage> respond)
    {
        _routes.Add((method, pathAndQuery, respond));
        return this;
    }

    public static HttpClient ClientFor(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.test/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        var pathAndQuery = request.RequestUri!.PathAndQuery;
        foreach (var route in _routes)
        {
            if (route.Method == request.Method && route.PathAndQuery == pathAndQuery)
                return route.Respond();
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
