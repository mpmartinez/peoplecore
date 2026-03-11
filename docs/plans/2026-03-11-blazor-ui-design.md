# PeopleCore Blazor WASM UI Design

**Date:** 2026-03-11

## Goal

Implement all HR module pages in the existing Blazor WASM app (`PeopleCore.Web`) using the existing shadcn-inspired component library. Apply a Corporate Navy blue color theme throughout.

---

## Decisions

| Decision | Choice |
|----------|--------|
| Pages scope | Dashboard + Employees + Departments + Positions + Teams + Attendance + Overtime + Holidays |
| Color theme | Corporate Navy (deep navy #1e5bbf, dark sidebar #0f2240) |
| Navigation | Grouped sidebar using ShadcnSidebar |
| List layout | DataGrid with toolbar (search + Add button), Dialog for add/edit/view |

---

## Phase 1: Color Theme

Update CSS custom properties in `wwwroot/css/app.css` (or a dedicated `theme.css`):

```css
:root {
  /* Navy Blue Palette */
  --primary: 214 89% 40%;               /* #1e5bbf */
  --primary-foreground: 0 0% 100%;

  --background: 210 20% 98%;            /* near-white blue tint */
  --foreground: 214 60% 12%;            /* very dark navy */

  --card: 0 0% 100%;
  --card-foreground: 214 60% 12%;

  --muted: 214 15% 93%;
  --muted-foreground: 214 20% 45%;

  --border: 214 20% 88%;
  --input: 214 20% 88%;
  --ring: 214 89% 40%;

  --accent: 214 89% 95%;               /* light blue accent */
  --accent-foreground: 214 89% 30%;

  --destructive: 0 72% 51%;
  --destructive-foreground: 0 0% 100%;

  --success: 142 71% 45%;
  --warning: 38 92% 50%;

  /* Sidebar — dark navy */
  --sidebar-background: 214 60% 12%;   /* #0f2240 */
  --sidebar-foreground: 214 15% 85%;
  --sidebar-accent: 214 50% 20%;
  --sidebar-accent-foreground: 0 0% 100%;
  --sidebar-border: 214 40% 18%;
  --sidebar-ring: 214 89% 40%;
  --sidebar-width: 250px;
  --sidebar-width-icon: 48px;
}
```

---

## Phase 2: Layout Shell

### MainLayout.razor
Replace existing Bootstrap-based layout with ShadcnSidebar-based layout:

```
ShadcnSidebar (left, dark navy)
  ShadcnSidebarHeader — PeopleCore logo + app name
  ShadcnSidebarContent
    ShadcnSidebarGroup: Overview
      ShadcnSidebarMenuItem: Dashboard (/)
    ShadcnSidebarGroup: Organization
      ShadcnSidebarMenuItem: Departments (/departments)
      ShadcnSidebarMenuItem: Positions (/positions)
      ShadcnSidebarMenuItem: Teams (/teams)
    ShadcnSidebarGroup: Workforce
      ShadcnSidebarMenuItem: Employees (/employees)
    ShadcnSidebarGroup: Time & Attendance
      ShadcnSidebarMenuItem: Attendance (/attendance)
      ShadcnSidebarMenuItem: Overtime (/overtime)
      ShadcnSidebarMenuItem: Holidays (/holidays)
  ShadcnSidebarFooter — Avatar + username + logout

ShadcnSidebarInset (main content area)
  Top bar: ShadcnSidebarTrigger + Breadcrumb + Avatar/user menu
  @Body
```

**Files:**
- Rewrite: `Layout/MainLayout.razor`
- Rewrite: `Layout/NavMenu.razor` → becomes sidebar nav content
- Delete: `Layout/MainLayout.razor.css` (no longer needed)
- Delete: `Layout/NavMenu.razor.css` (no longer needed)

---

## Phase 3: Pages

All pages follow this pattern:
```razor
@page "/module"
<PageTitle>Module — PeopleCore</PageTitle>

<!-- Page header -->
<div class="flex items-center justify-between mb-6">
  <div>
    <h1 class="text-2xl font-bold text-foreground">Module</h1>
    <p class="text-muted-foreground text-sm">Description</p>
  </div>
  <Button OnClick="OpenAddDialog">Add New</Button>
</div>

<!-- DataGrid -->
<DataGrid TItem="Dto" Items="items" Loading="isLoading"
          ShowSearch ShowPagination PageSize="20">
  <HeaderTemplate>...</HeaderTemplate>
  <RowTemplate Context="item">...</RowTemplate>
</DataGrid>

<!-- Add/Edit Dialog -->
<Dialog IsOpen="showDialog" Title="Add / Edit" Size="lg">
  <ChildContent>
    <form>...</form>
  </ChildContent>
  <FooterContent>
    <Button Variant="outline">Cancel</Button>
    <Button Variant="primary" Type="submit">Save</Button>
  </FooterContent>
</Dialog>
```

### Dashboard (`Pages/Dashboard.razor` → route `/`)
**Stat Cards (row of 4):**
- Total Employees (count from API)
- Present Today (attendance count)
- On Leave Today (placeholder for Phase 5)
- Overtime Pending (count from overtime API)

**Charts (2-column grid):**
- BarChart: Attendance this week (Mon–Fri, present count)
- PieChart: Headcount by Department

**Recent Hires (DataTable, last 5):**
- Columns: Name, Department, Position, Hire Date

### Employees (`Pages/Employees/EmployeesPage.razor` → `/employees`)
**DataGrid columns:**
- Avatar + Full Name
- Employee Number
- Department
- Position
- Employment Type badge
- Status badge (Active/Inactive)
- Actions: View | Edit | (future: Delete)

**Add/Edit Dialog (tabbed — General / Contact / Employment):**
- General: First/Last/Middle name, DOB, Gender
- Contact: Work email, phone
- Employment: Dept, Position, Team, Hire date, Employment type

**View Dialog:**
- InfoRow components for all fields

### Departments (`Pages/Organization/DepartmentsPage.razor` → `/departments`)
**DataGrid columns:** Name, Description, Employee Count, Actions (Edit)
**Dialog fields:** Name (Input), Description (Input)

### Positions (`Pages/Organization/PositionsPage.razor` → `/positions`)
**DataGrid columns:** Title, Department (Select), Employee Count, Actions
**Dialog fields:** Title (Input), Department (Select)

### Teams (`Pages/Organization/TeamsPage.razor` → `/teams`)
**DataGrid columns:** Name, Department, Member Count, Actions
**Dialog fields:** Name (Input), Department (Select)

### Attendance (`Pages/Attendance/AttendancePage.razor` → `/attendance`)
**Toolbar extras:** Employee Combobox filter + DatePicker range + Time In / Time Out buttons
**DataGrid columns:** Employee, Date, Time In, Time Out, Late (mins), Undertime (mins), Status badge
**Status badge variants:** Present (success), Absent (destructive), Holiday (warning)

### Overtime (`Pages/Attendance/OvertimePage.razor` → `/overtime`)
**DataGrid columns:** Employee, Date, Start/End Time, Hours, Reason, Status badge, Actions
**Actions:** Approve button (success) / Reject button (destructive) for Pending items
**AlertDialog:** Confirm before approve/reject

### Holidays (`Pages/Attendance/HolidaysPage.razor` → `/holidays`)
**DataGrid columns:** Name, Date, Type badge (Regular=primary/Special=warning), Recurring badge
**Dialog fields:** Name, Date (DatePicker), Type (Select), Recurring (Switch)

---

## Services Layer (Blazor-side)

Each page injects `HttpClient` and calls the existing API:

```csharp
// Services/EmployeeApiService.cs
public class EmployeeApiService(HttpClient http)
{
    public Task<PagedResult<EmployeeDto>> GetAllAsync(int page, int size) => ...
    public Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto) => ...
    public Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto dto) => ...
}
```

One service class per module, registered in `Program.cs`.

---

## File Structure

```
src/PeopleCore.Web/
├── Layout/
│   ├── MainLayout.razor          ← rewritten
│   └── NavMenu.razor             ← rewritten (sidebar nav)
├── Pages/
│   ├── Dashboard.razor           ← new
│   ├── Employees/
│   │   └── EmployeesPage.razor   ← new
│   ├── Organization/
│   │   ├── DepartmentsPage.razor ← new
│   │   ├── PositionsPage.razor   ← new
│   │   └── TeamsPage.razor       ← new
│   └── Attendance/
│       ├── AttendancePage.razor  ← new
│       ├── OvertimePage.razor    ← new
│       └── HolidaysPage.razor    ← new
├── Services/
│   ├── EmployeeApiService.cs     ← new
│   ├── OrganizationApiService.cs ← new
│   └── AttendanceApiService.cs   ← new
└── wwwroot/css/
    └── app.css                   ← theme variables updated
```

---

## Component Usage Summary

| Component | Used In |
|-----------|---------|
| ShadcnSidebar + group components | MainLayout |
| DashboardStatCard | Dashboard |
| BarChart, PieChart | Dashboard |
| DataGrid, DataTable | All list pages |
| Dialog, AlertDialog | All list pages |
| FormField, Input, Select, DatePicker | All form dialogs |
| Badge | Status columns |
| Avatar | Employees list, sidebar footer |
| Button | Toolbars, dialogs, actions |
| Breadcrumb | Top bar of every page |
| Spinner | Loading states |
| InfoRow | View detail dialogs |
| Tabs, TabPanel | Employee add/edit dialog |
| Switch | Holidays recurring toggle |
| Combobox | Attendance employee filter |
