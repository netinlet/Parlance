# PR #93 Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Address all blocking and non-blocking review feedback on PR #93 (Milestone 5 CLI pivot).

**Architecture:** Seven independent fixes. Tasks 1-2 are coupled (PARL discovery depends on build infra). Task 3 (FixCommand TFM) is independent but touches the same loader path. Tasks 4-7 are independent single-file changes.

**Tech Stack:** C#, MSBuild, Roslyn, xUnit

---

## File Map

| File | Action | Task |
|------|--------|------|
| `src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj` | Modify | 1 |
| `src/Parlance.Analyzers.Upstream/AnalyzerDllScanner.cs` | Modify | 2 |
| `tests/Parlance.Analyzers.Upstream.Tests/AnalyzerLoaderTests.cs` | Modify | 1, 2 |
| `src/Parlance.Cli/Commands/FixCommand.cs` | Modify | 3 |
| `src/Parlance.Analysis/AnalyzeOptions.cs` | Modify | 4 |
| `src/Parlance.Analysis/AnalysisService.cs` | Modify | 4 |
| `src/Parlance.Cli/Commands/AnalyzeCommand.cs` | Modify | 4 |
| `tests/Parlance.Cli.Tests/Formatting/TextFormatterTests.cs` | Modify | 4 |
| `src/Parlance.CSharp.Workspace/WorkspaceSessionHolder.cs` | Modify | 5 |
| `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs` | Modify | 6 |
| `src/Parlance.Cli/Commands/FixCommand.cs` | Modify | 7 |

---

### Task 1: Copy PARL analyzer DLL into analyzer-dlls at build time

**Context:** The old `AnalyzerLoader` hardcoded `typeof(PARL9003_UseDefaultLiteral).Assembly` to load PARL analyzers. This PR removed that (correctly — per policy, no hardcoding). But PARL9003 still exists as a netstandard2.0 DLL and should be discoverable through the unified scanner path by placing it in `analyzer-dlls/<tfm>/` alongside upstream DLLs.

**Files:**
- Modify: `src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj`
- Modify: `tests/Parlance.Analyzers.Upstream.Tests/AnalyzerLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

Add a test to `AnalyzerLoaderTests.cs` that asserts PARL9003 is present in loaded analyzers:

```csharp
[Fact]
public void LoadAll_Net10_IncludesParlAnalyzers()
{
    var analyzers = AnalyzerLoader.LoadAll("net10.0");
    var allIds = analyzers
        .SelectMany(a => a.SupportedDiagnostics)
        .Select(d => d.Id)
        .ToHashSet();

    Assert.Contains("PARL9003", allIds);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests/ --filter LoadAll_Net10_IncludesParlAnalyzers -v normal`
Expected: FAIL — PARL9003 not found in loaded analyzers

- [ ] **Step 3: Add ProjectReference and build target to copy PARL DLL**

In `Parlance.Analyzers.Upstream.csproj`, add a build-order-only ProjectReference and extend the `CopyAnalyzerDlls` target:

After the existing `</ItemGroup>` that closes the InternalsVisibleTo block (line 19), add:

```xml
<ItemGroup>
  <ProjectReference Include="..\Parlance.CSharp.Analyzers\Parlance.CSharp.Analyzers.csproj"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

In the `CopyAnalyzerDlls` target, add a new item group entry for the PARL DLL. Inside the existing `<ItemGroup>` within the target (after the `_BclShimDlls` item), add:

```xml
<_ParlAnalyzerDlls Include="$(MSBuildProjectDirectory)\..\Parlance.CSharp.Analyzers\bin\$(Configuration)\netstandard2.0\Parlance.CSharp.Analyzers.dll"
                   Condition="Exists('$(MSBuildProjectDirectory)\..\Parlance.CSharp.Analyzers\bin\$(Configuration)\netstandard2.0\Parlance.CSharp.Analyzers.dll')" />
```

Then add `@(_ParlAnalyzerDlls)` to the existing `<Copy>` task's `SourceFiles`:

```xml
<Copy SourceFiles="@(_NetAnalyzerDlls);@(_CodeStyleDlls);@(_RoslynatorDlls);@(_BclShimDlls);@(_ParlAnalyzerDlls)"
      DestinationFolder="$(MSBuildProjectDirectory)/analyzer-dlls/$(TargetFramework)"
      SkipUnchangedFiles="true" />
```

- [ ] **Step 4: Build and run test to verify it passes**

Run: `dotnet build src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj && dotnet build src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj && dotnet test tests/Parlance.Analyzers.Upstream.Tests/ --filter LoadAll_Net10_IncludesParlAnalyzers -v normal`
Expected: PASS

- [ ] **Step 5: Also verify net8.0**

Add test:

```csharp
[Fact]
public void LoadAll_Net8_IncludesParlAnalyzers()
{
    var analyzers = AnalyzerLoader.LoadAll("net8.0");
    var allIds = analyzers
        .SelectMany(a => a.SupportedDiagnostics)
        .Select(d => d.Id)
        .ToHashSet();

    Assert.Contains("PARL9003", allIds);
}
```

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests/ --filter LoadAll_Net8_IncludesParlAnalyzers -v normal`
Expected: PASS (PARL DLL is netstandard2.0, compatible with both TFMs)

- [ ] **Step 6: Update DoesNotIncludeParlFixProviders test name for clarity**

The existing test `LoadAll_DoesNotIncludeParlFixProviders` is still correct — PARL9003 has no CodeFixProvider. But rename for clarity since PARL analyzers ARE now loaded:

```csharp
[Fact]
public void LoadAll_ParlAnalyzersHaveNoFixProviders()
{
    var providers = FixProviderLoader.LoadAll("net10.0");
    var fixableIds = providers.SelectMany(p => p.FixableDiagnosticIds).ToHashSet();
    Assert.DoesNotContain(fixableIds, id => id.StartsWith("PARL"));
}
```

- [ ] **Step 7: Run all loader tests**

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests/ -v normal`
Expected: All pass

- [ ] **Step 8: Commit**

```
fix: include PARL analyzer DLL in scanner directories

The CopyAnalyzerDlls build target now copies the PARL analyzer DLL
into analyzer-dlls/<tfm>/ so the scanner discovers it naturally,
same as upstream analyzers — no hardcoding needed.
```

---

### Task 2: Add TFM fallback in AnalyzerDllScanner

**Context:** The CLI targets net10.0 only. When it builds, only the net10.0 `CopyAnalyzerDlls` target runs — `analyzer-dlls/net8.0/` won't exist unless a full solution build was done. If the CLI loads a solution with net8.0 projects, `AnalyzerLoader.LoadAll("net8.0")` returns empty and the project gets silently skipped. Fix: fall back to any available TFM directory when the requested one doesn't exist.

**Files:**
- Modify: `src/Parlance.Analyzers.Upstream/AnalyzerDllScanner.cs`
- Modify: `tests/Parlance.Analyzers.Upstream.Tests/AnalyzerLoaderTests.cs`

- [ ] **Step 1: Write failing test**

This test simulates the scenario by checking that loading for a TFM whose directory is missing still returns analyzers (falling back to the other TFM). Since we can't easily delete directories in a unit test without side effects, we test the behavior indirectly — both TFMs should return a non-empty set even when only one directory was populated.

Actually, both directories exist locally from a full solution build. The better test: verify that `ResolveAnalyzerDirectory` falls back. Since this is `internal`, test through the public API — verify that net8.0 returns at least the same core set as net10.0 (the fallback case returns the net10.0 set):

```csharp
[Fact]
public void LoadAll_BothTfms_ReturnNonEmpty()
{
    // Both TFMs should return analyzers, even if only one directory is populated.
    // The scanner falls back to an available directory when the requested one is missing.
    var net10 = AnalyzerLoader.LoadAll("net10.0");
    var net8 = AnalyzerLoader.LoadAll("net8.0");

    Assert.NotEmpty(net10);
    Assert.NotEmpty(net8);
}
```

This test already exists implicitly (`LoadAll_Net8_ReturnsAnalyzers` and `LoadAll_Net10_ReturnsAnalyzers`). The real change is in the scanner. Skip adding a new test — existing tests cover the behavior.

- [ ] **Step 2: Add fallback logic to ResolveAnalyzerDirectory**

In `AnalyzerDllScanner.cs`, modify `ResolveAnalyzerDirectory` to fall back when the requested TFM directory doesn't exist:

```csharp
private static string? ResolveAnalyzerDirectory(string targetFramework)
{
    var assemblyDir = Path.GetDirectoryName(typeof(AnalyzerDllScanner).Assembly.Location);

    if (assemblyDir is not null)
    {
        var localPath = Path.Combine(assemblyDir, "analyzer-dlls", targetFramework);
        if (Directory.Exists(localPath))
            return localPath;

        // Fallback: try other supported TFMs (e.g., net10.0 CLI analyzing net8.0 projects)
        var localBase = Path.Combine(assemblyDir, "analyzer-dlls");
        if (Directory.Exists(localBase))
        {
            var fallback = SupportedFrameworks
                .Where(f => f != targetFramework)
                .Select(f => Path.Combine(localBase, f))
                .FirstOrDefault(Directory.Exists);
            if (fallback is not null)
                return fallback;
        }
    }

    var srcDir = FindDirectoryAbove(assemblyDir, "src");
    if (srcDir is not null)
    {
        var devPath = Path.Combine(srcDir, "Parlance.Analyzers.Upstream", "analyzer-dlls", targetFramework);
        if (Directory.Exists(devPath))
            return devPath;

        // Fallback in dev scenario too
        var devBase = Path.Combine(srcDir, "Parlance.Analyzers.Upstream", "analyzer-dlls");
        if (Directory.Exists(devBase))
        {
            var fallback = SupportedFrameworks
                .Where(f => f != targetFramework)
                .Select(f => Path.Combine(devBase, f))
                .FirstOrDefault(Directory.Exists);
            if (fallback is not null)
                return fallback;
        }
    }

    return null;
}
```

- [ ] **Step 3: Run existing tests to verify no regressions**

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests/ -v normal`
Expected: All pass

- [ ] **Step 4: Commit**

```
fix: add TFM fallback in AnalyzerDllScanner

When the requested TFM directory doesn't exist (e.g., net10.0 CLI
analyzing net8.0 projects), the scanner now falls back to any
available TFM directory rather than returning empty.
```

---

### Task 3: Fix FixCommand TFM resolution — iterate per project

**Context:** `FixCommand` hardcodes `net10.0` for analyzer/fix-provider loading. `AnalysisService` already resolves TFM per project via `session.Projects`. The fix command should follow the same pattern.

**Files:**
- Modify: `src/Parlance.Cli/Commands/FixCommand.cs`

- [ ] **Step 1: Restructure FixCommand to resolve TFM per project**

Replace the hardcoded loading (lines 67-72) and restructure the fix loop to resolve TFM per project. The full `await using (session)` block becomes:

```csharp
await using (session)
{
    var originalSolution = session.CurrentSolution;
    var currentSolution = originalSolution;

    const int maxIterations = 50;
    for (var iteration = 0; iteration < maxIterations; iteration++)
    {
        var applied = false;

        foreach (var project in currentSolution.Projects)
        {
            // Resolve TFM per project, matching AnalysisService behavior
            var projectInfo = session.Projects.FirstOrDefault(p => p.Name == project.Name);
            var targetFramework = projectInfo?.ActiveTargetFramework ?? "net10.0";

            ImmutableArray<CodeFixProvider> fixProviders;
            ImmutableArray<DiagnosticAnalyzer> analyzers;
            try
            {
                fixProviders = FixProviderLoader.LoadAll(targetFramework);
                var fixableIds = fixProviders.SelectMany(fp => fp.FixableDiagnosticIds).ToHashSet();
                analyzers = AnalyzerLoader.LoadAll(targetFramework)
                    .Where(a => a.SupportedDiagnostics.Any(d => fixableIds.Contains(d.Id)))
                    .ToImmutableArray();
            }
            catch (ArgumentException)
            {
                fixProviders = FixProviderLoader.LoadAll("net10.0");
                var fixableIds = fixProviders.SelectMany(fp => fp.FixableDiagnosticIds).ToHashSet();
                analyzers = AnalyzerLoader.LoadAll("net10.0")
                    .Where(a => a.SupportedDiagnostics.Any(d => fixableIds.Contains(d.Id)))
                    .ToImmutableArray();
            }

            if (analyzers.IsEmpty) continue;

            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

            var fixableDiags = diagnostics
                .Where(d => d.Id != "AD0001")
                .Where(d => !suppress.Contains(d.Id))
                .ToList();

            foreach (var diagnostic in fixableDiags)
            {
                var fixProvider = fixProviders.FirstOrDefault(fp =>
                    fp.FixableDiagnosticIds.Contains(diagnostic.Id));
                if (fixProvider is null) continue;

                var tree = diagnostic.Location.SourceTree;
                if (tree is null) continue;

                var docId = currentSolution.GetDocumentIdsWithFilePath(tree.FilePath).FirstOrDefault();
                if (docId is null) continue;

                var document = currentSolution.GetDocument(docId);
                if (document is null) continue;

                var actions = new List<CodeAction>();
                var context = new CodeFixContext(document, diagnostic,
                    (action, _) => actions.Add(action), ct);
                await fixProvider.RegisterCodeFixesAsync(context);

                if (actions.Count == 0) continue;

                var operations = await actions[0].GetOperationsAsync(ct);
                foreach (var op in operations)
                {
                    if (op is ApplyChangesOperation applyOp)
                    {
                        currentSolution = applyOp.ChangedSolution;
                        applied = true;
                    }
                }

                if (applied) break;
            }

            if (applied) break;
        }

        if (!applied) break;
    }

    // Collect changed files (unchanged from current code)
    var fixedFiles = new List<(string FilePath, string NewContent)>();
    foreach (var project in currentSolution.Projects)
    {
        foreach (var document in project.Documents)
        {
            if (document.FilePath is null) continue;
            var origDocIds = originalSolution.GetDocumentIdsWithFilePath(document.FilePath);
            if (origDocIds.IsEmpty) continue;
            var origDoc = originalSolution.GetDocument(origDocIds[0]);
            if (origDoc is null) continue;

            var origText = (await origDoc.GetTextAsync(ct)).ToString();
            var newText = (await document.GetTextAsync(ct)).ToString();
            if (origText != newText)
                fixedFiles.Add((document.FilePath, newText));
        }
    }

    if (fixedFiles.Count == 0)
    {
        Console.WriteLine("No auto-fixes available.");
        return;
    }

    foreach (var (filePath, _) in fixedFiles)
        Console.WriteLine($"--- {filePath}");

    if (dryRun)
    {
        Console.WriteLine($"{fixedFiles.Count} file(s) would be modified. Remove --dry-run to apply.");
    }
    else
    {
        foreach (var (filePath, newContent) in fixedFiles)
            await File.WriteAllTextAsync(filePath, newContent, ct);
        Console.WriteLine($"Applied fixes to {fixedFiles.Count} file(s).");
    }
}
```

Also add the missing `using` for `Parlance.CSharp.Workspace` at the top if not already present (it is — line 9).

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/Parlance.Cli/Parlance.Cli.csproj`
Expected: Build succeeds

- [ ] **Step 3: Run CLI integration tests**

Run: `dotnet test tests/Parlance.Cli.Tests/ --filter Fix -v normal`
Expected: `Fix_DryRun_DoesNotModifyFiles` passes

- [ ] **Step 4: Commit**

```
fix: resolve TFM per project in fix command

FixCommand now resolves each project's ActiveTargetFramework and
loads the matching analyzer/fix-provider set, consistent with
AnalysisService behavior. Falls back to net10.0 for unsupported TFMs.
```

---

### Task 4: Push --suppress into AnalysisService so it affects scoring

**Context:** `--suppress` currently post-filters diagnostics in `AnalyzeCommand`, but the summary (totals, score) is computed before filtering. This makes output internally inconsistent. Fix: add `SuppressRuleIds` to `AnalyzeOptions`, filter in `AnalysisService` before scoring.

**Files:**
- Modify: `src/Parlance.Analysis/AnalyzeOptions.cs`
- Modify: `src/Parlance.Analysis/AnalysisService.cs`
- Modify: `src/Parlance.Cli/Commands/AnalyzeCommand.cs`

- [ ] **Step 1: Add SuppressRuleIds to AnalyzeOptions**

In `src/Parlance.Analysis/AnalyzeOptions.cs`:

```csharp
using System.Collections.Immutable;

namespace Parlance.Analysis;

public sealed record AnalyzeOptions(
    string? CurationSetName = null,
    int? MaxDiagnostics = null,
    ImmutableArray<string>? SuppressRuleIds = null);
```

- [ ] **Step 2: Apply suppress filter in AnalysisService before scoring**

In `src/Parlance.Analysis/AnalysisService.cs`, after the diagnostic collection loop (after line 132, the closing `}` of the `foreach (var (projectId, files)` loop), add suppress filtering before the curation step:

Replace lines 135-136:
```csharp
// Apply curation
var curated = CurationFilter.Apply(curationSet, allCurated.ToImmutable());
```

With:
```csharp
var collected = allCurated.ToImmutable();

// Apply --suppress filter before scoring so totals/score are consistent
if (options.SuppressRuleIds is { IsEmpty: false } suppressIds)
{
    var suppressSet = suppressIds.Value.ToHashSet(StringComparer.OrdinalIgnoreCase);
    collected = collected.Where(d => !suppressSet.Contains(d.RuleId)).ToImmutableList();
}

// Apply curation
var curated = CurationFilter.Apply(curationSet, collected);
```

- [ ] **Step 3: Pass suppress from AnalyzeCommand into AnalyzeOptions, remove post-filter**

In `src/Parlance.Cli/Commands/AnalyzeCommand.cs`, change the `AnalyzeFilesAsync` call (line 87) to pass suppress:

```csharp
var suppressArray = suppress.Length > 0
    ? suppress.ToImmutableArray()
    : (ImmutableArray<string>?)null;

result = await analysis.AnalyzeFilesAsync(
    allFiles,
    new AnalyzeOptions(curationSet, maxDiag, suppressArray),
    ct);
```

Add `using System.Collections.Immutable;` to the top if not already present.

Remove the post-filter block (lines 96-103):
```csharp
// Post-filter for --suppress (score already reflects all diagnostics)
if (suppress.Length > 0)
    result = result with
    {
        Diagnostics = result.Diagnostics
            .Where(d => !suppress.Contains(d.RuleId))
            .ToImmutableList()
    };
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/Parlance.Cli/Parlance.Cli.csproj`
Expected: Build succeeds

- [ ] **Step 5: Run formatter tests (they exercise FileAnalysisResult shape)**

Run: `dotnet test tests/Parlance.Cli.Tests/ --filter Formatting -v normal`
Expected: All pass

- [ ] **Step 6: Commit**

```
fix: push --suppress into AnalysisService before scoring

Suppressed rule IDs are now filtered before scoring so that summary
totals and idiomatic score are consistent with the listed diagnostics.
```

---

### Task 5: Add design intent comment to WorkspaceSessionHolder

**Context:** Copilot flagged that `SetSession`/`SetLoadFailure` are public and overwritable. This is a deliberate design decision — the holder is a simple DI-scoped container, set once by the CLI command or MCP server startup. Add a comment documenting the design intent.

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/WorkspaceSessionHolder.cs`

- [ ] **Step 1: Add design comment**

```csharp
namespace Parlance.CSharp.Workspace;

/// <summary>
/// DI-scoped container for the workspace session. Public setters are intentional —
/// the CLI command or MCP server sets the session once at startup. The holder's lifetime
/// is tied to the DI container, which disposes it (and the session) on shutdown.
/// </summary>
public sealed class WorkspaceSessionHolder : IAsyncDisposable
{
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```
docs: add design intent comment to WorkspaceSessionHolder
```

---

### Task 6: Quote args in CLI integration test fallback path

**Context:** The `dotnet run` fallback path in `RunCliAsync` joins args without quoting, so paths with spaces would break. The `dotnet exec` path handles this correctly. Fix: apply the same quoting logic to both paths.

**Files:**
- Modify: `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`

- [ ] **Step 1: Fix the fallback path quoting**

In `CliIntegrationTests.cs`, replace the `psi.Arguments = ...` block (lines 53-55) with:

```csharp
var quotedArgs = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

psi.Arguments = File.Exists(_cliDll)
    ? $"exec \"{_cliDll}\" {quotedArgs}"
    : $"run --project \"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Parlance.Cli", "Parlance.Cli.csproj"))}\" --no-build -- {quotedArgs}";
```

- [ ] **Step 2: Build and run integration tests**

Run: `dotnet test tests/Parlance.Cli.Tests/ --filter Integration -v normal`
Expected: All pass

- [ ] **Step 3: Commit**

```
fix: quote args in CLI test fallback path
```

---

### Task 7: Preserve file encoding in fix command

**Context:** `File.WriteAllTextAsync(filePath, newContent, ct)` uses UTF-8 without BOM, which can alter the encoding of files that were UTF-8-with-BOM or UTF-16. Fix: read the original file's encoding and use it when writing.

**Files:**
- Modify: `src/Parlance.Cli/Commands/FixCommand.cs`

- [ ] **Step 1: Preserve original encoding when collecting changed files**

Change the `fixedFiles` collection to include encoding. Replace the type and collection logic:

```csharp
var fixedFiles = new List<(string FilePath, string NewContent, Encoding Encoding)>();
foreach (var project in currentSolution.Projects)
{
    foreach (var document in project.Documents)
    {
        if (document.FilePath is null) continue;
        var origDocIds = originalSolution.GetDocumentIdsWithFilePath(document.FilePath);
        if (origDocIds.IsEmpty) continue;
        var origDoc = originalSolution.GetDocument(origDocIds[0]);
        if (origDoc is null) continue;

        var origSourceText = await origDoc.GetTextAsync(ct);
        var newSourceText = await document.GetTextAsync(ct);
        var origContent = origSourceText.ToString();
        var newContent = newSourceText.ToString();
        if (origContent != newContent)
        {
            var encoding = origSourceText.Encoding ?? Encoding.UTF8;
            fixedFiles.Add((document.FilePath, newContent, encoding));
        }
    }
}
```

Update the write loop:

```csharp
foreach (var (filePath, newContent, encoding) in fixedFiles)
    await File.WriteAllTextAsync(filePath, newContent, encoding, ct);
```

Update the display loop:

```csharp
foreach (var (filePath, _, _) in fixedFiles)
    Console.WriteLine($"--- {filePath}");
```

Ensure `using System.Text;` is at the top of the file.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Parlance.Cli/Parlance.Cli.csproj`
Expected: Build succeeds

- [ ] **Step 3: Run fix integration test**

Run: `dotnet test tests/Parlance.Cli.Tests/ --filter Fix -v normal`
Expected: Pass

- [ ] **Step 4: Commit**

```
fix: preserve file encoding when applying fixes

Use the original SourceText.Encoding when writing fixed files to
avoid dropping BOM or changing encoding from the original file.
```

---

## Verification

After all tasks:

- [ ] `dotnet build src/Parlance.Cli/Parlance.Cli.csproj` — CLI builds
- [ ] `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj` — MCP builds
- [ ] `dotnet test Parlance.sln` — all tests pass
- [ ] `dotnet format Parlance.sln --verify-no-changes` — formatting clean
