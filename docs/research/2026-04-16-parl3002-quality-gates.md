# PARL3002 — Quality Gates & External-Source Use Record

**Date:** 2026-04-16
**Status:** Authoritative source-use record for the internal cyclomatic complexity metric shipped on this branch. The visible `PARL3002` diagnostic is deferred; see `docs/research/2026-04-16-parl3002-cyclomatic-complexity-contract.md` § "Relationship To CA1502".

This document exists so a reviewer can answer, without reading external
repositories, the question: *what external material was consulted, what was
copied or adapted, and under which licenses?* It is the paperwork that carries
a third-party-grounded metric across a PR boundary.

---

## 1. Reviewed sources and licenses

| Source | Upstream location | License | Role in this work |
|---|---|---|---|
| JetBrains ReSharper Cyclomatic Complexity PowerToy | `artifacts/resharper-cyclomatic-complexity/` | **Apache-2.0** (Copyright 2017–2019 JetBrains s.r.o.) | Primary source-use base for the internal cyclomatic metric. Algorithm and fixture inspiration; no source copied. Upstream: <https://github.com/JetBrains/resharper-cyclomatic-complexity> |

No other third-party source was reviewed for this metric. Sonar's
cognitive-complexity source was reviewed for the sibling `PARL3001` work, not
for cyclomatic.

---

## 2. External-source use per source

### 2.1 JetBrains ReSharper Cyclomatic Complexity PowerToy (Apache-2.0)

- **Kind of use:** algorithm reference + fixture recreation.
- **Copied material:** none. The upstream is a ReSharper PSI/CFG plugin in
  Kotlin/C# dual-target; Parlance's metric is a Roslyn
  `CSharpSyntaxWalker`-era clean-room implementation over
  `Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph`.
- **Adapted material:** the *decision* of what counts as a predicate (McCabe
  π + 1 over a lowered CFG) and the set of fixture scenarios that exercise
  the edge cases — `SomeComplexMethod`, `ManySequentialIfs`,
  `ManyDeclarations`, `BoolAssignments`. Upstream "gold" scores (bare
  integers) are mirrored as expected values in Parlance tests. Gold integers
  are not copyrightable on their own.
- **Fixture recreation:** every upstream fixture used in Parlance tests is
  rewritten as a Parlance-owned string literal with branch structure and
  literal names preserved so the scores can be verified directly against the
  upstream gold. No `.cs` files are copied. See
  `docs/research/2026-04-16-parl3002-fixture-port-matrix.md` for the
  row-level record.
- **Algorithmic divergence from the upstream plugin:** Roslyn's lowered CFG
  collapses empty-branch bodies, which breaks the `E − N + 2P` formula for the
  `ManySequentialIfs` fixture. Parlance uses the π + 1 formulation (also from
  McCabe's 1976 paper) and documents the reason inline at
  `CyclomaticComplexityMetric.cs`. Both formulations are equivalent for
  structured code; switching formulation is not a behavioral divergence from
  the plugin, it is an implementation detail forced by the host CFG's
  lowering rules.
- **Single-path calculation, no fallback:** the metric returns a `Skipped`
  result when Roslyn cannot produce a usable CFG, rather than silently
  switching to a syntactic walker. See
  `docs/research/2026-04-16-parl3002-analysis.md` § "Post-Spike Decision Log"
  for the rationale. This is a *Parlance* design choice; it is not inherited
  from upstream.
- **Attribution obligation under Apache-2.0:** reproduce the license notice
  in `THIRD_PARTY_NOTICES.md` (done) and preserve any upstream NOTICE file
  (none present in the vendored copy).

---

## 3. Copied / adapted / skipped material — consolidated view

| Source | Copied | Adapted | Skipped (recorded, not used) |
|---|---|---|---|
| ReSharper Cyclomatic Complexity PowerToy | — | algorithm choice, fixture scenarios, expected gold scores | the ReSharper-specific plugin host, PSI CFG walker, Rider/Visual-Studio settings UI |

---

## 4. Fixture port decisions

Fixture ports are tracked in
`docs/research/2026-04-16-parl3002-fixture-port-matrix.md`. That matrix is the
authoritative, row-level record. Summary:

- Every reviewed upstream cyclomatic fixture is ported; none are skipped.
- Ports rewrite scenarios in Parlance-owned test strings; no `.cs` or
  `.gold` content is copied wholesale.
- One upstream-equivalent test case (`SwitchExpression`) documents a
  deliberate algorithmic divergence from a naive syntactic reading: CFG
  predicate-counting scores a `switch` expression with a discard arm as 4
  (includes the implicit no-match branch); naive syntax says 3. The CFG
  answer matches CA1502.

---

## 5. Experimental-namespace rationale

The metric ships under
`src/Parlance.CSharp.Analyzers/Metrics/Experimental/` and the corresponding
`Parlance.CSharp.Analyzers.Metrics.Experimental` namespace. It is `internal`
and is reached from tests via `InternalsVisibleTo("Parlance.CSharp.Tests")`.

Rationale:

- The metric exists to support consumers inside the Parlance engine
  (planned: `PARL3101` test-adequacy / CRAP-score rule) and curation-gate
  work. It is not intended as a public Roslyn helper.
- Keeping it `internal` and namespaced `Experimental` means a consumer who
  wires up against this metric sees the stability posture at every import
  site. If a stable public surface is ever needed, promotion out of the
  `Experimental` folder is a deliberate decision, not an accident.
- `[Experimental]` attributes are not available on `netstandard2.0`, which
  is the required target for Roslyn analyzers — hence namespace + path as
  the carrier for the signal instead of an attribute.

See `src/Parlance.CSharp.Analyzers/Metrics/Experimental/README.md` for the
long-form version of this rule set.

---

## 6. Pointer to `THIRD_PARTY_NOTICES.md`

The attributions required by Apache-2.0 are collected in the repository-root
`THIRD_PARTY_NOTICES.md`. The entry links back to the vendored license file
under `artifacts/resharper-cyclomatic-complexity/LICENSE`.

---

## 7. Sign-off checklist (satisfied by this document)

- [x] Reviewed sources and licenses listed with upstream locations.
- [x] Per-source external-source-use statement (copied/adapted/skipped).
- [x] Fixture port decisions linked to the port matrix.
- [x] Notices file exists and is referenced.
- [x] Experimental-namespace posture recorded with its reasoning.
- [x] Relationship to platform rule `CA1502` recorded in the contract doc.
