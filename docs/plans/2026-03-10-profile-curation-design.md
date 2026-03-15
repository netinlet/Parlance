# Phase 4: Profile & Curation System Design

## Problem

Parlance runs all loaded analyzers at their default severities and reports everything. The test run against 4 real repos showed:

- **Scores are meaningless:** TodoApi (exemplary modern C#) scores 0/100
- **Silent diagnostics dominate:** 80%+ of diagnostics are `silent`/`hidden` severity (IDE0008, IDE0055, IDE0320, etc.) — noise that drowns out signal
- **No curation layer exists:** The `--profile` flag validates the name but doesn't change analysis behavior. The shipped `default.editorconfig` is empty.

The core value proposition of Parlance — a **curated, opinionated** analysis experience — hasn't been built.

## Approach: Two Modes

### Discovery Mode (for us, now → later exposed to users)

Run all analyzers, collect everything, aggregate results to understand what rules matter at what severities across real codebases. This is how we **build** the profiles.

**Output:** Rule frequency reports, severity distribution, per-repo comparisons — the data we need to make informed curation decisions.

**User-facing (later):** `parlance analyze --profile discover` runs all rules and produces a suggested `.editorconfig` tailored to the codebase. Teams use this as a starting point.

### Curated Mode (the shipped product experience)

Apply a profile that filters to signal. Only rules and severities the profile enables are reported. The score reflects the curated view.

**This is what makes Parlance a product** instead of a raw Roslyn runner.

## Design

### 1. Post-Analysis Filtering Layer

Add a `ProfileFilter` that sits between `DiagnosticEnricher` and `IdiomaticScoreCalculator` in the pipeline:

```
Roslyn diagnostics (all analyzers, all severities)
        │
        ▼
DiagnosticEnricher (map to Parlance types)
        │
        ▼
ProfileFilter (NEW — filter + re-severity based on profile)
        │
        ▼
IdiomaticScoreCalculator (score only what the profile kept)
```

Why post-analysis filtering instead of feeding `.editorconfig` into Roslyn:

- **We need the raw data** for discovery mode. If we suppress at the Roslyn level, we can't aggregate what we didn't collect.
- **Faster iteration.** Changing a profile is changing a data structure, not editing and shipping `.editorconfig` files.
- **Profiles can be more expressive than `.editorconfig`.** We can filter by rule ID, category, severity, source package — not just `dotnet_diagnostic.XXXX.severity`.
- **`.editorconfig` generation becomes an output**, not an input. Once we know what a profile should contain, we can generate the `.editorconfig` for users who want IDE integration.

### 2. Profile Definition

A profile is a set of rules about which diagnostics to keep and at what severity.

```csharp
sealed record ProfileDefinition(
    string Name,
    string Description,
    ProfileFilterMode DefaultMode,        // Include or Exclude
    ImmutableArray<ProfileRule> Rules);

enum ProfileFilterMode { Include, Exclude }

sealed record ProfileRule(
    string? RuleId,                       // e.g. "IDE0055", or null for wildcard
    string? RulePrefix,                   // e.g. "IDE", "CA", "RCS", "PARL"
    DiagnosticSeverity? MinSeverity,      // keep only if >= this severity
    DiagnosticSeverity? OverrideSeverity, // re-map to this severity
    bool Exclude);                        // explicitly exclude this rule
```

**How it works:**

- `DefaultMode = Exclude`: Start with nothing, only include rules explicitly listed. This is the curated experience. The `default` profile uses this.
- `DefaultMode = Include`: Start with everything, remove rules explicitly excluded. The `discover` profile uses this — it's just raw output minus known-useless noise.

**Example — what `default` might look like** (informed by test data):

```csharp
new ProfileDefinition(
    Name: "default",
    Description: "Balanced — actionable findings at meaningful severities",
    DefaultMode: ProfileFilterMode.Exclude,
    Rules: [
        // PARL rules — all included at their default severity
        new(RulePrefix: "PARL", MinSeverity: null, OverrideSeverity: null, Exclude: false),

        // CA rules — design and reliability warnings
        new(RuleId: "CA1050", OverrideSeverity: DiagnosticSeverity.Suggestion, Exclude: false),
        // ... curated list ...

        // RCS rules — useful simplifications
        new(RuleId: "RCS1037", OverrideSeverity: DiagnosticSeverity.Suggestion, Exclude: false),
        new(RuleId: "RCS1118", OverrideSeverity: DiagnosticSeverity.Suggestion, Exclude: false),
        // ... curated list ...

        // IDE rules — only actionable ones
        new(RuleId: "IDE0005", OverrideSeverity: DiagnosticSeverity.Suggestion, Exclude: false),
        // IDE0008, IDE0055, IDE0320, etc. — NOT included (noise)
    ]);
```

### 3. Discovery Mode Output

When `--profile discover` is used, the tool runs all analyzers and appends an aggregation report:

```
═══════════════════════════════════════════════════
  RULE FREQUENCY REPORT
═══════════════════════════════════════════════════

  Rule         Severity     Count  Category
  ──────────────────────────────────────────────
  IDE0055      silent        1084  Formatting
  IDE0008      silent          60  Style
  IDE0160      silent          42  Style
  RCS1037      suggestion      50  Readability
  CA1050       suggestion       7  Design
  PARL0002     suggestion       3  Modernization
  ...

  Severity distribution:
    silent:      1,200 (85%)
    suggestion:    180 (13%)
    warning:        25 (2%)
    error:           0 (0%)

  Suggested profile baseline:
    Keep 25 rules at suggestion+
    Suppress 180+ silent/noise rules
    Estimated score with curation: 72/100
```

This gives us (and later, users) the data to make profile decisions.

### 4. Score Calculation Changes

The score calculator should only count diagnostics the profile kept. No changes to the formula — the filtering happens before scoring.

Additionally, the `discover` profile should report **two scores:**
- Raw score (all diagnostics, current behavior)
- Curated score (what the `default` profile would produce)

This lets us see immediately whether curation makes the score meaningful.

### 5. Test Script Improvements

Update `test-repos.sh` to:

1. Run with `--profile discover` to get the full picture
2. Show the aggregation report per repo
3. Show a cross-repo rule frequency summary at the end

This becomes our primary tool for iterating on profile definitions.

### 6. `.editorconfig` Generation (output, not input)

Once a profile is defined in code, we can generate the corresponding `.editorconfig`:

```bash
parlance rules --profile default --format editorconfig > .editorconfig
```

This serves two purposes:
- Users who want IDE integration get a `.editorconfig` that matches their CLI profile
- The discovery mode can output a suggested `.editorconfig` based on what it found

### 7. Planned Profiles

| Profile | DefaultMode | Description | When |
|---------|-------------|-------------|------|
| `discover` | Include | Everything minus known noise. Aggregation report. | Phase 4a (now) |
| `default` | Exclude | Curated baseline. Actionable findings only. | Phase 4b (after discovery data) |
| `strict` | Exclude | Elevates suggestions to warnings. More rules enabled. | Phase 4c |
| `minimal` | Exclude | Only bugs and correctness. For legacy codebases. | Phase 4c |
| `ai-agent` | Exclude | Tuned for AI output. Correctness + idioms, no style. | Phase 4c |
| `library` | Exclude | Adds API surface rules, ConfigureAwait, allocations. | Phase 4c |

### 8. Implementation Order

**Phase 4a — Discovery + Filtering Infrastructure:**

1. Add `ProfileDefinition` and `ProfileRule` types to `Parlance.Abstractions`
2. Add `ProfileFilter` to `Parlance.CSharp` — takes enriched diagnostics + profile, returns filtered diagnostics
3. Add `discover` profile — `Include` mode, excludes only compiler diagnostics (CS*) and duplicate `*FadeOut` rules
4. Wire `ProfileFilter` into `WorkspaceAnalyzer` pipeline between enrichment and scoring
5. Add `--profile discover` aggregation report to CLI text output
6. Update test script to use `--profile discover` and show cross-repo summaries
7. Run against curated repos, review data, iterate

**Phase 4b — Default Profile:**

8. Using discovery data, author the `default` profile with curated rule list
9. Score should be meaningful: TodoApi scores 90+, DotnetCrawler scores lower
10. Update test script to run both `discover` and `default`, compare results

**Phase 4c — Additional Profiles:**

11. Author `strict`, `minimal`, `ai-agent`, `library` profiles
12. Add `parlance rules --format editorconfig` for `.editorconfig` generation
13. Tests for each profile

## What This Unlocks

- **Meaningful scores** — the #1 usability problem from the test run
- **Actionable output** — 5-20 findings instead of 277-1,414
- **Data-driven curation** — profiles built from evidence, not guesses
- **The configurator path** — discovery mode is the CLI version of the blueprint's web configurator
- **MCP readiness** — the `ai-agent` profile makes the MCP server useful (Product 2)

## What This Does NOT Do

- No `.editorconfig` loading from user repos (deferred)
- No web UI configurator (deferred — CLI-first)
- No per-file or per-directory profile overrides
- No custom user-defined profiles (they get `.editorconfig` generation instead)
