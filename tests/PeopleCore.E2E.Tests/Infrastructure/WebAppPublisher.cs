using System.Diagnostics;

namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// Produces the published Blazor WebAssembly app the browser loads - the same
/// <c>dotnet publish -c Release</c> output the Dockerfile copies into nginx, trimmed and
/// fingerprinted, rather than the development server's view of the build output. A trimming or
/// fingerprinting fault that only shows in a published build is exactly the kind of thing an
/// end-to-end run is for.
/// </summary>
internal static class WebAppPublisher
{
    /// <summary>
    /// Set to an existing publish directory (the one containing <c>wwwroot</c>) to skip the
    /// publish. CI publishes in its own step and points the tests here, so the cost shows up in
    /// the step that pays it and a publish failure reads as a build failure, not a test failure.
    /// </summary>
    private const string PublishedDirVariable = "PEOPLECORE_E2E_WEB_DIR";

    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Returns the <c>wwwroot</c> of a published PeopleCore.Web.</summary>
    public static async Task<string> GetWebRootAsync()
    {
        var configured = Environment.GetEnvironmentVariable(PublishedDirVariable);
        var publishDir = string.IsNullOrWhiteSpace(configured)
            ? await PublishAsync()
            : Path.GetFullPath(configured);

        var webRoot = Path.Combine(publishDir, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
            throw new InvalidOperationException(
                $"'{publishDir}' does not look like a published PeopleCore.Web: there is no wwwroot/index.html. " +
                $"Publish it with `dotnet publish src/PeopleCore.Web -c Release -o <dir>` and point {PublishedDirVariable} at <dir>.");

        return webRoot;
    }

    private static async Task<string> PublishAsync()
    {
        var project = Path.Combine(RepositoryRoot(), "src", "PeopleCore.Web", "PeopleCore.Web.csproj");
        var output = Path.Combine(AppContext.BaseDirectory, "e2e-web");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "publish", project, "-c", "Release", "-o", output, "-nologo" })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start `dotnet publish`.");

        // Both streams drained concurrently: a full stderr pipe blocks the child, and waiting on
        // exit before reading would then never return.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(PublishTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`dotnet publish` of PeopleCore.Web did not finish within {PublishTimeout}.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"`dotnet publish` of PeopleCore.Web failed with exit code {process.ExitCode}.\n{await stdout}\n{await stderr}");

        return output;
    }

    /// <summary>Found by walking up to the solution file, so it holds in any checkout or worktree.</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PeopleCore.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find PeopleCore.slnx above {AppContext.BaseDirectory}. " +
            $"Set {PublishedDirVariable} to a published PeopleCore.Web instead.");
    }
}
