using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// Serves the published web app the way nginx does in production (see nginx.conf): static files,
/// with unknown routes falling back to index.html so the Blazor router can take deep links.
/// MapFallbackToFile only falls back for paths without a file extension, so a missing
/// _framework file 404s here as it does there, instead of arriving as HTML.
/// </summary>
internal sealed partial class WebAppHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private WebAppHost(WebApplication app) => _app = app;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Where the app's ApiClient is pointed. Assigned after the API starts, because the API in turn
    /// needs this host's origin for CORS; nothing asks for the app settings until a page loads.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    public static async Task<WebAppHost> StartAsync(string webRoot)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = webRoot
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();
        var host = new WebAppHost(app);

        app.Use(host.ServeAppSettings);

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".wasm"] = "application/wasm";
        contentTypes.Mappings[".dat"] = "application/octet-stream";
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
        app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });
        app.MapFallbackToFile("index.html");

        await app.StartAsync();

        host.BaseUrl = app.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single()
            .TrimEnd('/');
        return host;
    }

    /// <summary>
    /// Answers every appsettings file the app asks for with the test API's address. A published
    /// build runs as Production, and appsettings.Production.json names the live API - served as
    /// published, the suite would sign in against production. The base file is answered too, so
    /// which of the two the runtime reads last cannot matter.
    /// </summary>
    private async Task ServeAppSettings(HttpContext context, RequestDelegate next)
    {
        if (!AppSettingsPath().IsMatch(context.Request.Path.Value ?? string.Empty))
        {
            await next(context);
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { ApiBaseUrl }));
    }

    [GeneratedRegex(@"^/appsettings(\.[^/]+)?\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex AppSettingsPath();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
