using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using PeopleCore.Web;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// AuthorizeView logs "Authorization failed" at Information every time it hides something, and
// the sidebar alone asks eight role questions on every render - so an anonymous page load
// wrote thirty-odd of these to the console before the user had done anything. They are not
// failures; they are the nav working. Warning and above still comes through.
builder.Logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning);

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:5001";

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthStateProvider>();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddTransient<AuthTokenHandler>();
builder.Services.AddScoped<ToastService>();

builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
}).AddHttpMessageHandler<AuthTokenHandler>();

await builder.Build().RunAsync();
