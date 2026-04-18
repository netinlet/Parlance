# Third-Party Notices

Parlance contains portions that reference, port behavior from, or load at
runtime software from third parties. This file reproduces the attributions
required by those third parties' licenses, plus informational entries for
source-available projects Parlance reviewed but did not copy from.

Vendored copies of each upstream project live under `artifacts/` in this
repository; the `Upstream path` entries below point to the exact `LICENSE`
(or equivalent) file a reviewer should consult.

---

## JetBrains ReSharper Cyclomatic Complexity PowerToy — Apache-2.0

- **Upstream project:** <https://github.com/JetBrains/resharper-cyclomatic-complexity>
- **Upstream path (vendored):** `artifacts/resharper-cyclomatic-complexity/`
- **License file:** `artifacts/resharper-cyclomatic-complexity/LICENSE`
- **How Parlance uses it:** algorithmic and fixture reference for the internal
  cyclomatic complexity metric (`src/Parlance.CSharp.Analyzers/Metrics/Experimental/CyclomaticComplexityMetric.cs`). Parlance does not copy
  ReSharper PSI source; the metric is a clean-room Roslyn `ControlFlowGraph`
  predicate counter. Upstream test fixtures (`SomeComplexMethod`,
  `ManySequentialIfs`, `ManyDeclarations`, `BoolAssignments`) are adapted —
  branch structure and literal names preserved so the computed score can be
  verified against the upstream gold value — and the adaptation is documented
  inline at the test fixture declaration. Source-use record:
  `docs/research/2026-04-16-parl3002-quality-gates.md`.

```
Copyright 2017–2019 JetBrains s.r.o.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

The full Apache License 2.0 text is available at the URL above and is
reproduced verbatim in `artifacts/resharper-cyclomatic-complexity/LICENSE`.
