# PeopleCore HRMS — Implementation Plan Index

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement each phase plan task-by-task.

**Goal:** Build a complete internal HRMS for a Philippine-based company integrating with a separate payroll system.

**Architecture:** Layered monolith (Domain → Application → Infrastructure → API), standalone Blazor WASM frontend, ASP.NET Core Identity + JWT auth, MinIO for document storage, separate PostgreSQL DB from payroll with REST export endpoints.

**Tech Stack:** .NET 10.0.3 · ASP.NET Core Web API · Blazor WASM · PostgreSQL · EF Core · ASP.NET Core Identity · MinIO SDK · xUnit · Moq

---

## Phase Plans

| Phase | File | Covers |
|---|---|---|
| 1 | [phase-1-foundation.md](2026-03-10-hrms-phase-1-foundation.md) | Solution scaffold, Domain entities, DbContext, Identity, JWT, Generic repository |
| 2 | [phase-2-organization.md](2026-03-10-hrms-phase-2-organization.md) | Departments, Positions, Teams — services, tests, controllers |
| 3 | [phase-3-employees.md](2026-03-10-hrms-phase-3-employees.md) | Employee CRUD, Government IDs, Emergency Contacts, Documents (MinIO) |
| 4 | [phase-4-attendance.md](2026-03-10-hrms-phase-4-attendance.md) | Holidays, Time-in/out, Late/Undertime, Overtime requests |
| 5 | [phase-5-leave.md](2026-03-10-hrms-phase-5-leave.md) | Leave types, Balances, Requests, Accrual hosted service |
| 6 | [phase-6-recruitment.md](2026-03-10-hrms-phase-6-recruitment.md) | Job postings, Applicants, Interviews, Convert to Employee |
| 7 | [phase-7-performance.md](2026-03-10-hrms-phase-7-performance.md) | Review cycles, Performance reviews, KPI items |
| 8 | [phase-8-payroll-export.md](2026-03-10-hrms-phase-8-payroll-export.md) | Payroll integration export endpoints |
| 9 | [phase-9-blazor-web.md](2026-03-10-hrms-phase-9-blazor-web.md) | Blazor WASM standalone: auth, layout, all module pages |

---

## Execution Order

Phases must be executed in order — each phase depends on the previous.

## Global Conventions

- All C# files use `namespace PeopleCore.<Project>.<Module>` pattern
- PostgreSQL: snake_case table/column names via EF Core conventions
- All controllers return `ActionResult<T>` and use `[Authorize]`
- Services throw `DomainException` (custom) for business rule violations
- `ExceptionHandlingMiddleware` maps `DomainException` → 400 ProblemDetails
- All list endpoints return `PagedResult<T>` with `?page=1&pageSize=20`
- UUIDs for all PKs (`Guid` in C#)
- `AuditableEntity` base class on all domain entities
