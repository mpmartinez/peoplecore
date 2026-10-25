namespace PeopleCore.E2E.Tests.Infrastructure;

/// <summary>
/// The whole application, running for the duration of the test run: a fresh database, the API on
/// a real port, and the published web app in front of it. Started once and shared by every test
/// class through <see cref="E2ECollection"/> - publishing the web app and migrating an empty
/// database cost far more than any single test.
/// </summary>
public sealed class PeopleCoreApp : IAsyncLifetime
{
    private WebAppHost? _web;
    private ApiServer? _api;

    /// <summary>The origin the browser opens; relative URLs in tests resolve against it.</summary>
    public string WebUrl => _web?.BaseUrl ?? throw NotStarted();

    public string ApiUrl => _api?.BaseUrl ?? throw NotStarted();

    public async Task InitializeAsync()
    {
        var webRoot = await WebAppPublisher.GetWebRootAsync();
        await E2EDatabase.RecreateAsync();

        // The web host first: the API's CORS policy has to name the web app's origin, and that
        // origin is not known until the host has bound its port.
        _web = await WebAppHost.StartAsync(webRoot);

        _api = new ApiServer(_web.BaseUrl);
        _api.Start();
        _web.ApiBaseUrl = _api.BaseUrl;
    }

    public async Task DisposeAsync()
    {
        if (_api is not null) await _api.DisposeAsync();
        if (_web is not null) await _web.DisposeAsync();
        await E2EDatabase.DropAsync();
    }

    private static InvalidOperationException NotStarted() => new("The application has not been started.");
}

/// <summary>
/// Every end-to-end test class joins this collection. xUnit runs a collection's classes one after
/// another, which is what these need: they share one database and one signed-in admin, and are
/// driving a single-user UI rather than measuring concurrency.
/// </summary>
[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<PeopleCoreApp>
{
    public const string Name = "e2e";
}
