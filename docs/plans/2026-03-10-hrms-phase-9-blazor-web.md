# Phase 9: Blazor WASM Standalone Web App

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Standalone Blazor WASM app with JWT auth, shared layout/nav, and pages for all HR modules.

**Prereq:** Phase 8 complete (API fully functional).

---

### Task 21: Blazor Setup — Auth, HttpClient, Layout

**Files:**
- Modify: `src/PeopleCore.Web/Program.cs`
- Create: `src/PeopleCore.Web/Auth/JwtAuthStateProvider.cs`
- Create: `src/PeopleCore.Web/Services/ApiClient.cs`
- Modify: `src/PeopleCore.Web/wwwroot/appsettings.json`
- Modify: `src/PeopleCore.Web/Shared/MainLayout.razor`
- Modify: `src/PeopleCore.Web/Shared/NavMenu.razor`
- Create: `src/PeopleCore.Web/Pages/Auth/Login.razor`

**Step 1: Add NuGet packages**

```bash
dotnet add src/PeopleCore.Web/PeopleCore.Web.csproj package Microsoft.AspNetCore.Components.Authorization
dotnet add src/PeopleCore.Web/PeopleCore.Web.csproj package Blazored.LocalStorage
```

**Step 2: Configure Program.cs**

```csharp
// src/PeopleCore.Web/Program.cs
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:5001";

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthStateProvider>();
builder.Services.AddScoped<JwtAuthStateProvider>();

builder.Services.AddScoped(sp =>
{
    var authProvider = sp.GetRequiredService<JwtAuthStateProvider>();
    var client = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
    return client;
});

builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
```

**Step 3: appsettings.json (Blazor WASM — in wwwroot)**

```json
{
  "ApiBaseUrl": "https://localhost:5001"
}
```

**Step 4: Create JwtAuthStateProvider**

```csharp
// src/PeopleCore.Web/Auth/JwtAuthStateProvider.cs
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace PeopleCore.Web.Auth;

public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _httpClient;
    private const string TokenKey = "auth_token";

    public JwtAuthStateProvider(ILocalStorageService localStorage, HttpClient httpClient)
    {
        _localStorage = localStorage;
        _httpClient = httpClient;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _localStorage.GetItemAsync<string>(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var claims = ParseClaimsFromJwt(token);
        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task LoginAsync(string token)
    {
        await _localStorage.SetItemAsync(TokenKey, token);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task LogoutAsync()
    {
        await _localStorage.RemoveItemAsync(TokenKey);
        _httpClient.DefaultRequestHeaders.Authorization = null;
        NotifyAuthenticationStateChanged(Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        return token.Claims;
    }
}
```

**Step 5: Create ApiClient**

```csharp
// src/PeopleCore.Web/Services/ApiClient.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace PeopleCore.Web.Services;

/// <summary>
/// Typed HTTP client wrapping all API calls.
/// Add a method per endpoint as modules are built.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http) => _http = http;

    // Auth
    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
    }

    // Employees
    public async Task<PagedResult<EmployeeDto>?> GetEmployeesAsync(int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<EmployeeDto>>($"api/employees?page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<EmployeeDto?> GetEmployeeAsync(Guid id)
        => await _http.GetFromJsonAsync<EmployeeDto>($"api/employees/{id}", JsonOptions);

    // Leave
    public async Task<IReadOnlyList<LeaveBalanceDto>?> GetLeaveBalancesAsync(Guid employeeId)
        => await _http.GetFromJsonAsync<IReadOnlyList<LeaveBalanceDto>>($"api/leave-balances/{employeeId}", JsonOptions);

    public async Task<PagedResult<LeaveRequestDto>?> GetLeaveRequestsAsync(Guid? employeeId = null, int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<LeaveRequestDto>>(
            $"api/leave-requests?{(employeeId.HasValue ? $"employeeId={employeeId}&" : "")}page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<LeaveRequestDto?> CreateLeaveRequestAsync(object dto)
    {
        var response = await _http.PostAsJsonAsync("api/leave-requests", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LeaveRequestDto>(JsonOptions);
    }

    // Attendance
    public async Task<PagedResult<AttendanceRecordDto>?> GetAttendanceAsync(Guid? employeeId = null, int page = 1, int pageSize = 20)
        => await _http.GetFromJsonAsync<PagedResult<AttendanceRecordDto>>(
            $"api/attendance?{(employeeId.HasValue ? $"employeeId={employeeId}&" : "")}page={page}&pageSize={pageSize}", JsonOptions);

    public async Task<AttendanceRecordDto?> TimeInAsync(Guid employeeId)
    {
        var response = await _http.PostAsJsonAsync("api/attendance/time-in", new { employeeId, timeIn = DateTime.UtcNow });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>(JsonOptions);
    }

    public async Task<AttendanceRecordDto?> TimeOutAsync(Guid employeeId)
    {
        var response = await _http.PostAsJsonAsync("api/attendance/time-out", new { employeeId, timeOut = DateTime.UtcNow });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>(JsonOptions);
    }
}

// Shared response DTOs (client-side copies — keep in sync with API)
public record LoginResponse(string Token, string Email, IReadOnlyList<string> Roles);
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);
public record EmployeeDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, string FullName, string WorkEmail, string? DepartmentName, string? PositionTitle, string EmploymentStatus, bool IsActive);
public record LeaveBalanceDto(Guid Id, string LeaveTypeName, int Year, decimal TotalDays, decimal UsedDays, decimal RemainingDays);
public record LeaveRequestDto(Guid Id, string LeaveTypeName, string StartDate, string EndDate, decimal TotalDays, string Status, string? Reason);
public record AttendanceRecordDto(Guid Id, string AttendanceDate, string? TimeIn, string? TimeOut, int LateMinutes, int UndertimeMinutes, bool IsPresent);
```

**Step 6: Create Login page**

```razor
@* src/PeopleCore.Web/Pages/Auth/Login.razor *@
@page "/login"
@inject ApiClient Api
@inject PeopleCore.Web.Auth.JwtAuthStateProvider AuthProvider
@inject NavigationManager Nav

<div class="min-h-screen flex items-center justify-center bg-gray-50">
    <div class="max-w-md w-full bg-white rounded-lg shadow p-8">
        <h2 class="text-2xl font-bold text-center mb-6">PeopleCore Login</h2>

        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="bg-red-50 text-red-700 rounded p-3 mb-4 text-sm">@_error</div>
        }

        <EditForm Model="_form" OnValidSubmit="HandleLogin">
            <DataAnnotationsValidator />
            <div class="mb-4">
                <label class="block text-sm font-medium mb-1">Email</label>
                <InputText @bind-Value="_form.Email" class="w-full border rounded px-3 py-2" placeholder="you@company.com" />
            </div>
            <div class="mb-6">
                <label class="block text-sm font-medium mb-1">Password</label>
                <InputText @bind-Value="_form.Password" type="password" class="w-full border rounded px-3 py-2" />
            </div>
            <button type="submit" disabled="@_loading" class="w-full bg-blue-600 text-white py-2 rounded hover:bg-blue-700">
                @(_loading ? "Signing in..." : "Sign In")
            </button>
        </EditForm>
    </div>
</div>

@code {
    private readonly LoginForm _form = new();
    private string? _error;
    private bool _loading;

    private async Task HandleLogin()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await Api.LoginAsync(_form.Email, _form.Password);
            if (result is not null)
            {
                await AuthProvider.LoginAsync(result.Token);
                Nav.NavigateTo("/");
            }
        }
        catch
        {
            _error = "Invalid email or password.";
        }
        finally { _loading = false; }
    }

    private class LoginForm
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
```

**Step 7: Update App.razor to use AuthorizeRouteView**

```razor
@* src/PeopleCore.Web/App.razor *@
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    <RedirectToLogin />
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
        <NotFound>
            <LayoutView Layout="@typeof(MainLayout)">
                <p role="alert">Sorry, there's nothing at this address.</p>
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

**Step 8: Create RedirectToLogin component**

```razor
@* src/PeopleCore.Web/Shared/RedirectToLogin.razor *@
@inject NavigationManager Nav
@code {
    protected override void OnInitialized()
        => Nav.NavigateTo($"login?returnUrl={Uri.EscapeDataString(Nav.Uri)}", forceLoad: false);
}
```

**Step 9: Update MainLayout.razor**

```razor
@* src/PeopleCore.Web/Shared/MainLayout.razor *@
@inherits LayoutComponentBase
@inject PeopleCore.Web.Auth.JwtAuthStateProvider AuthProvider
@inject NavigationManager Nav

<div class="flex h-screen bg-gray-100">
    <NavMenu />
    <div class="flex-1 flex flex-col overflow-hidden">
        <header class="bg-white shadow px-6 py-3 flex justify-between items-center">
            <h1 class="text-xl font-semibold text-gray-800">PeopleCore HRMS</h1>
            <AuthorizeView>
                <Authorized>
                    <div class="flex items-center gap-4">
                        <span class="text-sm text-gray-600">@context.User.Identity?.Name</span>
                        <button @onclick="Logout" class="text-sm text-red-600 hover:text-red-800">Logout</button>
                    </div>
                </Authorized>
            </AuthorizeView>
        </header>
        <main class="flex-1 overflow-auto p-6">
            @Body
        </main>
    </div>
</div>

@code {
    private async Task Logout()
    {
        await AuthProvider.LogoutAsync();
        Nav.NavigateTo("/login");
    }
}
```

**Step 10: Update NavMenu.razor**

```razor
@* src/PeopleCore.Web/Shared/NavMenu.razor *@
<nav class="w-64 bg-blue-800 text-white flex flex-col p-4">
    <div class="text-xl font-bold mb-8 px-2">PeopleCore</div>

    <AuthorizeView Roles="Admin,HRManager">
        <Authorized>
            <NavSection Title="Organization">
                <NavItem Href="/departments" Icon="🏢" Label="Departments" />
                <NavItem Href="/positions" Icon="💼" Label="Positions" />
            </NavSection>
            <NavSection Title="Employees">
                <NavItem Href="/employees" Icon="👥" Label="All Employees" />
            </NavSection>
            <NavSection Title="Recruitment">
                <NavItem Href="/job-postings" Icon="📋" Label="Job Postings" />
                <NavItem Href="/applicants" Icon="🙋" Label="Applicants" />
            </NavSection>
        </Authorized>
    </AuthorizeView>

    <NavSection Title="My HR">
        <NavItem Href="/" Icon="🏠" Label="Dashboard" />
        <NavItem Href="/my-profile" Icon="👤" Label="My Profile" />
        <NavItem Href="/my-attendance" Icon="🕐" Label="Attendance" />
        <NavItem Href="/my-leave" Icon="📅" Label="Leave" />
    </NavSection>

    <AuthorizeView Roles="Admin,HRManager,Manager">
        <Authorized>
            <NavSection Title="Management">
                <NavItem Href="/leave-approvals" Icon="✅" Label="Leave Approvals" />
                <NavItem Href="/overtime-approvals" Icon="⏰" Label="OT Approvals" />
                <NavItem Href="/performance" Icon="📊" Label="Performance" />
            </NavSection>
        </Authorized>
    </AuthorizeView>
</nav>
```

**Step 11: Commit**

```bash
git add -A
git commit -m "feat: setup Blazor WASM with JWT auth, layout, nav, and API client"
```

---

### Task 22: Blazor — Employee Self-Service Pages

**Files:**
- Create: `src/PeopleCore.Web/Pages/Dashboard.razor`
- Create: `src/PeopleCore.Web/Pages/ESS/MyProfile.razor`
- Create: `src/PeopleCore.Web/Pages/ESS/MyAttendance.razor`
- Create: `src/PeopleCore.Web/Pages/ESS/MyLeave.razor`

**Step 1: Dashboard**

```razor
@* src/PeopleCore.Web/Pages/Dashboard.razor *@
@page "/"
@attribute [Authorize]
@inject ApiClient Api

<h2 class="text-2xl font-semibold mb-6">Dashboard</h2>

<div class="grid grid-cols-3 gap-4">
    <div class="bg-white rounded-lg shadow p-4">
        <h3 class="font-medium text-gray-500">Leave Balance</h3>
        @if (_balances is not null)
        {
            @foreach (var b in _balances.Take(3))
            {
                <div class="mt-2 flex justify-between text-sm">
                    <span>@b.LeaveTypeName</span>
                    <span class="font-semibold text-blue-600">@b.RemainingDays days</span>
                </div>
            }
        }
    </div>
    <div class="bg-white rounded-lg shadow p-4">
        <h3 class="font-medium text-gray-500">Today's Attendance</h3>
        @if (_todayAttendance is not null)
        {
            <div class="mt-2 text-sm">
                <div>Time In: <span class="font-medium">@(_todayAttendance.TimeIn ?? "--:--")</span></div>
                <div>Time Out: <span class="font-medium">@(_todayAttendance.TimeOut ?? "--:--")</span></div>
                @if (_todayAttendance.LateMinutes > 0)
                {
                    <div class="text-red-500">Late: @_todayAttendance.LateMinutes min</div>
                }
            </div>
        }
        else
        {
            <p class="text-sm text-gray-400 mt-2">Not clocked in yet.</p>
        }
    </div>
    <div class="bg-white rounded-lg shadow p-4">
        <h3 class="font-medium text-gray-500">Pending Leave Requests</h3>
        @if (_pendingLeaves is not null)
        {
            <div class="mt-2 text-2xl font-bold text-orange-500">@_pendingLeaves</div>
            <div class="text-sm text-gray-400">awaiting approval</div>
        }
    </div>
</div>

@code {
    private IReadOnlyList<LeaveBalanceDto>? _balances;
    private AttendanceRecordDto? _todayAttendance;
    private int? _pendingLeaves;

    // Note: In real implementation, get employeeId from AuthenticationState
    protected override async Task OnInitializedAsync()
    {
        // Placeholder — wire to current user's employee ID from auth state
    }
}
```

**Step 2: My Leave page (ESS)**

```razor
@* src/PeopleCore.Web/Pages/ESS/MyLeave.razor *@
@page "/my-leave"
@attribute [Authorize]
@inject ApiClient Api

<h2 class="text-2xl font-semibold mb-4">My Leave</h2>

<div class="grid grid-cols-2 gap-6">
    <!-- Leave Balances -->
    <div class="bg-white rounded-lg shadow p-4">
        <h3 class="font-medium mb-3">Leave Balances (@DateTime.UtcNow.Year)</h3>
        @if (_balances is null)
        {
            <p class="text-gray-400 text-sm">Loading...</p>
        }
        else
        {
            <table class="w-full text-sm">
                <thead>
                    <tr class="text-left text-gray-500 border-b">
                        <th class="pb-2">Type</th>
                        <th class="pb-2 text-right">Total</th>
                        <th class="pb-2 text-right">Used</th>
                        <th class="pb-2 text-right">Remaining</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var b in _balances)
                    {
                        <tr class="border-b">
                            <td class="py-2">@b.LeaveTypeName</td>
                            <td class="text-right">@b.TotalDays</td>
                            <td class="text-right">@b.UsedDays</td>
                            <td class="text-right font-semibold text-blue-600">@b.RemainingDays</td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </div>

    <!-- File Leave Request -->
    <div class="bg-white rounded-lg shadow p-4">
        <h3 class="font-medium mb-3">File Leave Request</h3>
        @if (_successMessage is not null)
        {
            <div class="bg-green-50 text-green-700 rounded p-3 mb-3 text-sm">@_successMessage</div>
        }
        @if (_error is not null)
        {
            <div class="bg-red-50 text-red-700 rounded p-3 mb-3 text-sm">@_error</div>
        }
        <div class="space-y-3">
            <div>
                <label class="text-sm font-medium">Leave Type</label>
                <select @bind="_leaveTypeId" class="w-full border rounded px-3 py-1.5 text-sm mt-1">
                    <option value="">-- Select --</option>
                    @* Leave types loaded from API *@
                </select>
            </div>
            <div class="grid grid-cols-2 gap-2">
                <div>
                    <label class="text-sm font-medium">From</label>
                    <input type="date" @bind="_startDate" class="w-full border rounded px-3 py-1.5 text-sm mt-1" />
                </div>
                <div>
                    <label class="text-sm font-medium">To</label>
                    <input type="date" @bind="_endDate" class="w-full border rounded px-3 py-1.5 text-sm mt-1" />
                </div>
            </div>
            <div>
                <label class="text-sm font-medium">Reason (optional)</label>
                <textarea @bind="_reason" class="w-full border rounded px-3 py-1.5 text-sm mt-1" rows="2"></textarea>
            </div>
            <button @onclick="SubmitLeaveRequest"
                    disabled="@_submitting"
                    class="w-full bg-blue-600 text-white py-2 rounded text-sm hover:bg-blue-700">
                @(_submitting ? "Submitting..." : "Submit Request")
            </button>
        </div>
    </div>
</div>

<!-- Leave History -->
<div class="mt-6 bg-white rounded-lg shadow p-4">
    <h3 class="font-medium mb-3">Leave History</h3>
    @if (_requests is null)
    {
        <p class="text-gray-400 text-sm">Loading...</p>
    }
    else if (!_requests.Items.Any())
    {
        <p class="text-gray-400 text-sm">No leave requests found.</p>
    }
    else
    {
        <table class="w-full text-sm">
            <thead>
                <tr class="text-left text-gray-500 border-b">
                    <th class="pb-2">Type</th>
                    <th class="pb-2">From</th>
                    <th class="pb-2">To</th>
                    <th class="pb-2 text-right">Days</th>
                    <th class="pb-2">Status</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var r in _requests.Items)
                {
                    <tr class="border-b">
                        <td class="py-2">@r.LeaveTypeName</td>
                        <td>@r.StartDate</td>
                        <td>@r.EndDate</td>
                        <td class="text-right">@r.TotalDays</td>
                        <td>
                            <span class="@GetStatusClass(r.Status) px-2 py-0.5 rounded-full text-xs">@r.Status</span>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    private IReadOnlyList<LeaveBalanceDto>? _balances;
    private PagedResult<LeaveRequestDto>? _requests;
    private string _leaveTypeId = string.Empty;
    private DateOnly _startDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _endDate = DateOnly.FromDateTime(DateTime.Today);
    private string? _reason;
    private string? _successMessage;
    private string? _error;
    private bool _submitting;

    protected override async Task OnInitializedAsync()
    {
        // Wire to current user's employee ID from auth state in real implementation
        _requests = await Api.GetLeaveRequestsAsync(page: 1, pageSize: 20);
    }

    private async Task SubmitLeaveRequest()
    {
        _submitting = true;
        _error = null;
        _successMessage = null;
        try
        {
            await Api.CreateLeaveRequestAsync(new
            {
                leaveTypeId = Guid.Parse(_leaveTypeId),
                startDate = _startDate,
                endDate = _endDate,
                reason = _reason
            });
            _successMessage = "Leave request submitted successfully.";
            _requests = await Api.GetLeaveRequestsAsync(page: 1, pageSize: 20);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally { _submitting = false; }
    }

    private static string GetStatusClass(string status) => status switch
    {
        "Approved" => "bg-green-100 text-green-700",
        "Rejected" => "bg-red-100 text-red-700",
        "Cancelled" => "bg-gray-100 text-gray-700",
        _ => "bg-yellow-100 text-yellow-700"
    };
}
```

**Step 3: My Attendance page**

```razor
@* src/PeopleCore.Web/Pages/ESS/MyAttendance.razor *@
@page "/my-attendance"
@attribute [Authorize]
@inject ApiClient Api

<h2 class="text-2xl font-semibold mb-4">My Attendance</h2>

<div class="bg-white rounded-lg shadow p-4 mb-6 flex gap-4 items-center">
    <button @onclick="TimeIn"
            disabled="@(_clockedIn || _processing)"
            class="bg-green-600 text-white px-6 py-2 rounded hover:bg-green-700 disabled:opacity-50">
        Time In
    </button>
    <button @onclick="TimeOut"
            disabled="@(!_clockedIn || _processing)"
            class="bg-red-600 text-white px-6 py-2 rounded hover:bg-red-700 disabled:opacity-50">
        Time Out
    </button>
    <span class="text-sm text-gray-500">Current time: @DateTime.Now.ToString("hh:mm tt")</span>
    @if (_message is not null)
    {
        <span class="text-sm @(_isError ? "text-red-600" : "text-green-600")">@_message</span>
    }
</div>

<div class="bg-white rounded-lg shadow p-4">
    <h3 class="font-medium mb-3">Recent Attendance</h3>
    @if (_records is null)
    {
        <p class="text-gray-400 text-sm">Loading...</p>
    }
    else
    {
        <table class="w-full text-sm">
            <thead>
                <tr class="text-left text-gray-500 border-b">
                    <th class="pb-2">Date</th>
                    <th class="pb-2">Time In</th>
                    <th class="pb-2">Time Out</th>
                    <th class="pb-2 text-right">Late (min)</th>
                    <th class="pb-2 text-right">Undertime (min)</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var r in _records.Items)
                {
                    <tr class="border-b">
                        <td class="py-2">@r.AttendanceDate</td>
                        <td>@(r.TimeIn ?? "--")</td>
                        <td>@(r.TimeOut ?? "--")</td>
                        <td class="text-right @(r.LateMinutes > 0 ? "text-red-600" : "")">@r.LateMinutes</td>
                        <td class="text-right @(r.UndertimeMinutes > 0 ? "text-orange-600" : "")">@r.UndertimeMinutes</td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    private PagedResult<AttendanceRecordDto>? _records;
    private bool _clockedIn;
    private bool _processing;
    private string? _message;
    private bool _isError;
    private Guid _employeeId; // Set from auth state

    protected override async Task OnInitializedAsync()
    {
        _records = await Api.GetAttendanceAsync(page: 1, pageSize: 20);
        _clockedIn = _records?.Items
            .FirstOrDefault(r => r.AttendanceDate == DateOnly.FromDateTime(DateTime.Today).ToString())
            ?.TimeIn != null;
    }

    private async Task TimeIn()
    {
        _processing = true;
        try
        {
            await Api.TimeInAsync(_employeeId);
            _clockedIn = true;
            _message = "Clocked in successfully.";
            _isError = false;
            _records = await Api.GetAttendanceAsync(page: 1, pageSize: 20);
        }
        catch (Exception ex) { _message = ex.Message; _isError = true; }
        finally { _processing = false; }
    }

    private async Task TimeOut()
    {
        _processing = true;
        try
        {
            await Api.TimeOutAsync(_employeeId);
            _message = "Clocked out successfully.";
            _isError = false;
            _records = await Api.GetAttendanceAsync(page: 1, pageSize: 20);
        }
        catch (Exception ex) { _message = ex.Message; _isError = true; }
        finally { _processing = false; }
    }
}
```

**Step 4: Build Blazor app**

```bash
dotnet build src/PeopleCore.Web/PeopleCore.Web.csproj
```
Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add Blazor ESS pages (dashboard, attendance, leave)"
```

---

### Task 23: Blazor — HR Manager Pages (Employees, Leave Approvals)

**Files:**
- Create: `src/PeopleCore.Web/Pages/HR/Employees.razor`
- Create: `src/PeopleCore.Web/Pages/HR/LeaveApprovals.razor`

Follow the same table/filter/action pattern as the ESS pages. Key differences:
- **Employees page:** DataGrid with search, filter by department/status, link to employee detail
- **Leave Approvals page:** List of pending leaves with Approve/Reject buttons

These pages follow the same Blazor component pattern already established. Implement using `ApiClient` methods.

**Step 1: Run full solution build**

```bash
dotnet build PeopleCore.sln
dotnet test PeopleCore.sln
```
Expected: All pass.

**Step 2: Final commit**

```bash
git add -A
git commit -m "feat: add HR manager pages and complete Blazor WASM frontend"
```

---

**Phase 9 complete. All phases done.**

## Post-Implementation Checklist

- [ ] Run `dotnet ef database update` to apply all migrations
- [ ] Seed Identity roles: Admin, HRManager, Manager, Employee, PayrollService
- [ ] Seed leave types (from schema SQL seed data)
- [ ] Seed Philippine holidays for current year
- [ ] Configure MinIO bucket (auto-created by MinioStorageService on first upload)
- [ ] Set `Jwt:Key` to a strong secret (32+ chars) in production
- [ ] Configure CORS `AllowedOrigins` with production Blazor WASM URL
- [ ] Run full test suite: `dotnet test PeopleCore.sln`
- [ ] Review Swagger UI at `/swagger` for all endpoints
