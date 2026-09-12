# Deploying PeopleCore

PeopleCore runs on the Hetzner box that already hosts SPMS.Training, under the same Dokploy
installation and behind the same Traefik. Postgres is Neon; employee documents are on
Cloudflare R2.

- Design and rationale: `docs/superpowers/specs/2026-09-12-hetzner-deployment-design.md`
- Stack definition: `docker-compose.dokploy.yml`
- Variables: `.env.example`

## How a deploy happens

Push to `main` → `.github/workflows/docker-publish.yml` builds `ghcr.io/mpmartinez/peoplecore-api:latest`
and `...-web:latest` → Dokploy pulls them, because the compose file sets `pull_policy: always`.

Traefik terminates TLS for `peoplecore.m2netsolutions.com` and splits by path: `/api` and
`/health` reach the API container, everything else reaches nginx serving the Blazor client.

## First-time setup

These are one-off and manual.

### 1. Neon

Create a database named `peoplecore` in the existing Neon project — the same project that
holds `training`. The role stays `neondb_owner`.

Copy the **pooled** connection string (the host ends in `-pooler`) and append
`SSL Mode=VerifyFull;No Reset On Close=true`. Neon serves publicly-trusted certificates, so
`VerifyFull` authenticates the server rather than merely encrypting the channel — this
connection crosses the public internet carrying the whole HRMS and payroll dataset, so
`Trust Server Certificate=true` (which disables that validation) has no place here. Keep
`No Reset On Close=true` exactly as it is: Neon's PgBouncer runs in transaction mode, and
Npgsql's server-side prepared statements break against it without that flag. The result:

```
Host=ep-<id>-pooler.<region>.aws.neon.tech;Database=peoplecore;Username=neondb_owner;Password=<secret>;SSL Mode=VerifyFull;No Reset On Close=true
```

The API applies its 11 migrations itself on first boot. There is nothing to run by hand.

### 2. Cloudflare R2

Create **two** buckets. The API writes to both:

| Bucket | Written by | Holds | Variable |
|---|---|---|---|
| `peoplecore-documents` | `EmployeeDocumentService` | Employee documents | `R2_BUCKET_NAME` |
| `resumes` | `CareersService` | Uploads from the public careers portal | `R2_RESUMES_BUCKET_NAME` |

1. Create both buckets. The names above are the defaults, used when the matching variable
   is unset or blank; to use different ones, set the variables to match what you created.
2. Create an R2 API token scoped to them with **Object Read & Write**.
3. Record the account id, the access key id, and the secret access key.

Whichever names you choose, both buckets must exist before the first upload. `R2StorageService` uploads straight through and
does not call `MakeBucketAsync` the way the MinIO provider does, so a missing bucket is not
created on demand — it fails with `NoSuchBucket`. For `resumes` that failure lands at the
end of a candidate's application, after they have filled the whole form in, which is why
this step asks for both even though only employee documents are exercised by the
post-deploy checks below.

The API logs the provider and both resolved bucket names on the first line of its boot log,
so a mistyped variable is visible there rather than at the first upload. That matters more
on MinIO than here: MinIO *does* call `MakeBucketAsync`, so a typo quietly creates an empty
bucket instead of failing, and whatever was stored under the old name stops being found.

Leave both **private**. The API hands out time-limited presigned URLs
(`R2StorageService.GetPresignedUrlAsync`), so no public access binding is needed — adding
one would expose every document to anyone who guessed a key.

### 3. DNS

Point `peoplecore.m2netsolutions.com` at the Hetzner box with an A record.

Do this **before** the first deploy. Traefik issues the certificate over HTTP-01, which
means the name has to resolve to the box before it can be validated.

### 4. GHCR visibility

The two packages default to private on their first push. Either make them public, or add a
registry credential in Dokploy. Dokploy cannot pull an image it cannot see, and the failure
reads as a generic pull error.

### 5. Dokploy

Create a **Compose** service pointing at `docker-compose.dokploy.yml`, then fill in the
Environment tab from `.env.example`:

| Variable | Source |
|---|---|
| `PEOPLECORE_DB_CONNECTION` | Step 1 |
| `JWT_KEY` | `openssl rand -base64 32` |
| `SEED_ADMIN_EMAIL` | the address that should own the first admin account |
| `SEED_ADMIN_PASSWORD` | chosen now; the API refuses to start without it on a fresh database |
| `R2_ACCOUNT_ID`, `R2_ACCESS_KEY`, `R2_SECRET_KEY`, `R2_BUCKET_NAME`, `R2_RESUMES_BUCKET_NAME` | Step 2 |

Then deploy.

## After the first deploy

Work through all five. The first two prove the stack; the last three cover the things no
amount of configuration review can confirm.

1. **Health** — `curl https://peoplecore.m2netsolutions.com/health` returns
   `{"status":"healthy"}`.
2. **Migrations** — the API container log shows `Applying 11 pending migration(s)...`
   followed by the seeding lines.
3. **Login** — sign in as `SEED_ADMIN_EMAIL` with `SEED_ADMIN_PASSWORD`.
4. **Generate a payslip PDF.** This is the one behaviour the deployment cannot prove by
   construction. Nothing in the code calls `FontManager.RegisterFont`, so QuestPDF and
   PDFsharp resolve glyphs from the container's system fonts — and missing fonts degrade to
   blank or substituted text rather than throwing. A payslip that renders correctly is the
   only evidence that the font packages in the `api` image are doing their job.
5. **Upload an employee document and download it again.** Exercises the R2 round trip and
   the presigned URL, which no other check touches.

## Troubleshooting

**The API container crash-loops with `InvalidOperationException` naming `Seed__AdminPassword`.**
Working as designed: no administrator exists in the database and no password is configured,
so the deployment would have come up with no way to log in. Set `SEED_ADMIN_PASSWORD` and
redeploy.

**The API container crash-loops with `InvalidOperationException` naming `Jwt:Key`.**
`JWT_KEY` is unset or shorter than 32 bytes.

**The log shows `MigrateAsync failed (connection pooler limitation)`.**
The pooler interrupted the migration lock. If the database was already migrated, this is
harmless and the next line confirms connectivity. On a *fresh* database it means the schema
is not there, and the seeding immediately afterwards will fail — redeploy to retry.

**The very first deploy logs an EF Core `fail:` line about `__EFMigrationsHistory`.**
Expected once, on a genuinely empty database, and not an incident. `GetPendingMigrationsAsync`
finds out which migrations have run by querying the history table — which does not exist yet
the first time — and EF logs that failed probe at error level before creating it. The
`Applying N pending migration(s)...` line immediately after is the one that matters. It does
not recur on later deploys.

**The client loads but every API call fails.**
Check `appsettings.Production.json` in the web image matches the deployed hostname, and that
the api router still outranks the web router — if the web router's priority ever meets or
exceeds 100, it swallows `/api` and the client gets `index.html` back instead of JSON.

**A deploy's CSS or JS changes do not reach a returning browser.**
`nginx.conf` serves `/css/` and `/js/` with `no-cache` precisely to prevent this. If it
regresses, check that those paths did not pick up the `_framework` immutable rule — only
`_framework` is content-fingerprinted and safe to cache for a year.

**The first deploy fails with a TLS or certificate-validation error from Npgsql.**
That is `SSL Mode=VerifyFull` in `PEOPLECORE_DB_CONNECTION` — the one part of the connection
string not yet exercised against real Neon. Confirm the string was copied from Neon's
pooled endpoint unmodified; a hand-edited host or a non-Neon Postgres in front of it is the
usual cause of a certificate Npgsql won't validate.
