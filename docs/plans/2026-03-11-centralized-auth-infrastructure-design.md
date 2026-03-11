# Centralized Auth & Infrastructure Design

**Date:** 2026-03-11

## Context

PeopleCore is a multi-tenant SaaS platform for manning agencies consisting of 6 interconnected apps:

- **PeopleCore (HR/HRMS)** — this repo
- **Payroll**
- **Accounting**
- **SPMSv2**
- **Allotment**
- **Training**

Each app is built with **ASP.NET Core (.NET 10) + Blazor WASM**. Clients (manning agencies) may subscribe to any subset of these apps.

---

## Design Decisions (Agreed)

### 1. Centralized Auth: Keycloak

- **Why:** Zero auth code to write, built-in SSO, multi-tenancy via Realms, per-client app access control via Clients, admin UI, 2FA, audit logs
- **Multi-tenancy model:** One Realm per manning agency
- **App access control:** Each app is a Keycloak Client; enable/disable per realm (agency)
- **Token strategy:** Standard JWT (OIDC), self-contained — apps validate locally without calling Keycloak on every request

### 2. High Availability Keycloak: Digital Ocean + Hetzner

- **Primary:** Digital Ocean VPS (2 vCPU / 2GB RAM, ~$18/mo)
- **Backup:** Hetzner VPS (2 vCPU / 2GB RAM, ~$5/mo)
- **Failover:** Cloudflare DNS health checks with automatic failover (60s TTL)
- **Phase 1:** Single primary + standby (both point to same DB)
- **Phase 2:** Full clustering with session sync (when needed)

### 3. High Availability PostgreSQL: Supabase + Neon

- **Primary:** Supabase PostgreSQL (Pro, $25/mo) — connection pooling via PgBouncer built-in
- **Replica:** Neon PostgreSQL (free tier to start) — logical replication from Supabase
- **Keycloak DB:** Hosted on Supabase, replica on Neon
- **App DBs:** Each app gets its own schema/database on Supabase

### 4. Object Storage: Cloudflare R2

- **Why:** Zero egress fees, S3-compatible API, native CDN integration, 10GB free tier
- **Structure:** Per-tenant key prefix `{tenantId}/{module}/{resource}`
- **Integration:** AWS SDK for .NET (S3-compatible)
- **Use cases:** Seafarer documents, payslips, certificates, DB backups

### 5. Blazor WASM Performance

- PWA + Service Worker caching (biggest impact — subsequent loads are instant)
- Brotli compression on web server
- IL Trimming (smaller download)
- Lazy loading per module
- .NET 10 Static SSR prerender for first contentful paint

---

## Architecture Overview

```
                    Cloudflare
                    ├── DNS + Health Check Failover
                    ├── R2 Object Storage (files + backups)
                    ├── CDN (serves static assets + R2 files)
                    └── WAF (protects all endpoints)
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
    Digital Ocean VPS           Hetzner VPS
    Keycloak (Primary)          Keycloak (Standby)
    auth.yourdomain.com         (failover target)
              │                         │
              └────────────┬────────────┘
                           │ JDBC
              ┌────────────┴────────────┐
              ▼                         ▼
    Supabase PostgreSQL         Neon PostgreSQL
    (Primary DB)                (Logical Replica)
    ├── keycloak DB             ├── keycloak replica
    ├── hrms DB                 └── hrms replica
    ├── payroll DB
    └── ... (per app)
                    ▲
                    │ S3-compatible API
              Cloudflare R2
              {tenantId}/seafarers/documents/
              {tenantId}/payroll/payslips/
              {tenantId}/training/certificates/
              backups/db/
              backups/keycloak/
```

## Per-Agency Realm Structure

```
Keycloak Server
├── Realm: agency-a
│   ├── Clients: hrms ✅ payroll ✅ accounting ✅ spmsv2 ✅ allotment ✅ training ✅
│   └── Users, Roles, Groups for Agency A
├── Realm: agency-b
│   ├── Clients: payroll ✅ hrms ✅  (accounting ❌ spmsv2 ❌ ...)
│   └── Users isolated from Agency A
└── Realm: agency-c
    ├── Clients: accounting ✅ only
    └── Users isolated
```

## Token Flow

```
User → Blazor WASM → redirect to Keycloak login page
                   ← JWT access token (15 min) + refresh token (8 hr)
     ← app renders

App  → API call with Bearer token
     → API validates JWT signature locally (cached public key)
     ← no Keycloak call needed per request
```

Internet outage: already-logged-in users continue working for up to 8 hours.

---

## Cost Summary

| Infrastructure | Provider | Cost/month |
|----------------|----------|-----------|
| Keycloak Primary | Digital Ocean (2vCPU/2GB) | $18 |
| Keycloak Standby | Hetzner (2vCPU/2GB) | $5 |
| PostgreSQL Primary | Supabase Pro | $25 |
| PostgreSQL Replica | Neon | $0 (free tier) |
| DNS + Failover + CDN + WAF | Cloudflare | $0 |
| R2 Storage (50GB) | Cloudflare R2 | ~$0.75 |
| **Total** | | **~$49/month** |