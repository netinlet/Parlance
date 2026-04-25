# Third-Party Notices

Parlance contains portions that reference, port behavior from, or load at
runtime software from third parties. This file reproduces the attributions
required by those third parties' licenses, plus informational entries for
source-available projects Parlance reviewed but did not copy from.

---

## JetBrains Cognitive Complexity plugin — MIT

- **Upstream project:** <https://github.com/matkoch/resharper-cognitive-complexity>
- **How Parlance uses it:** algorithm and fixture reference for `PARL3001`
  cognitive complexity. Parlance does not copy source code; it recreates
  fixture scenarios in Parlance-owned tests and implements the scoring rules
  on top of Roslyn's `CSharpSyntaxWalker`.

```
MIT License

Copyright 2019 Matthias Koch

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## JetBrains ReSharper Cyclomatic Complexity PowerToy — Apache-2.0

- **Upstream project:** <https://github.com/JetBrains/resharper-cyclomatic-complexity>
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

The full Apache License 2.0 text is available at the URL above.

---

## SonarAnalyzer for .NET — Sonar Source-Available License v1 (SSALv1)

- **Upstream project:** <https://github.com/SonarSource/sonar-dotnet>
- **License:** <https://sonarsource.com/license/ssal/>
- **How Parlance uses it:** *reviewed for algorithmic ideas only — no source
  is copied or adapted.* SSALv1 is not an OSI-open-source license; it permits
  source visibility and modification for internal use but imposes
  field-of-use restrictions. Parlance deliberately re-implements cognitive
  complexity independently on top of Roslyn rather than porting Sonar code.

This entry is informational. No redistribution of SonarAnalyzer source is
required or performed by Parlance. The SSALv1 header appears at the top of
each SonarAnalyzer source file; an abridged form, as it appears upstream, is:

```
SonarAnalyzer for .NET
Copyright (C) SonarSource Sàrl
mailto:info AT sonarsource DOT com

You can redistribute and/or modify this program under the terms of
the Sonar Source-Available License Version 1, as published by SonarSource Sàrl.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
See the Sonar Source-Available License for more details.
```
