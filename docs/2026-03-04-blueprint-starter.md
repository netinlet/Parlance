# Idiomatic C# — Product Blueprint

## Executive Summary

This blueprint defines two products built on a shared core: a **Developer Tool** (NuGet package + CLI) that helps human developers write idiomatic C#, and an **AI Quality Gate** (MCP server) that helps AI coding agents validate and improve the code they generate. Both products consume the same curated Roslyn analysis engine. The developer tool ships first and proves the ruleset; the AI quality gate wraps the same engine for programmatic consumption by agents. The MCP server is designed with a language-agnostic interface from day one — C# is the first language engine, but the tool schema, response format, and agent integration patterns are language-neutral so future engines (TypeScript, Rust, Go, etc.) slot in without breaking existing agent integrations.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                  LANGUAGE ENGINES (one per language)         │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │       C# Analysis Engine (ships first)              │    │
│  │                                                      │    │
│  │  ┌──────────────┐  ┌──────────────┐  ┌───────────┐  │    │
│  │  │  Curated     │  │  Custom      │  │  Roslyn   │  │    │
│  │  │  Ruleset     │  │  Analyzers   │  │  Compiler │  │    │
│  │  │  Config      │  │  (Original)  │  │  APIs     │  │    │
│  │  └──────────────┘  └──────────────┘  └───────────┘  │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │       Future: TypeScript, Rust, Go, etc.            │    │
│  │       (same interface contract, different internals) │    │
│  └─────────────────────────────────────────────────────┘    │
│                          │                                   │
│              ┌───────────┴───────────┐                       │
│              ▼                       ▼                       │
│  ┌──────────────────┐   ┌────────────────────────────┐      │
│  │  PRODUCT 1       │   │  PRODUCT 2                  │      │
│  │  Developer Tool  │   │  AI Quality Gate             │      │
│  │  (per-language)  │   │  (language-agnostic MCP)     │      │
│  │                  │   │                              │      │
│  │  • NuGet Package │   │  • MCP Server                │      │
│  │  • CLI Tool      │   │  • Language-neutral tools    │      │
│  │  • IDE Feedback  │   │  • Agent sends language +    │      │
│  │  • .editorconfig │   │    source, gets diagnostics  │      │
│  └──────────────────┘   └────────────────────────────┘      │
└─────────────────────────────────────────────────────────────┘
```

---

## Shared Core: The Analysis Engine

### Purpose

A single .NET library that takes C# source code (or a project/solution) as input and returns structured diagnostics with severity, category, explanation, and fix suggestions. This is the brain that both products consume.

### Project: `IdiomaticCSharp.Core`

**Target Framework:** net8.0 (minimum), net9.0  
**Key Dependencies:**
- `Microsoft.CodeAnalysis.CSharp` (Roslyn compiler APIs)
- `Microsoft.CodeAnalysis.CSharp.Workspaces` (project/solution loading)

### Curated Ruleset Composition

The ruleset is assembled from existing high-quality analyzer packages plus custom rules that fill gaps. Each rule has a documented rationale.

#### Upstream Analyzer Packages (Vendored Rules)

| Package | What We Take | Why |
|---------|-------------|-----|
| `Microsoft.CodeAnalysis.NetAnalyzers` | CA1xxx (design), CA2xxx (reliability), CA18xx (performance) | Microsoft's official best practices. Foundation layer. |
| `Roslynator.Analyzers` | ~200 selected rules from RCS1xxx | Broad coverage of C# idioms: simplification, readability, redundancy removal. |
| `Roslynator.Formatting.Analyzers` | RCS0xxx formatting rules | Consistent code layout. |
| `StyleCop.Analyzers` | SA1xxx (documentation), SA16xx (naming) | Documentation and naming conventions. |
| `AsyncFixer` | All 5 rules | Async/await anti-pattern detection. |
| `IDisposableAnalyzers` | IDISP001-IDISP025 | IDisposable correctness — a common source of bugs. |
| `ClrHeapAllocationAnalyzer` | HAA0xxx | Performance-sensitive allocation detection. |
| `SonarAnalyzer.CSharp` | Selected S-rules for security/reliability | Security patterns and cognitive complexity. |

#### Custom Analyzers (Original Rules)

Rules that don't exist in any upstream package but represent idiomatic C# patterns:

| Rule ID | Category | Description |
|---------|----------|-------------|
| `IC0001` | Modernization | Prefer primary constructors (C# 12+) where applicable |
| `IC0002` | Modernization | Prefer collection expressions (`[1, 2, 3]`) over `new List<int>{...}` |
| `IC0003` | Modernization | Prefer `required` properties over constructor-only initialization |
| `IC0004` | Pattern Matching | Use pattern matching over `is` + cast |
| `IC0005` | Pattern Matching | Use `switch` expression over `switch` statement for value returns |
| `IC0006` | LINQ | Prefer LINQ method syntax for simple transforms, query syntax for joins |
| `IC0007` | LINQ | Flag LINQ in hot paths (suggestion severity, not warning) |
| `IC0008` | Naming | Flag Hungarian notation, abbreviations, single-letter names outside loops |
| `IC0009` | Error Handling | Prefer guard clauses over nested if-else for preconditions |
| `IC0010` | Error Handling | Flag empty catch blocks |
| `IC0011` | Architecture | Flag `static` classes with more than N public methods (god class smell) |
| `IC0012` | Architecture | Flag methods with cyclomatic complexity > threshold |
| `IC0013` | Nullability | Flag `!` (null-forgiving operator) usage outside test projects |
| `IC0014` | Async | Flag `async void` methods outside event handlers |
| `IC0015` | Async | Flag missing `ConfigureAwait(false)` in library code |

#### Severity Configuration

Every rule has a default severity but is configurable via `.editorconfig`:

| Severity | Meaning | Examples |
|----------|---------|---------|
| `error` | Definite bug or critical issue | Disposed object use, async void |
| `warning` | Strong recommendation | Missing null check, empty catch |
| `suggestion` | Idiomatic improvement | Could use pattern matching, modern syntax |
| `silent` | Available but off by default | Style-only preferences |

#### Profiles

Pre-built `.editorconfig` profiles for common scenarios:

| Profile | Description |
|---------|-------------|
| `default` | Balanced — warnings for bugs, suggestions for idioms |
| `strict` | Elevates most suggestions to warnings. For teams wanting enforcement. |
| `library` | Adds ConfigureAwait, public API documentation, allocation warnings |
| `minimal` | Only bugs and security. For legacy codebases being gradually improved. |
| `ai-agent` | Tuned for AI output — emphasizes correctness and idiomatic patterns, suppresses style-only rules that agents shouldn't care about |

### Core API Surface

```csharp
namespace IdiomaticCSharp.Core;

/// The main entry point. Analyzes C# code and returns structured results.
public class AnalysisEngine
{
    /// Analyze a single C# source string. Fastest path for AI agents.
    public Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default);

    /// Analyze a .csproj or .sln on disk. Full semantic analysis.
    public Task<AnalysisResult> AnalyzeProjectAsync(
        string projectOrSolutionPath,
        AnalysisOptions? options = null,
        CancellationToken ct = default);

    /// Analyze a syntax tree directly (for callers already using Roslyn).
    public Task<AnalysisResult> AnalyzeSyntaxTreeAsync(
        SyntaxTree tree,
        SemanticModel? model = null,
        AnalysisOptions? options = null,
        CancellationToken ct = default);
}

public record AnalysisOptions
{
    public string Profile { get; init; } = "default";  // default, strict, library, minimal, ai-agent
    public string? EditorConfigPath { get; init; }       // override with custom .editorconfig
    public string[] TargetFrameworks { get; init; } = ["net8.0"];
    public bool IncludeFixSuggestions { get; init; } = true;
    public string[] SuppressRules { get; init; } = [];
    public int? MaxDiagnostics { get; init; }            // cap output for large codebases
}

public record AnalysisResult
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    public AnalysisSummary Summary { get; init; }
    public string? FixedSource { get; init; }            // auto-fixed version if fixes available
}

public record Diagnostic
{
    public string RuleId { get; init; }          // e.g. "IC0004", "RCS1003", "CA1062"
    public string Category { get; init; }        // e.g. "PatternMatching", "Naming", "Security"
    public DiagnosticSeverity Severity { get; init; }
    public string Message { get; init; }         // human-readable explanation
    public string? Rationale { get; init; }      // WHY this is idiomatic (for AI learning)
    public Location Location { get; init; }      // file, line, column, span
    public string? SuggestedFix { get; init; }   // code suggestion or description
    public string? FixedCode { get; init; }      // the actual fixed code snippet if available
    public string UpstreamSource { get; init; }  // which analyzer package this came from
}

public record AnalysisSummary
{
    public int TotalDiagnostics { get; init; }
    public int Errors { get; init; }
    public int Warnings { get; init; }
    public int Suggestions { get; init; }
    public Dictionary<string, int> ByCategory { get; init; }
    public double IdiomaticScore { get; init; }  // 0-100 composite score
}
```

### Analysis Pipeline (Internal)

```
Input (source string, project path, or syntax tree)
  │
  ▼
┌─────────────────────────────────┐
│  1. Parse / Load                │
│  • Source string → SyntaxTree   │
│  • Project → MSBuild workspace  │
│  • Load .editorconfig overrides │
└──────────────┬──────────────────┘
               │
               ▼
┌─────────────────────────────────┐
│  2. Build Compilation           │
│  • Create CSharpCompilation     │
│  • Add metadata references      │
│  • Resolve target framework     │
└──────────────┬──────────────────┘
               │
               ▼
┌─────────────────────────────────┐
│  3. Load Analyzer Assemblies    │
│  • Load per-profile analyzer    │
│  •   set from configuration     │
│  • Apply severity overrides     │
│  • Register custom IC-rules     │
└──────────────┬──────────────────┘
               │
               ▼
┌─────────────────────────────────┐
│  4. Run Analysis                │
│  • CompilationWithAnalyzers     │
│  •   .GetAnalyzerDiagnosticsAsync()     │
│  • Parallel execution           │
│  • Respect MaxDiagnostics cap   │
└──────────────┬──────────────────┘
               │
               ▼
┌─────────────────────────────────┐
│  5. Enrich Diagnostics          │
│  • Map to structured Diagnostic │
│  • Add Rationale text           │
│  • Compute code fix suggestions │
│  • Calculate IdiomaticScore     │
└──────────────┬──────────────────┘
               │
               ▼
┌─────────────────────────────────┐
│  6. (Optional) Auto-Fix         │
│  • Apply available CodeFixes    │
│  • Return fixed source          │
│  • Only for non-breaking fixes  │
└──────────────┬──────────────────┘
               │
               ▼
AnalysisResult (diagnostics + summary + optional fixed source)
```

---

## Product 1: Developer Tool

### What It Is

A NuGet package and CLI tool that gives C# developers a curated, opinionated analysis experience. Install the package, get immediate IDE feedback. Run the CLI in CI/CD for enforcement.

### Components

#### 1A. NuGet Analyzer Package: `IdiomaticCSharp.Analyzers`

**What it does:** Ships the curated analyzer set as a standard Roslyn analyzer package. Once installed, diagnostics light up in Visual Studio, Rider, and VS Code.

**Package structure:**
```
IdiomaticCSharp.Analyzers/
├── analyzers/
│   └── dotnet/
│       └── cs/
│           ├── IdiomaticCSharp.Core.dll
│           ├── IdiomaticCSharp.Analyzers.dll  (custom IC-rules)
│           └── [vendored upstream analyzer dlls]
├── build/
│   └── IdiomaticCSharp.Analyzers.props        (default .editorconfig injection)
├── content/
│   └── .editorconfig.default                  (default profile)
└── IdiomaticCSharp.Analyzers.nuspec
```

**Installation:**
```xml
<!-- In .csproj -->
<PackageReference Include="IdiomaticCSharp.Analyzers" Version="1.0.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; analyzers</IncludeAssets>
</PackageReference>
```

**Profile selection via .editorconfig:**
```ini
# .editorconfig at solution root
[*.cs]
idiomatic_csharp.profile = strict   # default | strict | library | minimal
```

#### 1B. CLI Tool: `idiomatic-cs`

**What it does:** Command-line analysis for CI/CD, code review automation, and batch analysis.

**Distribution:** .NET global tool (`dotnet tool install -g idiomatic-cs`)

**Commands:**

```bash
# Analyze a project or solution
idiomatic-cs analyze ./src/MyProject.csproj
idiomatic-cs analyze ./MySolution.sln

# Analyze a single file (uses in-memory compilation)
idiomatic-cs analyze ./Program.cs

# Apply auto-fixes
idiomatic-cs fix ./src/MyProject.csproj

# Output formats
idiomatic-cs analyze ./src --output json     # structured JSON
idiomatic-cs analyze ./src --output sarif    # SARIF for GitHub/Azure DevOps
idiomatic-cs analyze ./src --output text     # human-readable (default)

# Profile selection
idiomatic-cs analyze ./src --profile strict

# Score-only mode (for CI gates)
idiomatic-cs score ./src --min-score 80      # exit code 1 if below threshold

# List all rules with current severity
idiomatic-cs rules --profile default
idiomatic-cs rules --category PatternMatching
```

**CI/CD Integration Example (GitHub Actions):**
```yaml
- name: Idiomatic C# Analysis
  run: |
    dotnet tool install -g idiomatic-cs
    idiomatic-cs analyze ./src/MySolution.sln \
      --output sarif \
      --output-file results.sarif \
      --min-score 75

- name: Upload SARIF to GitHub Code Scanning
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```

**Understanding the SARIF → GitHub Code Scanning pipeline:**

SARIF (Static Analysis Results Interchange Format) is an OASIS standard JSON format that gives every static analysis tool a common output schema. Any tool that writes SARIF can feed results into any platform that reads it.

The `upload-sarif` action pushes our SARIF file to **GitHub Code Scanning** — a general-purpose platform feature for displaying static analysis results. Despite the action living under the `codeql-action` repo, CodeQL itself never sees or processes our file. CodeQL is one analysis tool that feeds into Code Scanning; our tool is another, completely independent one feeding into the same platform.

Once uploaded, the user gets:
- **PR inline annotations** — every diagnostic appears directly on the diff next to the relevant line, the same way CodeQL security findings appear. No log files to read.
- **Security → Code Scanning dashboard** — all findings aggregate into a persistent dashboard. Filter by rule, severity, file, branch. Dismiss, mark as false positive, or track over time across commits.
- **Branch protection** — teams can configure GitHub to block PR merges if there are code scanning alerts above a certain severity, enforced by GitHub itself.

The key insight: our idiomatic C# findings get the same first-class treatment in the GitHub UI that CodeQL findings get. One dashboard, one annotation system, one branch protection mechanism, multiple tools feeding into it.

#### 1C. Configuration & Profiles Package: `IdiomaticCSharp.Profiles`

**What it does:** Ships the `.editorconfig` profiles as a separate, lighter package for teams that already use their own analyzer stack but want the curated configuration.

```bash
dotnet new editorconfig-idiomatic --profile strict
```

### Developer Workflow

```
Developer writes C# code
        │
        ▼
IDE loads IdiomaticCSharp.Analyzers (via NuGet)
        │
        ▼
Roslyn runs analyzers in real-time as developer types
        │
        ▼
Diagnostics appear as squiggles / lightbulbs
  • Errors: red squiggles (must fix)
  • Warnings: yellow squiggles (should fix)
  • Suggestions: gray dots (consider fixing)
        │
        ▼
Developer applies code fixes via IDE lightbulb actions
        │
        ▼
On commit: CI runs `idiomatic-cs analyze` via CLI
  • Outputs SARIF report
  • Blocks merge if score < threshold
  • Posts summary comment on PR
```

---

## Product 2: AI Quality Gate (MCP Server)

### What It Is

An MCP (Model Context Protocol) server that exposes the analysis engine to AI coding agents. The agent generates C# code, calls the quality gate, gets back structured diagnostics with fix suggestions, and iterates until the code is idiomatic.

### Why MCP

MCP is an open protocol that Claude Code, Cursor, Windsurf, and other AI tools support. By implementing as an MCP server, the quality gate works with any compliant agent without custom integration per tool.

### Project: `Idiomatic.McpServer`

**Runs as:** A local MCP server process (stdio transport for CLI agents, HTTP/SSE for networked agents)

**Design principle:** The MCP interface is language-agnostic. Every tool takes a `language` parameter. The server dispatches to the appropriate language engine internally. C# is the only engine at launch, but adding a new language means implementing a new engine behind the same tool interface — agents don't need to change their integration.

### MCP Tool Definitions

The server exposes these tools to agents. All tool names are language-neutral.

#### Tool: `analyze`

The primary tool. Takes source code and a language, returns structured diagnostics.

```json
{
  "name": "analyze",
  "description": "Analyze source code for correctness, idioms, and best practices. Returns diagnostics with severity, category, explanation, rationale, and fix suggestions. Use the 'ai-agent' profile by default.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "language": {
        "type": "string",
        "enum": ["csharp"],
        "description": "Programming language of the source code. Currently supported: csharp. Future: typescript, rust, go."
      },
      "source_code": {
        "type": "string",
        "description": "The source code to analyze"
      },
      "profile": {
        "type": "string",
        "enum": ["default", "strict", "library", "minimal", "ai-agent"],
        "default": "ai-agent",
        "description": "Analysis profile. 'ai-agent' is tuned for AI-generated code."
      },
      "language_options": {
        "type": "object",
        "description": "Language-specific options. For csharp: { target_framework: 'net8.0' }. Each language engine defines its own options.",
        "default": {}
      },
      "context": {
        "type": "string",
        "description": "Optional: what this code is for (e.g., 'library', 'web API controller', 'unit test'). Adjusts rule weights."
      },
      "include_fixed_source": {
        "type": "boolean",
        "default": true,
        "description": "Return an auto-fixed version of the source code"
      }
    },
    "required": ["language", "source_code"]
  }
}
```

**Example Response:**
```json
{
  "language": "csharp",
  "summary": {
    "total_diagnostics": 4,
    "errors": 0,
    "warnings": 2,
    "suggestions": 2,
    "idiomatic_score": 72,
    "by_category": {
      "PatternMatching": 1,
      "Modernization": 1,
      "ErrorHandling": 1,
      "Naming": 1
    }
  },
  "diagnostics": [
    {
      "rule_id": "IC0004",
      "category": "PatternMatching",
      "severity": "suggestion",
      "message": "Use pattern matching instead of 'is' followed by cast",
      "rationale": "Pattern matching (introduced in C# 7.0) combines type checking and variable declaration in a single expression. It is more concise, avoids the double type-check, and is the idiomatic approach in modern C#.",
      "location": { "line": 12, "column": 9, "end_line": 12, "end_column": 45 },
      "original_code": "if (obj is MyType) { var x = (MyType)obj; ... }",
      "suggested_fix": "if (obj is MyType x) { ... }",
      "fix_confidence": "high"
    },
    {
      "rule_id": "IC0009",
      "category": "ErrorHandling",
      "severity": "warning",
      "message": "Prefer guard clause over nested conditional",
      "rationale": "Guard clauses (early returns for precondition failures) reduce nesting depth and make the happy path more readable. This is a widely adopted pattern in the .NET ecosystem.",
      "location": { "line": 5, "column": 5, "end_line": 20, "end_column": 6 },
      "original_code": "if (input != null) { ... long body ... }",
      "suggested_fix": "if (input is null) throw new ArgumentNullException(nameof(input));\n// ... rest at top level",
      "fix_confidence": "medium"
    }
  ],
  "fixed_source": "// ... the entire source with all high-confidence fixes applied ..."
}
```

#### Tool: `analyze_project`

For agents working on full projects (e.g., Claude Code with file system access).

```json
{
  "name": "analyze_project",
  "description": "Analyze an entire project on disk. Provides full semantic analysis including cross-file type resolution. The language is auto-detected from the project file type.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "project_path": {
        "type": "string",
        "description": "Path to project file (.csproj, .sln, tsconfig.json, Cargo.toml, etc.)"
      },
      "language": {
        "type": "string",
        "description": "Optional override. Auto-detected from project file if omitted."
      },
      "profile": {
        "type": "string",
        "default": "ai-agent"
      },
      "files_filter": {
        "type": "array",
        "items": { "type": "string" },
        "description": "Optional: only report diagnostics for these files (paths relative to project)"
      }
    },
    "required": ["project_path"]
  }
}
```

#### Tool: `fix`

Applies fixes and returns the corrected source. For agents that want a fix-only workflow.

```json
{
  "name": "fix",
  "description": "Apply automatic fixes to source code and return the corrected version. Only applies high-confidence fixes that won't change behavior.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "language": {
        "type": "string",
        "enum": ["csharp"],
        "description": "Programming language of the source code"
      },
      "source_code": {
        "type": "string",
        "description": "The source code to fix"
      },
      "fix_categories": {
        "type": "array",
        "items": { "type": "string" },
        "description": "Optional: only apply fixes in these categories",
        "default": ["all"]
      },
      "min_confidence": {
        "type": "string",
        "enum": ["high", "medium", "low"],
        "default": "high",
        "description": "Minimum confidence level for auto-applied fixes"
      }
    },
    "required": ["language", "source_code"]
  }
}
```

#### Tool: `explain_rule`

For agents (or agent-facing UIs) that want to understand why a rule exists.

```json
{
  "name": "explain_rule",
  "description": "Get detailed explanation of a rule, including rationale, examples, and configuration options.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "rule_id": {
        "type": "string",
        "description": "Rule ID (e.g., 'IC0004', 'RCS1003', 'CA1062')"
      },
      "language": {
        "type": "string",
        "description": "Optional. Helps disambiguate if rule IDs overlap across languages."
      }
    },
    "required": ["rule_id"]
  }
}
```

#### Tool: `list_rules`

For discovery and configuration.

```json
{
  "name": "list_rules",
  "description": "List all available rules, optionally filtered by language, category, or profile.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "language": {
        "type": "string",
        "enum": ["csharp"],
        "description": "Filter rules by language. If omitted, returns rules for all available languages."
      },
      "category": { "type": "string" },
      "profile": { "type": "string" },
      "severity": { "type": "string", "enum": ["error", "warning", "suggestion", "silent"] }
    }
  }
}
```

#### Tool: `supported_languages`

Discovery tool for agents to check what's available.

```json
{
  "name": "supported_languages",
  "description": "List all languages currently supported by the analysis server, with their available profiles and language-specific options.",
  "inputSchema": {
    "type": "object",
    "properties": {}
  }
}
```

**Example Response:**
```json
{
  "languages": [
    {
      "id": "csharp",
      "name": "C#",
      "engine": "Roslyn",
      "profiles": ["default", "strict", "library", "minimal", "ai-agent"],
      "project_file_types": [".csproj", ".sln"],
      "language_options_schema": {
        "target_framework": { "type": "string", "default": "net8.0", "examples": ["net8.0", "net9.0"] }
      }
    }
  ]
}
```

### MCP Server Configuration

**For Claude Code (stdio transport):**
```json
// .claude/mcp.json or ~/.config/claude-code/mcp.json
{
  "mcpServers": {
    "idiomatic": {
      "command": "dotnet",
      "args": ["tool", "run", "idiomatic-mcp"],
      "env": {
        "IDIOMATIC_PROFILE": "ai-agent"
      }
    }
  }
}
```

**For networked agents (HTTP/SSE transport):**
```bash
idiomatic-mcp serve --transport sse --port 3001
```

### Agent Workflow

```
AI Agent receives coding task from user
        │
        ▼
Agent generates initial code (in any supported language)
        │
        ▼
Agent calls MCP tool: analyze(language="csharp", source_code=..., profile="ai-agent")
        │
        ▼
MCP Server dispatches to C# Analysis Engine
        │
        ▼
Server returns structured diagnostics + idiomatic_score + fixed_source
        │
        ▼
Agent evaluates results:
  ├── Score >= 90? → Ship it. Return code to user.
  ├── Has auto-fixed source? → Use fixed_source as new baseline.
  └── Has remaining diagnostics? → Agent reads rationale,
      │   applies manual fixes, re-analyzes.
      │
      ▼
Agent calls analyze again with revised code
        │
        ▼
Repeat until score >= threshold or max iterations reached
        │
        ▼
Agent returns final code to user with analysis summary
```

**Recommended Agent System Prompt Integration:**
```
You have access to an MCP tool called "analyze" that checks source code
for correctness and idiomatic patterns. After writing code in a supported
language (check via "supported_languages" tool):

1. Call analyze with language and your generated code
2. If the idiomatic_score is below 85, apply the suggested fixes
3. If fixed_source is provided and all fixes are high-confidence, use it directly
4. Re-analyze to confirm improvements
5. Include the final score when presenting code to the user
```

---

## Product Surface 3: Rule Configurator & Analytics Platform

### What It Is

A web-based (and CLI-accessible) tool that lets teams build a custom `.editorconfig` interactively, preview what each rule does against real code, and optionally subscribe to a paid tier that tracks analysis trends over time.

### Components

#### 3A. Rule Configurator (Free — Web + CLI)

**Web UI:** A single-page app where users:
1. Start from a profile (default, strict, library, minimal) as a baseline
2. Browse every rule by category, toggle on/off, adjust severity
3. See a **live preview** — a sample C# file (or paste your own) with before/after diffs that update as you toggle rules
4. Export a `.editorconfig` file tailored to their choices
5. Optionally run a one-off analysis against pasted code or an uploaded `.cs` file

**CLI equivalent:**
```bash
# Interactive configurator in terminal
idiomatic-cs configure --base strict --interactive

# Non-interactive: start from profile, override specific rules
idiomatic-cs configure --base default \
  --enable IC0001:warning \
  --disable SA1600 \
  --output .editorconfig
```

**Key UX details:**
- Rules are grouped by category (Modernization, Pattern Matching, Async, Naming, etc.)
- Each rule shows: ID, one-line description, severity, source package, and a collapsible before/after code example
- The live preview panel highlights lines that would be flagged, with inline diagnostic messages
- A summary bar shows total diagnostics by severity as rules are toggled
- "Start from team template" option — paste a URL to an existing `.editorconfig` and the UI populates toggles to match, so teams can visualize and tweak what they already have

#### 3B. Analytics Dashboard (Paid — Server-Side)

**What it does:** Teams connect their projects and get historical tracking of code quality trends. The analysis results that Product 1 (CLI) and Product 2 (MCP server) already produce get forwarded to the analytics service for storage and visualization.

**Data ingestion — two paths:**
```bash
# Path 1: CLI uploads results after analysis
idiomatic-cs analyze ./src/MySolution.sln \
  --output sarif \
  --upload --team-token $IDIOMATIC_TEAM_TOKEN

# Path 2: MCP server forwards results automatically (configured per-team)
# In MCP server config:
{
  "analytics": {
    "enabled": true,
    "team_token": "$IDIOMATIC_TEAM_TOKEN",
    "endpoint": "https://api.idiomatic-csharp.dev/ingest"
  }
}
```

**Dashboard features:**

| Feature | What It Shows |
|---------|--------------|
| Score trend | Idiomatic score over time per project, branch, or repo |
| Rule heatmap | Which rules fire most frequently — surfaces systemic patterns |
| AI vs human comparison | Side-by-side quality metrics for AI-authored vs human-authored code |
| Suppression tracking | Which rules teams suppress most — feedback signal for profile tuning |
| Category breakdown | Trends per category (async, naming, pattern matching, etc.) |
| PR quality gate history | Pass/fail rate over time, average diagnostics per PR |
| Team benchmarking | Anonymized comparison against aggregate scores across all teams (opt-in) |

**Pricing model (candidates — TBD):**

| Tier | What You Get |
|------|-------------|
| Free | Configurator, one-off analysis runs, `.editorconfig` export, open source CLI + NuGet + MCP server |
| Team | Analytics dashboard, 90-day history, up to 5 repos, team benchmarking |
| Enterprise | Unlimited repos, unlimited history, custom rule packs, SLA support, SSO, self-hosted option |

### How It Connects to Products 1 & 2

```
┌────────────────────────┐
│  Product 3:            │
│  Configurator + Analytics  │
│                        │
│  ┌──────────────────┐  │
│  │  Web Configurator│──┼──► Generates .editorconfig
│  │  (Free)          │  │         │
│  └──────────────────┘  │         │ User drops into project
│                        │         ▼
│  ┌──────────────────┐  │    ┌──────────┐     ┌───────────┐
│  │  Analytics       │◄─┼────│Product 1 │     │Product 2  │
│  │  Dashboard       │◄─┼────│CLI       │     │MCP Server │
│  │  (Paid)          │  │    │--upload  │     │analytics: │
│  └──────────────────┘  │    └──────────┘     │  enabled  │
│         │              │                      └───────────┘
│         ▼              │
│  Historical trends,    │
│  team benchmarks,      │
│  AI vs human metrics   │
└────────────────────────┘
```

The configurator is the **onboarding funnel**: try rules → preview impact → export config → adopt Product 1 or 2. The analytics dashboard is the **retention and monetization layer**: once teams are generating analysis data through normal usage, they pay to store and visualize it.

### Configurator: Project & Repo Structure

```
src/
├── IdiomaticCSharp.Web/                ← Configurator web app
│   ├── IdiomaticCSharp.Web.csproj
│   ├── wwwroot/
│   │   └── index.html                 ← SPA entry point
│   ├── Api/
│   │   ├── AnalyzeEndpoint.cs         ← One-off analysis (free)
│   │   ├── ConfigureEndpoint.cs       ← Generate .editorconfig
│   │   └── IngestEndpoint.cs          ← Receive analytics data (paid)
│   └── Services/
│       └── AnalysisService.cs         ← Wraps IdiomaticCSharp.Core
│
├── IdiomaticCSharp.Analytics/          ← Analytics backend
│   ├── IdiomaticCSharp.Analytics.csproj
│   ├── Storage/
│   │   └── AnalysisResultStore.cs     ← Persists results (Postgres, etc.)
│   ├── Aggregation/
│   │   ├── TrendCalculator.cs
│   │   ├── HeatmapBuilder.cs
│   │   └── AiVsHumanComparator.cs
│   └── Api/
│       └── DashboardEndpoints.cs
```

### Updated Dependency Graph

```
IdiomaticCSharp.Core
        │
        ├──► IdiomaticCSharp.Analyzers      (Product 1: NuGet package)
        ├──► IdiomaticCSharp.Cli             (Product 1: CLI tool)
        ├──► IdiomaticCSharp.McpServer       (Product 2: MCP server)
        ├──► IdiomaticCSharp.Web             (Product 3: Configurator + API)
        └──► IdiomaticCSharp.Analytics       (Product 3: Analytics backend)
```

### Shared Artifacts

```
Repository: idiomatic/
├── src/
│   ├── Idiomatic.Abstractions/              ← SHARED: Language-agnostic interfaces
│   │   ├── IAnalysisEngine.cs               ← Interface all language engines implement
│   │   ├── AnalysisResult.cs                ← Shared response types
│   │   ├── Diagnostic.cs
│   │   ├── AnalysisOptions.cs
│   │   └── AnalysisSummary.cs
│   │
│   ├── Idiomatic.CSharp/                    ← C# language engine (first engine)
│   │   ├── CSharpAnalysisEngine.cs          ← Implements IAnalysisEngine
│   │   ├── Diagnostics/
│   │   │   ├── DiagnosticEnricher.cs        ← Adds rationale, fix suggestions
│   │   │   └── IdiomaticScoreCalculator.cs
│   │   ├── Profiles/
│   │   │   ├── default.editorconfig
│   │   │   ├── strict.editorconfig
│   │   │   ├── library.editorconfig
│   │   │   ├── minimal.editorconfig
│   │   │   └── ai-agent.editorconfig
│   │   ├── Rules/                           ← Custom IC-xxxx analyzers
│   │   │   ├── IC0001_PreferPrimaryConstructors.cs
│   │   │   ├── IC0002_PreferCollectionExpressions.cs
│   │   │   └── ... (one file per rule)
│   │   └── Fixes/                           ← CodeFix providers for custom rules
│   │       ├── IC0001_Fix.cs
│   │       └── ...
│   │
│   ├── Idiomatic.CSharp.Analyzers/          ← PRODUCT 1: NuGet analyzer package
│   │   ├── Idiomatic.CSharp.Analyzers.csproj
│   │   └── build/
│   │       └── Idiomatic.CSharp.Analyzers.props
│   │
│   ├── Idiomatic.CSharp.Cli/               ← PRODUCT 1: CLI tool
│   │   ├── Idiomatic.CSharp.Cli.csproj
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── AnalyzeCommand.cs
│   │       ├── FixCommand.cs
│   │       ├── ScoreCommand.cs
│   │       └── RulesCommand.cs
│   │
│   └── Idiomatic.McpServer/                 ← PRODUCT 2: Language-agnostic MCP server
│       ├── Idiomatic.McpServer.csproj
│       ├── Program.cs
│       ├── EngineRegistry.cs                ← Maps language → IAnalysisEngine
│       ├── Tools/
│       │   ├── AnalyzeTool.cs               ← Dispatches to engine by language
│       │   ├── AnalyzeProjectTool.cs
│       │   ├── FixTool.cs
│       │   ├── ExplainRuleTool.cs
│       │   ├── ListRulesTool.cs
│       │   └── SupportedLanguagesTool.cs
│       └── Transport/
│           ├── StdioTransport.cs
│           └── SseTransport.cs
│
│   ├── Idiomatic.Web/                       ← PRODUCT 3: Configurator web app
│   │   ├── Idiomatic.Web.csproj
│   │   ├── wwwroot/
│   │   │   └── index.html                   ← SPA entry point
│   │   ├── Api/
│   │   │   ├── AnalyzeEndpoint.cs           ← One-off analysis (free)
│   │   │   ├── ConfigureEndpoint.cs         ← Generate .editorconfig
│   │   │   └── IngestEndpoint.cs            ← Receive analytics data (paid)
│   │   └── Services/
│   │       └── AnalysisService.cs           ← Wraps IAnalysisEngine
│   │
│   └── Idiomatic.Analytics/                 ← PRODUCT 3: Analytics backend
│       ├── Idiomatic.Analytics.csproj
│       ├── Storage/
│       │   └── AnalysisResultStore.cs       ← Persists results (Postgres, etc.)
│       ├── Aggregation/
│       │   ├── TrendCalculator.cs
│       │   ├── HeatmapBuilder.cs
│       │   └── AiVsHumanComparator.cs
│       └── Api/
│           └── DashboardEndpoints.cs
│
├── tests/
│   ├── Idiomatic.CSharp.Tests/
│   │   ├── CSharpAnalysisEngineTests.cs
│   │   ├── Rules/                           ← One test file per custom rule
│   │   │   ├── IC0001Tests.cs
│   │   │   └── ...
│   │   └── Profiles/
│   │       └── ProfileTests.cs
│   ├── Idiomatic.CSharp.Cli.Tests/
│   ├── Idiomatic.McpServer.Tests/
│   ├── Idiomatic.Web.Tests/
│   └── Idiomatic.Analytics.Tests/
│
├── docs/
│   ├── rules/
│   │   └── csharp/                          ← Per-language rule docs
│   │       ├── IC0001.md
│   │       └── ...
│   └── profiles.md
│
├── samples/
│   ├── before-after/
│   │   └── csharp/                          ← Per-language examples
│   └── agent-integration/
│
└── Idiomatic.sln
```

### Dependency Graph

```
Idiomatic.Abstractions              ← Language-agnostic interfaces, shared types
        │
        ├──► Idiomatic.CSharp                (implements IAnalysisEngine using Roslyn)
        │         │
        │         ├──► Idiomatic.CSharp.Analyzers   (packages C# engine as NuGet analyzer)
        │         └──► Idiomatic.CSharp.Cli         (C#-specific CLI, uses System.CommandLine)
        │
        ├──► Idiomatic.McpServer             (references Abstractions + all language engines)
        │         │                           (dispatches via EngineRegistry)
        │         └──► [Future: Idiomatic.TypeScript, Idiomatic.Rust, etc.]
        │
        ├──► Idiomatic.Web                   (Configurator + one-off analysis API)
        │         │
        │         └──► Idiomatic.Analytics   (Storage, aggregation, dashboard API)
        │
        └──► [All language engines register into Web + Analytics via same Abstractions interface]
```

The critical design constraints:
- **Abstractions has zero language-specific knowledge.** It defines `IAnalysisEngine`, `AnalysisResult`, `Diagnostic`, and related types. All language engines implement the same interface.
- **Each language engine has zero knowledge of how it's consumed.** It returns the same `AnalysisResult` whether called by a NuGet IDE integration, a CLI, or the MCP server.
- **The MCP server has zero language-specific logic.** It receives a `language` parameter, looks up the corresponding `IAnalysisEngine` in the `EngineRegistry`, and delegates. Adding a new language means registering a new engine — no changes to tool definitions or transport code.

### Integration Points

| Scenario | Product 1 | Product 2 | Product 3 | How They Connect |
|----------|-----------|-----------|-----------|-----------------|
| Developer writes code in IDE | NuGet package provides real-time feedback | — | — | — |
| AI agent generates code | — | MCP server validates output | — | Same engine, same rules |
| CI/CD pipeline | CLI runs on PR | — | CLI `--upload` sends results to analytics | Same engine via CLI |
| AI agent in CI review | — | MCP server validates AI-authored PRs | MCP forwards results to analytics | Same engine via MCP |
| Developer configures rules | `.editorconfig` in repo | Agent inherits same config | Configurator generates the `.editorconfig` | Shared config means human and AI follow identical rules |
| Team uses both | NuGet + CLI in dev workflow | MCP server for AI pair programming | Dashboard shows trends from both | One ruleset, two enforcement points, one analytics layer |
| Team evaluates adoption | — | — | Configurator previews rules against real code | Onboarding funnel into Products 1 & 2 |
| Team tracks improvement | — | — | Dashboard shows score trends, AI vs human, rule heatmaps | Retention and monetization layer |

### The Key Insight

When a team uses the Configurator (Product 3) to build their `.editorconfig`, that same config file drives the NuGet package in their IDE (Product 1) and the MCP server validating their AI agent's output (Product 2). Human and AI are held to identical standards. The analytics dashboard then shows whether both are improving over time — one ruleset, two enforcement points, one feedback loop.

---

## Build Order & Implementation Plan

### Phase 1: Core Engine + Custom Rules

**Goal:** The `IdiomaticCSharp.Core` library compiles, loads analyzers, runs analysis, and returns structured results.

**Tasks:**
1. Set up solution structure (all projects, test projects)
2. Implement `AnalysisEngine.AnalyzeSourceAsync()` — in-memory compilation + analysis
3. Implement profile loading from `.editorconfig` files
4. Write first 5 custom analyzers (IC0001-IC0005) with tests
5. Implement `DiagnosticEnricher` — rationale text, fix suggestions
6. Implement `IdiomaticScoreCalculator` — composite scoring algorithm
7. Integration tests: known-bad C# in, expected diagnostics out

**Definition of Done:** Given a C# source string, the engine returns correct diagnostics with rationale and a meaningful score.

### Phase 2: CLI Tool (Product 1 — Part 1)

**Goal:** `dotnet tool install -g idiomatic-cs` works; `idiomatic-cs analyze` produces output.

**Tasks:**
1. Implement CLI using `System.CommandLine`
2. `analyze` command: text, JSON, SARIF output
3. `fix` command: apply auto-fixes, write to disk
4. `score` command: exit code based on threshold
5. `rules` command: list/filter rules
6. End-to-end tests against sample projects

### Phase 3: NuGet Analyzer Package (Product 1 — Part 2)

**Goal:** `<PackageReference Include="IdiomaticCSharp.Analyzers">` lights up in IDE.

**Tasks:**
1. Configure NuGet package to ship analyzers in correct folder structure
2. Build `.props` file for default configuration injection
3. Test in Visual Studio, Rider, VS Code with C# Dev Kit
4. Write remaining custom analyzers (IC0006-IC0015)
5. Publish to NuGet.org (or private feed for testing)

### Phase 4: MCP Server (Product 2)

**Goal:** AI agents can call `analyze_csharp` via MCP and get structured results.

**Tasks:**
1. Implement MCP server using .NET MCP SDK (or raw stdio JSON-RPC)
2. Implement all 5 tools: `analyze_csharp`, `analyze_csharp_project`, `fix_csharp`, `explain_rule`, `list_rules`
3. Implement stdio transport (for Claude Code)
4. Implement SSE transport (for networked agents)
5. Test with Claude Code: configure MCP, generate C# code, verify analysis loop works
6. Write agent integration guide with example system prompts
7. Publish as `dotnet tool install -g idiomatic-cs-mcp`

### Phase 5: Rule Configurator (Product 3 — Part 1)

**Goal:** A web app where users browse rules, toggle them, preview impact against real code, and export a custom `.editorconfig`.

**Tasks:**
1. Build API endpoint that lists all rules with metadata (ID, description, category, severity, source package, before/after code example)
2. Build API endpoint that accepts source code + rule configuration, runs analysis, returns diagnostics
3. Build API endpoint that generates `.editorconfig` from a set of rule selections
4. Build web SPA: rule browser with category grouping, toggle/severity controls, live preview panel with before/after diffs
5. Add `configure` command to CLI (`idiomatic-cs configure --interactive`)
6. Test against all profiles — configurator output should match shipped profile files exactly when same options selected

**Definition of Done:** User can browse rules, toggle them, see live impact on sample code, and download a working `.editorconfig`.

### Phase 6: Analytics Dashboard (Product 3 — Part 2)

**Goal:** Teams can upload analysis results and view historical trends.

**Tasks:**
1. Design data schema for storing analysis results (team, project, branch, commit, timestamp, diagnostics, score)
2. Implement ingest endpoint + authentication (team tokens)
3. Add `--upload` flag to CLI and analytics forwarding config to MCP server
4. Implement aggregation: score trends, rule heatmaps, category breakdowns
5. Implement AI vs human comparison (requires metadata tag on ingested results indicating source)
6. Build dashboard web UI
7. Implement team benchmarking (opt-in anonymized aggregate comparison)
8. Billing/tier enforcement

**Definition of Done:** A team running the CLI or MCP server with `--upload` can view their score trend, top-firing rules, and AI vs human code quality over time in a dashboard.

### Phase 7: Polish & Documentation

**Tasks:**
1. Rule documentation (one markdown page per rule with examples)
2. Profile documentation
3. Agent integration cookbook
4. Sample before/after code pairs
5. README, contributing guide, license (open source the ruleset, proprietary the server?)
6. Performance benchmarking (analysis should complete in < 2 seconds for single files)

---

## Technical Decisions & Constraints

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Language | C# / .NET 8+ | Roslyn APIs are .NET-native; dogfood our own ecosystem |
| Analyzer loading | In-process via `CompilationWithAnalyzers` | Fastest path; avoids process spawning |
| MCP SDK | `ModelContextProtocol` NuGet package (if available) or raw JSON-RPC over stdio | Prefer official SDK; fall back to manual implementation |
| CLI framework | `System.CommandLine` | Microsoft's modern CLI library for .NET |
| Test framework | xUnit + `Microsoft.CodeAnalysis.Testing` | Standard for Roslyn analyzer testing |
| Configuration | `.editorconfig` | Industry standard; already supported by Roslyn infrastructure |
| Output format | SARIF (for CI), JSON (for agents), text (for humans) | SARIF is the standard for static analysis tooling |
| Upstream analyzer vendoring | Reference as NuGet packages, configure via `.editorconfig` | Don't fork; compose. Easier to stay current. |
| Scoring algorithm | Weighted deduction from 100 | Errors deduct more than warnings; per-category weights configurable |
| Auto-fix safety | Only apply fixes marked "high confidence" by default | Agents can override; humans get lightbulb choice in IDE |

---

## Open Questions & Decisions

### DECIDED

1. **Licensing model:** Open source with paid services. The Core engine, analyzers, CLI, and ruleset are fully open source. Revenue comes from paid services built on top — candidates include hosted MCP server (managed quality gate as a service), team dashboards / analytics, SLA support, or a marketplace for custom rule packs. Exact paid service model TBD, but the analysis engine itself is open.

2. **Upstream analyzer versioning:** Pin to specific versions, but automate the update process. Use Dependabot or a similar tool to open PRs when upstream packages release new versions. Each update PR runs the full test suite against the pinned ruleset to catch breaking changes or new rules that conflict with our curation. A human reviews and merges — the pinning is automated, the decision to upgrade is not.

3. **C# language version targeting:** Yes — modernization rules must be language-version-aware. The engine should detect the project's `<LangVersion>` (or infer it from the target framework) and suppress modernization suggestions that require a newer language version than the project supports. A project targeting C# 10 should never see "prefer primary constructors" (C# 12). This applies to IC0001, IC0002, IC0003, and any future modernization rules. Implementation note: the `AnalysisOptions` should carry a `LanguageVersion` property, and each modernization analyzer should check it before reporting.

### TBD

4. **Scoring algorithm weights:** How much should each category contribute to the composite score? Needs empirical tuning against real codebases.

5. **Rule suppression in AI context:** Should the `ai-agent` profile suppress certain rules that create noise for agents but matter for humans (e.g., file-scoped namespaces, using directive ordering)?

6. **MCP server lifecycle:** Should it run as a persistent daemon or spawn per-request? Persistent is faster but uses memory.
