using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for end-to-end tests. Playwright's <see cref="PageTest"/> gives each test a new
/// browser context and page (so no cookie or local-storage sign-in leaks between tests); this
/// points that context at the running app and adds the sign-in every test starts with.
/// <para>
/// Browser and headed mode come from Playwright's own environment variables, for example
/// <c>BROWSER=firefox</c> or <c>HEADED=1</c>.
/// </para>
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETest(PeopleCoreApp app) : PageTest
{
    /// <summary>
    /// The first page load downloads and starts the .NET WebAssembly runtime, which takes several
    /// seconds on a cold CI runner - well past Playwright's 5-second assertion default.
    /// </summary>
    private const float TimeoutMs = 30_000;

    /// <summary>The live API, as named by src/PeopleCore.Web/wwwroot/appsettings.Production.json.</summary>
    private const string ProductionApi = "https://peoplecore.m2netsolutions.com/**";

    /// <summary>
    /// Failed tests leave a Playwright trace here: open it with `pwsh playwright.ps1 show-trace`.
    /// Now and then a passing test leaves one too. Playwright's TestOk counts any first-chance
    /// exception in the process as a failure, and that includes ones the API throws and handles
    /// itself. So a real failure always leaves a trace, but a trace does not prove a failure.
    /// </summary>
    public static string TracesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "playwright-traces");

    protected PeopleCoreApp App { get; } = app;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = App.WebUrl,
        // sw.js caches nothing, but a registered worker still sits between the page and the
        // network, and Playwright cannot route requests through one.
        ServiceWorkers = ServiceWorkerPolicy.Block,
        Locale = "en-PH",
        TimezoneId = "Asia/Manila"
    };

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        SetDefaultExpectTimeout(TimeoutMs);
        Context.SetDefaultTimeout(TimeoutMs);

        // Belt and braces for WebAppHost.ServeAppSettings: if the published app ever reads the
        // production API address anyway, its requests fail here rather than reach the live system.
        await Context.RouteAsync(ProductionApi, route => route.AbortAsync());

        await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
    }

    public override async Task DisposeAsync()
    {
        if (!TestOk)
        {
            Directory.CreateDirectory(TracesDirectory);
            await Context.Tracing.StopAsync(new()
            {
                Path = Path.Combine(TracesDirectory, $"{GetType().Name}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.zip")
            });
        }

        await base.DisposeAsync();
    }

    /// <summary>Signs in through the login page, and waits until the app shell is showing.</summary>
    protected async Task SignInAsync(
        string email = E2ECredentials.AdminEmail,
        string password = E2ECredentials.AdminPassword)
    {
        await Page.GotoAsync("/login");
        await FillSignInFormAsync(email, password);
        await Expect(Page.GetByTitle("Account")).ToBeVisibleAsync();
    }

    protected async Task FillSignInFormAsync(string email, string password)
    {
        await Page.GetByLabel("Email").FillAsync(email);
        // Exact: the reveal toggle beside the field is labelled "Show password".
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
    }

    /// <summary>A value no earlier test or run can have used, for names that must be unique.</summary>
    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";
}
