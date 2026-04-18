# Complexity Metrics — Quality Gates & External-Source Use Record

**Date:** 2026-04-16 (license reconciliation 2026-04-17)
**Status:** Authoritative source-use record for the `PARL3001` cognitive-complexity work on this branch. Cyclomatic-complexity work (`PARL3002`, internal metric) is tracked on a separate branch and has its own source-use record.

This document exists so a reviewer can answer, without reading external
repositories, the question: *what external material was consulted, what was
copied or adapted, and under which licenses?* It is the paperwork that the
process doc (`docs/analyzer-process.md`) requires before merging any rule
grounded in third-party source.

---

## 1. Reviewed sources and licenses

| Source | Upstream location | License | Role in this work |
|---|---|---|---|
| JetBrains Cognitive Complexity plugin | `artifacts/jetbrains-plugin-cognitivecomplexity/` | **MIT** (Copyright 2019 Matthias Koch) | Primary source-use base for `PARL3001` cognitive scoring (algorithm and fixture inspiration). Upstream: <https://github.com/matkoch/resharper-cognitive-complexity> |
| SonarAnalyzer for .NET (S3776 cognitive) | `artifacts/sonar-dotnet/analyzers/src/SonarAnalyzer.CSharp/Metrics/CSharpCognitiveComplexityMetric.cs`, `artifacts/sonar-dotnet/analyzers/src/SonarAnalyzer.Core/Metrics/CognitiveComplexity.cs` | **Sonar Source-Available License v1 (SSALv1)** — *not* OSI-open-source | Algorithmic-idea reference only. Reviewed for scoring table, secondary-location UX, and handling of modern C# constructs. **No code ported or adapted.** |

Reconciliation note (2026-04-17): a draft of the hardening plan labelled
the JetBrains cognitive plugin as Apache-2.0. The vendored
`LICENSE` file is unambiguously MIT; the plan has been corrected and this
table is the authoritative record.

---

## 2. External-source use per source

The process doc draws a distinction between *reading* an upstream source for
inspiration, *recreating fixtures* behaviorally, and *directly adapting or
copying* code. The table below records exactly what we did for each reviewed
source.

### 2.1 JetBrains Cognitive Complexity plugin (MIT)

- **Kind of use:** behavioral reference + fixture recreation.
- **Copied material:** none. Source files are C#/ReSharper-PSI-flavoured; Parlance's walker is a Roslyn `CSharpSyntaxWalker`.
- **Adapted material:** the scoring rule set (nesting+1 for control-flow constructs, flat +1 for `else`, `+1` per new logical-operator group, etc.). This is the algorithm Matthias Koch documents in the plugin's README and matches the Sonar white paper.
- **Fixture recreation:** Parlance ports every upstream test scenario listed in `docs/research/2026-04-16-complexity-fixture-port-matrix.md` by *rewriting* the scenario in a Parlance-owned test file. We do not copy `.cs` or `.gold` files.
- **Behavioral divergences:** documented in the contract doc (`docs/research/2026-04-16-parl3001-cognitive-complexity-contract.md`). The break-scoring decision (Parlance = 1, matching JetBrains; Sonar = 0) is recorded there with a source comment on the `BreakIncrement` constant.
- **Attribution obligation under MIT:** reproduce the copyright notice and permission notice in Parlance's notices file. See `THIRD_PARTY_NOTICES.md`.

### 2.2 SonarAnalyzer for .NET (SSALv1)

- **Kind of use:** algorithmic-idea reference — strict "read, do not copy" posture.
- **Copied material:** none. SSALv1 permits visibility and modification for internal use but restricts redistribution of the functionality as a competing service; to stay well clear of that line, Parlance does not port code.
- **Adapted material:** conceptual only. Specifically, Parlance's cognitive scoring follows the Sonar white paper (which is the published algorithm, not the source), and the secondary-location UX (per-increment `Location` + reason string, encoded through Roslyn's `additionalLocations` plus indexed `properties`) is a deliberate cross-check against Sonar's well-known behavior — re-implemented independently on top of Roslyn's `Diagnostic.Create` API.
- **Attribution obligation:** algorithmic ideas are not copyrightable; Parlance still lists SonarAnalyzer in `THIRD_PARTY_NOTICES.md` to make the provenance legible and to carry the SSALv1 header Sonar places on each source file.

---

## 3. Copied / adapted / skipped material — consolidated view

| Source | Copied | Adapted | Skipped (recorded, not used) |
|---|---|---|---|
| JetBrains cognitive plugin | — | scoring rules, fixture scenarios | plugin's ReSharper-specific UI and settings surface |
| SonarAnalyzer S3776 | — | — | all source; we read the algorithm, not the code |

---

## 4. Fixture port decisions

Fixture ports are tracked in `docs/research/2026-04-16-complexity-fixture-port-matrix.md`. That matrix is the authoritative, row-level record. Summary:

- Every upstream JetBrains cognitive fixture is either ported or explicitly skipped with a rationale.
- Ports rewrite the scenario in a Parlance-owned test file; no `.cs` or `.gold` content is copied.
- Gold scores are the only upstream "data" that crosses the boundary — and a bare integer is not copyrightable.

---

## 5. Pointer to `THIRD_PARTY_NOTICES.md`

The attributions required by the MIT license, plus the informational SSALv1
reference for SonarAnalyzer, are collected in the repository-root
`THIRD_PARTY_NOTICES.md`. Every entry links back to the upstream source path
under `artifacts/` so a reviewer can inspect the verbatim `LICENSE` file.

---

## 6. Sign-off checklist (satisfied by this document)

- [x] Reviewed sources and licenses listed with upstream locations.
- [x] Per-source external-source-use statement (copied/adapted/skipped).
- [x] Fixture port decisions linked to the port matrix.
- [x] Notices file exists and is referenced.
- [x] JetBrains cognitive plugin license reconciled (MIT, not Apache-2.0) against the vendored `LICENSE` file.
