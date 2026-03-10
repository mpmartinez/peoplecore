# Phase 1: Foundation — Solution Scaffold, Domain, Infrastructure

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create the solution structure, all domain entities, EF Core DbContext, Identity, JWT auth, and the generic repository.

**Architecture:** Five projects in one solution. Domain has zero dependencies. Application depends on Domain. Infrastructure depends on Application. API depends on Infrastructure.

**Tech Stack:** .NET 10.0.3 · EF Core · ASP.NET Core Identity · Npgsql · xUnit · Moq

---

### Task 1: Create Solution and Projects

**Files:**
- Create: `PeopleCore.sln`
- Create: `src/PeopleCore.Domain/PeopleCore.Domain.csproj`
- Create: `src/PeopleCore.Application/PeopleCore.Application.csproj`
- Create: `src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj`
- Create: `src/PeopleCore.API/PeopleCore.API.csproj`
- Create: `src/PeopleCore.Web/PeopleCore.Web.csproj`
- Create: `tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj`

**Step 1: Scaffold the solution**

```bash
cd "c:/M2NET PROJECTS/peoplecore"

dotnet new sln -n PeopleCore

dotnet new classlib -n PeopleCore.Domain -o src/PeopleCore.Domain --framework net10.0
dotnet new classlib -n PeopleCore.Application -o src/PeopleCore.Application --framework net10.0
dotnet new classlib -n PeopleCore.Infrastructure -o src/PeopleCore.Infrastructure --framework net10.0
dotnet new webapi -n PeopleCore.API -o src/PeopleCore.API --framework net10.0
dotnet new blazorwasm -n PeopleCore.Web -o src/PeopleCore.Web --framework net10.0

dotnet new xunit -n PeopleCore.Application.Tests -o tests/PeopleCore.Application.Tests --framework net10.0

dotnet sln add src/PeopleCore.Domain/PeopleCore.Domain.csproj
dotnet sln add src/PeopleCore.Application/PeopleCore.Application.csproj
dotnet sln add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj
dotnet sln add src/PeopleCore.API/PeopleCore.API.csproj
dotnet sln add src/PeopleCore.Web/PeopleCore.Web.csproj
dotnet sln add tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj
```

**Step 2: Add project references**

```bash
# Application depends on Domain
dotnet add src/PeopleCore.Application/PeopleCore.Application.csproj reference src/PeopleCore.Domain/PeopleCore.Domain.csproj

# Infrastructure depends on Application + Domain
dotnet add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj reference src/PeopleCore.Application/PeopleCore.Application.csproj
dotnet add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj reference src/PeopleCore.Domain/PeopleCore.Domain.csproj

# API depends on Infrastructure + Application
dotnet add src/PeopleCore.API/PeopleCore.API.csproj reference src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj
dotnet add src/PeopleCore.API/PeopleCore.API.csproj reference src/PeopleCore.Application/PeopleCore.Application.csproj

# Tests depend on Application
dotnet add tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj reference src/PeopleCore.Application/PeopleCore.Application.csproj
dotnet add tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj reference src/PeopleCore.Domain/PeopleCore.Domain.csproj
```

**Step 3: Add NuGet packages**

```bash
# Infrastructure packages
dotnet add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj package Microsoft.EntityFrameworkCore
dotnet add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj package Minio

# API packages
dotnet add src/PeopleCore.API/PeopleCore.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/PeopleCore.API/PeopleCore.API.csproj package Microsoft.EntityFrameworkCore.Design

# Test packages
dotnet add tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj package Moq
dotnet add tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj package FluentAssertions
```

**Step 4: Delete default boilerplate files**

```bash
rm src/PeopleCore.Domain/Class1.cs
rm src/PeopleCore.Application/Class1.cs
rm src/PeopleCore.Infrastructure/Class1.cs
```

**Step 5: Create folder structure**

```bash
# Domain folders
mkdir -p src/PeopleCore.Domain/Entities/Organization
mkdir -p src/PeopleCore.Domain/Entities/Employees
mkdir -p src/PeopleCore.Domain/Entities/Attendance
mkdir -p src/PeopleCore.Domain/Entities/Leave
mkdir -p src/PeopleCore.Domain/Entities/Recruitment
mkdir -p src/PeopleCore.Domain/Entities/Performance
mkdir -p src/PeopleCore.Domain/Enums
mkdir -p src/PeopleCore.Domain/Exceptions

# Application folders
mkdir -p src/PeopleCore.Application/Common/DTOs
mkdir -p src/PeopleCore.Application/Common/Interfaces
mkdir -p src/PeopleCore.Application/Employees/DTOs
mkdir -p src/PeopleCore.Application/Employees/Interfaces
mkdir -p src/PeopleCore.Application/Employees/Services
mkdir -p src/PeopleCore.Application/Organization/DTOs
mkdir -p src/PeopleCore.Application/Organization/Interfaces
mkdir -p src/PeopleCore.Application/Organization/Services
mkdir -p src/PeopleCore.Application/Attendance/DTOs
mkdir -p src/PeopleCore.Application/Attendance/Interfaces
mkdir -p src/PeopleCore.Application/Attendance/Services
mkdir -p src/PeopleCore.Application/Leave/DTOs
mkdir -p src/PeopleCore.Application/Leave/Interfaces
mkdir -p src/PeopleCore.Application/Leave/Services
mkdir -p src/PeopleCore.Application/Recruitment/DTOs
mkdir -p src/PeopleCore.Application/Recruitment/Interfaces
mkdir -p src/PeopleCore.Application/Recruitment/Services
mkdir -p src/PeopleCore.Application/Performance/DTOs
mkdir -p src/PeopleCore.Application/Performance/Interfaces
mkdir -p src/PeopleCore.Application/Performance/Services
mkdir -p src/PeopleCore.Application/PayrollIntegration/DTOs
mkdir -p src/PeopleCore.Application/PayrollIntegration/Interfaces
mkdir -p src/PeopleCore.Application/PayrollIntegration/Services

# Infrastructure folders
mkdir -p src/PeopleCore.Infrastructure/Persistence/Configurations/Organization
mkdir -p src/PeopleCore.Infrastructure/Persistence/Configurations/Employees
mkdir -p src/PeopleCore.Infrastructure/Persistence/Configurations/Attendance
mkdir -p src/PeopleCore.Infrastructure/Persistence/Configurations/Leave
mkdir -p src/PeopleCore.Infrastructure/Persistence/Configurations/Recruitment
mkdir -p src/PeopleCore.Infrastructure/Persistence/Configurations/Performance
mkdir -p src/PeopleCore.Infrastructure/Persistence/Repositories
mkdir -p src/PeopleCore.Infrastructure/Identity
mkdir -p src/PeopleCore.Infrastructure/Storage
mkdir -p src/PeopleCore.Infrastructure/Jobs

# API folders
mkdir -p src/PeopleCore.API/Controllers/Employees
mkdir -p src/PeopleCore.API/Controllers/Organization
mkdir -p src/PeopleCore.API/Controllers/Attendance
mkdir -p src/PeopleCore.API/Controllers/Leave
mkdir -p src/PeopleCore.API/Controllers/Recruitment
mkdir -p src/PeopleCore.API/Controllers/Performance
mkdir -p src/PeopleCore.API/Controllers/PayrollExport
mkdir -p src/PeopleCore.API/Controllers/Auth
mkdir -p src/PeopleCore.API/Middleware
mkdir -p src/PeopleCore.API/Extensions

# Test folders
mkdir -p tests/PeopleCore.Application.Tests/Employees
mkdir -p tests/PeopleCore.Application.Tests/Attendance
mkdir -p tests/PeopleCore.Application.Tests/Leave
mkdir -p tests/PeopleCore.Application.Tests/Recruitment
mkdir -p tests/PeopleCore.Application.Tests/Performance
```

**Step 6: Verify build**

```bash
dotnet build PeopleCore.sln
```
Expected: `Build succeeded. 0 Error(s)`

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution with 5 projects and folder structure"
```

---

### Task 2: Domain — Base Classes and Enums

**Files:**
- Create: `src/PeopleCore.Domain/Entities/AuditableEntity.cs`
- Create: `src/PeopleCore.Domain/Exceptions/DomainException.cs`
- Create: `src/PeopleCore.Domain/Enums/EmploymentStatus.cs`
- Create: `src/PeopleCore.Domain/Enums/GovernmentIdType.cs`
- Create: `src/PeopleCore.Domain/Enums/DocumentType.cs`
- Create: `src/PeopleCore.Domain/Enums/HolidayType.cs`
- Create: `src/PeopleCore.Domain/Enums/LeaveStatus.cs`
- Create: `src/PeopleCore.Domain/Enums/OvertimeStatus.cs`
- Create: `src/PeopleCore.Domain/Enums/ApplicantStatus.cs`
- Create: `src/PeopleCore.Domain/Enums/ReviewStatus.cs`

**Step 1: Create AuditableEntity**

```csharp
// src/PeopleCore.Domain/Entities/AuditableEntity.cs
namespace PeopleCore.Domain.Entities;

public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
```

**Step 2: Create DomainException**

```csharp
// src/PeopleCore.Domain/Exceptions/DomainException.cs
namespace PeopleCore.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
```

**Step 3: Create all enums**

```csharp
// src/PeopleCore.Domain/Enums/EmploymentStatus.cs
namespace PeopleCore.Domain.Enums;
public enum EmploymentStatus { Probationary, Regular, Contractual }

// src/PeopleCore.Domain/Enums/GovernmentIdType.cs
namespace PeopleCore.Domain.Enums;
public enum GovernmentIdType { SSS, PhilHealth, PagIbig, TIN }

// src/PeopleCore.Domain/Enums/DocumentType.cs
namespace PeopleCore.Domain.Enums;
public enum DocumentType { Resume, Contract, GovernmentId, Certificate, Other }

// src/PeopleCore.Domain/Enums/HolidayType.cs
namespace PeopleCore.Domain.Enums;
public enum HolidayType { RegularHoliday, SpecialNonWorking }

// src/PeopleCore.Domain/Enums/LeaveStatus.cs
namespace PeopleCore.Domain.Enums;
public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }

// src/PeopleCore.Domain/Enums/OvertimeStatus.cs
namespace PeopleCore.Domain.Enums;
public enum OvertimeStatus { Pending, Approved, Rejected }

// src/PeopleCore.Domain/Enums/ApplicantStatus.cs
namespace PeopleCore.Domain.Enums;
public enum ApplicantStatus { Applied, Screening, Interview, Offer, Hired, Rejected }

// src/PeopleCore.Domain/Enums/ReviewStatus.cs
namespace PeopleCore.Domain.Enums;
public enum ReviewStatus { Draft, Submitted, Completed }
```

**Step 4: Build and verify**

```bash
dotnet build src/PeopleCore.Domain/PeopleCore.Domain.csproj
```
Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add domain base classes and enums"
```

---

### Task 3: Domain — Organization and Employee Entities

**Files:**
- Create: `src/PeopleCore.Domain/Entities/Organization/Company.cs`
- Create: `src/PeopleCore.Domain/Entities/Organization/Department.cs`
- Create: `src/PeopleCore.Domain/Entities/Organization/Position.cs`
- Create: `src/PeopleCore.Domain/Entities/Organization/Team.cs`
- Create: `src/PeopleCore.Domain/Entities/Employees/Employee.cs`
- Create: `src/PeopleCore.Domain/Entities/Employees/EmployeeGovernmentId.cs`
- Create: `src/PeopleCore.Domain/Entities/Employees/EmergencyContact.cs`
- Create: `src/PeopleCore.Domain/Entities/Employees/EmployeeDocument.cs`

**Step 1: Create organization entities**

```csharp
// src/PeopleCore.Domain/Entities/Organization/Company.cs
namespace PeopleCore.Domain.Entities.Organization;

public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public ICollection<Department> Departments { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Organization/Department.cs
namespace PeopleCore.Domain.Entities.Organization;

public class Department : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid? ParentDepartmentId { get; set; }
    public Department? ParentDepartment { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public ICollection<Department> SubDepartments { get; set; } = [];
    public ICollection<Position> Positions { get; set; } = [];
    public ICollection<Team> Teams { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Organization/Position.cs
namespace PeopleCore.Domain.Entities.Organization;

public class Position : AuditableEntity
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Level { get; set; }
}

// src/PeopleCore.Domain/Entities/Organization/Team.cs
namespace PeopleCore.Domain.Entities.Organization;

public class Team : AuditableEntity
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}
```

**Step 2: Create employee entities**

```csharp
// src/PeopleCore.Domain/Entities/Employees/Employee.cs
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Domain.Entities.Employees;

public class Employee : AuditableEntity
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Suffix { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? CivilStatus { get; set; }
    public string Nationality { get; set; } = "Filipino";
    public string? PersonalEmail { get; set; }
    public string WorkEmail { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public string? Address { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
    public Guid? ReportingManagerId { get; set; }
    public Employee? ReportingManager { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public string EmploymentType { get; set; } = "FullTime";
    public DateOnly HireDate { get; set; }
    public DateOnly? RegularizationDate { get; set; }
    public DateOnly? SeparationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Is13thMonthEligible { get; set; } = true;
    public ICollection<EmployeeGovernmentId> GovernmentIds { get; set; } = [];
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = [];
    public ICollection<EmployeeDocument> Documents { get; set; } = [];

    public string FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();
}

// src/PeopleCore.Domain/Entities/Employees/EmployeeGovernmentId.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Employees;

public class EmployeeGovernmentId : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public GovernmentIdType IdType { get; set; }
    public string IdNumber { get; set; } = string.Empty;
}

// src/PeopleCore.Domain/Entities/Employees/EmergencyContact.cs
namespace PeopleCore.Domain.Entities.Employees;

public class EmergencyContact : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }
}

// src/PeopleCore.Domain/Entities/Employees/EmployeeDocument.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Employees;

public class EmployeeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DocumentType DocumentType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public long? FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public Guid? UploadedBy { get; set; }
}
```

**Step 3: Build**

```bash
dotnet build src/PeopleCore.Domain/PeopleCore.Domain.csproj
```
Expected: `Build succeeded. 0 Error(s)`

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add organization and employee domain entities"
```

---

### Task 4: Domain — Attendance, Leave, Recruitment, Performance Entities

**Files:**
- Create: `src/PeopleCore.Domain/Entities/Attendance/Holiday.cs`
- Create: `src/PeopleCore.Domain/Entities/Attendance/AttendanceRecord.cs`
- Create: `src/PeopleCore.Domain/Entities/Attendance/OvertimeRequest.cs`
- Create: `src/PeopleCore.Domain/Entities/Leave/LeaveType.cs`
- Create: `src/PeopleCore.Domain/Entities/Leave/LeaveBalance.cs`
- Create: `src/PeopleCore.Domain/Entities/Leave/LeaveRequest.cs`
- Create: `src/PeopleCore.Domain/Entities/Recruitment/JobPosting.cs`
- Create: `src/PeopleCore.Domain/Entities/Recruitment/Applicant.cs`
- Create: `src/PeopleCore.Domain/Entities/Recruitment/InterviewStage.cs`
- Create: `src/PeopleCore.Domain/Entities/Performance/ReviewCycle.cs`
- Create: `src/PeopleCore.Domain/Entities/Performance/PerformanceReview.cs`
- Create: `src/PeopleCore.Domain/Entities/Performance/KpiItem.cs`

**Step 1: Create Attendance entities**

```csharp
// src/PeopleCore.Domain/Entities/Attendance/Holiday.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Attendance;

public class Holiday : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly HolidayDate { get; set; }
    public HolidayType HolidayType { get; set; }
    public bool IsRecurring { get; set; } = false;
}

// src/PeopleCore.Domain/Entities/Attendance/AttendanceRecord.cs
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Attendance;

public class AttendanceRecord : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly AttendanceDate { get; set; }
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public int LateMinutes { get; set; } = 0;
    public int UndertimeMinutes { get; set; } = 0;
    public int OvertimeMinutes { get; set; } = 0;
    public bool IsPresent { get; set; } = false;
    public bool IsHoliday { get; set; } = false;
    public HolidayType? HolidayType { get; set; }
    public string? Remarks { get; set; }
}

// src/PeopleCore.Domain/Entities/Attendance/OvertimeRequest.cs
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Attendance;

public class OvertimeRequest : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly OvertimeDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public OvertimeStatus Status { get; set; } = OvertimeStatus.Pending;
    public Guid? ApprovedBy { get; set; }
    public Employee? Approver { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
}
```

**Step 2: Create Leave entities**

```csharp
// src/PeopleCore.Domain/Entities/Leave/LeaveType.cs
namespace PeopleCore.Domain.Entities.Leave;

public class LeaveType : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal MaxDaysPerYear { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool IsCarryOver { get; set; } = false;
    public decimal? CarryOverMaxDays { get; set; }
    public string? GenderRestriction { get; set; }  // "Male", "Female", null
    public bool RequiresDocument { get; set; } = false;
    public bool IsActive { get; set; } = true;
}

// src/PeopleCore.Domain/Entities/Leave/LeaveBalance.cs
namespace PeopleCore.Domain.Entities.Leave;

public class LeaveBalance : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal TotalDays { get; set; } = 0;
    public decimal UsedDays { get; set; } = 0;
    public decimal CarriedOverDays { get; set; } = 0;
    public decimal RemainingDays => TotalDays + CarriedOverDays - UsedDays;
}

// src/PeopleCore.Domain/Entities/Leave/LeaveRequest.cs
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Leave;

public class LeaveRequest : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TotalDays { get; set; }
    public string? Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public Guid? ApprovedBy { get; set; }
    public Employee? Approver { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
}
```

**Step 3: Create Recruitment entities**

```csharp
// src/PeopleCore.Domain/Entities/Recruitment/JobPosting.cs
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Domain.Entities.Recruitment;

public class JobPosting : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    public int Vacancies { get; set; } = 1;
    public string Status { get; set; } = "Draft";  // Draft, Open, Closed
    public DateTime? PostedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public ICollection<Applicant> Applicants { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Recruitment/Applicant.cs
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Recruitment;

public class Applicant : AuditableEntity
{
    public Guid JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ResumeStorageKey { get; set; }
    public ApplicantStatus Status { get; set; } = ApplicantStatus.Applied;
    public Guid? ConvertedEmployeeId { get; set; }
    public Employee? ConvertedEmployee { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public ICollection<InterviewStage> InterviewStages { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Recruitment/InterviewStage.cs
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Recruitment;

public class InterviewStage : AuditableEntity
{
    public Guid ApplicantId { get; set; }
    public Applicant Applicant { get; set; } = null!;
    public string StageName { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public Guid? InterviewerId { get; set; }
    public Employee? Interviewer { get; set; }
    public string? Outcome { get; set; }  // Passed, Failed, NoShow
    public string? Notes { get; set; }
}
```

**Step 4: Create Performance entities**

```csharp
// src/PeopleCore.Domain/Entities/Performance/ReviewCycle.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Performance;

public class ReviewCycle : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public int? Quarter { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Draft;
    public ICollection<PerformanceReview> Reviews { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Performance/PerformanceReview.cs
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Performance;

public class PerformanceReview : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid ReviewCycleId { get; set; }
    public ReviewCycle ReviewCycle { get; set; } = null!;
    public Guid ReviewerId { get; set; }
    public Employee Reviewer { get; set; } = null!;
    public decimal? SelfEvaluationScore { get; set; }
    public decimal? ManagerScore { get; set; }
    public decimal? FinalScore { get; set; }
    public string? SelfEvaluationComments { get; set; }
    public string? ManagerComments { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Draft;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<KpiItem> KpiItems { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Performance/KpiItem.cs
namespace PeopleCore.Domain.Entities.Performance;

public class KpiItem : AuditableEntity
{
    public Guid PerformanceReviewId { get; set; }
    public PerformanceReview PerformanceReview { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Actual { get; set; }
    public decimal Weight { get; set; } = 0;
    public decimal? Score { get; set; }
}
```

**Step 5: Build**

```bash
dotnet build src/PeopleCore.Domain/PeopleCore.Domain.csproj
```
Expected: `Build succeeded. 0 Error(s)`

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add attendance, leave, recruitment, performance domain entities"
```

---

### Task 5: Application — Common Interfaces and DTOs

**Files:**
- Create: `src/PeopleCore.Application/Common/DTOs/PagedResult.cs`
- Create: `src/PeopleCore.Application/Common/Interfaces/IRepository.cs`
- Create: `src/PeopleCore.Application/Common/Interfaces/IStorageService.cs`
- Create: `src/PeopleCore.Application/Common/Interfaces/ICurrentUserService.cs`

**Step 1: Create PagedResult**

```csharp
// src/PeopleCore.Application/Common/DTOs/PagedResult.cs
namespace PeopleCore.Application.Common.DTOs;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
        => new() { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
}
```

**Step 2: Create IRepository**

```csharp
// src/PeopleCore.Application/Common/Interfaces/IRepository.cs
using System.Linq.Expressions;

namespace PeopleCore.Application.Common.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
}
```

**Step 3: Create IStorageService**

```csharp
// src/PeopleCore.Application/Common/Interfaces/IStorageService.cs
namespace PeopleCore.Application.Common.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct = default);
    Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600);
}
```

**Step 4: Create ICurrentUserService**

```csharp
// src/PeopleCore.Application/Common/Interfaces/ICurrentUserService.cs
namespace PeopleCore.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? EmployeeId { get; }
    string? Email { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsInRole(string role);
}
```

**Step 5: Build**

```bash
dotnet build src/PeopleCore.Application/PeopleCore.Application.csproj
```
Expected: `Build succeeded. 0 Error(s)`

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add common application interfaces and PagedResult DTO"
```

---

### Task 6: Infrastructure — AppDbContext and EF Configurations

**Files:**
- Create: `src/PeopleCore.Infrastructure/Identity/ApplicationUser.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Organization/DepartmentConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Employees/EmployeeConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Attendance/AttendanceRecordConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs`

**Step 1: Create ApplicationUser**

```csharp
// src/PeopleCore.Infrastructure/Identity/ApplicationUser.cs
using Microsoft.AspNetCore.Identity;

namespace PeopleCore.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public Guid? EmployeeId { get; set; }
}
```

**Step 2: Create AppDbContext**

```csharp
// src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Organization
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Team> Teams => Set<Team>();

    // Employees
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeGovernmentId> EmployeeGovernmentIds => Set<EmployeeGovernmentId>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    // Attendance
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();

    // Leave
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    // Recruitment
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<InterviewStage> InterviewStages => Set<InterviewStage>();

    // Performance
    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<KpiItem> KpiItems => Set<KpiItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Use snake_case for all table and column names
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
    }
}
```

**Step 3: Create key EF configurations (representative examples)**

```csharp
// src/PeopleCore.Infrastructure/Persistence/Configurations/Organization/DepartmentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Organization;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Code).HasMaxLength(50);
        builder.HasOne(d => d.ParentDepartment)
               .WithMany(d => d.SubDepartments)
               .HasForeignKey(d => d.ParentDepartmentId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Company)
               .WithMany(c => c.Departments)
               .HasForeignKey(d => d.CompanyId);
    }
}

// src/PeopleCore.Infrastructure/Persistence/Configurations/Employees/EmployeeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Employees;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.EmployeeNumber).IsUnique();
        builder.Property(e => e.EmployeeNumber).IsRequired().HasMaxLength(50);
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.WorkEmail).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EmploymentStatus).HasConversion<string>();
        builder.HasOne(e => e.ReportingManager)
               .WithMany()
               .HasForeignKey(e => e.ReportingManagerId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.GovernmentIds)
               .WithOne(g => g.Employee)
               .HasForeignKey(g => g.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.EmergencyContacts)
               .WithOne(c => c.Employee)
               .HasForeignKey(c => c.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(e => e.FullName);
    }
}

// src/PeopleCore.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Leave;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Status).HasConversion<string>();
        builder.Property(l => l.TotalDays).HasPrecision(5, 2);
        builder.HasOne(l => l.Employee)
               .WithMany()
               .HasForeignKey(l => l.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Approver)
               .WithMany()
               .HasForeignKey(l => l.ApprovedBy)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.EmployeeId, l.StartDate, l.EndDate });
    }
}

// src/PeopleCore.Infrastructure/Persistence/Configurations/Attendance/AttendanceRecordConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Attendance;

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.EmployeeId, a.AttendanceDate }).IsUnique();
        builder.Property(a => a.HolidayType).HasConversion<string>();
    }
}
```

**Step 4: Build**

```bash
dotnet build src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj
```
Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add AppDbContext, Identity user, and EF Core configurations"
```

---

### Task 7: Infrastructure — Generic Repository

**Files:**
- Create: `src/PeopleCore.Infrastructure/Persistence/Repositories/Repository.cs`

**Step 1: Create generic repository**

```csharp
// src/PeopleCore.Infrastructure/Persistence/Repositories/Repository.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<T> DbSet;

    public Repository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await DbSet.FindAsync([id], ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await DbSet.ToListAsync(ct);

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await DbSet.AddAsync(entity, ct);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        DbSet.Update(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        DbSet.Remove(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => predicate is null
            ? await DbSet.CountAsync(ct)
            : await DbSet.CountAsync(predicate, ct);
}
```

**Step 2: Commit**

```bash
git add -A
git commit -m "feat: add generic repository implementation"
```

---

### Task 8: API — JWT Auth, Middleware, DI Wiring, and Initial Migration

**Files:**
- Create: `src/PeopleCore.API/Middleware/ExceptionHandlingMiddleware.cs`
- Create: `src/PeopleCore.API/Middleware/CurrentUserMiddleware.cs`
- Create: `src/PeopleCore.Infrastructure/Identity/CurrentUserService.cs`
- Create: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Modify: `src/PeopleCore.API/Program.cs`
- Modify: `src/PeopleCore.API/appsettings.json`
- Create: `src/PeopleCore.API/Controllers/Auth/AuthController.cs`

**Step 1: Create ExceptionHandlingMiddleware**

```csharp
// src/PeopleCore.API/Middleware/ExceptionHandlingMiddleware.cs
using System.Text.Json;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            var problem = new { title = "Business Rule Violation", detail = ex.Message, status = 400 };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
        catch (KeyNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";
            var problem = new { title = "Not Found", detail = ex.Message, status = 404 };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            var problem = new { title = "Internal Server Error", detail = "An unexpected error occurred.", status = 500 };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}
```

**Step 2: Create CurrentUserService**

```csharp
// src/PeopleCore.Infrastructure/Identity/CurrentUserService.cs
using System.Security.Claims;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.Infrastructure.Identity;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid? EmployeeId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("employee_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyList<string> Roles =>
        _httpContextAccessor.HttpContext?.User?.FindAll(ClaimTypes.Role)
            .Select(c => c.Value).ToList() ?? [];

    public bool IsInRole(string role) => Roles.Contains(role);
}
```

**Step 3: Create AuthController**

```csharp
// src/PeopleCore.API/Controllers/Auth/AuthController.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null) return Unauthorized(new { message = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded) return Unauthorized(new { message = "Invalid credentials." });

        var roles = await _userManager.GetRolesAsync(user);
        var token = GenerateJwtToken(user, roles);

        return Ok(new { token, email = user.Email, roles });
    }

    private string GenerateJwtToken(ApplicationUser user, IList<string> roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (user.EmployeeId.HasValue)
            claims.Add(new Claim("employee_id", user.EmployeeId.Value.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["Jwt:ExpiryMinutes"])),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Email, string Password);
```

**Step 4: Create ServiceExtensions**

```csharp
// src/PeopleCore.API/Extensions/ServiceExtensions.cs
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Infrastructure.Identity;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.API.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidAudience = configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!))
            };
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
```

**Step 5: Update Program.cs**

```csharp
// src/PeopleCore.API/Program.cs
using PeopleCore.API.Extensions;
using PeopleCore.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:5002")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

**Step 6: Update appsettings.json**

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=peoplecore;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "Key": "your-super-secret-key-at-least-32-characters-long",
    "Issuer": "peoplecore-api",
    "Audience": "peoplecore-web",
    "ExpiryMinutes": "60"
  },
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "BucketName": "peoplecore-documents",
    "UseSSL": false
  },
  "AllowedOrigins": "http://localhost:5002",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**Step 7: Create and run initial migration**

```bash
cd "c:/M2NET PROJECTS/peoplecore"
dotnet ef migrations add InitialCreate --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
dotnet ef database update --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
```
Expected: `Done. To undo this action, use 'ef migrations remove'`

**Step 8: Build entire solution**

```bash
dotnet build PeopleCore.sln
```
Expected: `Build succeeded. 0 Error(s)`

**Step 9: Commit**

```bash
git add -A
git commit -m "feat: add auth controller, JWT config, middleware, DI wiring, and initial migration"
```

---

**Phase 1 complete.** Continue with [Phase 2 — Organization Module](2026-03-10-hrms-phase-2-organization.md).
