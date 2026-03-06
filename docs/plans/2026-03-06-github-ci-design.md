# GitHub CI Build Design

## Overview

Add GitHub Actions CI and Dependabot to Parlance, modeled after the FormApps CI pipeline but scoped to a .NET-only project.

## CI Workflow (`.github/workflows/ci.yml`)

Single `dotnet` job on `ubuntu-latest`.

### Triggers

- Push to `main`
- Pull requests targeting `main` (from any branch)
- Manual dispatch (`workflow_dispatch`)

### Concurrency

Cancel in-progress runs per ref (`ci-${{ github.ref }}`).

### Environment Variables

- `DOTNET_NOLOGO: true`
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE: 1`
- `DOTNET_CLI_TELEMETRY_OPTOUT: 1`
- `TEST_RESULTS_DIR: ${{ github.workspace }}/.ci/test-results`

### Steps

1. Checkout (`actions/checkout@v6`)
2. Setup .NET (`actions/setup-dotnet@v5`, version from `global.json`)
3. Cache NuGet packages (`actions/cache@v5`, keyed on `*.csproj`, `*.props`, `*.targets`, `global.json`)
4. Restore dependencies (`dotnet restore Parlance.sln`)
5. Verify formatting (`dotnet format Parlance.sln --verify-no-changes --verbosity diagnostic`)
6. Build (`dotnet build Parlance.sln --configuration Release --no-restore`)
7. Prepare test results directory
8. Run tests (`dotnet test Parlance.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory $TEST_RESULTS_DIR`)
9. Install reportgenerator and generate coverage report, post to step summary
10. Upload test artifacts (`actions/upload-artifact@v6`, always runs)

## `global.json`

Pin .NET 10 SDK with `latestFeature` roll-forward policy.

## Dependabot (`.github/dependabot.yml`)

Two ecosystems:

- **NuGet:** weekly on Monday 08:00 CT, group minor+patch, limit 5 open PRs, target `main`
- **GitHub Actions:** weekly on Monday 08:00 CT, limit 3 open PRs, target `main`

Both labeled `dependencies`, commit prefix `[deps]`.

## Excluded (vs FormApps)

- No workload restore/cache (not needed)
- No dotnet tool manifest (reportgenerator installed inline in workflow)
- No frontend job (no frontend)
- No Postgres/DB test scripts
