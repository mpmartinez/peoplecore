# PeopleCore HRMS — System Design

**Date:** 2026-03-10
**Status:** Approved
**Stack:** ASP.NET Core Web API (.NET 10.0.3) · Blazor WASM (standalone) · PostgreSQL · MinIO

---

## Context

PeopleCore is an internal HRMS for a Philippine-based company. It integrates with a separately deployed payroll system (same stack, separate PostgreSQL database). The payroll system consumes HRMS data via dedicated export API endpoints. This is a single-tenant system.

---

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Architecture | Layered monolith, 5 projects | Matches stated requirements, right-sized for internal single-tenant |
| Auth | ASP.NET Core Identity + JWT | Handles employee login accounts; JWT works for Blazor WASM + API-to-API |
| Blazor hosting | Standalone WASM | Clean separation; deployed independently from API |
| Payroll integration | Separate DB + REST API | HRMS exposes export endpoints; payroll polls on cutoff schedule |
| File storage | MinIO (S3-compatible) | Keeps DB lean; on-premise capable; production-grade blob storage |
| Background jobs | .NET IHostedService + PeriodicTimer | No Hangfire dependency; sufficient for leave accrual |
| ORM | EF Core with IEntityTypeConfiguration | Fluent config per entity; snake_case PostgreSQL conventions |
| Testing | xUnit + Moq, unit tests only (phase 1) | Services depend on interfaces; trivial to mock without in-memory DB |

---

## Solution Structure

```
PeopleCore.sln
│
├── src/
│   ├── PeopleCore.Domain/           # Entities, Enums, ValueObjects
│   ├── PeopleCore.Application/      # Services, DTOs, Interfaces
│   ├── PeopleCore.Infrastructure/   # EF Core, Repositories, MinIO, Identity
│   ├── PeopleCore.API/              # Controllers, Auth, Middleware
│   └── PeopleCore.Web/              # Blazor WASM standalone
│
└── tests/
    ├── PeopleCore.Application.Tests/
    └── PeopleCore.Infrastructure.Tests/  (phase 2 — integration tests)
```

**Dependency flow (one-way only):**
```
Domain  ←  Application  ←  Infrastructure  ←  API
                ↑
              Web (via HTTP)
```

**Module folders (applied consistently across all layers):**
```
Employees/
Attendance/
Leave/
Organization/
Recruitment/
Performance/
PayrollIntegration/
```

---

## Domain Entities

All entities inherit `AuditableEntity` with `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`.

### Organization
- `Company` — name, address, contact info
- `Department` — name, companyId, parentDepartmentId (self-ref for hierarchy)
- `Team` — name, departmentId
- `Position` — title, departmentId, level/grade

### Employees
- `Employee` — personal info, employment info, departmentId, positionId, reportingManagerId (self-ref), employmentStatus, hireDate, regularizationDate, is13thMonthEligible
- `EmployeeGovernmentId` — employeeId, type (SSS, PhilHealth, PagIbig, TIN), idNumber
- `EmergencyContact` — employeeId, name, relationship, phone
- `EmployeeDocument` — employeeId, documentType, fileName, storageKey (MinIO path), uploadedAt

### Attendance
- `AttendanceRecord` — employeeId, date, timeIn, timeOut, lateMinutes, undertimeMinutes, overtimeMinutes, isPresent, isHoliday, holidayType
- `OvertimeRequest` — employeeId, date, startTime, endTime, totalMinutes, reason, status, approvedBy
- `Holiday` — name, date, type (RegularHoliday, SpecialNonWorking), isRecurring

### Leave
- `LeaveType` — name, code (VL/SL/EL/ML/PL/SPL), maxDaysPerYear, isPaid, isCarryOver, genderRestriction
- `LeaveBalance` — employeeId, leaveTypeId, year, totalDays, usedDays, carriedOverDays
- `LeaveRequest` — employeeId, leaveTypeId, startDate, endDate, totalDays, reason, status, approvedBy

### Recruitment
- `JobPosting` — title, departmentId, positionId, description, requirements, vacancies, status
- `Applicant` — jobPostingId, firstName, lastName, email, phone, resumeStorageKey, status, convertedEmployeeId
- `InterviewStage` — applicantId, stageName, scheduledAt, interviewerId, outcome, notes

### Performance
- `ReviewCycle` — name, year, quarter (null = annual), startDate, endDate, status
- `PerformanceReview` — employeeId, reviewCycleId, reviewerId, selfEvaluationScore, managerScore, finalScore, status
- `KpiItem` — performanceReviewId, description, target, actual, weight, score

### Enums
`EmploymentStatus` (Regular, Probationary, Contractual) · `LeaveStatus` (Pending, Approved, Rejected, Cancelled) · `OvertimeStatus` · `ApplicantStatus` (Applied, Screening, Interview, Offer, Hired, Rejected) · `ReviewStatus` · `HolidayType` · `GovernmentIdType` · `DocumentType`

---

## Application Layer

No MediatR. Straightforward service classes injected via DI.

### Service Interfaces

| Module | Interfaces |
|---|---|
| Employees | `IEmployeeService`, `IEmployeeDocumentService` |
| Organization | `IDepartmentService`, `IPositionService`, `ITeamService` |
| Attendance | `IAttendanceService`, `IOvertimeService`, `IHolidayService` |
| Leave | `ILeaveTypeService`, `ILeaveBalanceService`, `ILeaveRequestService` |
| Recruitment | `IJobPostingService`, `IApplicantService`, `IInterviewService` |
| Performance | `IReviewCycleService`, `IPerformanceReviewService` |
| Payroll Integration | `IPayrollExportService` |

### Business Logic (in Application services, not controllers)
- Leave validation — check balance, enforce gender restrictions, block overlapping dates
- Leave day counting — exclude weekends and Philippine holidays
- Attendance late/undertime calculation — compare actual vs. scheduled shift
- Overtime approval — manager can only approve their direct reports
- Leave accrual — monthly credit per LeaveType config
- Applicant → Employee conversion — `IApplicantService.ConvertToEmployeeAsync()`
- 13th month eligibility — based on hire date and employment status

### Repository Interfaces
- `IRepository<T>` — generic base: `GetByIdAsync`, `GetAllAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`
- Module-specific extensions: `IAttendanceRepository` (summary queries), `ILeaveRequestRepository` (overlap check)

### DTOs
- Located in `Application/{Module}/DTOs/`
- Separate `CreateDto`, `UpdateDto`, `ResponseDto` per entity
- Domain entities never exposed directly to API

---

## Infrastructure Layer

```
Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs               # extends IdentityDbContext<ApplicationUser>
│   ├── Configurations/               # IEntityTypeConfiguration<T> per entity
│   ├── Repositories/                 # Repository<T> base + module-specific
│   └── Migrations/
├── Identity/
│   └── ApplicationUser.cs            # extends IdentityUser, has EmployeeId FK
├── Storage/
│   └── MinioStorageService.cs        # implements IStorageService
├── Jobs/
│   └── LeaveAccrualHostedService.cs  # IHostedService + PeriodicTimer
└── ServiceExtensions.cs
```

### ApplicationUser → Employee Link
```csharp
public class ApplicationUser : IdentityUser
{
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
}
```

### Storage Interface
```csharp
public interface IStorageService
{
    Task<string> UploadAsync(string bucketName, string objectKey, Stream stream, string contentType);
    Task<Stream> DownloadAsync(string bucketName, string objectKey);
    Task DeleteAsync(string bucketName, string objectKey);
}
```
Object key pattern: `employees/{employeeId}/{documentType}/{filename}`

### Configuration (appsettings.json)
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=peoplecore;Username=...;Password=..."
  },
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "...",
    "SecretKey": "...",
    "BucketName": "peoplecore-documents"
  },
  "Jwt": {
    "Key": "...",
    "Issuer": "peoplecore-api",
    "Audience": "peoplecore-web",
    "ExpiryMinutes": 60
  }
}
```

---

## API Layer

```
API/
├── Controllers/
│   ├── Employees/
│   ├── Attendance/
│   ├── Leave/
│   ├── Organization/
│   ├── Recruitment/
│   ├── Performance/
│   ├── PayrollExport/
│   └── Auth/
├── Middleware/
│   ├── ExceptionHandlingMiddleware.cs   # maps domain exceptions to ProblemDetails
│   └── CurrentUserMiddleware.cs
├── Extensions/
│   └── ServiceExtensions.cs
└── Program.cs
```

### Auth Endpoints
```
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
```

### Roles
`Admin` · `HRManager` · `Manager` · `Employee`

### Key API Endpoints

```
# Employees
GET    /api/employees
GET    /api/employees/{id}
POST   /api/employees
PUT    /api/employees/{id}
DELETE /api/employees/{id}
GET    /api/employees/{id}/documents
POST   /api/employees/{id}/documents
GET    /api/employees/{id}/government-ids
PUT    /api/employees/{id}/government-ids

# Organization
GET/POST/PUT/DELETE /api/departments
GET/POST/PUT/DELETE /api/positions
GET/POST/PUT/DELETE /api/teams

# Attendance
GET  /api/attendance
POST /api/attendance/time-in
POST /api/attendance/time-out
GET  /api/attendance/summary
POST /api/overtime-requests
PUT  /api/overtime-requests/{id}/approve
PUT  /api/overtime-requests/{id}/reject

# Leave
GET  /api/leave-types
GET  /api/leave-requests
POST /api/leave-requests
PUT  /api/leave-requests/{id}/approve
PUT  /api/leave-requests/{id}/reject
PUT  /api/leave-requests/{id}/cancel
GET  /api/leave-balances/{employeeId}
GET  /api/holidays

# Recruitment
GET/POST/PUT       /api/job-postings
GET/POST/PUT       /api/applicants
POST               /api/applicants/{id}/convert-to-employee
POST/PUT           /api/interviews

# Performance
GET/POST/PUT  /api/review-cycles
GET/POST/PUT  /api/performance-reviews
POST          /api/performance-reviews/{id}/submit

# Payroll Integration (consumed by payroll system)
GET  /api/payroll-export/employees
GET  /api/payroll-export/attendance-summary    # ?from=&to=&employeeId=
GET  /api/payroll-export/approved-leaves       # ?from=&to=
GET  /api/payroll-export/approved-overtime     # ?from=&to=
GET  /api/payroll-export/status-changes        # ?from=&to=
```

### Standards
- All list endpoints: `?page=1&pageSize=20` → `PagedResult<T>`
- Errors: RFC 7807 `ProblemDetails` via `ExceptionHandlingMiddleware`
- No try/catch in controllers

---

## Database Schema

See full DDL in [docs/plans/2026-03-10-hrms-schema.sql](2026-03-10-hrms-schema.sql).

**Tables:**
`companies` · `departments` · `positions` · `teams` · `employees` · `employee_government_ids` · `emergency_contacts` · `employee_documents` · `holidays` · `attendance_records` · `overtime_requests` · `leave_types` · `leave_balances` · `leave_requests` · `job_postings` · `applicants` · `interview_stages` · `review_cycles` · `performance_reviews` · `kpi_items`

**Conventions:** `uuid` PKs · snake_case · `TIMESTAMPTZ` for all timestamps · audit columns on all tables · `UNIQUE` constraints on natural keys (employee_number, government_id per type, leave_balance per employee+type+year)

---

## Testing Strategy

**Tools:** xUnit + Moq

**Scope (Phase 1 — unit tests):**
- `PeopleCore.Application.Tests` — one test class per service
- Services depend only on interfaces → trivial to mock, no in-memory DB needed

**What is tested:**
- Leave validation (insufficient balance, overlap, gender restriction)
- Leave day counting (excludes weekends + holidays)
- Attendance late/undertime calculation
- Overtime approval authorization (manager → direct reports only)
- Applicant → Employee conversion mapping
- 13th month eligibility logic

**Phase 2 (future):** Integration tests against a test PostgreSQL instance using Testcontainers.

---

## Philippine Compliance

- `employee_government_ids` tracks SSS, PhilHealth, Pag-IBIG, TIN per employee
- `holidays` table pre-seeded with PH regular and special non-working holidays; `is_recurring` flag for annual holidays
- `leave_types` seeded with: Vacation Leave, Sick Leave, Emergency Leave, Maternity Leave (female-only, 105 days), Paternity Leave (male-only, 7 days), Solo Parent Leave
- `is_13th_month_eligible` flag on `employees`; eligibility computed from hire date and employment status
- Leave day counting excludes PH holidays and weekends per labor code

---

## Payroll Integration Notes

The payroll system authenticates with a dedicated service account JWT. Payroll export endpoints return flat, denormalized JSON — no joins required on the payroll side. Recommended polling interval: per cutoff period (1st and 15th of the month).
