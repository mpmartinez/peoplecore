# PeopleCore Feature Expansion Design
**Date:** 2026-03-14
**Features:** Shift Scheduling, Leave Accruals, Careers Portal API, Advanced Analytics, PWA
**Approach:** API-first, implement one feature at a time in order listed

---

## Implementation Order

1. Shift Scheduling
2. Leave Accruals
3. Careers Portal API
4. Advanced Analytics
5. PWA

---

## 1. Shift Scheduling

### Domain Entities

**`ShiftTemplate`**
- `Id`, `Name` (e.g. "Morning", "Evening", "Graveyard")
- `StartTime`, `EndTime` (TimeOnly)
- `BreakMinutes` (int)
- `IsNightShift` (bool — for night differential payroll export)
- `IsActive`

**`RotatingPattern`**
- `Id`, `Name` (e.g. "Week A/B Rotation")
- `CycleLengthDays` (e.g. 14 for 2-week rotation)
- `Slots` → collection of `RotatingPatternSlot`

**`RotatingPatternSlot`**
- `Id`, `RotatingPatternId`
- `DayOffset` (0 to CycleLengthDays-1)
- `ShiftTemplateId` (nullable — null = rest day)

**`EmployeeShiftAssignment`**
- `Id`, `EmployeeId`
- `ShiftTemplateId` (nullable — fixed shift)
- `RotatingPatternId` (nullable — rotating schedule)
- `PatternStartDate` (anchor date to resolve which cycle day applies)
- `EffectiveFrom`, `EffectiveTo` (DateOnly, EffectiveTo nullable = indefinite)

### API Endpoints
```
GET/POST/PUT/DELETE  /api/shift-templates
GET/POST/PUT/DELETE  /api/rotating-patterns
GET/POST             /api/shift-assignments
DELETE               /api/shift-assignments/{id}
GET                  /api/shift-assignments/{employeeId}/schedule?from=&to=   (resolved daily schedule)
```

### Authorization
`Admin`, `HRManager` — full CRUD
`Manager` — read-only for their team
`Employee` — read-only for own schedule

### Attendance Integration
Late minutes computation reads the employee's resolved shift start time for the day instead of a hardcoded value.

### Payroll Export Impact
`PayrollAttendanceSummaryDto` gains `ShiftName` (string) and `IsNightShift` (bool) fields for M2NET Payroll night differential computation.

---

## 2. Leave Accruals

### Domain Entities

**`LeaveAccrualPolicy`**
- `Id`, `LeaveTypeId`
- `TenureMonthsMin` (int)
- `TenureMonthsMax` (int, nullable — null = open-ended)
- `DaysPerYear` (decimal)
- `AccrualFrequency` (enum: `Monthly`, `Annual`)
- `IsActive`

Example (Vacation Leave):
| TenureMonthsMin | TenureMonthsMax | DaysPerYear |
|---|---|---|
| 0 | 11 | 0 (probationary) |
| 12 | 23 | 15 |
| 24 | 59 | 18 |
| 60 | null | 21 |

**`LeaveAccrualTransaction`** (immutable ledger)
- `Id`, `EmployeeId`, `LeaveTypeId`
- `AccrualDate`
- `DaysAccrued` (decimal)
- `PolicySnapshot` (JSON string — audit trail)
- `PeriodYear`, `PeriodMonth`

### Accrual Engine
`LeaveAccrualHostedService` implements `IHostedService`, runs on the 1st of every month:
1. For each active employee, compute tenure months from `HireDate`
2. Look up matching `LeaveAccrualPolicy` per active leave type
3. Calculate `DaysAccrued = DaysPerYear / 12`
4. Idempotency check: skip if transaction already exists for `(EmployeeId, LeaveTypeId, PeriodYear, PeriodMonth)`
5. Insert `LeaveAccrualTransaction`, upsert `LeaveBalance.TotalDays`

### Migration Strategy
Existing `LeaveBalance.TotalDays` values are migrated as a single opening `LeaveAccrualTransaction` per employee per leave type.

### API Endpoints
```
GET/POST/PUT/DELETE  /api/leave-accrual-policies
GET                  /api/leave-accrual-policies/{leaveTypeId}/rules
POST                 /api/leave-accruals/run-manual          (Admin only)
GET                  /api/employees/{id}/accrual-history
```

### Authorization
`Admin`, `HRManager` — policy management and manual run
`Employee` — read own accrual history

---

## 3. Careers Portal API

### Principles
- No authentication on public endpoints
- Separate controller prefix `/api/careers`
- CORS allows configured external origins
- Rate limiting on application submission

### API Endpoints

**Public (anonymous):**
```
GET  /api/careers/jobs              List open job postings
GET  /api/careers/jobs/{id}         Job posting detail
POST /api/careers/jobs/{id}/apply   Submit application
```

**`GET /api/careers/jobs` — response fields:**
`id`, `title`, `description`, `requirements`, `departmentName`, `positionTitle`, `vacancies`, `postedAt`
Only `Status = Open` postings returned.

**`POST /api/careers/jobs/{id}/apply` — request:**
```json
{
  "firstName": "",
  "lastName": "",
  "email": "",
  "phone": "",
  "resumeBase64": "",
  "resumeFileName": ""
}
```
Response: `201 Created` with `{ "applicationId", "message": "Application received." }`

### No New Entities
Submissions create an `Applicant` record with `Status = Applied`. Existing HR applicant tracking picks it up automatically.

### Validation
- Email unique per job posting (no duplicate applications)
- File: max 5MB, allowed types PDF/DOCX
- Required: firstName, lastName, email, phone, resume

### Configuration (`appsettings.json`)
```json
"CareersPortal": {
  "AllowedOrigins": ["https://careers.yourcompany.com"],
  "MaxApplicationsPerHourPerIp": 3
}
```

---

## 4. Advanced Analytics

### Audience Tiers
- **HR Manager** — operational metrics
- **Executive (Admin)** — strategic KPIs

### HR Analytics Endpoints (`Admin`, `HRManager`)
```
GET  /api/analytics/hr/headcount               Headcount by dept, status, employment type
GET  /api/analytics/hr/turnover                Separations vs hires by month/quarter
GET  /api/analytics/hr/attendance              Attendance rate, late %, undertime % by dept
GET  /api/analytics/hr/leave-utilization       Leave used vs balance by type and dept
GET  /api/analytics/hr/overtime                Overtime hours by dept/employee
GET  /api/analytics/hr/recruitment-funnel      Applicants → Screened → Interview → Offer → Hired
GET  /api/analytics/hr/performance-distribution Score distribution per review cycle
```

### Executive Analytics Endpoints (`Admin`)
```
GET  /api/analytics/executive/workforce-summary    Headcount, active vs inactive, by dept
GET  /api/analytics/executive/hiring-trend         New hires per month (last 12 months)
GET  /api/analytics/executive/attrition-rate       Monthly/quarterly attrition %
GET  /api/analytics/executive/leave-summary        Total leave days consumed company-wide
GET  /api/analytics/executive/performance-overview Avg performance score per dept per cycle
```

### Common Query Parameters
```
?from=YYYY-MM-DD&to=YYYY-MM-DD
?departmentId={guid}              (optional)
?groupBy=month|quarter|year
```

### Standard Response Shape
```json
{
  "period": { "from": "2026-01-01", "to": "2026-03-31" },
  "data": [ { "label": "IT", "value": 42, "trend": "+5%" } ],
  "generatedAt": "2026-03-14T10:00:00Z"
}
```

### Implementation
- Computed on-demand via EF Core optimized queries
- Results cached 15 minutes via `IMemoryCache`
- No separate data warehouse needed at this stage

### Blazor Dashboard
New `/analytics` page with role-gated HR and Executive sections. Charts via Chart.js (JS interop).

---

## 5. PWA

### Scope
Online-only. No offline caching or background sync. Goal: installable on mobile, mobile-friendly UI.

### Files to Add
- `wwwroot/manifest.json` — web app manifest
- `wwwroot/sw.js` — minimal passthrough service worker (registers PWA, no caching)
- `wwwroot/icons/icon-192.png`, `icon-512.png`, `apple-touch-icon.png`

### `manifest.json`
```json
{
  "name": "PeopleCore",
  "short_name": "PeopleCore",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#ffffff",
  "theme_color": "#1e40af",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```

### `index.html` additions
```html
<link rel="manifest" href="/manifest.json" />
<meta name="theme-color" content="#1e40af" />
<meta name="mobile-web-app-capable" content="yes" />
<meta name="apple-mobile-web-app-capable" content="yes" />
```

### Mobile Navigation: Footer Tab Bar
- Existing `NavMenu.razor` sidebar hidden on mobile (`@media` breakpoint)
- Fixed bottom footer nav shown on mobile with 5 tabs:
  - **Home** (Dashboard)
  - **Attendance** (/my-attendance)
  - **Leave** (/my-leave)
  - **Profile** (/my-profile)
  - **More** (role-based: Approvals for Manager/HR, HR tools for HRManager)
- Role-based tab visibility (Manager sees Approvals, HR sees HR menu)
- Pure CSS implementation, no JS required

### Responsive UI Audit
Pages to update for mobile:
- Tables → card stacks on small screens
- Forms → full-width inputs
- Time In/Out button on `/my-attendance` → large prominent button
- No hamburger menu — footer nav handles mobile navigation

### No New NuGet Packages
Static file additions + CSS adjustments only.

---

## Summary Table

| Feature | New Entities | New Endpoints | Background Job | UI Changes |
|---|---|---|---|---|
| Shift Scheduling | 4 | 8 | No | Schedule view page |
| Leave Accruals | 2 | 5 | Yes (monthly) | Accrual history |
| Careers Portal API | 0 | 3 | No | None (external site) |
| Advanced Analytics | 0 | 12 | No | Analytics dashboard |
| PWA | 0 | 0 | No | Footer nav, responsive |
