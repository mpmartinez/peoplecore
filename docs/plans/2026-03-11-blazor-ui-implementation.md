# PeopleCore Blazor WASM UI Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement all HR module pages (Dashboard, Employees, Departments, Positions, Teams, Attendance, Overtime, Holidays) in Blazor WASM using the existing shadcn-inspired component library with a Corporate Navy blue theme.

**Architecture:** Replace the default Bootstrap layout with a ShadcnSidebar-based layout. Each page uses DataGrid + Dialog pattern. API calls go through dedicated service classes injected via HttpClient. All components already exist in `Components/UI/` — pages just compose them.

**Tech Stack:** Blazor WASM .NET 10, Tailwind CSS (CDN), shadcn-inspired components (namespace `IAS.Client.Components.UI`), HttpClient for API calls, CSS custom properties for theming.

**Important:** Component namespaces are `IAS.Client.Components.UI` and `IAS.Client.Components.UI.Sidebar` — NOT `PeopleCore.Web.Components.UI`. This must be in `_Imports.razor`.

---

## Task 1: Add Tailwind CSS + Corporate Navy Theme

**Files:**
- Modify: `src/PeopleCore.Web/wwwroot/index.html`
- Modify: `src/PeopleCore.Web/wwwroot/css/app.css`
- Modify: `src/PeopleCore.Web/_Imports.razor`

**Step 1: Add Tailwind CDN + Google Fonts to index.html**

Add inside `<head>` after the existing `<meta>` tags:
```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&display=swap" rel="stylesheet" />
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css" />
<script src="https://cdn.tailwindcss.com"></script>
<script>
    tailwind.config = {
        theme: {
            extend: {
                colors: {
                    border: 'hsl(var(--border))',
                    input: 'hsl(var(--input))',
                    ring: 'hsl(var(--ring))',
                    background: 'hsl(var(--background))',
                    foreground: 'hsl(var(--foreground))',
                    primary: {
                        DEFAULT: 'hsl(var(--primary))',
                        foreground: 'hsl(var(--primary-foreground))',
                    },
                    secondary: {
                        DEFAULT: 'hsl(var(--secondary))',
                        foreground: 'hsl(var(--secondary-foreground))',
                    },
                    destructive: {
                        DEFAULT: 'hsl(var(--destructive))',
                        foreground: 'hsl(var(--destructive-foreground))',
                    },
                    muted: {
                        DEFAULT: 'hsl(var(--muted))',
                        foreground: 'hsl(var(--muted-foreground))',
                    },
                    accent: {
                        DEFAULT: 'hsl(var(--accent))',
                        foreground: 'hsl(var(--accent-foreground))',
                    },
                    card: {
                        DEFAULT: 'hsl(var(--card))',
                        foreground: 'hsl(var(--card-foreground))',
                    },
                    sidebar: {
                        DEFAULT: 'hsl(var(--sidebar-background))',
                        foreground: 'hsl(var(--sidebar-foreground))',
                        accent: 'hsl(var(--sidebar-accent))',
                        'accent-foreground': 'hsl(var(--sidebar-accent-foreground))',
                        border: 'hsl(var(--sidebar-border))',
                        ring: 'hsl(var(--sidebar-ring))',
                    },
                },
                borderRadius: {
                    lg: 'var(--radius)',
                    md: 'calc(var(--radius) - 2px)',
                    sm: 'calc(var(--radius) - 4px)',
                },
                fontFamily: {
                    sans: ['Inter', 'system-ui', 'sans-serif'],
                },
            },
        },
    }
</script>
```

**Step 2: Replace app.css content entirely**

```css
/* ============================================
   PeopleCore — Corporate Navy Theme
   ============================================ */

/* Base font */
html, body {
    font-family: 'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif;
    font-size: 14px;
}

/* ============================================
   CSS Custom Properties — Corporate Navy
   ============================================ */
:root {
    /* Primary — Navy Blue */
    --primary: 214 89% 40%;
    --primary-foreground: 0 0% 100%;

    /* Background */
    --background: 210 20% 98%;
    --foreground: 214 60% 12%;

    /* Cards */
    --card: 0 0% 100%;
    --card-foreground: 214 60% 12%;

    /* Popovers */
    --popover: 0 0% 100%;
    --popover-foreground: 214 60% 12%;

    /* Secondary */
    --secondary: 214 15% 93%;
    --secondary-foreground: 214 60% 20%;

    /* Muted */
    --muted: 214 15% 93%;
    --muted-foreground: 214 20% 45%;

    /* Accent */
    --accent: 214 89% 95%;
    --accent-foreground: 214 89% 30%;

    /* Status */
    --destructive: 0 72% 51%;
    --destructive-foreground: 0 0% 100%;

    /* Borders */
    --border: 214 20% 88%;
    --input: 214 20% 88%;
    --ring: 214 89% 40%;

    /* Border radius */
    --radius: 0.5rem;

    /* Sidebar — Dark Navy */
    --sidebar-background: 214 60% 12%;
    --sidebar-foreground: 214 15% 85%;
    --sidebar-accent: 214 50% 22%;
    --sidebar-accent-foreground: 0 0% 100%;
    --sidebar-border: 214 40% 18%;
    --sidebar-ring: 214 89% 50%;
    --sidebar-width: 250px;
    --sidebar-width-icon: 48px;
}

/* ============================================
   Layout helpers
   ============================================ */
h1:focus { outline: none; }

/* ============================================
   Shadcn component base styles
   ============================================ */

/* Table */
.table { width: 100%; border-collapse: collapse; }
.table-header th { background: hsl(var(--muted)); border-bottom: 1px solid hsl(var(--border)); }
.table-body tr:hover { background: hsl(var(--muted) / 0.5); }
.table-row { border-bottom: 1px solid hsl(var(--border)); }

/* DataGrid toolbar */
.datagrid-toolbar {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    flex-wrap: wrap;
}

/* Dashboard grid */
.dashboard-grid {
    display: grid;
    gap: 1rem;
}

/* Dashboard panel */
.dashboard-panel {
    border-radius: var(--radius);
    border: 1px solid hsl(var(--border));
    background: hsl(var(--card));
}

/* Scrollbar styling */
.scrollbar-thin::-webkit-scrollbar { width: 6px; }
.scrollbar-thumb-border::-webkit-scrollbar-thumb {
    background-color: hsl(var(--border));
    border-radius: 3px;
}
.scrollbar-track-transparent::-webkit-scrollbar-track { background: transparent; }

/* ============================================
   Blazor system styles (keep these)
   ============================================ */
.valid.modified:not([type=checkbox]) { outline: 1px solid #26b050; }
.invalid { outline: 1px solid hsl(var(--destructive)); }
.validation-message { color: hsl(var(--destructive)); }

#blazor-error-ui {
    color-scheme: light only;
    background: lightyellow;
    bottom: 0;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    box-sizing: border-box;
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}
#blazor-error-ui .dismiss {
    cursor: pointer;
    position: absolute;
    right: 0.75rem;
    top: 0.5rem;
}

.loading-progress {
    position: absolute;
    display: block;
    width: 8rem;
    height: 8rem;
    inset: 20vh 0 auto 0;
    margin: 0 auto;
}
.loading-progress circle {
    fill: none;
    stroke: hsl(var(--muted));
    stroke-width: 0.6rem;
    transform-origin: 50% 50%;
    transform: rotate(-90deg);
}
.loading-progress circle:last-child {
    stroke: hsl(var(--primary));
    stroke-dasharray: calc(3.141 * var(--blazor-load-percentage, 0%) * 0.8), 500%;
    transition: stroke-dasharray 0.05s ease-in-out;
}
.loading-progress-text {
    position: absolute;
    text-align: center;
    font-weight: bold;
    inset: calc(20vh + 3.25rem) 0 auto 0.2rem;
}
.loading-progress-text:after {
    content: var(--blazor-load-percentage-text, "Loading");
}

/* Collapsible animations */
@keyframes collapsible-down {
    from { height: 0; opacity: 0; }
    to { height: var(--radix-collapsible-content-height, auto); opacity: 1; }
}
@keyframes collapsible-up {
    from { height: var(--radix-collapsible-content-height, auto); opacity: 1; }
    to { height: 0; opacity: 0; }
}
.animate-collapsible-down { animation: collapsible-down 0.2s ease-out; }
.animate-collapsible-up { animation: collapsible-up 0.2s ease-out; }

/* Caret blink for OTP */
@keyframes caret-blink {
    0%, 70%, 100% { opacity: 1; }
    20%, 50% { opacity: 0; }
}
.animate-caret-blink { animation: caret-blink 1.2s ease-out infinite; }
```

**Step 3: Update _Imports.razor — add component namespaces**

Replace entire file:
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using PeopleCore.Web
@using PeopleCore.Web.Layout
@using PeopleCore.Web.Services
@using IAS.Client.Components.UI
@using IAS.Client.Components.UI.Sidebar
```

**Step 4: Build to verify no compile errors**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```
Expected: Build succeeded, 0 errors.

**Step 5: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/wwwroot/ src/PeopleCore.Web/_Imports.razor
git commit -m "feat: add Corporate Navy theme and Tailwind CSS to Blazor WASM"
```

---

## Task 2: Rebuild Layout Shell

**Files:**
- Rewrite: `src/PeopleCore.Web/Layout/MainLayout.razor`
- Rewrite: `src/PeopleCore.Web/Layout/NavMenu.razor`
- Delete CSS: `src/PeopleCore.Web/Layout/MainLayout.razor.css`
- Delete CSS: `src/PeopleCore.Web/Layout/NavMenu.razor.css`

**Step 1: Rewrite MainLayout.razor**

```razor
@inherits LayoutComponentBase
@inject NavigationManager Nav

<div class="flex h-screen overflow-hidden bg-background">

    <!-- Sidebar -->
    <ShadcnSidebar @ref="_sidebar"
                   IsOpen="@_sidebarOpen"
                   IsOpenChanged="@(v => _sidebarOpen = v)"
                   Collapsible="offcanvas">
        <NavMenu />
    </ShadcnSidebar>

    <!-- Mobile overlay -->
    @if (_sidebarOpen && _isMobile)
    {
        <div class="fixed inset-0 z-30 bg-black/60 lg:hidden"
             @onclick="CloseSidebar"></div>
    }

    <!-- Main content -->
    <div class="flex flex-col flex-1 overflow-hidden"
         style="margin-left: var(--sidebar-width); transition: margin-left 0.3s ease;">

        <!-- Top bar -->
        <header class="flex h-14 items-center gap-3 border-b border-border bg-card px-4 shrink-0 shadow-sm">

            <!-- Sidebar toggle -->
            <button class="inline-flex items-center justify-center rounded-md p-2
                           text-muted-foreground hover:bg-muted hover:text-foreground
                           transition-colors focus:outline-none"
                    @onclick="ToggleSidebar">
                <svg class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M4 6h16M4 12h16M4 18h16"/>
                </svg>
            </button>

            <!-- Breadcrumb slot (pages inject via CascadingValue in future) -->
            <div class="flex-1 text-sm text-muted-foreground">
                PeopleCore HR
            </div>

            <!-- User avatar -->
            <div class="flex items-center gap-2">
                <Avatar Fallback="AD" Size="sm" />
                <span class="text-sm font-medium hidden sm:block">Admin</span>
            </div>
        </header>

        <!-- Page content -->
        <main class="flex-1 overflow-y-auto p-6">
            @Body
        </main>
    </div>
</div>

@code {
    private ShadcnSidebar? _sidebar;
    private bool _sidebarOpen = true;
    private bool _isMobile = false;

    private void ToggleSidebar() => _sidebarOpen = !_sidebarOpen;
    private void CloseSidebar() => _sidebarOpen = false;
}
```

**Step 2: Rewrite NavMenu.razor**

```razor
@inject NavigationManager Nav

<ShadcnSidebarHeader>
    <div class="flex items-center gap-3 px-2 py-3">
        <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-primary">
            <svg class="h-5 w-5 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                <path stroke-linecap="round" stroke-linejoin="round"
                      d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
        </div>
        <div>
            <p class="text-sm font-semibold text-sidebar-foreground leading-none">PeopleCore</p>
            <p class="text-xs text-sidebar-foreground/60 leading-none mt-0.5">HR Management</p>
        </div>
    </div>
</ShadcnSidebarHeader>

<ShadcnSidebarContent>

    <!-- Overview -->
    <ShadcnSidebarGroup>
        <ShadcnSidebarGroupLabel>Overview</ShadcnSidebarGroupLabel>
        <ShadcnSidebarGroupContent>
            <ShadcnSidebarMenu>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/" TooltipContent="Dashboard">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/>
                            <rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>
                        </svg>
                        Dashboard
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
            </ShadcnSidebarMenu>
        </ShadcnSidebarGroupContent>
    </ShadcnSidebarGroup>

    <ShadcnSidebarSeparator />

    <!-- Organization -->
    <ShadcnSidebarGroup>
        <ShadcnSidebarGroupLabel>Organization</ShadcnSidebarGroupLabel>
        <ShadcnSidebarGroupContent>
            <ShadcnSidebarMenu>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/departments" TooltipContent="Departments">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4"/>
                        </svg>
                        Departments
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/positions" TooltipContent="Positions">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M21 13.255A23.931 23.931 0 0112 15c-3.183 0-6.22-.62-9-1.745M16 6V4a2 2 0 00-2-2h-4a2 2 0 00-2 2v2m4 6h.01M5 20h14a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"/>
                        </svg>
                        Positions
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/teams" TooltipContent="Teams">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z"/>
                        </svg>
                        Teams
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
            </ShadcnSidebarMenu>
        </ShadcnSidebarGroupContent>
    </ShadcnSidebarGroup>

    <ShadcnSidebarSeparator />

    <!-- Workforce -->
    <ShadcnSidebarGroup>
        <ShadcnSidebarGroupLabel>Workforce</ShadcnSidebarGroupLabel>
        <ShadcnSidebarGroupContent>
            <ShadcnSidebarMenu>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/employees" TooltipContent="Employees">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"/>
                        </svg>
                        Employees
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
            </ShadcnSidebarMenu>
        </ShadcnSidebarGroupContent>
    </ShadcnSidebarGroup>

    <ShadcnSidebarSeparator />

    <!-- Time & Attendance -->
    <ShadcnSidebarGroup>
        <ShadcnSidebarGroupLabel>Time &amp; Attendance</ShadcnSidebarGroupLabel>
        <ShadcnSidebarGroupContent>
            <ShadcnSidebarMenu>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/attendance" TooltipContent="Attendance">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"/>
                        </svg>
                        Attendance
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/overtime" TooltipContent="Overtime">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z"/>
                        </svg>
                        Overtime
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
                <ShadcnSidebarMenuItem>
                    <ShadcnSidebarMenuButton Href="/holidays" TooltipContent="Holidays">
                        <svg slot="icon" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"/>
                        </svg>
                        Holidays
                    </ShadcnSidebarMenuButton>
                </ShadcnSidebarMenuItem>
            </ShadcnSidebarMenu>
        </ShadcnSidebarGroupContent>
    </ShadcnSidebarGroup>

</ShadcnSidebarContent>

<ShadcnSidebarFooter>
    <div class="flex items-center gap-3 px-2 py-3 border-t border-sidebar-border">
        <Avatar Fallback="AD" Size="sm" />
        <div class="flex-1 min-w-0">
            <p class="text-sm font-medium text-sidebar-foreground truncate">Admin User</p>
            <p class="text-xs text-sidebar-foreground/60 truncate">admin@peoplecore.com</p>
        </div>
    </div>
</ShadcnSidebarFooter>
```

**Step 3: Delete old CSS files**
```bash
rm "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/Layout/MainLayout.razor.css"
rm "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/Layout/NavMenu.razor.css"
```

**Step 4: Build**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```
Expected: Build succeeded, 0 errors.

**Step 5: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Layout/
git commit -m "feat: rebuild layout with ShadcnSidebar and grouped navigation"
```

---

## Task 3: API Service Classes

**Files:**
- Create: `src/PeopleCore.Web/Services/OrganizationApiService.cs`
- Create: `src/PeopleCore.Web/Services/EmployeeApiService.cs`
- Create: `src/PeopleCore.Web/Services/AttendanceApiService.cs`
- Create: `src/PeopleCore.Web/Services/Models/ApiModels.cs`
- Modify: `src/PeopleCore.Web/Program.cs`

**Step 1: Create shared API models**

File: `src/PeopleCore.Web/Services/Models/ApiModels.cs`
```csharp
namespace PeopleCore.Web.Services.Models;

// Shared
public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

// Organization
public record DepartmentDto(Guid Id, string Name, string? Description, int EmployeeCount);
public record CreateDepartmentDto(string Name, string? Description);
public record UpdateDepartmentDto(string Name, string? Description);

public record PositionDto(Guid Id, string Title, Guid? DepartmentId, string? DepartmentName, int EmployeeCount);
public record CreatePositionDto(string Title, Guid? DepartmentId);
public record UpdatePositionDto(string Title, Guid? DepartmentId);

public record TeamDto(Guid Id, string Name, Guid? DepartmentId, string? DepartmentName, int MemberCount);
public record CreateTeamDto(string Name, Guid? DepartmentId);
public record UpdateTeamDto(string Name, Guid? DepartmentId);

// Employees
public record EmployeeDto(
    Guid Id, string EmployeeNumber, string FirstName, string LastName,
    string? MiddleName, string FullName, string? DepartmentName, string? PositionTitle,
    string? TeamName, string WorkEmail, string? PhoneNumber, string Gender,
    DateOnly DateOfBirth, DateOnly HireDate, string EmploymentType, bool IsActive,
    Guid? DepartmentId, Guid? PositionId, Guid? TeamId);

public record CreateEmployeeDto(
    string FirstName, string LastName, string? MiddleName,
    string Gender, DateOnly DateOfBirth, string WorkEmail, string? PhoneNumber,
    DateOnly HireDate, string EmploymentType,
    Guid? DepartmentId, Guid? PositionId, Guid? TeamId);

public record UpdateEmployeeDto(
    string FirstName, string LastName, string? MiddleName,
    string Gender, DateOnly DateOfBirth, string WorkEmail, string? PhoneNumber,
    DateOnly HireDate, string EmploymentType, bool IsActive,
    Guid? DepartmentId, Guid? PositionId, Guid? TeamId);

// Attendance
public record AttendanceRecordDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    DateOnly AttendanceDate, DateTime? TimeIn, DateTime? TimeOut,
    int LateMinutes, int UndertimeMinutes, int OvertimeMinutes,
    bool IsPresent, bool IsHoliday, string? Remarks);

public record TimeInRequest(Guid EmployeeId, DateTime TimeIn);
public record TimeOutRequest(Guid EmployeeId, DateTime TimeOut);

public record OvertimeRequestDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    DateOnly OvertimeDate, DateTime StartTime, DateTime EndTime,
    int TotalMinutes, string Reason, string Status,
    Guid? ApprovedBy, DateTime? ApprovedAt, string? RejectionReason);

public record CreateOvertimeRequestDto(
    Guid EmployeeId, DateOnly OvertimeDate,
    DateTime StartTime, DateTime EndTime, string Reason);

public record ApproveOvertimeDto(Guid ApproverId);
public record RejectOvertimeDto(string RejectionReason);

public record HolidayDto(Guid Id, string Name, DateOnly HolidayDate, string HolidayType, bool IsRecurring);
public record CreateHolidayDto(string Name, DateOnly HolidayDate, string HolidayType, bool IsRecurring);
```

**Step 2: Create OrganizationApiService**

File: `src/PeopleCore.Web/Services/OrganizationApiService.cs`
```csharp
using PeopleCore.Web.Services.Models;

namespace PeopleCore.Web.Services;

public class OrganizationApiService(HttpClient http)
{
    // Departments
    public Task<PagedResult<DepartmentDto>?> GetDepartmentsAsync(int page = 1, int size = 100)
        => http.GetFromJsonAsync<PagedResult<DepartmentDto>>($"api/departments?page={page}&pageSize={size}");

    public Task<DepartmentDto?> CreateDepartmentAsync(CreateDepartmentDto dto)
        => PostAsync<DepartmentDto>("api/departments", dto);

    public Task<DepartmentDto?> UpdateDepartmentAsync(Guid id, UpdateDepartmentDto dto)
        => PutAsync<DepartmentDto>($"api/departments/{id}", dto);

    public Task DeleteDepartmentAsync(Guid id)
        => http.DeleteAsync($"api/departments/{id}");

    // Positions
    public Task<PagedResult<PositionDto>?> GetPositionsAsync(int page = 1, int size = 100)
        => http.GetFromJsonAsync<PagedResult<PositionDto>>($"api/positions?page={page}&pageSize={size}");

    public Task<PositionDto?> CreatePositionAsync(CreatePositionDto dto)
        => PostAsync<PositionDto>("api/positions", dto);

    public Task<PositionDto?> UpdatePositionAsync(Guid id, UpdatePositionDto dto)
        => PutAsync<PositionDto>($"api/positions/{id}", dto);

    // Teams
    public Task<PagedResult<TeamDto>?> GetTeamsAsync(int page = 1, int size = 100)
        => http.GetFromJsonAsync<PagedResult<TeamDto>>($"api/teams?page={page}&pageSize={size}");

    public Task<TeamDto?> CreateTeamAsync(CreateTeamDto dto)
        => PostAsync<TeamDto>("api/teams", dto);

    public Task<TeamDto?> UpdateTeamAsync(Guid id, UpdateTeamDto dto)
        => PutAsync<TeamDto>($"api/teams/{id}", dto);

    // Helpers
    private async Task<T?> PostAsync<T>(string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private async Task<T?> PutAsync<T>(string url, object body)
    {
        var response = await http.PutAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }
}
```

**Step 3: Create EmployeeApiService**

File: `src/PeopleCore.Web/Services/EmployeeApiService.cs`
```csharp
using PeopleCore.Web.Services.Models;

namespace PeopleCore.Web.Services;

public class EmployeeApiService(HttpClient http)
{
    public Task<PagedResult<EmployeeDto>?> GetAllAsync(int page = 1, int size = 20, string? search = null)
    {
        var url = $"api/employees?page={page}&pageSize={size}";
        if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return http.GetFromJsonAsync<PagedResult<EmployeeDto>>(url);
    }

    public Task<EmployeeDto?> GetByIdAsync(Guid id)
        => http.GetFromJsonAsync<EmployeeDto>($"api/employees/{id}");

    public async Task<EmployeeDto?> CreateAsync(CreateEmployeeDto dto)
    {
        var response = await http.PostAsJsonAsync("api/employees", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeDto>();
    }

    public async Task<EmployeeDto?> UpdateAsync(Guid id, UpdateEmployeeDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/employees/{id}", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeDto>();
    }
}
```

**Step 4: Create AttendanceApiService**

File: `src/PeopleCore.Web/Services/AttendanceApiService.cs`
```csharp
using PeopleCore.Web.Services.Models;

namespace PeopleCore.Web.Services;

public class AttendanceApiService(HttpClient http)
{
    // Attendance
    public Task<PagedResult<AttendanceRecordDto>?> GetAttendanceAsync(
        Guid? employeeId = null, DateOnly? from = null, DateOnly? to = null,
        int page = 1, int size = 20)
    {
        var url = $"api/attendance?page={page}&pageSize={size}";
        if (employeeId.HasValue) url += $"&employeeId={employeeId}";
        if (from.HasValue) url += $"&from={from:yyyy-MM-dd}";
        if (to.HasValue) url += $"&to={to:yyyy-MM-dd}";
        return http.GetFromJsonAsync<PagedResult<AttendanceRecordDto>>(url);
    }

    public async Task<AttendanceRecordDto?> TimeInAsync(TimeInRequest request)
    {
        var response = await http.PostAsJsonAsync("api/attendance/time-in", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>();
    }

    public async Task<AttendanceRecordDto?> TimeOutAsync(TimeOutRequest request)
    {
        var response = await http.PostAsJsonAsync("api/attendance/time-out", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AttendanceRecordDto>();
    }

    // Overtime
    public Task<PagedResult<OvertimeRequestDto>?> GetOvertimeAsync(
        Guid? employeeId = null, string? status = null, int page = 1, int size = 20)
    {
        var url = $"api/overtime-requests?page={page}&pageSize={size}";
        if (employeeId.HasValue) url += $"&employeeId={employeeId}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return http.GetFromJsonAsync<PagedResult<OvertimeRequestDto>>(url);
    }

    public async Task<OvertimeRequestDto?> ApproveOvertimeAsync(Guid id, ApproveOvertimeDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/overtime-requests/{id}/approve", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OvertimeRequestDto>();
    }

    public async Task<OvertimeRequestDto?> RejectOvertimeAsync(Guid id, RejectOvertimeDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/overtime-requests/{id}/reject", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OvertimeRequestDto>();
    }

    // Holidays
    public Task<List<HolidayDto>?> GetHolidaysAsync(int year)
        => http.GetFromJsonAsync<List<HolidayDto>>($"api/holidays?year={year}");

    public async Task<HolidayDto?> CreateHolidayAsync(CreateHolidayDto dto)
    {
        var response = await http.PostAsJsonAsync("api/holidays", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HolidayDto>();
    }

    public Task DeleteHolidayAsync(Guid id)
        => http.DeleteAsync($"api/holidays/{id}");
}
```

**Step 5: Register services in Program.cs**

Read `src/PeopleCore.Web/Program.cs` then add before `await builder.Build().RunAsync()`:
```csharp
// API base URL — update to your actual API URL
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri("https://localhost:7001/") });

builder.Services.AddScoped<OrganizationApiService>();
builder.Services.AddScoped<EmployeeApiService>();
builder.Services.AddScoped<AttendanceApiService>();
```

Also add to `_Imports.razor`:
```razor
@using PeopleCore.Web.Services.Models
```

**Step 6: Build**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```
Expected: Build succeeded, 0 errors.

**Step 7: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Services/ src/PeopleCore.Web/Program.cs src/PeopleCore.Web/_Imports.razor
git commit -m "feat: add API service classes for Organization, Employees, Attendance"
```

---

## Task 4: Dashboard Page

**Files:**
- Rewrite: `src/PeopleCore.Web/Pages/Home.razor` (route `/`)

**Step 1: Replace Home.razor**

```razor
@page "/"
@inject EmployeeApiService EmployeeSvc
@inject AttendanceApiService AttendanceSvc

<PageTitle>Dashboard — PeopleCore</PageTitle>

<!-- Page header -->
<div class="mb-6">
    <h1 class="text-2xl font-bold text-foreground">Dashboard</h1>
    <p class="text-sm text-muted-foreground mt-1">Welcome back. Here's what's happening today.</p>
</div>

<!-- Stat cards -->
<div class="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4 mb-6">
    <DashboardStatCard Label="Total Employees"
                       Value="@_totalEmployees.ToString()"
                       FooterSecondary="Active headcount" />
    <DashboardStatCard Label="Present Today"
                       Value="@_presentToday.ToString()"
                       FooterSecondary="Clocked in today" />
    <DashboardStatCard Label="On Leave"
                       Value="@_onLeave.ToString()"
                       FooterSecondary="Away today" />
    <DashboardStatCard Label="Overtime Pending"
                       Value="@_overtimePending.ToString()"
                       FooterSecondary="Awaiting approval" />
</div>

<!-- Charts row -->
<div class="grid grid-cols-1 lg:grid-cols-3 gap-4 mb-6">
    <div class="lg:col-span-2">
        <BarChart Title="Attendance This Week"
                  Description="Daily present count Mon–Fri"
                  Labels="@_weekLabels"
                  Datasets="@_weekDatasets" />
    </div>
    <div>
        <PieChart Title="By Department"
                  Description="Headcount distribution"
                  Labels="@_deptLabels"
                  Data="@_deptData" />
    </div>
</div>

<!-- Recent hires -->
<Card>
    <CardHeader>
        <CardTitle>Recent Hires</CardTitle>
        <CardDescription>Last 5 employees added</CardDescription>
    </CardHeader>
    <CardContent>
        @if (_loading)
        {
            <div class="flex justify-center py-8"><Spinner /></div>
        }
        else
        {
            <Table>
                <TableHead>
                    <TableRow>
                        <TableHeader>Employee</TableHeader>
                        <TableHeader>Department</TableHeader>
                        <TableHeader>Position</TableHeader>
                        <TableHeader>Hire Date</TableHeader>
                        <TableHeader>Status</TableHeader>
                    </TableRow>
                </TableHead>
                <TableBody>
                    @foreach (var emp in _recentHires)
                    {
                        <TableRow>
                            <TableCell>
                                <div class="flex items-center gap-2">
                                    <Avatar Fallback="@GetInitials(emp.FullName)" Size="sm" />
                                    <div>
                                        <p class="font-medium text-sm">@emp.FullName</p>
                                        <p class="text-xs text-muted-foreground">@emp.EmployeeNumber</p>
                                    </div>
                                </div>
                            </TableCell>
                            <TableCell>@(emp.DepartmentName ?? "—")</TableCell>
                            <TableCell>@(emp.PositionTitle ?? "—")</TableCell>
                            <TableCell>@emp.HireDate.ToString("MMM d, yyyy")</TableCell>
                            <TableCell>
                                <Badge Variant="@(emp.IsActive ? "success" : "secondary")">
                                    @(emp.IsActive ? "Active" : "Inactive")
                                </Badge>
                            </TableCell>
                        </TableRow>
                    }
                </TableBody>
            </Table>
        }
    </CardContent>
</Card>

@code {
    private bool _loading = true;
    private int _totalEmployees, _presentToday, _onLeave, _overtimePending;
    private List<EmployeeDto> _recentHires = [];

    // Chart data
    private string[] _weekLabels = ["Mon", "Tue", "Wed", "Thu", "Fri"];
    private List<ChartDataset> _weekDatasets = [];
    private string[] _deptLabels = [];
    private double[] _deptData = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var employees = await EmployeeSvc.GetAllAsync(page: 1, size: 100);
            _totalEmployees = employees?.TotalCount ?? 0;
            _recentHires = employees?.Items
                .OrderByDescending(e => e.HireDate)
                .Take(5)
                .ToList() ?? [];

            // Department breakdown for pie chart
            var deptGroups = _recentHires
                .GroupBy(e => e.DepartmentName ?? "Unassigned")
                .ToList();
            _deptLabels = deptGroups.Select(g => g.Key).ToArray();
            _deptData = deptGroups.Select(g => (double)g.Count()).ToArray();

            // Overtime pending
            var overtime = await AttendanceSvc.GetOvertimeAsync(status: "Pending", size: 100);
            _overtimePending = overtime?.TotalCount ?? 0;

            // Mock attendance this week (real data would come from attendance API)
            _weekDatasets =
            [
                new ChartDataset
                {
                    Label = "Present",
                    Data = [42, 45, 43, 47, 40],
                    BackgroundColor = "hsl(214, 89%, 40%)",
                    BorderColor = "hsl(214, 89%, 35%)"
                }
            ];

            _presentToday = 42;
            _onLeave = 3;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}"
            : name.Length > 0 ? name[0].ToString() : "?";
    }
}
```

**Step 2: Check ChartDataset type exists**

The `BarChart` and `PieChart` components reference `ChartDataset`. Verify it exists:
```bash
grep -r "ChartDataset" "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/Components/" --include="*.razor" --include="*.cs" -l
```

If it's in a different namespace, add that `@using` to `_Imports.razor`.

**Step 3: Build**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```
Expected: Build succeeded, 0 errors.

**Step 4: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Pages/Home.razor
git commit -m "feat: add Dashboard page with stat cards and charts"
```

---

## Task 5: Employees Page

**Files:**
- Create: `src/PeopleCore.Web/Pages/Employees/EmployeesPage.razor`

**Step 1: Create EmployeesPage.razor**

```razor
@page "/employees"
@inject EmployeeApiService EmployeeSvc
@inject OrganizationApiService OrgSvc

<PageTitle>Employees — PeopleCore</PageTitle>

<!-- Header -->
<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Employees</h1>
        <p class="text-sm text-muted-foreground mt-1">Manage your workforce</p>
    </div>
    <Button Variant="primary" OnClick="OpenAddDialog">
        <svg class="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4"/>
        </svg>
        Add Employee
    </Button>
</div>

<!-- DataGrid -->
<DataGrid TItem="EmployeeDto"
          Items="@_filtered"
          Loading="@_loading"
          ShowToolbar ShowSearch ShowPagination PageSize="15"
          OnSearch="OnSearch">
    <HeaderTemplate>
        <TableHeader>Employee</TableHeader>
        <TableHeader>Emp #</TableHeader>
        <TableHeader>Department</TableHeader>
        <TableHeader>Position</TableHeader>
        <TableHeader>Type</TableHeader>
        <TableHeader>Status</TableHeader>
        <TableHeader></TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="emp">
        <TableCell>
            <div class="flex items-center gap-3">
                <Avatar Fallback="@GetInitials(emp.FullName)" Size="sm" />
                <div>
                    <p class="font-medium text-sm">@emp.FullName</p>
                    <p class="text-xs text-muted-foreground">@emp.WorkEmail</p>
                </div>
            </div>
        </TableCell>
        <TableCell class="text-sm text-muted-foreground">@emp.EmployeeNumber</TableCell>
        <TableCell class="text-sm">@(emp.DepartmentName ?? "—")</TableCell>
        <TableCell class="text-sm">@(emp.PositionTitle ?? "—")</TableCell>
        <TableCell><Badge Variant="secondary">@emp.EmploymentType</Badge></TableCell>
        <TableCell>
            <Badge Variant="@(emp.IsActive ? "success" : "outline")">
                @(emp.IsActive ? "Active" : "Inactive")
            </Badge>
        </TableCell>
        <TableCell>
            <div class="flex items-center gap-1">
                <Button Size="icon-sm" Variant="ghost" Title="View" OnClick="() => OpenViewDialog(emp)">
                    <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/>
                        <path stroke-linecap="round" stroke-linejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"/>
                    </svg>
                </Button>
                <Button Size="icon-sm" Variant="ghost" Title="Edit" OnClick="() => OpenEditDialog(emp)">
                    <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"/>
                    </svg>
                </Button>
            </div>
        </TableCell>
    </RowTemplate>
</DataGrid>

<!-- View Dialog -->
<Dialog IsOpen="@_showView" IsOpenChanged="@(v => _showView = v)" OnClose="CloseDialogs"
        Title="Employee Details" Size="lg">
    <ChildContent>
        @if (_selected != null)
        {
            <div class="flex items-center gap-4 mb-6 pb-4 border-b border-border">
                <Avatar Fallback="@GetInitials(_selected.FullName)" Size="xl" />
                <div>
                    <h3 class="text-lg font-semibold">@_selected.FullName</h3>
                    <p class="text-sm text-muted-foreground">@_selected.EmployeeNumber</p>
                    <Badge Variant="@(_selected.IsActive ? "success" : "secondary")" Class="mt-1">
                        @(_selected.IsActive ? "Active" : "Inactive")
                    </Badge>
                </div>
            </div>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-6">
                <InfoRow Label="Work Email" Value="@_selected.WorkEmail" />
                <InfoRow Label="Phone" Value="@(_selected.PhoneNumber ?? "—")" />
                <InfoRow Label="Department" Value="@(_selected.DepartmentName ?? "—")" />
                <InfoRow Label="Position" Value="@(_selected.PositionTitle ?? "—")" />
                <InfoRow Label="Team" Value="@(_selected.TeamName ?? "—")" />
                <InfoRow Label="Employment Type" Value="@_selected.EmploymentType" />
                <InfoRow Label="Hire Date" Value="@_selected.HireDate.ToString("MMMM d, yyyy")" />
                <InfoRow Label="Date of Birth" Value="@_selected.DateOfBirth.ToString("MMMM d, yyyy")" />
                <InfoRow Label="Gender" Value="@_selected.Gender" />
            </div>
        }
    </ChildContent>
</Dialog>

<!-- Add/Edit Dialog -->
<Dialog IsOpen="@_showForm" IsOpenChanged="@(v => _showForm = v)" OnClose="CloseDialogs"
        Title="@(_editId.HasValue ? "Edit Employee" : "Add Employee")" Size="xl">
    <ChildContent>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField LabelText="First Name" Required>
                <Input @bind-Value="_form.FirstName" Placeholder="First name" />
            </FormField>
            <FormField LabelText="Last Name" Required>
                <Input @bind-Value="_form.LastName" Placeholder="Last name" />
            </FormField>
            <FormField LabelText="Middle Name">
                <Input @bind-Value="_form.MiddleName" Placeholder="Middle name (optional)" />
            </FormField>
            <FormField LabelText="Gender" Required>
                <Select @bind-Value="_form.Gender">
                    <option value="">Select gender</option>
                    <option value="Male">Male</option>
                    <option value="Female">Female</option>
                    <option value="Other">Other</option>
                </Select>
            </FormField>
            <FormField LabelText="Work Email" Required>
                <Input @bind-Value="_form.WorkEmail" Type="email" Placeholder="name@company.com" />
            </FormField>
            <FormField LabelText="Phone Number">
                <Input @bind-Value="_form.PhoneNumber" Placeholder="+63 9XX XXX XXXX" />
            </FormField>
            <FormField LabelText="Department">
                <Select @bind-Value="_form.DepartmentIdStr">
                    <option value="">Select department</option>
                    @foreach (var dept in _departments)
                    {
                        <option value="@dept.Id">@dept.Name</option>
                    }
                </Select>
            </FormField>
            <FormField LabelText="Position">
                <Select @bind-Value="_form.PositionIdStr">
                    <option value="">Select position</option>
                    @foreach (var pos in _positions)
                    {
                        <option value="@pos.Id">@pos.Title</option>
                    }
                </Select>
            </FormField>
            <FormField LabelText="Employment Type" Required>
                <Select @bind-Value="_form.EmploymentType">
                    <option value="">Select type</option>
                    <option value="FullTime">Full Time</option>
                    <option value="PartTime">Part Time</option>
                    <option value="Contract">Contract</option>
                    <option value="Probationary">Probationary</option>
                </Select>
            </FormField>
            <FormField LabelText="Hire Date" Required>
                <Input @bind-Value="_form.HireDateStr" Type="date" />
            </FormField>
            <FormField LabelText="Date of Birth" Required>
                <Input @bind-Value="_form.DateOfBirthStr" Type="date" />
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-4">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="CloseDialogs">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="SaveEmployee">
                @(_editId.HasValue ? "Update" : "Add") Employee
            </Button>
        </div>
    </FooterContent>
</Dialog>

@code {
    private bool _loading = true, _showView, _showForm, _saving;
    private string? _error;
    private Guid? _editId;
    private EmployeeDto? _selected;
    private List<EmployeeDto> _all = [], _filtered = [];
    private List<DepartmentDto> _departments = [];
    private List<PositionDto> _positions = [];
    private EmployeeForm _form = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        var depts = await OrgSvc.GetDepartmentsAsync();
        _departments = depts?.Items ?? [];
        var positions = await OrgSvc.GetPositionsAsync();
        _positions = positions?.Items ?? [];
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var result = await EmployeeSvc.GetAllAsync(size: 200);
        _all = result?.Items ?? [];
        _filtered = [.._all];
        _loading = false;
    }

    private void OnSearch(string term)
    {
        _filtered = string.IsNullOrWhiteSpace(term)
            ? [.._all]
            : _all.Where(e =>
                e.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.EmployeeNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (e.DepartmentName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    private void OpenAddDialog()
    {
        _editId = null;
        _form = new();
        _error = null;
        _showForm = true;
    }

    private void OpenEditDialog(EmployeeDto emp)
    {
        _editId = emp.Id;
        _form = new EmployeeForm
        {
            FirstName = emp.FirstName, LastName = emp.LastName, MiddleName = emp.MiddleName,
            Gender = emp.Gender, WorkEmail = emp.WorkEmail, PhoneNumber = emp.PhoneNumber,
            EmploymentType = emp.EmploymentType,
            HireDateStr = emp.HireDate.ToString("yyyy-MM-dd"),
            DateOfBirthStr = emp.DateOfBirth.ToString("yyyy-MM-dd"),
            DepartmentIdStr = emp.DepartmentId?.ToString() ?? "",
            PositionIdStr = emp.PositionId?.ToString() ?? ""
        };
        _error = null;
        _showForm = true;
    }

    private void OpenViewDialog(EmployeeDto emp) { _selected = emp; _showView = true; }
    private void CloseDialogs() { _showView = _showForm = false; _selected = null; }

    private async Task SaveEmployee()
    {
        _saving = true;
        _error = null;
        try
        {
            var deptId = Guid.TryParse(_form.DepartmentIdStr, out var d) ? d : (Guid?)null;
            var posId = Guid.TryParse(_form.PositionIdStr, out var p) ? p : (Guid?)null;
            var hireDate = DateOnly.Parse(_form.HireDateStr);
            var dob = DateOnly.Parse(_form.DateOfBirthStr);

            if (_editId.HasValue)
            {
                await EmployeeSvc.UpdateAsync(_editId.Value, new UpdateEmployeeDto(
                    _form.FirstName, _form.LastName, _form.MiddleName,
                    _form.Gender, dob, _form.WorkEmail, _form.PhoneNumber,
                    hireDate, _form.EmploymentType, true, deptId, posId, null));
            }
            else
            {
                await EmployeeSvc.CreateAsync(new CreateEmployeeDto(
                    _form.FirstName, _form.LastName, _form.MiddleName,
                    _form.Gender, dob, _form.WorkEmail, _form.PhoneNumber,
                    hireDate, _form.EmploymentType, deptId, posId, null));
            }
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally { _saving = false; }
    }

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[^1][0]}" : name.Length > 0 ? name[0].ToString() : "?";
    }

    private class EmployeeForm
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string Gender { get; set; } = "";
        public string WorkEmail { get; set; } = "";
        public string? PhoneNumber { get; set; }
        public string EmploymentType { get; set; } = "";
        public string HireDateStr { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        public string DateOfBirthStr { get; set; } = "1990-01-01";
        public string DepartmentIdStr { get; set; } = "";
        public string PositionIdStr { get; set; } = "";
    }
}
```

**Step 2: Build**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```

**Step 3: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Pages/Employees/
git commit -m "feat: add Employees page with DataGrid, view and add/edit dialogs"
```

---

## Task 6: Departments, Positions, Teams Pages

**Files:**
- Create: `src/PeopleCore.Web/Pages/Organization/DepartmentsPage.razor`
- Create: `src/PeopleCore.Web/Pages/Organization/PositionsPage.razor`
- Create: `src/PeopleCore.Web/Pages/Organization/TeamsPage.razor`

**Step 1: Create DepartmentsPage.razor**

```razor
@page "/departments"
@inject OrganizationApiService OrgSvc

<PageTitle>Departments — PeopleCore</PageTitle>

<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Departments</h1>
        <p class="text-sm text-muted-foreground mt-1">Manage organizational departments</p>
    </div>
    <Button Variant="primary" OnClick="OpenAdd">
        <svg class="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4"/>
        </svg>
        Add Department
    </Button>
</div>

<DataGrid TItem="DepartmentDto" Items="@_filtered" Loading="@_loading"
          ShowToolbar ShowSearch ShowPagination PageSize="15" OnSearch="OnSearch">
    <HeaderTemplate>
        <TableHeader>Name</TableHeader>
        <TableHeader>Description</TableHeader>
        <TableHeader>Employees</TableHeader>
        <TableHeader></TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="dept">
        <TableCell class="font-medium">@dept.Name</TableCell>
        <TableCell class="text-sm text-muted-foreground">@(dept.Description ?? "—")</TableCell>
        <TableCell>
            <Badge Variant="secondary">@dept.EmployeeCount</Badge>
        </TableCell>
        <TableCell>
            <Button Size="icon-sm" Variant="ghost" OnClick="() => OpenEdit(dept)">
                <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"/>
                </svg>
            </Button>
        </TableCell>
    </RowTemplate>
</DataGrid>

<Dialog IsOpen="@_showForm" IsOpenChanged="@(v => _showForm = v)" OnClose="Close"
        Title="@(_editId.HasValue ? "Edit Department" : "Add Department")" Size="md">
    <ChildContent>
        <div class="space-y-4">
            <FormField LabelText="Name" Required>
                <Input @bind-Value="_name" Placeholder="Department name" />
            </FormField>
            <FormField LabelText="Description">
                <Input @bind-Value="_description" Placeholder="Optional description" />
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="Close">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="Save">Save</Button>
        </div>
    </FooterContent>
</Dialog>

@code {
    private bool _loading = true, _showForm, _saving;
    private string? _error;
    private Guid? _editId;
    private string _name = "", _description = "";
    private List<DepartmentDto> _all = [], _filtered = [];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        var result = await OrgSvc.GetDepartmentsAsync();
        _all = result?.Items ?? [];
        _filtered = [.._all];
        _loading = false;
    }

    private void OnSearch(string term) =>
        _filtered = string.IsNullOrWhiteSpace(term) ? [.._all]
            : _all.Where(d => d.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

    private void OpenAdd() { _editId = null; _name = ""; _description = ""; _error = null; _showForm = true; }

    private void OpenEdit(DepartmentDto d)
    {
        _editId = d.Id;
        _name = d.Name;
        _description = d.Description ?? "";
        _error = null;
        _showForm = true;
    }

    private void Close() => _showForm = false;

    private async Task Save()
    {
        _saving = true; _error = null;
        try
        {
            if (_editId.HasValue)
                await OrgSvc.UpdateDepartmentAsync(_editId.Value, new UpdateDepartmentDto(_name, _description));
            else
                await OrgSvc.CreateDepartmentAsync(new CreateDepartmentDto(_name, _description));
            Close();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

**Step 2: Create PositionsPage.razor**

```razor
@page "/positions"
@inject OrganizationApiService OrgSvc

<PageTitle>Positions — PeopleCore</PageTitle>

<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Positions</h1>
        <p class="text-sm text-muted-foreground mt-1">Manage job positions</p>
    </div>
    <Button Variant="primary" OnClick="OpenAdd">
        <svg class="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4"/>
        </svg>
        Add Position
    </Button>
</div>

<DataGrid TItem="PositionDto" Items="@_filtered" Loading="@_loading"
          ShowToolbar ShowSearch ShowPagination PageSize="15" OnSearch="OnSearch">
    <HeaderTemplate>
        <TableHeader>Title</TableHeader>
        <TableHeader>Department</TableHeader>
        <TableHeader>Employees</TableHeader>
        <TableHeader></TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="pos">
        <TableCell class="font-medium">@pos.Title</TableCell>
        <TableCell class="text-sm text-muted-foreground">@(pos.DepartmentName ?? "—")</TableCell>
        <TableCell><Badge Variant="secondary">@pos.EmployeeCount</Badge></TableCell>
        <TableCell>
            <Button Size="icon-sm" Variant="ghost" OnClick="() => OpenEdit(pos)">
                <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"/>
                </svg>
            </Button>
        </TableCell>
    </RowTemplate>
</DataGrid>

<Dialog IsOpen="@_showForm" IsOpenChanged="@(v => _showForm = v)" OnClose="Close"
        Title="@(_editId.HasValue ? "Edit Position" : "Add Position")" Size="md">
    <ChildContent>
        <div class="space-y-4">
            <FormField LabelText="Title" Required>
                <Input @bind-Value="_title" Placeholder="Position title" />
            </FormField>
            <FormField LabelText="Department">
                <Select @bind-Value="_departmentIdStr">
                    <option value="">No department</option>
                    @foreach (var d in _departments)
                    {
                        <option value="@d.Id">@d.Name</option>
                    }
                </Select>
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="Close">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="Save">Save</Button>
        </div>
    </FooterContent>
</Dialog>

@code {
    private bool _loading = true, _showForm, _saving;
    private string? _error;
    private Guid? _editId;
    private string _title = "", _departmentIdStr = "";
    private List<PositionDto> _all = [], _filtered = [];
    private List<DepartmentDto> _departments = [];

    protected override async Task OnInitializedAsync()
    {
        var depts = await OrgSvc.GetDepartmentsAsync();
        _departments = depts?.Items ?? [];
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var result = await OrgSvc.GetPositionsAsync();
        _all = result?.Items ?? [];
        _filtered = [.._all];
        _loading = false;
    }

    private void OnSearch(string term) =>
        _filtered = string.IsNullOrWhiteSpace(term) ? [.._all]
            : _all.Where(p => p.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

    private void OpenAdd() { _editId = null; _title = ""; _departmentIdStr = ""; _error = null; _showForm = true; }

    private void OpenEdit(PositionDto p)
    {
        _editId = p.Id; _title = p.Title;
        _departmentIdStr = p.DepartmentId?.ToString() ?? "";
        _error = null; _showForm = true;
    }

    private void Close() => _showForm = false;

    private async Task Save()
    {
        _saving = true; _error = null;
        try
        {
            var deptId = Guid.TryParse(_departmentIdStr, out var d) ? d : (Guid?)null;
            if (_editId.HasValue)
                await OrgSvc.UpdatePositionAsync(_editId.Value, new UpdatePositionDto(_title, deptId));
            else
                await OrgSvc.CreatePositionAsync(new CreatePositionDto(_title, deptId));
            Close();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

**Step 3: Create TeamsPage.razor** (same pattern as Positions)

```razor
@page "/teams"
@inject OrganizationApiService OrgSvc

<PageTitle>Teams — PeopleCore</PageTitle>

<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Teams</h1>
        <p class="text-sm text-muted-foreground mt-1">Manage teams within departments</p>
    </div>
    <Button Variant="primary" OnClick="OpenAdd">
        <svg class="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4"/>
        </svg>
        Add Team
    </Button>
</div>

<DataGrid TItem="TeamDto" Items="@_filtered" Loading="@_loading"
          ShowToolbar ShowSearch ShowPagination PageSize="15" OnSearch="OnSearch">
    <HeaderTemplate>
        <TableHeader>Name</TableHeader>
        <TableHeader>Department</TableHeader>
        <TableHeader>Members</TableHeader>
        <TableHeader></TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="team">
        <TableCell class="font-medium">@team.Name</TableCell>
        <TableCell class="text-sm text-muted-foreground">@(team.DepartmentName ?? "—")</TableCell>
        <TableCell><Badge Variant="secondary">@team.MemberCount</Badge></TableCell>
        <TableCell>
            <Button Size="icon-sm" Variant="ghost" OnClick="() => OpenEdit(team)">
                <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"/>
                </svg>
            </Button>
        </TableCell>
    </RowTemplate>
</DataGrid>

<Dialog IsOpen="@_showForm" IsOpenChanged="@(v => _showForm = v)" OnClose="Close"
        Title="@(_editId.HasValue ? "Edit Team" : "Add Team")" Size="md">
    <ChildContent>
        <div class="space-y-4">
            <FormField LabelText="Name" Required>
                <Input @bind-Value="_name" Placeholder="Team name" />
            </FormField>
            <FormField LabelText="Department">
                <Select @bind-Value="_departmentIdStr">
                    <option value="">No department</option>
                    @foreach (var d in _departments)
                    {
                        <option value="@d.Id">@d.Name</option>
                    }
                </Select>
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="Close">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="Save">Save</Button>
        </div>
    </FooterContent>
</Dialog>

@code {
    private bool _loading = true, _showForm, _saving;
    private string? _error;
    private Guid? _editId;
    private string _name = "", _departmentIdStr = "";
    private List<TeamDto> _all = [], _filtered = [];
    private List<DepartmentDto> _departments = [];

    protected override async Task OnInitializedAsync()
    {
        var depts = await OrgSvc.GetDepartmentsAsync();
        _departments = depts?.Items ?? [];
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var result = await OrgSvc.GetTeamsAsync();
        _all = result?.Items ?? [];
        _filtered = [.._all];
        _loading = false;
    }

    private void OnSearch(string term) =>
        _filtered = string.IsNullOrWhiteSpace(term) ? [.._all]
            : _all.Where(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

    private void OpenAdd() { _editId = null; _name = ""; _departmentIdStr = ""; _error = null; _showForm = true; }

    private void OpenEdit(TeamDto t)
    {
        _editId = t.Id; _name = t.Name;
        _departmentIdStr = t.DepartmentId?.ToString() ?? "";
        _error = null; _showForm = true;
    }

    private void Close() => _showForm = false;

    private async Task Save()
    {
        _saving = true; _error = null;
        try
        {
            var deptId = Guid.TryParse(_departmentIdStr, out var d) ? d : (Guid?)null;
            if (_editId.HasValue)
                await OrgSvc.UpdateTeamAsync(_editId.Value, new UpdateTeamDto(_name, deptId));
            else
                await OrgSvc.CreateTeamAsync(new CreateTeamDto(_name, deptId));
            Close();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

**Step 4: Build**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```

**Step 5: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Pages/Organization/
git commit -m "feat: add Departments, Positions, Teams pages"
```

---

## Task 7: Attendance Page

**Files:**
- Create: `src/PeopleCore.Web/Pages/Attendance/AttendancePage.razor`

**Step 1: Create AttendancePage.razor**

```razor
@page "/attendance"
@inject AttendanceApiService AttendanceSvc
@inject EmployeeApiService EmployeeSvc

<PageTitle>Attendance — PeopleCore</PageTitle>

<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Attendance</h1>
        <p class="text-sm text-muted-foreground mt-1">Daily time records</p>
    </div>
</div>

<DataGrid TItem="AttendanceRecordDto" Items="@_records" Loading="@_loading"
          ShowToolbar ShowSearch="false" ShowPagination PageSize="20">
    <ToolbarContent>
        <!-- Date filter -->
        <div class="flex items-center gap-2">
            <Input @bind-Value="_fromStr" Type="date" Class="w-36" />
            <span class="text-muted-foreground text-sm">to</span>
            <Input @bind-Value="_toStr" Type="date" Class="w-36" />
            <Button Variant="outline" OnClick="LoadAsync">Filter</Button>
        </div>
        <div class="ml-auto flex gap-2">
            <Button Variant="primary" OnClick="OpenTimeIn">Time In</Button>
            <Button Variant="outline" OnClick="OpenTimeOut">Time Out</Button>
        </div>
    </ToolbarContent>
    <HeaderTemplate>
        <TableHeader>Employee</TableHeader>
        <TableHeader>Date</TableHeader>
        <TableHeader>Time In</TableHeader>
        <TableHeader>Time Out</TableHeader>
        <TableHeader>Late (min)</TableHeader>
        <TableHeader>Undertime (min)</TableHeader>
        <TableHeader>Status</TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="rec">
        <TableCell class="font-medium text-sm">@rec.EmployeeName</TableCell>
        <TableCell class="text-sm">@rec.AttendanceDate.ToString("MMM d, yyyy")</TableCell>
        <TableCell class="text-sm">@(rec.TimeIn?.ToString("hh:mm tt") ?? "—")</TableCell>
        <TableCell class="text-sm">@(rec.TimeOut?.ToString("hh:mm tt") ?? "—")</TableCell>
        <TableCell>
            @if (rec.LateMinutes > 0)
            {
                <Badge Variant="warning">@rec.LateMinutes</Badge>
            }
            else { <span class="text-sm text-muted-foreground">0</span> }
        </TableCell>
        <TableCell>
            @if (rec.UndertimeMinutes > 0)
            {
                <Badge Variant="warning">@rec.UndertimeMinutes</Badge>
            }
            else { <span class="text-sm text-muted-foreground">0</span> }
        </TableCell>
        <TableCell>
            <Badge Variant="@(rec.IsPresent ? "success" : "outline")">
                @(rec.IsPresent ? "Present" : "Absent")
            </Badge>
        </TableCell>
    </RowTemplate>
</DataGrid>

<!-- Time In Dialog -->
<Dialog IsOpen="@_showTimeIn" IsOpenChanged="@(v => _showTimeIn = v)" OnClose="CloseDialogs"
        Title="Record Time In" Size="md">
    <ChildContent>
        <div class="space-y-4">
            <FormField LabelText="Employee" Required>
                <Select @bind-Value="_employeeIdStr">
                    <option value="">Select employee</option>
                    @foreach (var emp in _employees)
                    {
                        <option value="@emp.Id">@emp.FullName</option>
                    }
                </Select>
            </FormField>
            <FormField LabelText="Time In" Required>
                <Input @bind-Value="_timeInStr" Type="datetime-local" />
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="CloseDialogs">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="SaveTimeIn">Record Time In</Button>
        </div>
    </FooterContent>
</Dialog>

<!-- Time Out Dialog -->
<Dialog IsOpen="@_showTimeOut" IsOpenChanged="@(v => _showTimeOut = v)" OnClose="CloseDialogs"
        Title="Record Time Out" Size="md">
    <ChildContent>
        <div class="space-y-4">
            <FormField LabelText="Employee" Required>
                <Select @bind-Value="_employeeIdStr">
                    <option value="">Select employee</option>
                    @foreach (var emp in _employees)
                    {
                        <option value="@emp.Id">@emp.FullName</option>
                    }
                </Select>
            </FormField>
            <FormField LabelText="Time Out" Required>
                <Input @bind-Value="_timeOutStr" Type="datetime-local" />
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="CloseDialogs">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="SaveTimeOut">Record Time Out</Button>
        </div>
    </FooterContent>
</Dialog>

@code {
    private bool _loading = true, _showTimeIn, _showTimeOut, _saving;
    private string? _error;
    private string _fromStr = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    private string _toStr = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    private string _employeeIdStr = "";
    private string _timeInStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
    private string _timeOutStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
    private List<AttendanceRecordDto> _records = [];
    private List<EmployeeDto> _employees = [];

    protected override async Task OnInitializedAsync()
    {
        var emps = await EmployeeSvc.GetAllAsync(size: 200);
        _employees = emps?.Items ?? [];
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var from = DateOnly.TryParse(_fromStr, out var f) ? f : (DateOnly?)null;
        var to = DateOnly.TryParse(_toStr, out var t) ? t : (DateOnly?)null;
        var result = await AttendanceSvc.GetAttendanceAsync(from: from, to: to, size: 100);
        _records = result?.Items ?? [];
        _loading = false;
    }

    private void OpenTimeIn()
    {
        _employeeIdStr = "";
        _timeInStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
        _error = null;
        _showTimeIn = true;
    }

    private void OpenTimeOut()
    {
        _employeeIdStr = "";
        _timeOutStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
        _error = null;
        _showTimeOut = true;
    }

    private void CloseDialogs() { _showTimeIn = _showTimeOut = false; }

    private async Task SaveTimeIn()
    {
        _saving = true; _error = null;
        try
        {
            if (!Guid.TryParse(_employeeIdStr, out var empId)) throw new Exception("Select an employee.");
            await AttendanceSvc.TimeInAsync(new TimeInRequest(empId, DateTime.Parse(_timeInStr)));
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }

    private async Task SaveTimeOut()
    {
        _saving = true; _error = null;
        try
        {
            if (!Guid.TryParse(_employeeIdStr, out var empId)) throw new Exception("Select an employee.");
            await AttendanceSvc.TimeOutAsync(new TimeOutRequest(empId, DateTime.Parse(_timeOutStr)));
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

**Step 2: Build + Commit**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Pages/Attendance/AttendancePage.razor
git commit -m "feat: add Attendance page with time-in/out dialogs and date filter"
```

---

## Task 8: Overtime & Holidays Pages

**Files:**
- Create: `src/PeopleCore.Web/Pages/Attendance/OvertimePage.razor`
- Create: `src/PeopleCore.Web/Pages/Attendance/HolidaysPage.razor`

**Step 1: Create OvertimePage.razor**

```razor
@page "/overtime"
@inject AttendanceApiService AttendanceSvc

<PageTitle>Overtime — PeopleCore</PageTitle>

<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Overtime Requests</h1>
        <p class="text-sm text-muted-foreground mt-1">Review and approve overtime</p>
    </div>
</div>

<DataGrid TItem="OvertimeRequestDto" Items="@_records" Loading="@_loading"
          ShowToolbar ShowSearch ShowPagination PageSize="15" OnSearch="OnSearch">
    <HeaderTemplate>
        <TableHeader>Employee</TableHeader>
        <TableHeader>Date</TableHeader>
        <TableHeader>Start</TableHeader>
        <TableHeader>End</TableHeader>
        <TableHeader>Hours</TableHeader>
        <TableHeader>Reason</TableHeader>
        <TableHeader>Status</TableHeader>
        <TableHeader></TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="ot">
        <TableCell class="font-medium text-sm">@ot.EmployeeName</TableCell>
        <TableCell class="text-sm">@ot.OvertimeDate.ToString("MMM d, yyyy")</TableCell>
        <TableCell class="text-sm">@ot.StartTime.ToString("hh:mm tt")</TableCell>
        <TableCell class="text-sm">@ot.EndTime.ToString("hh:mm tt")</TableCell>
        <TableCell class="text-sm">@(Math.Round(ot.TotalMinutes / 60.0, 1))h</TableCell>
        <TableCell class="text-sm max-w-[150px] truncate" title="@ot.Reason">@ot.Reason</TableCell>
        <TableCell>
            <Badge Variant="@GetStatusVariant(ot.Status)">@ot.Status</Badge>
        </TableCell>
        <TableCell>
            @if (ot.Status == "Pending")
            {
                <div class="flex gap-1">
                    <Button Size="sm" Variant="primary" OnClick="() => OpenApprove(ot)">Approve</Button>
                    <Button Size="sm" Variant="destructive" OnClick="() => OpenReject(ot)">Reject</Button>
                </div>
            }
        </TableCell>
    </RowTemplate>
</DataGrid>

<!-- Approve Dialog -->
<AlertDialog IsOpen="@_showApprove" IsOpenChanged="@(v => _showApprove = v)"
             Title="Approve Overtime" Description="@($"Approve overtime request for {_selected?.EmployeeName}?")"
             ConfirmText="Approve" ConfirmVariant="primary"
             Loading="@_saving"
             OnConfirm="ConfirmApprove" OnCancel="CloseDialogs" />

<!-- Reject Dialog -->
<Dialog IsOpen="@_showReject" IsOpenChanged="@(v => _showReject = v)" OnClose="CloseDialogs"
        Title="Reject Overtime" Size="md">
    <ChildContent>
        <FormField LabelText="Reason for rejection" Required>
            <Input @bind-Value="_rejectionReason" Placeholder="Enter reason..." />
        </FormField>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="CloseDialogs">Cancel</Button>
            <Button Variant="destructive" Loading="@_saving" OnClick="ConfirmReject">Reject</Button>
        </div>
    </FooterContent>
</Dialog>

@code {
    private bool _loading = true, _showApprove, _showReject, _saving;
    private string? _error, _rejectionReason;
    private OvertimeRequestDto? _selected;
    private List<OvertimeRequestDto> _all = [], _records = [];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        var result = await AttendanceSvc.GetOvertimeAsync(size: 100);
        _all = result?.Items ?? [];
        _records = [.._all];
        _loading = false;
    }

    private void OnSearch(string term) =>
        _records = string.IsNullOrWhiteSpace(term) ? [.._all]
            : _all.Where(o => o.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

    private void OpenApprove(OvertimeRequestDto ot) { _selected = ot; _showApprove = true; }
    private void OpenReject(OvertimeRequestDto ot) { _selected = ot; _rejectionReason = ""; _error = null; _showReject = true; }
    private void CloseDialogs() { _showApprove = _showReject = false; }

    private async Task ConfirmApprove()
    {
        _saving = true;
        try
        {
            await AttendanceSvc.ApproveOvertimeAsync(_selected!.Id, new ApproveOvertimeDto(Guid.Empty));
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }

    private async Task ConfirmReject()
    {
        _saving = true; _error = null;
        try
        {
            await AttendanceSvc.RejectOvertimeAsync(_selected!.Id, new RejectOvertimeDto(_rejectionReason ?? ""));
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }

    private static string GetStatusVariant(string status) => status switch
    {
        "Approved" => "success",
        "Rejected" => "destructive",
        _ => "warning"
    };
}
```

**Step 2: Create HolidaysPage.razor**

```razor
@page "/holidays"
@inject AttendanceApiService AttendanceSvc

<PageTitle>Holidays — PeopleCore</PageTitle>

<div class="flex items-center justify-between mb-6">
    <div>
        <h1 class="text-2xl font-bold text-foreground">Holidays</h1>
        <p class="text-sm text-muted-foreground mt-1">Manage public and special holidays</p>
    </div>
    <Button Variant="primary" OnClick="OpenAdd">
        <svg class="h-4 w-4 mr-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4"/>
        </svg>
        Add Holiday
    </Button>
</div>

<DataGrid TItem="HolidayDto" Items="@_filtered" Loading="@_loading"
          ShowToolbar ShowSearch ShowPagination PageSize="15" OnSearch="OnSearch">
    <HeaderTemplate>
        <TableHeader>Name</TableHeader>
        <TableHeader>Date</TableHeader>
        <TableHeader>Type</TableHeader>
        <TableHeader>Recurring</TableHeader>
        <TableHeader></TableHeader>
    </HeaderTemplate>
    <RowTemplate Context="h">
        <TableCell class="font-medium">@h.Name</TableCell>
        <TableCell class="text-sm">@h.HolidayDate.ToString("MMMM d, yyyy")</TableCell>
        <TableCell>
            <Badge Variant="@(h.HolidayType == "RegularHoliday" ? "primary" : "warning")">
                @(h.HolidayType == "RegularHoliday" ? "Regular" : "Special")
            </Badge>
        </TableCell>
        <TableCell>
            @if (h.IsRecurring)
            {
                <Badge Variant="secondary">Recurring</Badge>
            }
            else
            {
                <span class="text-sm text-muted-foreground">One-time</span>
            }
        </TableCell>
        <TableCell>
            <Button Size="icon-sm" Variant="ghost" Class="text-destructive hover:text-destructive"
                    OnClick="() => ConfirmDelete(h)">
                <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"/>
                </svg>
            </Button>
        </TableCell>
    </RowTemplate>
</DataGrid>

<!-- Add Dialog -->
<Dialog IsOpen="@_showForm" IsOpenChanged="@(v => _showForm = v)" OnClose="CloseDialogs"
        Title="Add Holiday" Size="md">
    <ChildContent>
        <div class="space-y-4">
            <FormField LabelText="Name" Required>
                <Input @bind-Value="_name" Placeholder="e.g. New Year's Day" />
            </FormField>
            <FormField LabelText="Date" Required>
                <Input @bind-Value="_dateStr" Type="date" />
            </FormField>
            <FormField LabelText="Type" Required>
                <Select @bind-Value="_type">
                    <option value="RegularHoliday">Regular Holiday</option>
                    <option value="SpecialNonWorking">Special Non-Working</option>
                </Select>
            </FormField>
            <FormField LabelText="Recurring annually">
                <Switch @bind-Value="_recurring">Repeat every year</Switch>
            </FormField>
        </div>
        @if (!string.IsNullOrEmpty(_error))
        {
            <Alert Variant="destructive" Title="Error" Class="mt-3">@_error</Alert>
        }
    </ChildContent>
    <FooterContent>
        <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-border">
            <Button Variant="outline" OnClick="CloseDialogs">Cancel</Button>
            <Button Variant="primary" Loading="@_saving" OnClick="Save">Add Holiday</Button>
        </div>
    </FooterContent>
</Dialog>

<!-- Delete Confirm -->
<AlertDialog IsOpen="@_showDelete" IsOpenChanged="@(v => _showDelete = v)"
             Title="Delete Holiday" Description="@($"Delete '{_deleteTarget?.Name}'? This cannot be undone.")"
             ConfirmText="Delete" ConfirmVariant="destructive"
             Loading="@_saving"
             OnConfirm="ExecuteDelete" OnCancel="CloseDialogs" />

@code {
    private bool _loading = true, _showForm, _showDelete, _saving;
    private string? _error;
    private string _name = "", _dateStr = "", _type = "RegularHoliday";
    private bool _recurring;
    private HolidayDto? _deleteTarget;
    private List<HolidayDto> _all = [], _filtered = [];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _all = await AttendanceSvc.GetHolidaysAsync(DateTime.Today.Year) ?? [];
        _filtered = [.._all];
        _loading = false;
    }

    private void OnSearch(string term) =>
        _filtered = string.IsNullOrWhiteSpace(term) ? [.._all]
            : _all.Where(h => h.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

    private void OpenAdd()
    {
        _name = ""; _dateStr = ""; _type = "RegularHoliday"; _recurring = false;
        _error = null; _showForm = true;
    }

    private void ConfirmDelete(HolidayDto h) { _deleteTarget = h; _showDelete = true; }
    private void CloseDialogs() { _showForm = _showDelete = false; }

    private async Task Save()
    {
        _saving = true; _error = null;
        try
        {
            if (!DateOnly.TryParse(_dateStr, out var date)) throw new Exception("Invalid date.");
            await AttendanceSvc.CreateHolidayAsync(new CreateHolidayDto(_name, date, _type, _recurring));
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }

    private async Task ExecuteDelete()
    {
        _saving = true;
        try
        {
            await AttendanceSvc.DeleteHolidayAsync(_deleteTarget!.Id);
            CloseDialogs();
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _saving = false; }
    }
}
```

**Step 3: Build**
```bash
dotnet build "c:/M2NET PROJECTS/peoplecore/src/PeopleCore.Web/"
```
Expected: Build succeeded, 0 errors.

**Step 4: Commit**
```bash
cd "c:/M2NET PROJECTS/peoplecore"
git add src/PeopleCore.Web/Pages/Attendance/
git commit -m "feat: add Overtime and Holidays pages"
```

---

## Summary

### Pages Delivered
| Route | Page | File |
|-------|------|------|
| `/` | Dashboard | `Pages/Home.razor` |
| `/employees` | Employees | `Pages/Employees/EmployeesPage.razor` |
| `/departments` | Departments | `Pages/Organization/DepartmentsPage.razor` |
| `/positions` | Positions | `Pages/Organization/PositionsPage.razor` |
| `/teams` | Teams | `Pages/Organization/TeamsPage.razor` |
| `/attendance` | Attendance | `Pages/Attendance/AttendancePage.razor` |
| `/overtime` | Overtime | `Pages/Attendance/OvertimePage.razor` |
| `/holidays` | Holidays | `Pages/Attendance/HolidaysPage.razor` |

### Components Used
`DashboardStatCard` · `BarChart` · `PieChart` · `DataGrid` · `Dialog` · `AlertDialog` · `FormField` · `Input` · `Select` · `Switch` · `Badge` · `Avatar` · `Button` · `Alert` · `InfoRow` · `Table/TableHead/TableBody/TableRow/TableCell/TableHeader` · `Card/CardHeader/CardContent/CardTitle/CardDescription` · `Spinner` · `ShadcnSidebar` (all sidebar sub-components)