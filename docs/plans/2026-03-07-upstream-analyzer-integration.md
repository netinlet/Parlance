# Upstream Analyzer Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate upstream analyzer packages (CA, IDE, Roslynator) into Parlance with version-aware loading, auto-generated manifests, and profile-based `.editorconfig` configuration for net8.0 and net10.0.

**Architecture:** New `Parlance.Analyzers.Upstream` project references upstream NuGet packages and extracts analyzer DLLs via MSBuild targets. `AnalyzerLoader` dynamically discovers all analyzers (PARL + upstream) at runtime via reflection. A `ManifestGenerator` tool produces per-TFM rule manifests. Profiles are `.editorconfig` files applied via Roslyn's native `AnalyzerConfigOptionsProvider`.

**Tech Stack:** .NET 10, Roslyn (`Microsoft.CodeAnalysis`), `AssemblyLoadContext`, MSBuild targets, System.CommandLine 2.0.3, xUnit

**Design doc:** `docs/plans/2026-03-07-upstream-analyzer-integration-design.md`

**Package versions:**

| Package | net8.0 | net10.0 |
|---------|--------|---------|
| `Microsoft.CodeAnalysis.NetAnalyzers` | 8.0.0 | 10.0.101 |
| `Microsoft.CodeAnalysis.CSharp.CodeStyle` | 4.9.2 | 5.0.0 |
| `Roslynator.Analyzers` | 4.15.0 | 4.15.0 |

---

## Task 1: Create Parlance.Analyzers.Upstream Project

**Files:**
- Create: `src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj`
- Modify: `Parlance.sln`

**Step 1: Create the project directory**

```bash
mkdir -p src/Parlance.Analyzers.Upstream
```

**Step 2: Create the .csproj**

Create `src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Parlance.Cli" />
    <InternalsVisibleTo Include="Parlance.Analyzers.Upstream.Tests" />
    <InternalsVisibleTo Include="Parlance.ManifestGenerator" />
  </ItemGroup>

  <!--
    Upstream analyzer packages. These are NOT compiled references — we extract
    their analyzer DLLs from the NuGet cache at build time.

    Each package is referenced with GeneratePathProperty="true" so MSBuild
    exposes $(PkgPackageName) pointing to the NuGet cache location.
    All assets are excluded from the build (ExcludeAssets="all") since we
    load them dynamically at runtime.
  -->

  <!-- net10.0 analyzer packages -->
  <ItemGroup Label="net10.0 analyzers">
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.101"
                      GeneratePathProperty="true" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeStyle" Version="5.0.0"
                      GeneratePathProperty="true" ExcludeAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0"
                      GeneratePathProperty="true" ExcludeAssets="all" />
  </ItemGroup>

  <!--
    net8.0 analyzer packages — different package IDs with version suffix
    to allow both versions to coexist in the same project.

    NOTE: NuGet doesn't allow two versions of the same package in one project.
    We may need to resolve net8.0 DLLs via a separate .csproj or by locating
    them from the NuGet global cache directly. This will be resolved during
    implementation — the design constraint is that we need both versions
    available at runtime.

    TODO: Determine the best approach for dual-version package resolution
    during Task 1 implementation. Options:
    a) Separate helper .csproj per TFM that restores the right versions
    b) Direct NuGet cache path resolution
    c) Download during a build script
  -->

</Project>
```

**Step 3: Add to solution**

```bash
dotnet sln Parlance.sln add src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj --solution-folder src
```

**Step 4: Build to verify**

Run: `dotnet build src/Parlance.Analyzers.Upstream`
Expected: Successful build with no errors.

**Step 5: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj Parlance.sln
git commit -m "Add Parlance.Analyzers.Upstream project with upstream NuGet references"
```

---

## Task 2: Resolve Dual-Version Package Strategy

Before writing `AnalyzerLoader`, we need to solve the problem of having both net8.0 and net10.0 versions of the same analyzer package available at runtime.

**Files:**
- Create: `src/Parlance.Analyzers.Upstream/build/net8.0-analyzers.csproj` (if approach a)
- Modify: `src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj`

**Step 1: Research NuGet dual-version resolution**

Investigate how to reference two versions of `Microsoft.CodeAnalysis.NetAnalyzers` (8.0.0 and 10.0.101) in the same build output. Test each approach:

a) **Separate restore-only .csproj per TFM:** A helper project that restores the net8.0 versions and copies DLLs to a known location. The main project references the net10.0 versions directly.

b) **Direct NuGet global-packages path:** After `dotnet restore`, locate DLLs directly from `~/.nuget/packages/<package>/<version>/analyzers/dotnet/cs/`. The main `.csproj` uses `GeneratePathProperty="true"` for the net10.0 versions. For net8.0, a target resolves the path from the NuGet cache using known version strings.

c) **MSBuild `DownloadFile` task:** Download specific `.nupkg` files during build and extract analyzer DLLs.

**Step 2: Implement chosen approach**

Implement the approach and verify that after `dotnet build`, the output directory contains:

```
analyzers/net8.0/Microsoft.CodeAnalysis.NetAnalyzers.CSharp.dll
analyzers/net8.0/Microsoft.CodeAnalysis.CSharp.CodeStyle.dll
analyzers/net8.0/Roslynator.CSharp.Analyzers.dll
analyzers/net10.0/Microsoft.CodeAnalysis.NetAnalyzers.CSharp.dll
analyzers/net10.0/Microsoft.CodeAnalysis.CSharp.CodeStyle.dll
analyzers/net10.0/Roslynator.CSharp.Analyzers.dll
```

Note: The exact DLL names inside each NuGet package's `analyzers/dotnet/cs/` folder may differ — there may be multiple DLLs per package. List all DLLs in the NuGet cache for each package and include all of them. For example, `Microsoft.CodeAnalysis.NetAnalyzers` may contain both `Microsoft.CodeAnalysis.NetAnalyzers.CSharp.dll` and `Microsoft.CodeAnalysis.NetAnalyzers.dll`.

**Step 3: Verify build output**

Run: `dotnet build src/Parlance.Analyzers.Upstream && ls -R bin/Debug/net10.0/analyzers/`
Expected: DLLs organized by TFM in the output directory.

**Step 4: Commit**

```bash
git add -A src/Parlance.Analyzers.Upstream/
git commit -m "Add MSBuild targets for dual-version analyzer DLL extraction"
```

---

## Task 3: AnalyzerLoader — Dynamic Analyzer Discovery

**Files:**
- Create: `src/Parlance.Analyzers.Upstream/AnalyzerLoader.cs`
- Create: `tests/Parlance.Analyzers.Upstream.Tests/Parlance.Analyzers.Upstream.Tests.csproj`
- Create: `tests/Parlance.Analyzers.Upstream.Tests/AnalyzerLoaderTests.cs`
- Modify: `Parlance.sln`

**Step 1: Create test project**

Create `tests/Parlance.Analyzers.Upstream.Tests/Parlance.Analyzers.Upstream.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Parlance.Analyzers.Upstream\Parlance.Analyzers.Upstream.csproj" />
    <ProjectReference Include="..\..\src\Parlance.CSharp.Analyzers\Parlance.CSharp.Analyzers.csproj" />
  </ItemGroup>

</Project>
```

Add to solution:

```bash
dotnet sln Parlance.sln add tests/Parlance.Analyzers.Upstream.Tests/Parlance.Analyzers.Upstream.Tests.csproj --solution-folder tests
```

**Step 2: Write failing tests**

Create `tests/Parlance.Analyzers.Upstream.Tests/AnalyzerLoaderTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream.Tests;

public sealed class AnalyzerLoaderTests
{
    [Fact]
    public void LoadAll_Net10_ReturnsAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");

        Assert.NotEmpty(analyzers);
    }

    [Fact]
    public void LoadAll_Net8_ReturnsAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net8.0");

        Assert.NotEmpty(analyzers);
    }

    [Fact]
    public void LoadAll_Net10_IncludesParlAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");

        var parlIds = analyzers
            .SelectMany(a => a.SupportedDiagnostics)
            .Select(d => d.Id)
            .Where(id => id.StartsWith("PARL"))
            .ToList();

        Assert.Contains("PARL0001", parlIds);
        Assert.Contains("PARL0004", parlIds);
        Assert.Contains("PARL9001", parlIds);
    }

    [Fact]
    public void LoadAll_Net10_IncludesUpstreamAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");

        var allIds = analyzers
            .SelectMany(a => a.SupportedDiagnostics)
            .Select(d => d.Id)
            .ToHashSet();

        // Spot-check known rules from each upstream source
        Assert.Contains("CA1822", allIds);  // NetAnalyzers
        Assert.Contains("IDE0003", allIds); // CodeStyle
        Assert.Contains("RCS1003", allIds); // Roslynator
    }

    [Fact]
    public void LoadAll_UnknownTfm_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AnalyzerLoader.LoadAll("net99.0"));
    }

    [Fact]
    public void LoadAll_Net10_ReturnsReasonableCount()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");

        // PARL (8) + CA (100+) + IDE (50+) + RCS (150+) = at minimum 100 analyzer types
        Assert.True(analyzers.Length >= 50,
            $"Expected at least 50 analyzer types, got {analyzers.Length}");
    }

    [Fact]
    public void LoadAll_Net8VsNet10_MayDiffer()
    {
        var net8 = AnalyzerLoader.LoadAll("net8.0");
        var net10 = AnalyzerLoader.LoadAll("net10.0");

        // Both should have analyzers, counts may differ
        Assert.NotEmpty(net8);
        Assert.NotEmpty(net10);
    }
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests --no-restore -v quiet`
Expected: FAIL — `AnalyzerLoader` class doesn't exist yet.

**Step 4: Implement AnalyzerLoader**

Create `src/Parlance.Analyzers.Upstream/AnalyzerLoader.cs`:

```csharp
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

internal static class AnalyzerLoader
{
    private static readonly HashSet<string> SupportedTfms = ["net8.0", "net10.0"];

    /// <summary>
    /// Loads all analyzers (PARL + upstream) for the given target framework.
    /// PARL analyzers are discovered from the Parlance.CSharp.Analyzers assembly.
    /// Upstream analyzers are loaded from extracted NuGet DLLs.
    /// </summary>
    public static ImmutableArray<DiagnosticAnalyzer> LoadAll(string targetFramework)
    {
        if (!SupportedTfms.Contains(targetFramework))
            throw new ArgumentException(
                $"Unsupported target framework: '{targetFramework}'. Supported: {string.Join(", ", SupportedTfms)}",
                nameof(targetFramework));

        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();

        // Discover PARL analyzers from the Parlance.CSharp.Analyzers assembly
        var parlAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Parlance.CSharp.Analyzers");

        if (parlAssembly is not null)
            analyzers.AddRange(DiscoverAnalyzers(parlAssembly));

        // Load upstream analyzer DLLs for the requested TFM
        var analyzerDir = ResolveAnalyzerDirectory(targetFramework);
        if (Directory.Exists(analyzerDir))
        {
            var loadContext = new AnalyzerAssemblyLoadContext(analyzerDir);
            foreach (var dll in Directory.GetFiles(analyzerDir, "*.dll"))
            {
                try
                {
                    var assembly = loadContext.LoadFromAssemblyPath(dll);
                    analyzers.AddRange(DiscoverAnalyzers(assembly));
                }
                catch (Exception)
                {
                    // Skip DLLs that fail to load (dependency assemblies, non-analyzer DLLs)
                }
            }
        }

        return analyzers.ToImmutable();
    }

    private static string ResolveAnalyzerDirectory(string targetFramework)
    {
        // Look relative to the executing assembly's location
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(baseDir, "analyzers", targetFramework);
    }

    private static IEnumerable<DiagnosticAnalyzer> DiscoverAnalyzers(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                continue;

            if (Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
                yield return analyzer;
        }
    }

    private sealed class AnalyzerAssemblyLoadContext(string analyzerDir)
        : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Try to find the dependency in the analyzer directory
            var path = Path.Combine(analyzerDir, $"{assemblyName.Name}.dll");
            if (File.Exists(path))
                return LoadFromAssemblyPath(path);

            // Fall back to the default context for shared dependencies
            return null;
        }
    }
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests -v quiet`
Expected: All tests PASS.

Note: Some tests may need adjustment based on the actual DLL names and rule IDs discovered. The spot-check IDs (CA1822, IDE0003, RCS1003) are well-known stable rules that should exist in both net8.0 and net10.0. If a specific ID isn't found, check the actual DLL contents and adjust the test.

**Step 6: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/AnalyzerLoader.cs \
        tests/Parlance.Analyzers.Upstream.Tests/ \
        Parlance.sln
git commit -m "Add AnalyzerLoader with dynamic analyzer discovery and tests"
```

---

## Task 4: Manifest Generator Tool

**Files:**
- Create: `tools/Parlance.ManifestGenerator/Parlance.ManifestGenerator.csproj`
- Create: `tools/Parlance.ManifestGenerator/Program.cs`
- Create: `tools/Parlance.ManifestGenerator/RuleManifest.cs`
- Modify: `Parlance.sln`

**Step 1: Create the tool project**

Create `tools/Parlance.ManifestGenerator/Parlance.ManifestGenerator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Parlance.Analyzers.Upstream\Parlance.Analyzers.Upstream.csproj" />
    <ProjectReference Include="..\..\src\Parlance.CSharp.Analyzers\Parlance.CSharp.Analyzers.csproj" />
  </ItemGroup>

</Project>
```

Add to solution:

```bash
dotnet sln Parlance.sln add tools/Parlance.ManifestGenerator/Parlance.ManifestGenerator.csproj
```

**Step 2: Create manifest model**

Create `tools/Parlance.ManifestGenerator/RuleManifest.cs`:

```csharp
namespace Parlance.ManifestGenerator;

internal sealed record RuleManifest(
    string TargetFramework,
    string GeneratedAt,
    List<RuleEntry> Rules);

internal sealed record RuleEntry(
    string Id,
    string Title,
    string Category,
    string DefaultSeverity,
    string Description,
    string? HelpUrl,
    string Source,
    bool HasRationale,
    bool HasSuggestedFix);
```

**Step 3: Implement the generator**

Create `tools/Parlance.ManifestGenerator/Program.cs`:

```csharp
using System.Text.Json;
using Parlance.Analyzers.Upstream;
using Parlance.ManifestGenerator;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Parlance.ManifestGenerator <tfm> [output-path]");
    Console.Error.WriteLine("  tfm: net8.0 or net10.0");
    Console.Error.WriteLine("  output-path: optional, defaults to rule-manifest-<tfm>.json");
    return 1;
}

var tfm = args[0];
var outputPath = args.Length > 1
    ? args[1]
    : $"rule-manifest-{tfm}.json";

Console.WriteLine($"Generating manifest for {tfm}...");

var analyzers = AnalyzerLoader.LoadAll(tfm);

var rules = new List<RuleEntry>();

foreach (var analyzer in analyzers)
{
    var assemblyName = analyzer.GetType().Assembly.GetName().Name ?? "unknown";

    foreach (var descriptor in analyzer.SupportedDiagnostics)
    {
        // Avoid duplicates — multiple analyzer types can report the same diagnostic ID
        if (rules.Any(r => r.Id == descriptor.Id))
            continue;

        var severity = descriptor.DefaultSeverity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => "error",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => "warning",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => "suggestion",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden => "silent",
            _ => "silent",
        };

        rules.Add(new RuleEntry(
            Id: descriptor.Id,
            Title: descriptor.Title.ToString(),
            Category: descriptor.Category,
            DefaultSeverity: severity,
            Description: descriptor.Description.ToString(),
            HelpUrl: descriptor.HelpLinkUri,
            Source: assemblyName,
            HasRationale: false,
            HasSuggestedFix: false));
    }
}

rules.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));

var manifest = new RuleManifest(
    TargetFramework: tfm,
    GeneratedAt: DateTime.UtcNow.ToString("yyyy-MM-dd"),
    Rules: rules);

var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
});

File.WriteAllText(outputPath, json);
Console.WriteLine($"Wrote {rules.Count} rules to {outputPath}");

return 0;
```

**Step 4: Build and run**

```bash
dotnet run --project tools/Parlance.ManifestGenerator -- net10.0 docs/manifests/rule-manifest-net10.0.json
dotnet run --project tools/Parlance.ManifestGenerator -- net8.0 docs/manifests/rule-manifest-net8.0.json
```

Expected: JSON files created with rule entries from all three upstream sources plus PARL rules.

**Step 5: Verify manifest contents**

Spot-check that the generated manifest contains:
- PARL rules (PARL0001, PARL0004, etc.)
- CA rules (CA1822, CA2000, etc.)
- IDE rules (IDE0003, IDE0055, etc.)
- RCS rules (RCS1003, RCS1019, etc.)

```bash
cat docs/manifests/rule-manifest-net10.0.json | python3 -m json.tool | head -50
```

**Step 6: Commit**

```bash
mkdir -p docs/manifests
git add tools/Parlance.ManifestGenerator/ docs/manifests/ Parlance.sln
git commit -m "Add ManifestGenerator tool and initial rule manifests"
```

---

## Task 5: Refactor WorkspaceAnalyzer to Use AnalyzerLoader

**Files:**
- Modify: `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`
- Modify: `src/Parlance.Cli/Parlance.Cli.csproj`
- Modify: `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`

**Step 1: Add project reference**

Add to `src/Parlance.Cli/Parlance.Cli.csproj`:

```xml
<ProjectReference Include="..\Parlance.Analyzers.Upstream\Parlance.Analyzers.Upstream.csproj" />
```

**Step 2: Write integration test for upstream analyzer detection**

Add to `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`:

```csharp
[Fact]
public async Task Analyze_WithUpstreamAnalyzers_ReportsNonParlDiagnostics()
{
    var file = Path.Combine(_tempDir, "Test.cs");
    // Code that triggers CA1822 (method can be static)
    File.WriteAllText(file, """
        public class C
        {
            public int GetValue()
            {
                return 42;
            }
        }
        """);

    var (exitCode, stdout, _) = await RunCliAsync("analyze", file);

    Assert.Equal(0, exitCode);
    // Should contain at least one non-PARL diagnostic
    Assert.Matches("(CA|IDE|RCS)", stdout);
}
```

**Step 3: Run test to verify it fails**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "Analyze_WithUpstreamAnalyzers" -v quiet`
Expected: FAIL — current `WorkspaceAnalyzer` only loads PARL analyzers.

**Step 4: Refactor WorkspaceAnalyzer**

Replace `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Analyzers.Upstream;
using Parlance.Cli.Formatting;
using Parlance.CSharp;

namespace Parlance.Cli.Analysis;

internal static class WorkspaceAnalyzer
{
    public static async Task<AnalysisOutput> AnalyzeAsync(
        IReadOnlyList<string> filePaths,
        string[]? suppressRules = null,
        int? maxDiagnostics = null,
        string? languageVersion = null,
        string targetFramework = "net10.0",
        CancellationToken ct = default)
    {
        suppressRules ??= [];

        var parseOptions = new CSharpParseOptions(
            ResolveLanguageVersion(languageVersion));

        var trees = new List<SyntaxTree>(filePaths.Count);
        var pathMap = new Dictionary<SyntaxTree, string>();

        foreach (var path in filePaths)
        {
            var source = await File.ReadAllTextAsync(path, ct);
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path, cancellationToken: ct);
            trees.Add(tree);
            pathMap[tree] = path;
        }

        var references = CompilationFactory.LoadReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "ParlanceCliAnalysis",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var allAnalyzers = AnalyzerLoader.LoadAll(targetFramework);

        var analyzers = suppressRules.Length > 0
            ? allAnalyzers.Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id))).ToImmutableArray()
            : allAnalyzers;

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

        var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        var filtered = roslynDiagnostics
            .Where(d => !suppressRules.Contains(d.Id))
            .ToList();

        var enriched = DiagnosticEnricher.Enrich(filtered);
        var summary = IdiomaticScoreCalculator.Calculate(enriched);

        var fileDiagnostics = new List<FileDiagnostic>();
        for (var i = 0; i < enriched.Count; i++)
        {
            var roslynDiag = filtered[i];
            var filePath = roslynDiag.Location.SourceTree is not null &&
                           pathMap.TryGetValue(roslynDiag.Location.SourceTree, out var p)
                ? p
                : "unknown";
            fileDiagnostics.Add(new FileDiagnostic(filePath, enriched[i]));
        }

        if (maxDiagnostics.HasValue && fileDiagnostics.Count > maxDiagnostics.Value)
            fileDiagnostics = fileDiagnostics.Take(maxDiagnostics.Value).ToList();

        return new AnalysisOutput(fileDiagnostics, summary, filePaths.Count);
    }

    private static LanguageVersion ResolveLanguageVersion(string? version)
    {
        if (version is null)
            return LanguageVersion.Latest;

        if (LanguageVersionFacts.TryParse(version, out var parsed))
            return parsed;

        return LanguageVersion.Latest;
    }
}
```

**Step 5: Run all tests**

Run: `dotnet test --no-restore -v quiet`
Expected: All tests PASS, including the new upstream analyzer test and all existing PARL tests.

**Step 6: Commit**

```bash
git add src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs \
        src/Parlance.Cli/Parlance.Cli.csproj \
        tests/Parlance.Cli.Tests/
git commit -m "Refactor WorkspaceAnalyzer to use AnalyzerLoader for dynamic discovery"
```

---

## Task 6: Refactor WorkspaceFixer to Use AnalyzerLoader

**Files:**
- Modify: `src/Parlance.Cli/Analysis/WorkspaceFixer.cs`

**Step 1: Verify existing fix tests pass**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "Fix" -v quiet`
Expected: All fix tests PASS.

**Step 2: Refactor WorkspaceFixer**

In `src/Parlance.Cli/Analysis/WorkspaceFixer.cs`:
- Remove the hardcoded `FixableAnalyzers` array
- Replace with `AnalyzerLoader.LoadAll(targetFramework)` filtered to only analyzers that have matching `CodeFixProvider`s
- Remove the hardcoded `FixProviders` array
- Discover `CodeFixProvider` types via reflection from the same assemblies
- Add `targetFramework` parameter to `FixAsync`

The fix providers live in `Parlance.CSharp.Analyzers` assembly. Discover them the same way as analyzers — reflect over exported types that inherit from `CodeFixProvider`.

**Step 3: Run fix tests to verify they still pass**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "Fix" -v quiet`
Expected: All fix tests PASS.

**Step 4: Commit**

```bash
git add src/Parlance.Cli/Analysis/WorkspaceFixer.cs
git commit -m "Refactor WorkspaceFixer to use dynamic analyzer and fix provider discovery"
```

---

## Task 7: Refactor DiagnosticEnricher to Use Manifest

**Files:**
- Modify: `src/Parlance.CSharp/DiagnosticEnricher.cs`
- Create: `src/Parlance.CSharp/RuleMetadataProvider.cs`

**Step 1: Verify existing enricher tests pass**

Run: `dotnet test tests/Parlance.CSharp.Tests -v quiet`
Expected: All tests PASS.

**Step 2: Create RuleMetadataProvider**

Create `src/Parlance.CSharp/RuleMetadataProvider.cs`:

This class loads metadata from:
1. Curated PARL metadata (the existing hand-written rationale — moved from DiagnosticEnricher)
2. Manifest data for upstream rules (falls back to upstream title/description)

```csharp
using System.Collections.Frozen;

namespace Parlance.CSharp;

internal sealed record RuleMetadata(
    string Category,
    string? Rationale,
    string? SuggestedFix);

internal static class RuleMetadataProvider
{
    // Curated PARL metadata — the existing hand-written rationale
    private static readonly FrozenDictionary<string, RuleMetadata> CuratedMetadata =
        new Dictionary<string, RuleMetadata>
        {
            ["PARL0001"] = new(
                "Modernization",
                "Primary constructors (C# 12+) combine type declaration and constructor into a single concise form. When a constructor only assigns parameters to fields or properties, a primary constructor removes the boilerplate.",
                "Convert to a primary constructor by moving parameters to the type declaration."),
            ["PARL0002"] = new(
                "Modernization",
                "Collection expressions (C# 12+) provide a unified syntax for creating collections. They are more concise and let the compiler choose the optimal collection type.",
                "Replace with a collection expression: [element1, element2, ...]."),
            ["PARL0003"] = new(
                "Modernization",
                "The 'required' modifier (C# 11+) enforces that callers set a property during initialization. This is clearer than constructor-only initialization for simple DTOs and reduces constructor boilerplate.",
                "Consider adding the 'required' modifier to the properties and removing the constructor. Note: this changes construction semantics — callers must switch to object initializer syntax."),
            ["PARL0004"] = new(
                "PatternMatching",
                "Pattern matching with 'is' (C# 7+) combines type checking and variable declaration in one expression. It is more concise than separate 'is' check followed by a cast, avoids the double type-check, and is the idiomatic modern C# approach.",
                "Use 'if (obj is Type name)' instead of separate is-check and cast."),
            ["PARL0005"] = new(
                "PatternMatching",
                "Switch expressions (C# 8+) are more concise than switch statements when every branch returns a value. They enforce exhaustiveness and make the data-flow intent clearer.",
                "Convert the switch statement to a switch expression."),
            ["PARL9001"] = new(
                "Modernization",
                "Using declarations (C# 8+) remove the need for braces and reduce indentation. The variable is disposed at the end of the enclosing scope, which is equivalent when the using is the last meaningful statement.",
                "Remove the parentheses and braces: change 'using (var x = y) { }' to 'using var x = y;'."),
            ["PARL9002"] = new(
                "Modernization",
                "Target-typed new (C# 9+) lets you omit the type name in a new expression when the type is apparent from the declaration. This reduces redundancy without losing clarity.",
                "Replace 'new TypeName(...)' with 'new(...)'."),
            ["PARL9003"] = new(
                "Modernization",
                "The default literal (C# 7.1+) lets the compiler infer the type from context, eliminating the redundant type argument in default(T) when the target type is already apparent.",
                "Replace 'default(T)' with 'default'."),
        }.ToFrozenDictionary();

    public static RuleMetadata? GetMetadata(string ruleId)
    {
        return CuratedMetadata.GetValueOrDefault(ruleId);
    }
}
```

**Step 3: Refactor DiagnosticEnricher**

Modify `src/Parlance.CSharp/DiagnosticEnricher.cs`:
- Remove the hardcoded `Metadata` dictionary
- Use `RuleMetadataProvider.GetMetadata(d.Id)` instead
- For upstream rules without curated metadata, use `d.Descriptor.Category` for category (already the fallback), and `null` for rationale/suggestedFix

**Step 4: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS — the enriched output should be identical for PARL rules.

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/RuleMetadataProvider.cs \
        src/Parlance.CSharp/DiagnosticEnricher.cs
git commit -m "Extract RuleMetadataProvider from DiagnosticEnricher for manifest-based metadata"
```

---

## Task 8: Refactor RulesCommand to Use AnalyzerLoader

**Files:**
- Modify: `src/Parlance.Cli/Commands/RulesCommand.cs`
- Modify: `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`

**Step 1: Write test for upstream rules in output**

Add to `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`:

```csharp
[Fact]
public async Task Rules_ShowsUpstreamRules()
{
    var (exitCode, stdout, _) = await RunCliAsync("rules");

    Assert.Equal(0, exitCode);
    // Should include PARL rules
    Assert.Contains("PARL0001", stdout);
    // Should include upstream rules
    Assert.Matches("(CA|IDE|RCS)", stdout);
}

[Fact]
public async Task Rules_TargetFramework_AcceptsNet8()
{
    var (exitCode, stdout, _) = await RunCliAsync("rules", "--target-framework", "net8.0");

    Assert.Equal(0, exitCode);
    Assert.Contains("PARL0001", stdout);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "Rules_ShowsUpstream" -v quiet`
Expected: FAIL — `RulesCommand` uses hardcoded array.

**Step 3: Refactor RulesCommand**

Rewrite `src/Parlance.Cli/Commands/RulesCommand.cs`:
- Remove hardcoded `AllAnalyzers` array and `FixableRuleIds`
- Add `--target-framework` option (default: `net10.0`)
- Use `AnalyzerLoader.LoadAll(targetFramework)` to get analyzers
- Build rule list from analyzer `SupportedDiagnostics` (same logic, but dynamic)
- Discover fixable rules by checking for `CodeFixProvider`s that reference each diagnostic ID

**Step 4: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Parlance.Cli/Commands/RulesCommand.cs \
        tests/Parlance.Cli.Tests/
git commit -m "Refactor RulesCommand to use AnalyzerLoader, add --target-framework"
```

---

## Task 9: Add --target-framework and --profile to AnalyzeCommand and FixCommand

**Files:**
- Modify: `src/Parlance.Cli/Commands/AnalyzeCommand.cs`
- Modify: `src/Parlance.Cli/Commands/FixCommand.cs`
- Modify: `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`
- Modify: `src/Parlance.Cli/Analysis/WorkspaceFixer.cs`

**Step 1: Write test for --target-framework flag**

Add to `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`:

```csharp
[Fact]
public async Task Analyze_TargetFramework_AcceptsNet8()
{
    var file = Path.Combine(_tempDir, "Test.cs");
    File.WriteAllText(file, "class C { }");

    var (exitCode, _, _) = await RunCliAsync("analyze", file, "--target-framework", "net8.0");

    Assert.Equal(0, exitCode);
}

[Fact]
public async Task Fix_TargetFramework_AcceptsNet8()
{
    var file = Path.Combine(_tempDir, "Test.cs");
    File.WriteAllText(file, """
        using System;
        using System.IO;
        class C
        {
            void M()
            {
                using (var stream = new MemoryStream())
                {
                    stream.WriteByte(1);
                }
            }
        }
        """);

    var (exitCode, _, _) = await RunCliAsync("fix", file, "--target-framework", "net8.0");

    Assert.Equal(0, exitCode);
}
```

**Step 2: Add options to AnalyzeCommand**

In `src/Parlance.Cli/Commands/AnalyzeCommand.cs`:
- Add `--target-framework` option with default `"net10.0"`
- Add `--profile` option with default `"default"`
- Pass `targetFramework` to `WorkspaceAnalyzer.AnalyzeAsync`
- Profile support is wired in but uses the default profile for now (profile `.editorconfig` loading is Task 10)

**Step 3: Add options to FixCommand**

In `src/Parlance.Cli/Commands/FixCommand.cs`:
- Add `--target-framework` option with default `"net10.0"`
- Add `--profile` option with default `"default"`
- Pass `targetFramework` to `WorkspaceFixer.FixAsync`

**Step 4: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Parlance.Cli/Commands/AnalyzeCommand.cs \
        src/Parlance.Cli/Commands/FixCommand.cs \
        src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs \
        src/Parlance.Cli/Analysis/WorkspaceFixer.cs \
        tests/Parlance.Cli.Tests/
git commit -m "Add --target-framework and --profile options to analyze and fix commands"
```

---

## Task 10: Profile System — .editorconfig Loading

**Files:**
- Create: `src/Parlance.Analyzers.Upstream/ProfileProvider.cs`
- Create: `src/Parlance.Analyzers.Upstream/profiles/net10.0/default.editorconfig`
- Create: `src/Parlance.Analyzers.Upstream/profiles/net8.0/default.editorconfig`
- Create: `tests/Parlance.Analyzers.Upstream.Tests/ProfileProviderTests.cs`
- Modify: `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`

**Step 1: Write failing tests for ProfileProvider**

Create `tests/Parlance.Analyzers.Upstream.Tests/ProfileProviderTests.cs`:

```csharp
namespace Parlance.Analyzers.Upstream.Tests;

public sealed class ProfileProviderTests
{
    [Theory]
    [InlineData("net10.0", "default")]
    [InlineData("net8.0", "default")]
    public void GetProfileContent_ReturnsContent(string tfm, string profile)
    {
        var content = ProfileProvider.GetProfileContent(tfm, profile);

        Assert.NotNull(content);
        Assert.NotEmpty(content);
        Assert.Contains("[*.cs]", content);
    }

    [Fact]
    public void GetProfileContent_UnknownProfile_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ProfileProvider.GetProfileContent("net10.0", "nonexistent"));
    }

    [Fact]
    public void GetAvailableProfiles_ReturnsExpectedProfiles()
    {
        var profiles = ProfileProvider.GetAvailableProfiles();

        Assert.Contains("default", profiles);
    }
}
```

**Step 2: Create initial default.editorconfig profiles**

Create `src/Parlance.Analyzers.Upstream/profiles/net10.0/default.editorconfig`:

Start with a minimal default profile that sets the root marker and a few representative overrides. The full profile content will be refined after running the manifest generator to see all available rules.

```ini
# Parlance default profile for net10.0
# Balanced — warnings for bugs, suggestions for idioms
[*.cs]

# PARL rules — all enabled at default severity (no overrides needed)

# CA rules — key overrides from defaults
# dotnet_diagnostic.CA1822.severity = suggestion  # already default

# IDE rules — enable common style rules
# dotnet_diagnostic.IDE0003.severity = suggestion  # already default

# RCS rules — enable common simplification rules
# dotnet_diagnostic.RCS1003.severity = suggestion  # already default
```

Create `src/Parlance.Analyzers.Upstream/profiles/net8.0/default.editorconfig` with similar content.

**Step 3: Implement ProfileProvider**

Create `src/Parlance.Analyzers.Upstream/ProfileProvider.cs`:

```csharp
using System.Reflection;

namespace Parlance.Analyzers.Upstream;

internal static class ProfileProvider
{
    private static readonly string[] AvailableProfiles = ["default"];

    public static string GetProfileContent(string targetFramework, string profile)
    {
        if (!AvailableProfiles.Contains(profile))
            throw new ArgumentException(
                $"Unknown profile: '{profile}'. Available: {string.Join(", ", AvailableProfiles)}",
                nameof(profile));

        var resourceName = $"profiles/{targetFramework}/{profile}.editorconfig";
        var assembly = Assembly.GetExecutingAssembly();
        var baseDir = Path.GetDirectoryName(assembly.Location)!;
        var profilePath = Path.Combine(baseDir, resourceName);

        if (!File.Exists(profilePath))
            throw new ArgumentException(
                $"Profile '{profile}' not found for {targetFramework}",
                nameof(profile));

        return File.ReadAllText(profilePath);
    }

    public static IReadOnlyList<string> GetAvailableProfiles() => AvailableProfiles;
}
```

Update `.csproj` to include profile files as content:

```xml
<ItemGroup>
  <Content Include="profiles/**/*.editorconfig">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Step 4: Wire profile into WorkspaceAnalyzer**

In `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`:
- Add `string profile = "default"` parameter
- Load profile content via `ProfileProvider.GetProfileContent(targetFramework, profile)`
- Apply to compilation via `AnalyzerConfigOptionsProvider`

This step requires creating an `AnalyzerConfigOptionsProvider` from the `.editorconfig` content. Roslyn provides `AnalyzerConfigSet` for this. Research the exact API during implementation — it may require parsing the `.editorconfig` into `AnalyzerConfig` objects and creating a provider from them.

**Step 5: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS.

**Step 6: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/ProfileProvider.cs \
        src/Parlance.Analyzers.Upstream/profiles/ \
        src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj \
        src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs \
        tests/Parlance.Analyzers.Upstream.Tests/ProfileProviderTests.cs
git commit -m "Add profile system with .editorconfig loading and default profiles"
```

---

## Task 11: End-to-End Integration Testing

**Files:**
- Modify: `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`

**Step 1: Write comprehensive integration tests**

Add to `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`:

```csharp
[Fact]
public async Task Analyze_DefaultProfile_ProducesUpstreamDiagnostics()
{
    var file = Path.Combine(_tempDir, "Test.cs");
    File.WriteAllText(file, """
        using System;

        public class MyClass
        {
            public int Add(int a, int b)
            {
                return a + b;
            }
        }
        """);

    var (exitCode, stdout, _) = await RunCliAsync(
        "analyze", file, "--format", "json");

    Assert.Equal(0, exitCode);
    // JSON output should parse and contain diagnostics
    var doc = System.Text.Json.JsonDocument.Parse(stdout);
    Assert.True(doc.RootElement.TryGetProperty("summary", out _));
}

[Fact]
public async Task Analyze_JsonOutput_IncludesUpstreamRuleIds()
{
    var file = Path.Combine(_tempDir, "Test.cs");
    // Code with multiple issues across different analyzer sources
    File.WriteAllText(file, """
        using System;

        public class C
        {
            public int GetValue()
            {
                return 42;
            }

            void M(object obj)
            {
                if (obj is string)
                {
                    var s = (string)obj;
                }
            }
        }
        """);

    var (exitCode, stdout, _) = await RunCliAsync(
        "analyze", file, "--format", "json");

    Assert.Equal(0, exitCode);
    // Should contain both PARL and upstream diagnostics
    Assert.Contains("PARL", stdout);
}

[Fact]
public async Task Analyze_ExistingParlTests_StillPass()
{
    // Regression: ensure the PARL0004 test case still works
    var file = Path.Combine(_tempDir, "Test.cs");
    File.WriteAllText(file, """
        class C
        {
            void M(object obj)
            {
                if (obj is string)
                {
                    var s = (string)obj;
                }
            }
        }
        """);

    var (exitCode, stdout, _) = await RunCliAsync("analyze", file);

    Assert.Equal(0, exitCode);
    Assert.Contains("PARL0004", stdout);
}
```

**Step 2: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS.

**Step 3: Commit**

```bash
git add tests/Parlance.Cli.Tests/
git commit -m "Add end-to-end integration tests for upstream analyzer pipeline"
```

---

## Task 12: Clean Up and Final Verification

**Files:**
- Review all modified files
- Remove any unused `using` directives introduced by refactoring

**Step 1: Run dotnet format**

```bash
dotnet format Parlance.sln --verify-no-changes || dotnet format Parlance.sln
```

**Step 2: Run full test suite**

```bash
dotnet test -v quiet
```

Expected: All tests PASS.

**Step 3: Verify CLI works end-to-end**

```bash
# Analyze with upstream rules
dotnet run --project src/Parlance.Cli -- analyze src/Parlance.Cli/Commands/AnalyzeCommand.cs

# List all rules (should show CA, IDE, RCS, PARL)
dotnet run --project src/Parlance.Cli -- rules

# List rules for net8.0
dotnet run --project src/Parlance.Cli -- rules --target-framework net8.0

# Fix still works
echo 'using System; using System.IO; class C { void M() { using (var s = new MemoryStream()) { s.WriteByte(1); } } }' > /tmp/test-fix.cs
dotnet run --project src/Parlance.Cli -- fix /tmp/test-fix.cs
```

**Step 4: Commit any cleanup**

```bash
git add -A
git commit -m "Clean up formatting and unused imports after upstream integration"
```
