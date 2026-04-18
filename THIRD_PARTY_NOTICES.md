# Third-Party Notices

Parlance contains portions that reference, port behavior from, or load at
runtime software from third parties. This file reproduces the attributions
required by those third parties' licenses, plus informational entries for
source-available projects Parlance reviewed but did not copy from.

Vendored copies of each upstream project live under `artifacts/` in this
repository; the `Upstream path` entries below point to the exact `LICENSE`
(or equivalent) file a reviewer should consult.

---

## JetBrains Cognitive Complexity plugin — MIT

- **Upstream project:** <https://github.com/matkoch/resharper-cognitive-complexity>
- **Upstream path (vendored):** `artifacts/jetbrains-plugin-cognitivecomplexity/`
- **License file:** `artifacts/jetbrains-plugin-cognitivecomplexity/LICENSE`
- **How Parlance uses it:** algorithm and fixture reference for `PARL3001` cognitive complexity. Parlance does not copy source code; it recreates fixture scenarios in Parlance-owned tests and implements the scoring rules on top of Roslyn's `CSharpSyntaxWalker`. Source-use record: `docs/research/2026-04-16-complexity-metrics-quality-gates.md` § 2.1.

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

## SonarAnalyzer for .NET — Sonar Source-Available License v1 (SSALv1)

- **Upstream project:** <https://github.com/SonarSource/sonar-dotnet>
- **Upstream path (vendored):** `artifacts/sonar-dotnet/`
- **Relevant source:** `artifacts/sonar-dotnet/analyzers/src/SonarAnalyzer.CSharp/Metrics/CSharpCognitiveComplexityMetric.cs`, `artifacts/sonar-dotnet/analyzers/src/SonarAnalyzer.Core/Metrics/CognitiveComplexity.cs`.
- **How Parlance uses it:** *review for algorithmic ideas only — no source is copied or adapted.* SSALv1 is not an OSI-open-source license; it permits source visibility and modification for internal use but imposes field-of-use restrictions. Parlance deliberately re-implements cognitive complexity independently on top of Roslyn rather than porting Sonar code. Source-use record: `docs/research/2026-04-16-complexity-metrics-quality-gates.md` § 2.3.

This entry is informational. No redistribution of SonarAnalyzer source is
required or performed by Parlance. The SSALv1 header appears at the top of
each SonarAnalyzer source file; the canonical license text is available at
<https://sonarsource.com/license/ssal/>. An abridged form of the header — as
it appears on each reviewed source file — is:

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
