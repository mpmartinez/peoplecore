using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using System.Net.Http.Headers;

namespace PeopleCore.Web.Auth;

public class AuthTokenHandler : DelegatingHandler
{
    private readonly ILocalStorageService _localStorage;
    private readonly NavigationManager _navigation;
    private const string TokenKey = "auth_token";

    public AuthTokenHandler(ILocalStorageService localStorage, NavigationManager navigation)
    {
        _localStorage = localStorage;
        _navigation = navigation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _localStorage.GetItemAsync<string>(TokenKey);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        // Only redirect to login on 401 if this isn't already an auth request
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            !request.RequestUri!.AbsolutePath.Contains("/auth/"))
        {
            await _localStorage.RemoveItemAsync(TokenKey);
            _navigation.NavigateTo("/login", forceLoad: true);
        }

        return response;
    }
}
