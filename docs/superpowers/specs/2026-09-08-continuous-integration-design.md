# Continuous Integration — Design

**Date:** 2026-09-08
**Status:** Approved for planning
**Follows:** the repository tests phase

## Context

PeopleCore has 390 tests and **no CI of any kind** — no `.github/workflows`, no other pipeline
configuration, nothing in the README about running them. The suite executes only when somebody
remembers to type `dotnet test`.

That was tolerable when the tests were few and the project was one person's. It is less so now.
The suite encodes statutory arithmetic — SSS brackets, BIR withholding, DOLE premium rates, the
₱90,000 thirteenth-month cap — and the rules that keep a caller from putting an arbitrary figure on
a tax certificate. None of it is enforced by anything except discipline.

The repository tests made this sharper. They need a reachable PostgreSQL, and their design recorded
"CI needs one" as an open risk. `PostgresFixture` already reads `PEOPLECORE_TEST_POSTGRES` for
exactly this purpose, and nothing has ever set it. This closes that loop.

**Goal:** every push to `main` builds the solution, runs all 390 tests against a real PostgreSQL,
and fails on a vulnerable dependency.

## Scope

### In

- One GitHub Actions workflow: restore, build, test, dependency audit.
- A PostgreSQL service container so the repository tests actually run.
- A weekly scheduled run.
- The `Directory.Build.props` path fix that Linux requires (see *Prerequisite*).

### Out

- **Deployment.** This is CI, not CD. Publishing images or artifacts needs targets, secrets and a
  rollback story, and belongs in its own design.
- **A formatting gate.** There is no `.editorconfig`, so `dotnet format --verify-no-changes` would
  enforce whatever defaults the SDK ships rather than a style anyone chose, and would fail on the
  existing code from the first run. Agreeing a style is a prerequisite, not a side effect.
- **Coverage thresholds.** `coverlet.collector` is already referenced, so collecting coverage is
  easy — but a threshold is a number someone has to defend, and picking one arbitrarily to make a
  badge green teaches the team to game it.
- **Branch protection.** A repository setting, not a file in the repository. Worth turning on once
  this is green, but it cannot be committed.

## Prerequisite: the build must work on Linux

`Directory.Build.props` resolves the vendored core project as:

```
$(MSBuildThisFileDirectory)src\M2NET.Core\M2NET.Core.csproj
```

Windows separators. Every build in this repository's history has run on Windows, so this has never
been exercised anywhere else. MSBuild on Unix normally normalises backslashes in path properties,
but "normally" is a poor foundation for the gate on every push, and the failure mode is an
unhelpful missing-project error at restore time.

Changing it to forward slashes costs nothing — MSBuild accepts them on Windows too — and removes
the question entirely. This is the one production file this work touches.

## Approach

**A single GitHub Actions workflow on `ubuntu-latest`.**

One job rather than several. The steps are sequential and share a working tree; splitting build from
test would mean uploading and re-downloading build output to save nothing, since the whole run is a
couple of minutes.

Linux rather than Windows despite Windows being the development platform. It is the cheaper runner,
and the application is already designed to run there — the 2316 renderer embeds its own TTF
specifically so "a payroll run in a Linux container produces the same bytes as one on a developer's
Windows machine". CI running on Linux turns that claim into something checked rather than asserted.

### Triggers

| Trigger | Why |
|---|---|
| `push` to `main` | The main event. This repository's history is linear, single-author, pushed straight to `main` — so a push-triggered build is the only one that would reliably fire. |
| `pull_request` | Costs nothing and works if a PR is ever opened. Designing only for today's workflow would mean rewriting this the first time someone contributes. |
| `workflow_dispatch` | Lets a run be started by hand, which is how a suspected-flaky test gets re-run without an empty commit. |
| `schedule`, weekly | The reason the dependency audit is worth having. |

**The weekly run is not redundant.** A vulnerability scan that only fires on push reports advisories
when you happen to push; the Microsoft.OpenApi CVE found earlier in this project was sitting in code
nobody had touched. A scheduled run catches an advisory published against unchanged code, and
incidentally catches bit-rot when the runner image or the SDK moves under a project that has not.

### PostgreSQL

A `postgres:18-alpine` service container, matching the `m2net-postgres` server the tests were
developed against (18.1). Pinned to the major version rather than `latest`, so a Postgres 19 release
cannot change the meaning of a test run overnight.

The workflow sets:

```
PEOPLECORE_TEST_POSTGRES: Host=localhost;Port=5432;Username=postgres;Password=postgres
```

Deliberately **without** a `Database`. That is the format `PostgresFixture` requires: it appends
`peoplecore_repotests` itself, and the database name is intentionally not configurable so the
truncation guard cannot be bypassed by an environment variable. The seam exists; this is the first
thing to use it.

A health check gates the job on the server accepting connections. Without one, the first test run
races the container's startup and fails intermittently — the worst possible first impression for a
new pipeline, because it teaches people to re-run rather than to read.

### The dependency audit

`dotnet list package --vulnerable --include-transitive`, failing the job on any advisory.

The command exits 0 whether or not it finds anything, so the step greps its output and fails
explicitly. Transitive packages are included because that is where the last one was: a direct
reference to something that pulled in a vulnerable `Microsoft.OpenApi`.

It runs today against zero known vulnerabilities, which is the right moment to make it a hard gate —
it can start failing only on a genuine regression rather than on a backlog someone has to triage
before the pipeline is usable.

**The accepted cost:** an advisory published overnight against an untouched package will fail the
next unrelated push. That is the gate working rather than misfiring, the remedy is a version bump,
and the weekly run means it is usually already known before it blocks anything.

## Architecture

```
.github/workflows/ci.yml     the whole pipeline
Directory.Build.props        forward slashes (the Linux prerequisite)
```

Steps in order:

1. `actions/checkout`
2. `actions/setup-dotnet` pinned to `10.0.x`
3. NuGet package cache, keyed on the project files
4. `dotnet restore PeopleCore.slnx`
5. `dotnet build PeopleCore.slnx --no-restore`
6. `dotnet test PeopleCore.slnx --no-build`
7. the dependency audit

A job-level timeout stops a hung container burning the full six-hour default. A concurrency group
cancels a superseded run when two pushes land close together.

The SDK is pinned by the workflow rather than by a `global.json`, because a `global.json` also
constrains local development and this work has no reason to reach onto anyone's machine. If SDK
drift ever causes a "works locally, fails in CI", that is the moment to add one.

## Testing

CI is itself the test, and it cannot be verified from a laptop — there is no local GitHub Actions
runner here, and a workflow file that looks right is not a workflow that runs.

So verification is: push it, watch the run, and read the log for the things that would silently
pass. Specifically —

- The Infrastructure test project must report **31 tests**, not 0. A workflow that fails to reach
  PostgreSQL could otherwise skip or error them in a way that still shows a green tick if the exit
  code is mishandled.
- The Application project must report **359**.
- The dependency audit step must appear in the log having actually run, rather than being skipped.
- The Linux build must succeed, proving the `Directory.Build.props` fix.

If the first run fails, that is the pipeline doing its job on its first day, and the fix belongs in
the same change rather than in a follow-up.

## Risks

**A newly published CVE blocks an unrelated push.** Accepted deliberately, as above.

**The service container's PostgreSQL diverges from production.** Pinned to the same major as
`m2net-postgres`. If that server is upgraded, this tag should move with it — a comment in the
workflow says so, because the two are only linked by someone remembering.

**Free-tier minutes — checked, not a risk.** `mpmartinez/peoplecore` is public, and GitHub Actions
is free for public repositories. The weekly run costs nothing. Recorded because it would become a
real constraint if the repository were ever made private.

**Pushing the workflow needs `workflow` token scope.** A `.github/workflows/` file cannot be pushed
by a token without it; the push is rejected outright rather than partially applied. The `gh`
credential in use has it, so this is a note for whoever pushes next from a different machine, not a
blocker.

## Acceptance criteria

1. `.github/workflows/ci.yml` exists and runs on push to `main`, on pull requests, on manual
   dispatch, and weekly.
2. A run builds the solution on Linux and reports **359 Application tests and 31 Infrastructure
   tests**, all passing.
3. The Infrastructure tests connect to a `postgres:18-alpine` service container via
   `PEOPLECORE_TEST_POSTGRES`, set without a `Database` component.
4. A vulnerable direct or transitive package fails the job.
5. `Directory.Build.props` uses forward slashes and the solution still builds on Windows.
6. No deployment step, no formatting gate, no coverage threshold.
7. The first real run on GitHub is observed to pass before this is called done — watched with
   `gh run watch`, not inferred from the workflow file looking correct.
