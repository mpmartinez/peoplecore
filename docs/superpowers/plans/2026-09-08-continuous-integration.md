# Continuous Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every push to `main` builds the solution on Linux, runs all 390 tests against a real PostgreSQL, and fails on a vulnerable dependency.

**Architecture:** One GitHub Actions workflow, one job, on `ubuntu-latest`, with a `postgres:18-alpine` service container. Triggered by pushes to `main`, pull requests, manual dispatch, and a weekly schedule.

**Tech Stack:** GitHub Actions, .NET 10 SDK, PostgreSQL 18.

## Global Constraints

- **This cannot be verified locally.** There is no GitHub Actions runner here. A workflow file that looks correct is not a workflow that runs, so the work is not done until a real run has been watched and read. Task 3 exists for that and must not be skipped.
- **Pushing requires `workflow` token scope.** A token without it has the push rejected outright when it touches `.github/workflows/`. The `gh` credential in use has it (`gist, read:org, repo, workflow, write:packages`).
- **The test step must report 359 and 31.** A pipeline that reaches PostgreSQL but silently runs zero database tests would still show a green tick. Reading the counts is the check, not the exit code.
- **`PEOPLECORE_TEST_POSTGRES` carries no `Database` component.** `PostgresFixture` appends `peoplecore_repotests` itself, and the database name is deliberately not configurable so the truncation guard cannot be bypassed by an environment variable. Adding `Database=` to that value would not help and would misrepresent how the fixture works.
- **No deployment step, no formatting gate, no coverage threshold.** All three were excluded by the design for stated reasons.
- **Only two files change:** `Directory.Build.props` and the new workflow. Nothing under `src/` or `tests/`.

---

### Task 1: Make the build work on Linux

**Files:**
- Modify: `Directory.Build.props`

**Interfaces:**
- Produces: nothing new. `M2NetCoreProject` keeps its name and meaning; only the separator changes.

**Why this is first.** Every build in this repository's history has run on Windows, and
`Directory.Build.props` resolves the vendored core project with backslashes. MSBuild on Unix
normally normalises those, but if it does not, the whole pipeline fails at restore with an
unhelpful missing-project error — and it would be diagnosed as "CI is broken" rather than as a path
separator. Removing the question first costs one character class.

- [ ] **Step 1: Change the separators**

In `Directory.Build.props`, replace the property value:

```xml
    <M2NetCoreProject>$(MSBuildThisFileDirectory)src\M2NET.Core\M2NET.Core.csproj</M2NetCoreProject>
```

with:

```xml
    <M2NetCoreProject>$(MSBuildThisFileDirectory)src/M2NET.Core/M2NET.Core.csproj</M2NetCoreProject>
```

Forward slashes work on Windows too — MSBuild normalises them there — so this is portable rather
than a swap of one platform's problem for the other's.

- [ ] **Step 2: Prove the Windows build still works**

```bash
dotnet build PeopleCore.slnx
```

Expected: `0 Error(s)`. This is the only local evidence available that the change is safe; the
Linux half is proven in Task 3.

- [ ] **Step 3: Run the tests**

```bash
dotnet test PeopleCore.slnx
```

Expected: `Failed: 0` across both projects — 359 and 31. A path change should not affect tests, and
this confirms it did not.

- [ ] **Step 4: Commit**

```bash
git add Directory.Build.props
git commit -m "build: use portable separators when resolving the vendored core project"
```

---

### Task 2: The workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `PeopleCore.slnx`, and `PostgresFixture`'s `PEOPLECORE_TEST_POSTGRES` contract — a connection string with no `Database` component.

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:
  schedule:
    # Mondays, 02:00 UTC. This is the point of the dependency audit rather than decoration: a scan
    # that only fires on push reports advisories when you happen to push, and the Microsoft.OpenApi
    # CVE found in this project was sitting in code nobody had touched. It also catches bit-rot
    # when the runner image or the SDK moves under a project that has not changed.
    - cron: '0 2 * * 1'

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    timeout-minutes: 20

    services:
      postgres:
        # Matches the m2net-postgres server the repository tests were developed against (18.1).
        # Pinned to the major so a PostgreSQL 19 release cannot change what a test run means
        # overnight. If that server is upgraded, move this tag with it - nothing links the two
        # except somebody remembering.
        image: postgres:18-alpine
        env:
          POSTGRES_USER: postgres
          POSTGRES_PASSWORD: postgres
        ports:
          - 5432:5432
        # Without a health check the first test run races the server's startup and fails
        # intermittently - the worst possible first impression for a pipeline, because it teaches
        # people to re-run rather than to read.
        options: >-
          --health-cmd "pg_isready -U postgres"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 10

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          cache: true
          cache-dependency-path: '**/*.csproj'

      - name: Restore
        run: dotnet restore PeopleCore.slnx

      - name: Build
        run: dotnet build PeopleCore.slnx --no-restore --configuration Release

      - name: Test
        env:
          # No Database component on purpose. PostgresFixture appends peoplecore_repotests itself,
          # and the database name is deliberately not configurable - making it so is exactly how
          # the fixture's truncation guard would get bypassed by accident.
          PEOPLECORE_TEST_POSTGRES: Host=localhost;Port=5432;Username=postgres;Password=postgres
        run: dotnet test PeopleCore.slnx --no-build --configuration Release --verbosity normal

      - name: Audit dependencies
        run: |
          # `dotnet list package --vulnerable` exits 0 whether or not it finds anything, so the
          # result has to be inspected rather than the exit code trusted. Transitive packages are
          # included because that is where the last one was: a vulnerable Microsoft.OpenApi pulled
          # in by a direct reference.
          #
          # Parsed as JSON rather than grepped for English. A security gate keyed on prose stops
          # firing the day that prose is reworded, and it fails OPEN - silently reporting clean
          # forever. The JSON shape is structural: under --vulnerable, a project with nothing to
          # report carries only "path", and a "frameworks" array appears only when there are
          # findings.
          dotnet list PeopleCore.slnx package --vulnerable --include-transitive --format json > audit.json
          python3 - audit.json <<'PYCHECK'
          import json, sys

          data = json.load(open(sys.argv[1]))
          hits = []
          for project in data.get('projects', []):
              for framework in project.get('frameworks') or []:
                  for key in ('topLevelPackages', 'transitivePackages'):
                      for package in framework.get(key) or []:
                          hits.append(f"{package['id']} {package.get('resolvedVersion', '')}"
                                      f"  ({project['path']})")

          if hits:
              print('::error::Vulnerable packages found:')
              for hit in hits:
                  print('  ' + hit)
              sys.exit(1)

          print('No vulnerable packages.')
          PYCHECK
```

Note `--configuration Release` on both build and test. They must match, or `--no-build` fails
looking for output that was written to the other configuration's folder.

The audit check was verified locally in both directions before being written here: against the
real `dotnet list` output it reports no hits, and against that same output with a finding planted
in the shape `dotnet` emits it reports `Microsoft.OpenApi 1.6.14`. A gate that has only ever been
seen to pass is not known to work.

- [ ] **Step 2: Check the YAML parses**

```bash
python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('valid YAML')"
```

Expected: `valid YAML`. This catches indentation mistakes locally instead of after a push. It does
**not** validate the workflow schema — only GitHub can do that, and Task 3 is where it happens.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: build, test against PostgreSQL, and audit dependencies on every push"
```

---

### Task 3: Push it and watch it run

**Files:** none. This task produces evidence, not code.

**This task cannot be skipped.** Everything up to here is a file that looks right. The spec's
acceptance criteria require an observed run, because the failure modes that matter — a service
container that never becomes reachable, a test step that runs zero database tests and still exits
0, an audit step that is silently skipped — all look like success in the file.

- [ ] **Step 1: Merge to main and push**

```bash
git checkout main && git merge --ff-only claude/workspace-analysis-72a977 && git push origin main
```

The push itself triggers the first run. If the push is rejected with a scope error, the token
lacks `workflow` — stop and report it rather than working around it.

- [ ] **Step 2: Watch the run**

```bash
gh run watch --exit-status
```

Expected: the run completes successfully. `--exit-status` makes a failed run a non-zero exit here,
so a failure cannot be mistaken for a pass.

- [ ] **Step 3: Read the log for what a green tick would not tell you**

```bash
gh run view --log | grep -E "Passed!|Failed!|vulnerable packages|Determining projects"
```

Verify all three:

1. **`PeopleCore.Application.Tests` reports `Passed: 359`.**
2. **`PeopleCore.Infrastructure.Tests` reports `Passed: 31`** — not 0, and not absent. This is the
   line that proves the service container was actually reachable. A run where PostgreSQL never came
   up could otherwise finish green with the database tests never having run.
3. **The audit step ran** and reported no vulnerable packages, rather than being skipped.

If the Infrastructure count is 0 or the project is missing from the output, the pipeline is
reporting success while testing nothing. Fix it before proceeding — that is a worse state than no
CI, because it manufactures false confidence.

- [ ] **Step 4: If the first run fails, fix it here**

A first-run failure is the pipeline doing its job on day one, not a reason to defer. Diagnose from
the log, fix, commit and push again. Likely causes, in rough order of probability:

- **The NuGet cache step errors.** `cache: true` on `setup-dotnet` is an optimisation worth about
  thirty seconds. Removing `cache` and `cache-dependency-path` is the fix; do not spend time on it.
- **`dotnet-version: '10.0.x'` resolves to an SDK the projects reject.** Pin more tightly to the
  locally-verified `10.0.400`.
- **The Linux build fails on the vendored core project.** Task 1 exists to prevent this; if it
  still happens, the path needs `$([MSBuild]::NormalizePath(...))` rather than plain separators.
- **The database tests fail to connect.** Check the health check passed before the test step
  started, and that `PEOPLECORE_TEST_POSTGRES` has no `Database` component.

- [ ] **Step 5: Record the result**

Report the run's URL and the three verified numbers. If anything was changed in Step 4, say what
and why — a pipeline that needed fixing on its first run is ordinary, and hiding it makes the next
person distrust the log.

---

## Definition of done

- [ ] `.github/workflows/ci.yml` exists on `main`.
- [ ] A real GitHub Actions run has completed successfully and been watched, not inferred.
- [ ] That run's log shows `Passed: 359` for Application and `Passed: 31` for Infrastructure.
- [ ] The audit step appears in the log and reports no vulnerable packages.
- [ ] `Directory.Build.props` uses forward slashes, and `dotnet build` still succeeds on Windows.
- [ ] No file under `src/` or `tests/` was modified.
- [ ] The workflow contains no deployment step, no `dotnet format`, and no coverage threshold.
