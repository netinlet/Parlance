# The IDE for the AI Agent: A Structured Analysis

## 1. What Roslyn Actually Provides Beyond Diagnostics

Parlance today uses a thin slice of Roslyn: parse source text, create a compilation, run analyzers, collect diagnostics. This is roughly 5% of what Roslyn can tell you about code. Here is the full inventory of what the compiler platform exposes, organized by what an AI agent cannot reliably derive from reading source text alone.

### Semantic Model (the crown jewel)

The `SemanticModel` is the bridge between syntax (what the code looks like) and semantics (what the code means). Key APIs:

- **`GetTypeInfo(node)`** -- Resolves the actual type of any expression. An AI reading `var x = GetThing()` has to guess what type `x` is. Roslyn knows definitively.
- **`GetSymbolInfo(node)`** -- Resolves any name reference to its declaration. Method overload resolution, extension method binding, implicit conversions -- all resolved.
- **`LookupSymbols(position)`** -- Returns every symbol visible at a given cursor position: locals, parameters, type members, accessible extension methods, namespaces. This is literally intellisense. An AI agent has no equivalent.
- **`GetDeclaredSymbol(node)`** -- Maps a declaration syntax node to its semantic symbol, giving access to the full symbol metadata (accessibility, attributes, containing type, etc.).
- **`GetConstantValue(node)`** -- Evaluates compile-time constants. Knows that `nameof(x)` resolves to `"x"`.
- **`GetAliasInfo(node)`** -- Resolves using aliases.
- **`GetConversion(node)`** -- Reports implicit and explicit conversions the compiler inserts, including boxing, numeric widening, user-defined conversions.

### Data Flow Analysis

`SemanticModel.AnalyzeDataFlow(firstStatement, lastStatement)` returns:

- **AlwaysAssigned** -- Variables guaranteed to be assigned in a region.
- **WrittenInside / WrittenOutside** -- Which variables are mutated where.
- **ReadInside / ReadOutside** -- Which variables are consumed where.
- **DataFlowsIn / DataFlowsOut** -- Variables that cross the region boundary.
- **VariablesDeclared** -- Locals introduced in the region.
- **Captured / CapturedInside / CapturedOutside** -- Variables captured by lambdas/local functions.

This is profoundly useful for an AI agent. When an agent wants to extract a method, it needs to know which variables flow in (become parameters) and which flow out (become return values). Without data flow analysis, it guesses. With it, it knows.

### Control Flow Analysis

`SemanticModel.AnalyzeControlFlow(firstStatement, lastStatement)` returns:

- **EntryPoints** -- Statements that transfer control into the region.
- **ExitPoints** -- Statements that transfer control out (return, break, throw, goto).
- **EndPointIsReachable** -- Whether execution can fall through the end of the region.
- **ReturnStatements** -- All return/yield statements in the region.

An AI agent trying to understand whether a code path can throw, whether a branch always returns, or whether a loop can exit early -- control flow analysis gives compiler-verified answers.

### IOperation API and Control Flow Graphs

The `IOperation` tree is Roslyn's language-neutral semantic representation. Unlike syntax trees (which are language-specific), operations represent the compiler's lowered understanding: loops become conditional branches, null-coalescing becomes conditional evaluation, pattern matching becomes a decision tree.

`ControlFlowGraph` built from IOperation provides basic blocks with explicit branch edges. This enables:

- Points-to analysis (what does this reference point to?)
- Value tracking across branches
- Null-state analysis
- Dispose-state tracking

### Workspace APIs

- **`MSBuildWorkspace`** -- Loads a real .sln/.csproj with all project references, NuGet dependencies, compilation options, and target framework resolved. This is the difference between analyzing a file in isolation and understanding the entire project.
- **`Solution` / `Project` / `Document`** -- The workspace object model gives you the project dependency graph, all documents, all references, all compilation options.
- **`Compilation`** -- Per-project, includes all syntax trees, all references, all symbols. You can query the entire type system.

### Symbol APIs

Once you have an `ISymbol`, you get:

- Full type hierarchy (base types, interfaces implemented)
- All members (methods, properties, fields, events) with full signatures
- Attributes and their arguments
- Accessibility (public, internal, protected, private)
- Containing namespace, containing type, containing assembly
- Whether it's virtual, abstract, sealed, static, readonly, etc.
- XML documentation comments
- `SymbolKey` for stable cross-request identification

### What Roslyn Tells an Agent That the Agent Cannot Figure Out From Source

1. **Resolved types** -- `var`, generic type inference, implicit conversions, overload resolution
2. **Cross-file symbol resolution** -- Which `Logger` class does this `using` bring in? What are its methods?
3. **NuGet dependency surface** -- What types and methods are available from referenced packages?
4. **Target framework capabilities** -- Is `Span<T>` available? Is `required` supported? What language version?
5. **Data flow correctness** -- Is this variable definitely assigned before use? Is this captured by a closure?
6. **Control flow completeness** -- Does every branch return? Can this throw?
7. **Type compatibility** -- Can this type be assigned to that interface? What conversions exist?
8. **Extension method resolution** -- Which extension methods are in scope? Which one wins overload resolution?

An AI agent reading source code is doing sophisticated pattern matching on text. Roslyn provides ground truth from the compiler.

---

## 2. What AI Coding Agents Actually Struggle With in C#

Based on research and direct experience with how LLMs generate C# code, the failure modes cluster into distinct categories:

### Category A: Things the agent literally cannot know without project context

- **Target framework mismatch** -- Generating `file-scoped namespaces` for a netstandard2.0 project, using `required` properties in C# 10, using collection expressions in C# 11.
- **Available APIs** -- Using `ImmutableArray` without knowing if `System.Collections.Immutable` is referenced. Calling methods that exist in .NET 9 but not .NET 8.
- **NuGet package APIs** -- Using the wrong overload, the deprecated method, or the method from v2 when the project references v1.
- **Project conventions** -- Not knowing the project uses nullable reference types, or that the editorconfig enforces certain styles.

### Category B: Semantic errors that look syntactically correct

- **Async/await pitfalls** -- Returning `null` from `async Task<T>` (causes NullReferenceException when awaited). Using `.Result` or `.Wait()` causing deadlocks. Missing `ConfigureAwait(false)` in library code. Creating `async void` methods (fire-and-forget with no error handling).
- **Dispose pattern failures** -- Not disposing `HttpClient`, `DbConnection`, `Stream` objects. Using `using` on types that don't implement `IDisposable`. Not implementing `IAsyncDisposable` when holding async resources.
- **Nullability errors** -- Missing null checks on dereferences. Wrong nullable annotations on generics. Not understanding the difference between `T?` where `T : class` vs `T : struct`.
- **Collection type misuse** -- Using `List<T>` where `IReadOnlyList<T>` is the correct return type. Exposing mutable collections from properties. Not understanding that `IEnumerable<T>` might be lazily evaluated and cause multiple enumeration.
- **Incorrect LINQ** -- Using `First()` instead of `FirstOrDefault()` without guaranteeing the element exists. Not understanding that `OrderBy().ThenBy()` is different from `OrderBy().OrderBy()`. Materializing queries too early or too late in EF Core.

### Category C: Stale patterns (the agent's training data is outdated)

- **Pre-C# 12 patterns when C# 13 exists** -- Not using primary constructors, collection expressions, `nameof` in attributes, etc.
- **Old API patterns** -- Using `WebClient` instead of `HttpClient`. Using `Thread.Sleep` instead of `Task.Delay`. Old-style `string.Format` instead of interpolation.
- **Deprecated framework APIs** -- Using APIs marked `[Obsolete]` that the agent learned from older training data.

### Category D: Architectural/design mistakes

- **Wrong abstraction level** -- Making everything `public`, not sealing classes, not using interfaces where needed.
- **God classes** -- Generating huge classes because the agent doesn't see the solution's existing decomposition.
- **Inconsistent patterns** -- Using dependency injection in some classes but `new` in others. Mixing sync and async patterns.
- **Missing validation** -- Not adding argument validation, not checking preconditions, not handling edge cases.

### The key insight

Categories A and B are where Roslyn semantic analysis provides the most value. The agent is literally missing information (A) or making errors that only the compiler can catch (B). Categories C and D are where curated rules and opinionated analysis add value -- the agent needs guidance about what idiomatic looks like, not just what compiles.

---

## 3. The "IDE for AI" Concept

### What a human developer gets from an IDE

A developer in Visual Studio or Rider has, at all times:

1. **Real-time compilation** -- Red squiggles appear instantly. The developer never writes more than a few lines before getting feedback.
2. **Intellisense** -- At any point, pressing `.` shows every available member with types and documentation. The developer never has to memorize APIs.
3. **Go-to-definition** -- Click any symbol to see its source or metadata. The developer can explore unfamiliar code by navigating the call graph.
4. **Find all references** -- "Who calls this method? Who implements this interface? Who overrides this virtual?" The developer understands impact before making changes.
5. **Refactoring tools** -- Rename, extract method, change signature, move type -- all with compiler-verified correctness.
6. **Quick fixes** -- "Add using", "Implement interface", "Generate constructor" -- boilerplate generation that's guaranteed correct.
7. **Project context** -- The IDE knows the target framework, the NuGet packages, the project references, the build configuration.
8. **Error list** -- All diagnostics across the entire solution, categorized by severity, with links to the offending code.

### What an AI agent gets today

When Claude, Copilot, or Cursor generates C# code, it gets:

1. **Source text** -- The contents of files the agent has been given or can read.
2. **Maybe build output** -- If the agent runs `dotnet build`, it sees compiler errors. But this is batch, not interactive.
3. **Nothing else** -- No type resolution, no symbol navigation, no intellisense, no refactoring verification.

The agent is writing code as if using Notepad. It has extraordinary pattern-matching ability (which is why it works at all), but zero semantic grounding.

### The gap

The fundamental gap is this: **a human developer never writes code without continuous compiler feedback. An AI agent writes code with zero compiler feedback until it explicitly asks for a build.**

This creates a specific failure pattern: the agent generates 50+ lines of code, runs the build, gets 12 errors, tries to fix them, introduces 3 new errors, iterates. This is the "edit-build-fix loop" that wastes tokens, time, and context window.

### What "IDE for AI" means concretely

It means giving the agent access to the same semantic information the human developer has, but through an API designed for programmatic consumption:

- **Before writing code**: "What types are available in this namespace? What methods does this interface require? What is the signature of this overload?"
- **While writing code**: "Does this expression type-check? Is this variable in scope? Am I missing a using directive?"
- **After writing code**: "What diagnostics does this produce? What would break if I changed this method signature? Is this change safe to apply?"

The LSAP (Language Server Agent Protocol) project has already identified this need. Their framing: LSP is designed for editors with atomic operations, but agents need cognitive capabilities -- higher-level semantic queries composed from multiple LSP operations. The agent doesn't want `textDocument/completion` at position 47:12 -- it wants "what can I call on this HttpClient instance?"

---

## 4. What SonarQube Does and Why It's a Side-Effect

### SonarQube's C# capabilities

SonarQube's C# analyzer (sonar-dotnet) provides approximately 400+ rules across four categories:

1. **Bugs** -- Code that is demonstrably wrong or more likely wrong than not. Examples: null dereference, dead code, always-true conditions, resource leaks.
2. **Vulnerabilities** -- Code that could be exploited. Examples: SQL injection, path traversal, insecure deserialization, hardcoded credentials.
3. **Security Hotspots** -- Code that is security-sensitive and needs human review. Examples: using regular expressions (ReDoS risk), using cryptography (weak algorithm risk), handling user input.
4. **Code Smells** -- Code that is neither buggy nor insecure but reduces maintainability. Examples: too-complex methods, duplicated code, naming violations, unused parameters.

SonarQube also provides:

- **Technical debt estimation** -- Time estimates to fix each issue.
- **Quality gates** -- Pass/fail criteria based on thresholds (e.g., "no new bugs, coverage above 80%").
- **Code coverage tracking** -- Integration with test coverage reports.
- **Duplications detection** -- Identifies copy-pasted code blocks.
- **Cognitive complexity scoring** -- Measures how hard code is to understand.

### Why this is a "side-effect" of the AI-first tool

If you build a tool that gives an AI agent deep semantic understanding of C# code, you necessarily build:

1. **A compilation pipeline** -- You need MSBuildWorkspace to load projects, resolve references, create compilations. SonarQube needs the same thing.
2. **A diagnostic engine** -- You run analyzers to tell the agent what's wrong. This is literally what SonarQube does.
3. **A scoring/quality model** -- You need to tell the agent "how good is this code?" which is a quality gate.
4. **A fix pipeline** -- You give the agent the ability to apply code fixes. SonarQube's auto-fix is the same capability.
5. **A rule curation layer** -- You decide which rules matter and at what severity. SonarQube's quality profiles are the same concept.

The AI-first framing means you build all the SonarQube capabilities, but you also build the semantic navigation, symbol resolution, type querying, and impact analysis that SonarQube doesn't provide. SonarQube tells you what's wrong. The IDE-for-AI also tells you what's right, what's available, and what would happen if you changed something.

Put differently: SonarQube is a judge. The IDE-for-AI is a judge, a guide, and a pair programmer.

### The business implication

SonarQube charges significant license fees for their server product. If Parlance builds the AI-first semantic tool and exposes SonarQube-equivalent diagnostics as a natural output, the quality-gate functionality for human CI/CD pipelines becomes a free side-effect of the AI infrastructure. This is strategically interesting because it means Parlance can offer SonarQube-class quality gating without it being the primary development cost.

---

## 5. LSP as an Interface (and Why It's Not Quite Right)

### What LSP provides that's relevant

LSP defines these capabilities that map well to AI agent needs:

- `textDocument/definition` -- Go to definition
- `textDocument/references` -- Find all references
- `textDocument/implementation` -- Find implementations
- `textDocument/typeDefinition` -- Go to type definition
- `textDocument/hover` -- Get type info and documentation at a position
- `textDocument/completion` -- Intellisense/autocomplete
- `textDocument/signatureHelp` -- Method signature information
- `textDocument/rename` -- Safe rename across files
- `textDocument/codeAction` -- Available quick fixes and refactorings
- `textDocument/diagnostic` -- Pull diagnostics
- `workspace/symbol` -- Search symbols across the workspace
- `callHierarchy/incomingCalls` / `outgoingCalls` -- Call graph navigation
- `typeHierarchy/supertypes` / `subtypes` -- Type hierarchy navigation

### Why LSP is not quite right for agents

LSP is designed around a human workflow: cursor position, single document focus, incremental edits. An AI agent's workflow is fundamentally different:

1. **Agents think in symbols, not positions** -- An agent wants "tell me about the `IAnalysisEngine` interface" not "hover at line 4, column 18 of file X."
2. **Agents need batch queries** -- "Show me all types that implement `IDisposable` but don't implement `IAsyncDisposable`" is a natural agent query that requires composing multiple LSP calls.
3. **Agents need predictive answers** -- "If I add this parameter to this method, what call sites break?" is not an LSP operation.
4. **Agents need curated context** -- LSP returns raw data. An agent needs data shaped for its context window: relevant, compact, prioritized.

### The LSAP approach

The LSAP (Language Server Agent Protocol) project addresses this by defining a semantic abstraction layer over LSP. Rather than exposing raw LSP operations, it composes them into agent-friendly capabilities. For example, instead of requiring the agent to call `textDocument/definition` then `textDocument/references` then `callHierarchy/incomingCalls`, LSAP provides a single "understand this symbol" operation that returns a structured snapshot.

### What this means for Parlance

Parlance should not implement LSP. That's already solved by `roslyn-language-server`. Instead, Parlance should implement a higher-level API -- whether exposed via MCP, CLI, or direct library calls -- that provides **curated semantic queries** backed by Roslyn. The interface should be symbol-centric and intent-centric, not position-centric.

---

## 6. What Makes This Different From "Just Tell the AI to Use Roslyn"

### The raw Roslyn problem

An AI agent could theoretically use Roslyn directly. Give it the NuGet packages, let it write C# code that calls `MSBuildWorkspace.Create()`, loads a solution, queries the semantic model. Why not?

Several reasons:

1. **Setup complexity** -- Loading an MSBuildWorkspace correctly requires handling MSBuild discovery, target framework resolution, NuGet restore, project reference resolution, and diagnostic handling for partially broken projects. This is dozens of lines of finicky code that's easy to get wrong.
2. **API surface area** -- Roslyn's public API surface is enormous. `SemanticModel` alone has 50+ methods. An agent would waste context window just understanding what APIs exist.
3. **Interpretation gap** -- Roslyn returns `ISymbol`, `IOperation`, `DataFlowAnalysis` objects. These are compiler internals. An agent needs "this variable might be null here" not "the NullableFlowState of this symbol at this position is MaybeNull."
4. **No opinion** -- Roslyn tells you facts. It doesn't tell you whether code is good. "You're using `List<T>` as a return type" is a Roslyn fact. "You should return `IReadOnlyList<T>` for encapsulation" is an opinion that requires curation.
5. **Performance and lifecycle** -- Keeping a workspace loaded, incrementally updating it as the agent makes changes, managing memory -- this is hard engineering that shouldn't be the agent's problem.

### The curation value

The difference between Roslyn and Parlance should be analogous to the difference between a compiler and an IDE:

- **Roslyn** = "Here are all the facts about this code"
- **Parlance** = "Here's what you need to know, what's wrong, what's available, and what you should do about it"

Specific curation axes:

- **Relevance filtering** -- Don't dump 500 symbols when the agent needs the 3 that matter for its current task.
- **Severity calibration** -- Not all diagnostics are equally important. Parlance's scoring model weights them.
- **Idiomatic direction** -- "This compiles, but modern C# would use pattern matching here" is guidance Roslyn can't provide.
- **Action prioritization** -- "These 3 fixes are safe to auto-apply, these 2 need review" requires judgment.
- **Context shaping** -- Format outputs for LLM consumption: structured, compact, with enough context to act on but not so much that it overwhelms the context window.

---

## 7. The MCP Server: What Tools Would an "IDE for AI" Expose?

### Existing Roslyn MCP servers as prior art

The research found at least 6 Roslyn-powered MCP servers already in existence:

- **SharpLens MCP** -- 58 tools, covering symbol search, method source, references, sync
- **SharpToolsMCP (kooshi)** -- Solution loading, project overview, member listing, definition viewing, implementation finding, reference finding, surgical code changes
- **MCP AI Server for Visual Studio** -- 41 tools (13 Roslyn + 19 debugger), symbol search, call graphs, rename, build/test
- **RoslynMCP (multiple implementations)** -- Symbol resolution, dependency analysis, complexity analysis, wildcard search

These are all raw Roslyn capability wrappers. None of them provide the curated, opinionated layer that Parlance's positioning implies.

### What Parlance's MCP tools should look like

Organized by the agent's workflow phases:

**Phase 1: Understand (before writing code)**

| Tool | What it does | Why raw Roslyn isn't enough |
|---|---|---|
| `parlance/load-workspace` | Load solution/project, return health status, TFM, language version, package refs | Curates the MSBuildWorkspace loading with health reporting and degradation handling |
| `parlance/describe-type` | Full type description: members, hierarchy, interfaces, attributes | Filters to relevant members, includes idiomatic usage notes |
| `parlance/available-symbols` | What symbols are accessible at a given scope | Filtered and categorized, not the raw 2000-symbol dump from LookupSymbols |
| `parlance/project-conventions` | EditorConfig rules, nullable context, implicit usings, TFM constraints | Distills project configuration into "rules the agent must follow" |
| `parlance/dependency-graph` | Project references and their public API surfaces | Scoped to what the agent needs, not the entire solution graph |

**Phase 2: Validate (while/after writing code)**

| Tool | What it does | Why raw Roslyn isn't enough |
|---|---|---|
| `parlance/analyze` | Run diagnostics with scoring, severity, fix availability | Already exists. The curated diagnostic layer with idiomatic scoring. |
| `parlance/type-check` | Verify an expression/statement resolves correctly | Quick feedback without a full build -- "does this compile?" |
| `parlance/check-impact` | "If I change this signature, what breaks?" | Composes find-references + type checking + data flow into a single answer |
| `parlance/validate-pattern` | "Is this async/dispose/nullable pattern correct?" | Curated pattern validators beyond raw diagnostics |

**Phase 3: Navigate (understanding existing code)**

| Tool | What it does | Why raw Roslyn isn't enough |
|---|---|---|
| `parlance/find-implementations` | All implementations of an interface/abstract member | Same as raw Roslyn but with relevance ranking |
| `parlance/trace-calls` | Call graph: who calls this, what does this call | Depth-limited, filtered to the agent's likely interest |
| `parlance/trace-data-flow` | How does this value flow through the code? | Translates DataFlowAnalysis into agent-readable narrative |
| `parlance/find-similar` | "Show me how this pattern is used elsewhere in the codebase" | Semantic similarity, not text grep |

**Phase 4: Act (applying changes)**

| Tool | What it does | Why raw Roslyn isn't enough |
|---|---|---|
| `parlance/fix` | Apply curated code fixes | Already exists. Policy-gated, safe subset. |
| `parlance/refactor-preview` | Preview what a refactoring would change | Shows diff before applying, with impact assessment |
| `parlance/refactor-apply` | Apply a previewed refactoring | Workspace-versioned, rejects stale actions |
| `parlance/generate-boilerplate` | Generate IDisposable impl, equality members, etc. | Compiler-correct generation, not pattern-matched guessing |

### The critical differentiator from existing Roslyn MCP servers

Existing servers expose Roslyn. Parlance would expose **judgment on top of Roslyn**:

- Not "here are 47 available code actions" but "here are the 3 you should apply and why"
- Not "here's the raw type hierarchy" but "this type should implement IAsyncDisposable because it holds async resources"
- Not "here are all symbols in scope" but "given what you're trying to do, these are the types and methods that matter"

---

## 8. Why AI-First Changes Everything About How the Product Should Be Built

### The original framing vs. the new framing

**Original blueprint**: "Developer Tool first, AI Quality Gate second." This implies: build a NuGet package and CLI that developers use directly, then wrap it for AI consumption.

**New framing**: "The tool IS the IDE for the AI agent. Developer tooling is a side-effect." This implies: build the semantic engine and MCP interface that agents consume, and let the CLI/NuGet package fall out as a simpler view of the same capabilities.

### What changes concretely

**Architecture priority shifts:**

1. **MSBuildWorkspace becomes essential, not optional** -- The current engine analyzes individual files with an ad-hoc compilation. For an AI IDE, you need the full solution loaded with real references. This is foundational, not a nice-to-have.

2. **The core abstraction changes** -- `IAnalysisEngine.AnalyzeSourceAsync(string sourceCode)` is a file-centric API. The AI IDE needs a workspace-centric API: load a solution, query symbols, navigate references, check impacts, run diagnostics, apply fixes -- all against a persistent semantic model.

3. **Stateful session management becomes central** -- The CLI is stateless (run, report, exit). An AI IDE is stateful: load workspace, keep it hot, process a stream of queries and mutations, handle incremental updates. This is a fundamentally different runtime model.

4. **Output format changes** -- The CLI formats for human reading (text, JSON). The AI IDE formats for LLM consumption: structured, token-efficient, with enough context to act on but compact enough to fit in a context window.

5. **The MCP server becomes the primary interface, not a wrapper** -- Instead of MCP wrapping the CLI, the CLI becomes a simplified view of the MCP server's capabilities. The MCP server is the product; the CLI is one client.

**What stays the same:**

- The analyzers and code fixes (PARL rules) are still valuable
- The scoring model is still valuable
- The upstream analyzer integration (NetAnalyzers, Roslynator) is still valuable
- The NuGet package for direct IDE integration is still valuable
- The curated, opinionated approach is still the differentiator

**What the roadmap should prioritize:**

1. **MSBuildWorkspace integration** -- Replace the ad-hoc compilation with real workspace loading
2. **Semantic query layer** -- Build the symbol resolution, type querying, reference finding, impact analysis capabilities on top of the loaded workspace
3. **MCP server with curated tools** -- Expose the semantic layer through MCP with the opinionated, agent-friendly interface
4. **Persistent session model** -- Workspace loading, incremental updates, health monitoring
5. **CLI as thin client** -- The CLI becomes a consumer of the same engine the MCP server uses

### The competitive landscape

The research reveals that multiple Roslyn MCP servers already exist (SharpLens, SharpToolsMCP, MCP AI Server for VS, etc.). But they all share a common characteristic: they are **raw Roslyn capability wrappers**. They expose the compiler's API through MCP with minimal curation.

None of them provide:
- Opinionated quality analysis (what SonarQube does)
- Idiomatic scoring (what Parlance already does)
- Curated fix guidance (which fixes are safe, which need review)
- Pattern validation (is this async pattern correct?)
- Intent-aware responses (given what you're trying to do, here's what matters)

The positioning gap is clear: raw Roslyn wrappers give you a compiler through MCP. Parlance would give you an **opinionated senior developer** through MCP -- one who has a compiler, a linter, a style guide, and architectural judgment.

### The SonarQube angle

If Parlance builds the AI-first semantic tool with MSBuildWorkspace, full diagnostic analysis, quality scoring, and fix capabilities, it will naturally produce everything SonarQube produces (static analysis, quality gates, technical debt) as a subset of its capabilities. But it will also produce things SonarQube cannot: semantic navigation, type resolution, impact analysis, code generation guidance, and real-time agent feedback.

This means Parlance could displace SonarQube for C# projects not by competing with SonarQube directly, but by making SonarQube's capabilities a free feature of the AI development infrastructure that teams are already adopting.

---

## Summary of Key Findings

1. **Roslyn provides ground truth that AI agents cannot derive from text** -- type resolution, data flow, control flow, symbol resolution, cross-project references. This is the fundamental value proposition.

2. **AI agents fail systematically in C# on exactly the things Roslyn can verify** -- async patterns, dispose patterns, nullable correctness, type compatibility, API availability. The failure modes and the solution capabilities are perfectly matched.

3. **Existing Roslyn MCP servers are raw wrappers, not curated tools** -- The market gap is not "expose Roslyn via MCP" (already done 6+ times) but "expose opinionated C# judgment via MCP backed by Roslyn."

4. **The AI-first framing means MSBuildWorkspace and stateful sessions become foundational** -- The current file-centric, stateless architecture needs to evolve toward a workspace-centric, session-based model.

5. **SonarQube capabilities fall out naturally from the AI infrastructure** -- Diagnostics, scoring, quality gates, and fix guidance are all subsets of what the AI IDE needs. Building for the agent gets you SonarQube for free.

6. **LSP is not the right interface for agents** -- Agents need symbol-centric, intent-centric, batch-oriented queries. MCP with curated tools is the right interface. LSP integration for human editors remains a separate (and already solved) concern.

7. **The differentiator is curation, not capability** -- Anyone can wrap Roslyn in MCP. The value is in deciding what matters, how to present it, what's safe to auto-fix, and what idiomatic direction to push toward. That's what Parlance already does with its analyzer rules and scoring, and it's what should extend to the full semantic layer.

---

## Sources

- [SonarQube C# Rules Catalog](https://rules.sonarsource.com/csharp/)
- [SonarQube C# Documentation](https://docs.sonarsource.com/sonarqube-server/2025.6/analyzing-source-code/languages/csharp)
- [Roslyn SemanticModel Source](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/Compilation/SemanticModel.cs)
- [Learn Roslyn Now: Data Flow Analysis](https://joshvarty.com/2015/02/05/learn-roslyn-now-part-8-data-flow-analysis/)
- [Learn Roslyn Now: Semantic Model](https://joshvarty.com/2014/10/30/learn-roslyn-now-part-7-introducing-the-semantic-model/)
- [Roslyn Data Flow Analysis Based Analyzers Guide](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/Writing%20dataflow%20analysis%20based%20analyzers.md)
- [Roslyn ControlFlowGraph API](https://github.com/dotnet/roslyn/issues/24104)
- [Get Started with Semantic Analysis - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis)
- [LSAP - Language Server Agent Protocol](https://github.com/lsp-client/LSAP)
- [LSP: The Secret Weapon for AI Coding Tools](https://amirteymoori.com/lsp-language-server-protocol-ai-coding-tools/)
- [Give Your AI Coding Agent Eyes: LSP Integration](https://tech-talk.the-experts.nl/give-your-ai-coding-agent-eyes-how-lsp-integration-transform-coding-agents-4ccae8444929)
- [SharpLens MCP Server](https://github.com/pzalutski-pixel/sharplens-mcp)
- [SharpToolsMCP](https://github.com/kooshi/SharpToolsMCP)
- [MCP AI Server for Visual Studio](https://github.com/LadislavSopko/mcp-ai-server-visual-studio)
- [RoslynMCP](https://github.com/carquiza/RoslynMCP)
- [Roslyn MCP Server (egorpavlikhin)](https://github.com/egorpavlikhin/roslyn-mcp)
- [Using MSBuildWorkspace](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3)
- [Using Coding Agents with LSP on Large Codebases](https://medium.com/@dconsonni/using-coding-agents-with-language-server-protocols-on-large-codebases-24334bfff834)
- [Common C# Mistakes Developers Make](https://medium.com/@win_48866/common-c-mistakes-developers-make-and-how-to-avoid-them-like-a-pro-1d2736426056)
- [SonarQube Rules Overview](https://docs.sonarsource.com/sonarqube-server/2025.4/user-guide/rules/overview)
