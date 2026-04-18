# Experimental Metrics

Metrics under this folder are **consumer-facing only** — other analyzers, curation
rules, or engine code may call them, but they are **not part of the published
API surface** and carry no backward-compatibility guarantee.

## Why "Experimental"

Parlance ships two kinds of code inside `Parlance.CSharp.Analyzers`:

1. **Stable analyzer surface** — the diagnostic IDs, message formats, and EditorConfig
   options that downstream NuGet consumers depend on. Changes here follow normal
   deprecation rules.
2. **Internal building blocks** used *only* by Parlance itself to implement those
   analyzers. That is what this folder holds.

Experimental metrics are:

- `internal`, not `public`.
- Exposed to `Parlance.CSharp.Tests` via `InternalsVisibleTo`.
- Allowed to change shape between any two commits. A rename or signature change
  is a normal refactor, not a breaking change.

The namespace suffix (`Parlance.CSharp.Analyzers.Metrics.Experimental`) and the
`/Experimental/` path both exist to keep that status visible at every call site
rather than requiring a reader to chase the `internal` keyword.

## Current metrics

### `CyclomaticComplexityMetric`

McCabe cyclomatic complexity via Roslyn's lowered `ControlFlowGraph`, counted as
π + 1 (predicate blocks plus one). See:

- `CyclomaticComplexityMetric.cs` — the implementation and a long file-header
  explanation of the algorithm choice.
- `docs/research/2026-04-16-parl3002-cyclomatic-complexity-contract.md` — the
  behavioral contract, including the decision to ship the metric without an
  accompanying `PARL3002` diagnostic (platform rule `CA1502` already reports at
  the IL level).
- `docs/research/2026-04-16-parl3002-analysis.md` — the spike log and the
  "single CFG path, no syntactic fallback" rationale.

## Intended consumers

- **`PARL3101`** (test-adequacy / CRAP score) is the first planned consumer.
  CRAP = `cyclomatic(m)² × (1 − coverage(m))³ + cyclomatic(m)`, so it needs a
  cyclomatic number per method. Tracked by the GitHub issue titled
  *"PARL3101: CRAP-score test-adequacy rule"*.
- **Future curation gates** that want to weight diagnostics by method shape.
- Not intended as a public Roslyn helper library. If a stable surface is ever
  needed, promote the metric out of this folder and write a stability contract
  at that time.

## Rules for adding a metric here

1. Keep it `internal sealed`.
2. Keep the calculation single-path. A metric that silently falls back to a
   different algorithm produces path-dependent numbers, which makes threshold
   consumers unreliable. Return an explicit "skipped" result instead.
3. Write a contract doc under `docs/research/` before the first consumer wires
   the metric into a diagnostic.
4. Put a file-level comment on the implementation explaining the theory and
   any Roslyn-specific quirks — readers should not have to reconstruct the
   decision from commit history.
