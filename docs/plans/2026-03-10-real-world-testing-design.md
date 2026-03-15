# Real-World Testing Design

## Goal

Run Parlance against real public C# repositories to validate correctness, usability, and robustness. Learn what's useful and what isn't.

## Approach

A shell script (`tools/test-repos.sh`) that clones curated repos and runs Parlance against each, printing structured output to console for interactive review.

## Repo Tiers

- **Tier 1 (buildable):** Full pipeline — analyze, fix preview, fix apply, rebuild to verify fixes don't break compilation.
- **Tier 2 (syntax-only):** Analyze and fix preview only. Tests whether syntax-based PARL rules produce useful output on code that won't compile under our synthetic compilation.

## Curated Repos

| Repo | Tier | Rationale |
|---|---|---|
| `davidfowl/TodoApi` | 1 | Tiny, modern C# — should score well, baseline |
| `jbogard/MediatR` | 1 | Small library, clean patterns, popular |
| `ardalis/CleanArchitecture` | 1 | Medium, opinionated architecture |
| `mehmetozkaya/DotnetCrawler` | 2 | .NET Core 2.2 era, old patterns, modernization target |

## Per-Repo Sequence

1. Shallow clone to temp directory
2. **Tier 1 only:** `dotnet restore && dotnet build` — record pass/fail
3. `parlance analyze <src> --format text` — full output to console
4. `parlance analyze <src> --format json` — capture for potential later use
5. `parlance fix <src>` — dry-run preview
6. **Tier 1 + build passed:** `parlance fix <src> --apply`, then `dotnet build` — verify fixes don't break compilation
7. Print summary: score, diagnostic counts, fix count, post-fix build status, timing

## What We're Looking For

- **Correctness:** Are diagnostics accurate? False positives on real code?
- **Usability:** Is the output readable and actionable? Are scores meaningful?
- **Robustness:** Does it crash? Handle edge cases (generated code, partial files)?
- **Tier 2 signal:** Do syntax-based rules still produce value without full type resolution?

## Non-Goals

- No reporting infrastructure, databases, or web UI
- No parallel execution — sequential for readable output
- No CI integration (yet)
