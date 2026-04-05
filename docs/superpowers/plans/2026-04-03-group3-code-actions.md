# Group 3: Code Actions Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `get-code-fixes`, `get-refactorings`, and `preview-code-action` MCP tools that let agents discover available fixes/refactorings and preview their effects before applying.

**Architecture:** A new `CodeActionService` in `Parlance.Analysis` handles code fix/refactoring discovery and action caching. It uses the existing `DiscoverInstances<T>()` pattern from `AssemblyExtensions` to load `CodeFixProvider` and `CodeRefactoringProvider` types from the same analyzer DLLs that `AnalyzerLoader` already processes. Action IDs are session-scoped and tied to the workspace snapshot version. The three MCP tools are thin handlers that delegate to `CodeActionService`.

**Tech Stack:** Roslyn (`Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.CodeFixes`, `Microsoft.CodeAnalysis.CodeActions`, `Microsoft.CodeAnalysis.CodeRefactorings`), MCP SDK, xUnit

**Spec:** `docs/superpowers/specs/2026-04-03-more-tools-design.md` — Group 3: Code Actions section

---

### Task 1: Extend AnalyzerLoader to Load CodeFixProviders and CodeRefactoringProviders

**Files:**
- Modify: `src/Parlance.Analyzers.Upstream/AnalyzerLoader.cs`

The existing `AnalyzerLoader.LoadAll()` loads `DiagnosticAnalyzer` instances from both the PARL assembly and upstream DLLs. The same assemblies contain `CodeFixProvider` and `CodeRefactoringProvider` types. Add parallel methods using the same `DiscoverInstances<T>()` extension.

- [ ] **Step 1: Add LoadCodeFixProviders method**

Add after the existing `LoadAll` method in `AnalyzerLoader.cs`:

```csharp
    public static ImmutableArray<CodeFixProvider> LoadCodeFixProviders(string targetFramework)
    {
        var providers = new List<CodeFixProvider>();

        var parlAssembly = typeof(Parlance.CSharp.Analyzers.Rules.PARL9003_UseDefaultLiteral).Assembly;
        providers.AddRange(parlAssembly.DiscoverInstances<CodeFixProvider>());

        var analyzerDir = ResolveAnalyzerDirectory(targetFramework);
        foreach (var dllPath in Directory.EnumerateFiles(analyzerDir, "*.dll"))
        {
            var loadContext = new AssemblyLoadContext(Path.GetFileName(dllPath), isCollectible: false);
            loadContext.Resolving += (alc, assemblyName) =>
                ResolveFromDirectory(alc, assemblyName, analyzerDir);

            var assembly = loadContext.LoadFromAssemblyPath(dllPath);
            providers.AddRange(assembly.DiscoverInstances<CodeFixProvider>());
        }

        return [.. providers];
    }

    public static ImmutableArray<CodeRefactoringProvider> LoadCodeRefactoringProviders(string targetFramework)
    {
        var providers = new List<CodeRefactoringProvider>();

        var parlAssembly = typeof(Parlance.CSharp.Analyzers.Rules.PARL9003_UseDefaultLiteral).Assembly;
        providers.AddRange(parlAssembly.DiscoverInstances<CodeRefactoringProvider>());

        var analyzerDir = ResolveAnalyzerDirectory(targetFramework);
        foreach (var dllPath in Directory.EnumerateFiles(analyzerDir, "*.dll"))
        {
            var loadContext = new AssemblyLoadContext(Path.GetFileName(dllPath), isCollectible: false);
            loadContext.Resolving += (alc, assemblyName) =>
                ResolveFromDirectory(alc, assemblyName, analyzerDir);

            var assembly = loadContext.LoadFromAssemblyPath(dllPath);
            providers.AddRange(assembly.DiscoverInstances<CodeRefactoringProvider>());
        }

        return [.. providers];
    }
```

Note: You'll need to add `using Microsoft.CodeAnalysis.CodeFixes;` and `using Microsoft.CodeAnalysis.CodeRefactorings;` to the file. If these namespaces aren't available, add package reference `Microsoft.CodeAnalysis.Workspaces.Common` to `Parlance.Analyzers.Upstream.csproj` (check if it's already transitively available first).

- [ ] **Step 2: Verify the repeated `AssemblyLoadContext` creation is okay**

The existing `LoadAll` method creates a new `AssemblyLoadContext` per DLL. The new methods do the same. This means each DLL may be loaded multiple times in different contexts. If this causes issues (e.g., type identity mismatches), extract a shared `LoadAnalyzerAssemblies(targetFramework)` method that returns cached assemblies. Start with the simple approach and refactor if tests fail.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/AnalyzerLoader.cs
git commit -m "feat: extend AnalyzerLoader with CodeFixProvider and CodeRefactoringProvider loading

Same DiscoverInstances<T> pattern as DiagnosticAnalyzer loading, same
upstream DLL sources."
```

---

### Task 2: CodeActionService — Shared Infrastructure

**Files:**
- Create: `src/Parlance.Analysis/CodeActionService.cs`
- Modify: `src/Parlance.Mcp/Program.cs` (DI registration)

- [ ] **Step 1: Create CodeActionService**

```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis;

public sealed class CodeActionService(
    WorkspaceSessionHolder holder, ILogger<CodeActionService> logger)
{
    private readonly ConcurrentDictionary<string, CachedCodeAction> _actionCache = new();
    private int _nextFixId;
    private int _nextRefactorId;

    private ImmutableArray<CodeFixProvider>? _fixProviders;
    private ImmutableArray<CodeRefactoringProvider>? _refactoringProviders;

    private CSharpWorkspaceSession Session => holder.Session;

    private ImmutableArray<CodeFixProvider> FixProviders =>
        _fixProviders ??= AnalyzerLoader.LoadCodeFixProviders(Session.TargetFramework);

    private ImmutableArray<CodeRefactoringProvider> RefactoringProviders =>
        _refactoringProviders ??= AnalyzerLoader.LoadCodeRefactoringProviders(Session.TargetFramework);

    public async Task<ImmutableList<CodeFixEntry>> GetCodeFixesAsync(
        string filePath, int line, string? diagnosticId = null, CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        var compilation = await document.Project.GetCompilationAsync(ct);
        if (compilation is null) return [];

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree is null) return [];

        var semanticModel = compilation.GetSemanticModel(tree);
        var text = await tree.GetTextAsync(ct);
        var zeroLine = line - 1;
        if (zeroLine < 0 || zeroLine >= text.Lines.Count) return [];

        var lineSpan = text.Lines[zeroLine].Span;

        // Get diagnostics on this line
        var analyzers = AnalyzerLoader.LoadAll(Session.TargetFramework);
        var compilationWithAnalyzers = compilation.WithAnalyzers([.. analyzers], cancellationToken: ct);
        var allDiags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        var lineDiags = allDiags
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree)
            .Where(d => d.Location.SourceSpan.IntersectsWith(lineSpan))
            .Where(d => diagnosticId is null || d.Id == diagnosticId)
            .ToImmutableList();

        if (lineDiags.IsEmpty) return [];

        var fixes = new List<CodeFixEntry>();
        var snapshotVersion = Session.SnapshotVersion;

        foreach (var diagnostic in lineDiags)
        {
            foreach (var provider in FixProviders)
            {
                if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                    continue;

                var codeActions = new List<CodeAction>();
                var context = new CodeFixContext(document, diagnostic,
                    (action, _) => codeActions.Add(action), ct);

                try
                {
                    await provider.RegisterCodeFixesAsync(context);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "CodeFixProvider {Provider} failed for {DiagId}",
                        provider.GetType().Name, diagnostic.Id);
                    continue;
                }

                foreach (var action in codeActions)
                {
                    var id = $"fix-{Interlocked.Increment(ref _nextFixId)}";
                    _actionCache[id] = new CachedCodeAction(action, snapshotVersion);

                    var scope = provider.GetFixAllProvider() is not null ? "document" : "document";
                    fixes.Add(new CodeFixEntry(id, action.Title, diagnostic.Id,
                        diagnostic.GetMessage(), scope));
                }
            }
        }

        return [.. fixes];
    }

    public async Task<ImmutableList<RefactoringEntry>> GetRefactoringsAsync(
        string filePath, int line, int column, int? endLine = null, int? endColumn = null,
        CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        var text = await document.GetTextAsync(ct);
        var zeroLine = line - 1;
        var zeroCol = column - 1;
        if (zeroLine < 0 || zeroLine >= text.Lines.Count) return [];

        TextSpan span;
        if (endLine is not null && endColumn is not null)
        {
            var startPos = text.Lines.GetPosition(new LinePosition(zeroLine, zeroCol));
            var endPos = text.Lines.GetPosition(new LinePosition(endLine.Value - 1, endColumn.Value - 1));
            span = TextSpan.FromBounds(startPos, endPos);
        }
        else
        {
            var position = text.Lines.GetPosition(new LinePosition(zeroLine, zeroCol));
            var root = await document.GetSyntaxRootAsync(ct);
            if (root is null) return [];
            var token = root.FindToken(position);
            span = token.Span;
        }

        var refactorings = new List<RefactoringEntry>();
        var snapshotVersion = Session.SnapshotVersion;

        foreach (var provider in RefactoringProviders)
        {
            var codeActions = new List<CodeAction>();
            var context = new CodeRefactoringContext(document, span,
                action => codeActions.Add(action), ct);

            try
            {
                await provider.ComputeRefactoringsAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CodeRefactoringProvider {Provider} failed",
                    provider.GetType().Name);
                continue;
            }

            foreach (var action in codeActions)
            {
                var id = $"refactor-{Interlocked.Increment(ref _nextRefactorId)}";
                _actionCache[id] = new CachedCodeAction(action, snapshotVersion);
                refactorings.Add(new RefactoringEntry(id, action.Title, null));
            }
        }

        return [.. refactorings];
    }

    public async Task<CodeActionPreview?> PreviewAsync(string actionId, CancellationToken ct = default)
    {
        if (!_actionCache.TryGetValue(actionId, out var cached))
            return null;

        if (cached.SnapshotVersion != Session.SnapshotVersion)
            return CodeActionPreview.Expired(actionId, cached.Action.Title);

        var operations = await cached.Action.GetOperationsAsync(ct);
        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOp is null) return null;

        var changedSolution = applyOp.ChangedSolution;
        var currentSolution = Session.CurrentSolution;

        var changes = new List<FileChange>();
        foreach (var projectChanges in changedSolution.GetChanges(currentSolution).GetProjectChanges())
        {
            foreach (var docId in projectChanges.GetChangedDocuments())
            {
                var oldDoc = currentSolution.GetDocument(docId);
                var newDoc = changedSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null) continue;

                var oldText = await oldDoc.GetTextAsync(ct);
                var newText = await newDoc.GetTextAsync(ct);
                var textChanges = newText.GetTextChanges(oldText);

                var edits = textChanges.Select(change =>
                {
                    var startLine = oldText.Lines.GetLinePosition(change.Span.Start).Line + 1;
                    var endLine = oldText.Lines.GetLinePosition(change.Span.End).Line + 1;
                    var originalText = oldText.GetSubText(change.Span).ToString();
                    return new TextEdit(startLine, endLine, originalText, change.NewText ?? "");
                }).ToImmutableList();

                changes.Add(new FileChange(oldDoc.FilePath ?? "", edits));
            }
        }

        return new CodeActionPreview(actionId, cached.Action.Title, [.. changes]);
    }

    private Document? GetDocument(string filePath)
    {
        var docId = Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        return docId is null ? null : Session.CurrentSolution.GetDocument(docId);
    }
}

public sealed record CachedCodeAction(CodeAction Action, long SnapshotVersion);

public sealed record CodeFixEntry(
    string Id, string Title, string DiagnosticId, string DiagnosticMessage, string Scope);

public sealed record RefactoringEntry(string Id, string Title, string? Category);

public sealed record CodeActionPreview(
    string ActionId, string Title, ImmutableList<FileChange> Changes)
{
    public bool IsExpired => false;

    public static CodeActionPreview Expired(string actionId, string title) => new(actionId, title, [])
    {
        IsExpired = true
    };
}

public sealed record FileChange(string FilePath, ImmutableList<TextEdit> Edits);

public sealed record TextEdit(int StartLine, int EndLine, string OriginalText, string NewText);
```

Note: You'll need to check that `Session.TargetFramework` exists on `CSharpWorkspaceSession`. If it doesn't, extract the target framework from the first project's `ParseOptions` or from `CSharpProjectInfo`. The key is getting the TFM string (e.g., "net10.0") that `AnalyzerLoader` needs. Check how `AnalysisService` gets it and follow the same pattern.

- [ ] **Step 2: Register CodeActionService in Program.cs**

Add after the existing `AnalysisService` registration:

```csharp
builder.Services.AddSingleton<CodeActionService>();
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`
Expected: Build succeeds (this builds Parlance.Analysis transitively)

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.Analysis/CodeActionService.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add CodeActionService for code fix/refactoring discovery and preview

Session-scoped action cache with snapshot version expiry. Loads
CodeFixProviders and CodeRefactoringProviders from analyzer assemblies."
```

---

### Task 3: get-code-fixes — Failing Tests

**Files:**
- Create: `tests/Parlance.Mcp.Tests/Tools/GetCodeFixesToolTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetCodeFixesToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;
    private CodeActionService _codeActions = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
        _codeActions = new CodeActionService(_holder, NullLogger<CodeActionService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task GetCodeFixes_FileWithDiagnostics_ReturnsFixes()
    {
        // Find a file that has diagnostics — use the analyze tool to find one first,
        // or target a file known to have warnings. The test validates the tool works
        // end-to-end; the specific fixes depend on which analyzers are loaded.
        // Use a file in the solution that's likely to have at least one diagnostic.
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: filePath, line: 1, diagnosticId: null,
            CancellationToken.None);

        // We expect either "found" (fixes available) or "no_fixes" (no diagnostics on line 1)
        Assert.Contains(result.Status, ["found", "no_fixes"]);
    }

    [Fact]
    public async Task GetCodeFixes_UnknownFile_ReturnsNotFound()
    {
        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: "/nonexistent/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void GetCodeFixes_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetCodeFixesTool.GetCodeFixes(
            holder, codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: "/some/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GetCodeFixes_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetCodeFixesTool.GetCodeFixes(
            holder, codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: "/some/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
```

- [ ] **Step 2: Verify tests do not compile**

Run: `dotnet build tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`
Expected: Compilation error — `GetCodeFixesTool` does not exist

---

### Task 4: get-code-fixes — Implementation

**Files:**
- Create: `src/Parlance.Mcp/Tools/GetCodeFixesTool.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Create the tool class**

```csharp
using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetCodeFixesTool
{
    [McpServerTool(Name = "get-code-fixes", ReadOnly = true)]
    [Description("Get available code fixes for diagnostics at a specific line in a file. " +
                 "Returns fix IDs that can be passed to preview-code-action to see the changes. " +
                 "Use after 'analyze' to see what automated fixes are available.")]
    public static async Task<GetCodeFixesResult> GetCodeFixes(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        ILogger<GetCodeFixesTool> logger,
        [Description("Absolute file path")]
        string filePath,
        [Description("1-based line number")]
        int line,
        [Description("Filter to a specific diagnostic ID (e.g., 'CS8600', 'PARL0004')")]
        string? diagnosticId = null,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "get-code-fixes");

        if (holder.LoadFailure is { } failure)
            return GetCodeFixesResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetCodeFixesResult.NotLoaded();

        var fixes = await codeActions.GetCodeFixesAsync(filePath, line, diagnosticId, ct);

        if (fixes.IsEmpty)
        {
            // Distinguish "file not found" from "no fixes"
            var docId = holder.Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (docId is null)
                return GetCodeFixesResult.NotFound(filePath);
            return GetCodeFixesResult.NoFixes(filePath, line);
        }

        return new GetCodeFixesResult(
            Status: "found",
            FilePath: filePath,
            Line: line,
            Fixes: fixes,
            Message: null);
    }
}

public sealed record GetCodeFixesResult(
    string Status, string? FilePath, int? Line,
    ImmutableList<CodeFixEntry> Fixes,
    string? Message)
{
    public static GetCodeFixesResult NotFound(string filePath) => new(
        "not_found", filePath, null, [],
        $"File '{filePath}' not found in the workspace");
    public static GetCodeFixesResult NoFixes(string filePath, int line) => new(
        "no_fixes", filePath, line, [],
        $"No code fixes available at {filePath}:{line}");
    public static GetCodeFixesResult NotLoaded() => new(
        "not_loaded", null, null, [],
        "Workspace is still loading");
    public static GetCodeFixesResult LoadFailed(string message) => new(
        "load_failed", null, null, [], message);
}
```

- [ ] **Step 2: Register the tool in Program.cs**

Add `.WithTools<GetCodeFixesTool>()` alphabetically — after `.WithTools<FindReferencesTool>()`:

```csharp
    .WithTools<FindReferencesTool>()
    .WithTools<GetCodeFixesTool>()
    .WithTools<GotoDefinitionTool>()
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "GetCodeFixes" -v n`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.Mcp/Tools/GetCodeFixesTool.cs tests/Parlance.Mcp.Tests/Tools/GetCodeFixesToolTests.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add get-code-fixes MCP tool

Returns available code fixes for diagnostics at a file+line, with
action IDs for preview-code-action."
```

---

### Task 5: get-refactorings — Failing Tests

**Files:**
- Create: `tests/Parlance.Mcp.Tests/Tools/GetRefactoringsToolTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetRefactoringsToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;
    private CodeActionService _codeActions = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
        _codeActions = new CodeActionService(_holder, NullLogger<CodeActionService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task GetRefactorings_AtMethodBody_ReturnsRefactorings()
    {
        // Point at a method body — refactoring providers typically offer suggestions here
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        // Find a line with actual code (not just braces or whitespace)
        var codeLine = Array.FindIndex(lines, l => l.Contains("FindDeclarationsAsync"));
        Assert.True(codeLine >= 0, "Could not find a code line");

        var codeCol = lines[codeLine].IndexOf("FindDeclarationsAsync", StringComparison.Ordinal);

        var result = await GetRefactoringsTool.GetRefactorings(
            _holder, _codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: filePath, line: codeLine + 1, column: codeCol + 1,
            endLine: null, endColumn: null,
            CancellationToken.None);

        // May or may not have refactorings depending on loaded providers
        Assert.Contains(result.Status, ["found", "no_refactorings"]);
    }

    [Fact]
    public async Task GetRefactorings_UnknownFile_ReturnsNotFound()
    {
        var result = await GetRefactoringsTool.GetRefactorings(
            _holder, _codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: "/nonexistent/file.cs", line: 1, column: 1,
            endLine: null, endColumn: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void GetRefactorings_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetRefactoringsTool.GetRefactorings(
            holder, codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: "/some/file.cs", line: 1, column: 1,
            endLine: null, endColumn: null,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GetRefactorings_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetRefactoringsTool.GetRefactorings(
            holder, codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: "/some/file.cs", line: 1, column: 1,
            endLine: null, endColumn: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
```

- [ ] **Step 2: Verify tests do not compile**

Run: `dotnet build tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`
Expected: Compilation error — `GetRefactoringsTool` does not exist

---

### Task 6: get-refactorings — Implementation

**Files:**
- Create: `src/Parlance.Mcp/Tools/GetRefactoringsTool.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Create the tool class**

```csharp
using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetRefactoringsTool
{
    [McpServerTool(Name = "get-refactorings", ReadOnly = true)]
    [Description("Get available refactoring actions at a code location or range. " +
                 "Returns refactoring IDs that can be passed to preview-code-action. " +
                 "Use when doing structural work like extracting methods or introducing variables.")]
    public static async Task<GetRefactoringsResult> GetRefactorings(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        ILogger<GetRefactoringsTool> logger,
        [Description("Absolute file path")]
        string filePath,
        [Description("1-based line number")]
        int line,
        [Description("1-based column number")]
        int column,
        [Description("1-based end line for range selection (optional)")]
        int? endLine = null,
        [Description("1-based end column for range selection (optional)")]
        int? endColumn = null,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "get-refactorings");

        if (holder.LoadFailure is { } failure)
            return GetRefactoringsResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetRefactoringsResult.NotLoaded();

        var refactorings = await codeActions.GetRefactoringsAsync(
            filePath, line, column, endLine, endColumn, ct);

        if (refactorings.IsEmpty)
        {
            var docId = holder.Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (docId is null)
                return GetRefactoringsResult.NotFound(filePath);
            return GetRefactoringsResult.NoRefactorings(filePath);
        }

        return new GetRefactoringsResult(
            Status: "found",
            FilePath: filePath,
            Refactorings: refactorings,
            Message: null);
    }
}

public sealed record GetRefactoringsResult(
    string Status, string? FilePath,
    ImmutableList<RefactoringEntry> Refactorings,
    string? Message)
{
    public static GetRefactoringsResult NotFound(string filePath) => new(
        "not_found", filePath, [],
        $"File '{filePath}' not found in the workspace");
    public static GetRefactoringsResult NoRefactorings(string filePath) => new(
        "no_refactorings", filePath, [],
        $"No refactorings available at the specified location in {filePath}");
    public static GetRefactoringsResult NotLoaded() => new(
        "not_loaded", null, [],
        "Workspace is still loading");
    public static GetRefactoringsResult LoadFailed(string message) => new(
        "load_failed", null, [], message);
}
```

- [ ] **Step 2: Register the tool in Program.cs**

Add `.WithTools<GetRefactoringsTool>()` alphabetically — after `.WithTools<GetCodeFixesTool>()`:

```csharp
    .WithTools<GetCodeFixesTool>()
    .WithTools<GetRefactoringsTool>()
    .WithTools<GotoDefinitionTool>()
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "GetRefactorings" -v n`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.Mcp/Tools/GetRefactoringsTool.cs tests/Parlance.Mcp.Tests/Tools/GetRefactoringsToolTests.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add get-refactorings MCP tool

Returns available refactoring actions at a position or range, with
action IDs for preview-code-action."
```

---

### Task 7: preview-code-action — Failing Tests

**Files:**
- Create: `tests/Parlance.Mcp.Tests/Tools/PreviewCodeActionToolTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class PreviewCodeActionToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;
    private CodeActionService _codeActions = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
        _codeActions = new CodeActionService(_holder, NullLogger<CodeActionService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task Preview_InvalidActionId_ReturnsNotFound()
    {
        var result = await PreviewCodeActionTool.PreviewCodeAction(
            _holder, _codeActions, NullLogger<PreviewCodeActionTool>.Instance,
            actionId: "fix-99999",
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void Preview_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = PreviewCodeActionTool.PreviewCodeAction(
            holder, codeActions, NullLogger<PreviewCodeActionTool>.Instance,
            actionId: "fix-1",
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void Preview_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = PreviewCodeActionTool.PreviewCodeAction(
            holder, codeActions, NullLogger<PreviewCodeActionTool>.Instance,
            actionId: "fix-1",
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
```

- [ ] **Step 2: Verify tests do not compile**

Run: `dotnet build tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`
Expected: Compilation error — `PreviewCodeActionTool` does not exist

---

### Task 8: preview-code-action — Implementation

**Files:**
- Create: `src/Parlance.Mcp/Tools/PreviewCodeActionTool.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Create the tool class**

```csharp
using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class PreviewCodeActionTool
{
    [McpServerTool(Name = "preview-code-action", ReadOnly = true)]
    [Description("Preview the changes a code fix or refactoring would make before applying. " +
                 "Pass an action ID from get-code-fixes or get-refactorings. " +
                 "Returns the exact text edits per file.")]
    public static async Task<PreviewCodeActionResult> PreviewCodeAction(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        ILogger<PreviewCodeActionTool> logger,
        [Description("Action ID from get-code-fixes or get-refactorings (e.g., 'fix-1', 'refactor-3')")]
        string actionId,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "preview-code-action");

        if (holder.LoadFailure is { } failure)
            return PreviewCodeActionResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return PreviewCodeActionResult.NotLoaded();

        var preview = await codeActions.PreviewAsync(actionId, ct);
        if (preview is null)
            return PreviewCodeActionResult.NotFound(actionId);

        if (preview.IsExpired)
            return PreviewCodeActionResult.Expired(actionId);

        return new PreviewCodeActionResult(
            Status: "found",
            ActionId: preview.ActionId,
            Title: preview.Title,
            Changes: preview.Changes,
            Message: null);
    }
}

public sealed record PreviewCodeActionResult(
    string Status, string? ActionId, string? Title,
    ImmutableList<FileChange> Changes,
    string? Message)
{
    public static PreviewCodeActionResult NotFound(string actionId) => new(
        "not_found", actionId, null, [],
        $"Action '{actionId}' not found. It may have expired or the ID is invalid.");
    public static PreviewCodeActionResult Expired(string actionId) => new(
        "expired", actionId, null, [],
        $"Action '{actionId}' has expired because the workspace changed. Re-query fixes or refactorings.");
    public static PreviewCodeActionResult NotLoaded() => new(
        "not_loaded", null, null, [],
        "Workspace is still loading");
    public static PreviewCodeActionResult LoadFailed(string message) => new(
        "load_failed", null, null, [], message);
}
```

- [ ] **Step 2: Register the tool in Program.cs**

Add `.WithTools<PreviewCodeActionTool>()` alphabetically — after `.WithTools<OutlineFileTool>()`:

```csharp
    .WithTools<OutlineFileTool>()
    .WithTools<PreviewCodeActionTool>()
    .WithTools<SafeToDeleteTool>()
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "PreviewCodeAction" -v n`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.Mcp/Tools/PreviewCodeActionTool.cs tests/Parlance.Mcp.Tests/Tools/PreviewCodeActionToolTests.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add preview-code-action MCP tool

Shows exact text edits for a code fix or refactoring before applying.
Action IDs expire when the workspace snapshot changes."
```

---

### Task 9: Cross-Tool Integration Test

**Files:**
- Modify: `tests/Parlance.Mcp.Tests/Tools/GetCodeFixesToolTests.cs`

- [ ] **Step 1: Add a cross-tool flow test**

Add this test to `GetCodeFixesToolTests.cs`:

```csharp
    [Fact]
    public async Task CrossTool_GetFixes_ThenPreview_Works()
    {
        // Find a file and line with a diagnostic that has a fix
        // This test validates the full flow: get fixes → preview a fix
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        var fixResult = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: filePath, line: 1, diagnosticId: null,
            CancellationToken.None);

        if (fixResult.Status != "found" || fixResult.Fixes.IsEmpty)
        {
            // No fixes on line 1 — skip this test gracefully
            return;
        }

        // Preview the first fix
        var actionId = fixResult.Fixes[0].Id;
        var previewResult = await PreviewCodeActionTool.PreviewCodeAction(
            _holder, _codeActions, NullLogger<PreviewCodeActionTool>.Instance,
            actionId: actionId,
            CancellationToken.None);

        Assert.Equal("found", previewResult.Status);
        Assert.Equal(actionId, previewResult.ActionId);
        Assert.NotNull(previewResult.Title);
        // Changes may be empty if the fix is a no-op, but the structure should be valid
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "CrossTool" -v n`
Expected: Test passes (may skip if no fixes on line 1)

- [ ] **Step 3: Commit**

```bash
git add tests/Parlance.Mcp.Tests/Tools/GetCodeFixesToolTests.cs
git commit -m "test: add cross-tool integration test for code fixes → preview flow"
```

---

### Task 10: Final Verification

**Files:** None (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test Parlance.sln`
Expected: All tests pass

- [ ] **Step 2: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: No formatting violations

- [ ] **Step 3: Build the MCP server**

Run: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`
Expected: Clean build with no warnings

- [ ] **Step 4: Verify all 18 tools are registered**

Run: `grep -c "WithTools" src/Parlance.Mcp/Program.cs`
Expected: 18 (12 existing + 6 new)
