# Upstream Analyzer Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate upstream analyzer packages (CA, IDE, Roslynator) into Parlance with version-aware loading, auto-generated manifests, and profile-based `.editorconfig` configuration for net8.0 and net10.0.

**Architecture:** A multi-target helper project (`Parlance.Analyzers.Upstream`) restores per-TFM analyzer packages via conditional `PackageReference` and copies DLLs to a known output layout. `AnalyzerLoader` dynamically discovers all analyzers (PARL + upstream) at runtime via reflection and `AssemblyLoadContext`. A `ManifestGenerator` tool produces per-TFM rule manifests. Profiles are `.editorconfig` files applied via Roslyn's native `AnalyzerConfigOptionsProvider`.

**Tech Stack:** .NET 10, Roslyn (`Microsoft.CodeAnalysis`), `AssemblyLoadContext`, MSBuild targets, System.CommandLine 2.0.3, xUnit

**Design doc:** `docs/plans/2026-03-07-upstream-analyzer-integration-design.md`

**Package versions:**

| Package | net8.0 | net10.0 |
|---------|--------|---------|
| `Microsoft.CodeAnalysis.NetAnalyzers` | 8.0.0 | 10.0.101 |
| `Microsoft.CodeAnalysis.CSharp.CodeStyle` | 4.9.2 | 5.0.0 |
| `Roslynator.Analyzers` | 4.15.0 | 4.15.0 |

**Multi-version strategy:** Single multi-target helper `.csproj` with conditional `PackageReference` per TFM. NuGet resolves each TFM independently, giving the correct package version per target. `GeneratePathProperty` exposes `$(PkgPackageName)` per TFM for a `CopyAnalyzerDlls` MSBuild target. Adding a future TFM (e.g., net12.0) = add the TFM string + one conditional `ItemGroup` in one file.

---

## Task 1: Create Parlance.Analyzers.Upstream Project with Multi-Target DLL Extraction

This task creates the multi-target helper project that restores all upstream analyzer packages per TFM and copies their DLLs to a known output layout (`analyzer-dlls/{tfm}/`).

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
    <!--
      Multi-target across all supported TFMs. Each TFM gets its own
      conditional PackageReference, so NuGet resolves the correct
      analyzer version per target independently.

      To add a new TFM: add it here, add a conditional ItemGroup below.
    -->
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <!-- Suppress "SDK has newer analyzers" warning from older package versions -->
    <_SkipUpgradeNetAnalyzersNuGetWarning>true</_SkipUpgradeNetAnalyzersNuGetWarning>
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
    Upstream analyzer packages — version-pinned per TFM.
    Each package uses GeneratePathProperty="true" so $(PkgPackageName) resolves
    to the NuGet cache path for that version. ExcludeAssets="all" prevents
    the analyzers from running on our own code during build.

    To add a new TFM: copy one of these ItemGroups, change the Condition
    and package versions. That's it.
  -->

  <ItemGroup Condition="'$(TargetFramework)'=='net8.0'">
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0"
                      GeneratePathProperty="true" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeStyle" Version="4.9.2"
                      GeneratePathProperty="true" ExcludeAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0"
                      GeneratePathProperty="true" ExcludeAssets="all" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)'=='net10.0'">
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.101"
                      GeneratePathProperty="true" ExcludeAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeStyle" Version="5.0.0"
                      GeneratePathProperty="true" ExcludeAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0"
                      GeneratePathProperty="true" ExcludeAssets="all" />
  </ItemGroup>

  <!--
    Copy analyzer DLLs from NuGet cache to a known output layout.
    After build, analyzer-dlls/{tfm}/ contains all analyzer assemblies
    for that target framework.

    The glob patterns include both analyzers/dotnet/cs/ and analyzers/dotnet/
    because the DLL layout changed between package versions.
  -->
  <Target Name="CopyAnalyzerDlls" AfterTargets="Build"
          Condition="'$(TargetFramework)' != ''">
    <ItemGroup>
      <!-- NetAnalyzers -->
      <_NetAnalyzerDlls Include="$(PkgMicrosoft_CodeAnalysis_NetAnalyzers)/analyzers/dotnet/cs/**/*.dll" />
      <_NetAnalyzerDlls Include="$(PkgMicrosoft_CodeAnalysis_NetAnalyzers)/analyzers/dotnet/*.dll" />

      <!-- CodeStyle (IDE rules) -->
      <_CodeStyleDlls Include="$(PkgMicrosoft_CodeAnalysis_CSharp_CodeStyle)/analyzers/dotnet/cs/**/*.dll" />
      <_CodeStyleDlls Include="$(PkgMicrosoft_CodeAnalysis_CSharp_CodeStyle)/analyzers/dotnet/*.dll" />

      <!-- Roslynator -->
      <_RoslynatorDlls Include="$(PkgRoslynator_Analyzers)/analyzers/dotnet/cs/**/*.dll" />
      <_RoslynatorDlls Include="$(PkgRoslynator_Analyzers)/analyzers/dotnet/*.dll" />
    </ItemGroup>

    <MakeDir Directories="$(MSBuildProjectDirectory)/analyzer-dlls/$(TargetFramework)" />
    <Copy SourceFiles="@(_NetAnalyzerDlls);@(_CodeStyleDlls);@(_RoslynatorDlls)"
          DestinationFolder="$(MSBuildProjectDirectory)/analyzer-dlls/$(TargetFramework)"
          SkipUnchangedFiles="true" />
  </Target>

  <!-- Ship profile .editorconfig files as content -->
  <ItemGroup>
    <Content Include="profiles/**/*.editorconfig">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

</Project>
```

**Step 3: Add to solution**

```bash
dotnet sln Parlance.sln add src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj --solution-folder src
```

**Step 4: Restore and build**

```bash
dotnet restore src/Parlance.Analyzers.Upstream
dotnet build src/Parlance.Analyzers.Upstream
```

Expected: Successful build for both TFMs.

**Step 5: Verify analyzer DLLs were extracted**

```bash
ls src/Parlance.Analyzers.Upstream/analyzer-dlls/net8.0/
ls src/Parlance.Analyzers.Upstream/analyzer-dlls/net10.0/
```

Expected: Both directories contain `.dll` files from all three upstream packages. Note the exact DLL names — they will be needed for `AnalyzerLoader`. If a directory is missing DLLs, check the glob patterns in the `CopyAnalyzerDlls` target against the actual NuGet cache layout for that package version:

```bash
find ~/.nuget/packages/microsoft.codeanalysis.netanalyzers/8.0.0/analyzers -name "*.dll"
find ~/.nuget/packages/microsoft.codeanalysis.netanalyzers/10.0.101/analyzers -name "*.dll"
find ~/.nuget/packages/microsoft.codeanalysis.csharp.codestyle/5.0.0/analyzers -name "*.dll"
find ~/.nuget/packages/roslynator.analyzers/4.15.0/analyzers -name "*.dll"
```

Adjust glob patterns until all DLLs are captured.

**Step 6: Add analyzer-dlls to .gitignore**

The `analyzer-dlls/` directory is a build artifact — don't commit binaries.

```bash
echo "src/Parlance.Analyzers.Upstream/analyzer-dlls/" >> .gitignore
```

**Step 7: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/Parlance.Analyzers.Upstream.csproj Parlance.sln .gitignore
git commit -m "Add Parlance.Analyzers.Upstream with multi-target per-TFM analyzer extraction"
```

---

## Task 2: AnalyzerLoader — Dynamic Analyzer Discovery

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

        // PARL (8) + CA (100+) + IDE (50+) + RCS (150+) = at minimum 50 analyzer types
        Assert.True(analyzers.Length >= 50,
            $"Expected at least 50 analyzer types, got {analyzers.Length}");
    }

    [Fact]
    public void LoadAll_Net8VsNet10_BothHaveAnalyzers()
    {
        var net8 = AnalyzerLoader.LoadAll("net8.0");
        var net10 = AnalyzerLoader.LoadAll("net10.0");

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
    /// Upstream analyzers are loaded from extracted NuGet DLLs in analyzer-dlls/{tfm}/.
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
        // Look for analyzer-dlls/{tfm}/ relative to the project source directory.
        // The CopyAnalyzerDlls MSBuild target places DLLs here during build.
        // At runtime, resolve relative to the executing assembly.
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        // First try the build output location (analyzer-dlls/ in the project dir)
        var projectDir = FindProjectDirectory(baseDir);
        if (projectDir is not null)
        {
            var projectAnalyzerDir = Path.Combine(projectDir, "analyzer-dlls", targetFramework);
            if (Directory.Exists(projectAnalyzerDir))
                return projectAnalyzerDir;
        }

        // Fallback: relative to the output assembly
        return Path.Combine(baseDir, "analyzer-dlls", targetFramework);
    }

    private static string? FindProjectDirectory(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "*.csproj").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
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
            var path = Path.Combine(analyzerDir, $"{assemblyName.Name}.dll");
            if (File.Exists(path))
                return LoadFromAssemblyPath(path);

            return null;
        }
    }
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Analyzers.Upstream.Tests -v quiet`
Expected: All tests PASS.

Note: Some spot-check IDs (CA1822, IDE0003, RCS1003) are well-known stable rules. If a specific ID isn't found, inspect the extracted DLLs and adjust the test to use an ID that exists.

**Step 6: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/AnalyzerLoader.cs \
        tests/Parlance.Analyzers.Upstream.Tests/ \
        Parlance.sln
git commit -m "Add AnalyzerLoader with dynamic analyzer discovery and tests"
```

---

## Task 3: Manifest Generator Tool

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
mkdir -p docs/manifests
dotnet run --project tools/Parlance.ManifestGenerator -- net10.0 docs/manifests/rule-manifest-net10.0.json
dotnet run --project tools/Parlance.ManifestGenerator -- net8.0 docs/manifests/rule-manifest-net8.0.json
```

Expected: JSON files created with rule entries from all three upstream sources plus PARL rules.

**Step 5: Verify manifest contents**

Spot-check that the generated manifest contains rules from each source:

```bash
cat docs/manifests/rule-manifest-net10.0.json | python3 -m json.tool | head -50
grep -c '"id"' docs/manifests/rule-manifest-net10.0.json
grep -c '"id"' docs/manifests/rule-manifest-net8.0.json
```

**Step 6: Commit**

```bash
git add tools/Parlance.ManifestGenerator/ docs/manifests/ Parlance.sln
git commit -m "Add ManifestGenerator tool and initial rule manifests"
```

---

## Task 4: Refactor WorkspaceAnalyzer to Use AnalyzerLoader

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
- Remove the hardcoded `AllAnalyzers` array
- Add `targetFramework` parameter (default: `"net10.0"`)
- Call `AnalyzerLoader.LoadAll(targetFramework)` instead
- Everything else stays the same — the pipeline doesn't change, just the input set grows

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

Run: `dotnet test -v quiet`
Expected: All tests PASS, including the new upstream analyzer test and all existing PARL tests.

**Step 6: Commit**

```bash
git add src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs \
        src/Parlance.Cli/Parlance.Cli.csproj \
        tests/Parlance.Cli.Tests/
git commit -m "Refactor WorkspaceAnalyzer to use AnalyzerLoader for dynamic discovery"
```

---

## Task 5: Refactor WorkspaceFixer to Use AnalyzerLoader

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

## Task 6: Refactor DiagnosticEnricher to Use RuleMetadataProvider

**Files:**
- Modify: `src/Parlance.CSharp/DiagnosticEnricher.cs`
- Create: `src/Parlance.CSharp/RuleMetadataProvider.cs`

**Step 1: Verify existing enricher tests pass**

Run: `dotnet test tests/Parlance.CSharp.Tests -v quiet`
Expected: All tests PASS.

**Step 2: Create RuleMetadataProvider**

Create `src/Parlance.CSharp/RuleMetadataProvider.cs`:

Move the existing hand-written PARL rationale from `DiagnosticEnricher` into a `RuleMetadataProvider` class. This class holds curated metadata for PARL rules and returns `null` for upstream rules (they fall back to upstream-provided descriptions).

```csharp
using System.Collections.Frozen;

namespace Parlance.CSharp;

internal sealed record RuleMetadata(
    string Category,
    string? Rationale,
    string? SuggestedFix);

internal static class RuleMetadataProvider
{
    private static readonly FrozenDictionary<string, RuleMetadata> CuratedMetadata =
        new Dictionary<string, RuleMetadata>
        {
            // ... existing PARL metadata entries moved from DiagnosticEnricher ...
        }.ToFrozenDictionary();

    public static RuleMetadata? GetMetadata(string ruleId)
    {
        return CuratedMetadata.GetValueOrDefault(ruleId);
    }
}
```

**Step 3: Refactor DiagnosticEnricher**

Modify `src/Parlance.CSharp/DiagnosticEnricher.cs`:
- Remove the hardcoded `Metadata` dictionary and `RuleMetadata` record (moved to `RuleMetadataProvider`)
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

## Task 7: Refactor RulesCommand to Use AnalyzerLoader

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
    Assert.Contains("PARL0001", stdout);
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

## Task 8: Add --target-framework and --profile to AnalyzeCommand and FixCommand

**Files:**
- Modify: `src/Parlance.Cli/Commands/AnalyzeCommand.cs`
- Modify: `src/Parlance.Cli/Commands/FixCommand.cs`
- Modify: `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`

**Step 1: Write tests for new flags**

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

**Step 2: Add options to AnalyzeCommand and FixCommand**

In both commands:
- Add `--target-framework` option with default `"net10.0"`
- Add `--profile` option with default `"default"` (wired but profile loading is Task 9)
- Pass `targetFramework` through to `WorkspaceAnalyzer.AnalyzeAsync` / `WorkspaceFixer.FixAsync`

**Step 3: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS.

**Step 4: Commit**

```bash
git add src/Parlance.Cli/Commands/AnalyzeCommand.cs \
        src/Parlance.Cli/Commands/FixCommand.cs \
        tests/Parlance.Cli.Tests/
git commit -m "Add --target-framework and --profile options to analyze and fix commands"
```

---

## Task 9: Profile System — .editorconfig Loading

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

Create `src/Parlance.Analyzers.Upstream/profiles/net10.0/default.editorconfig` and `profiles/net8.0/default.editorconfig`:

Start with a minimal default profile. Full profile content will be refined after reviewing the generated manifests.

```ini
# Parlance default profile for net10.0
# Balanced — warnings for bugs, suggestions for idioms
[*.cs]

# PARL rules — all enabled at default severity (no overrides needed)

# CA rules — key overrides from defaults
# dotnet_diagnostic.CA1822.severity = suggestion

# IDE rules — enable common style rules
# dotnet_diagnostic.IDE0003.severity = suggestion

# RCS rules — enable common simplification rules
# dotnet_diagnostic.RCS1003.severity = suggestion
```

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

        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var profilePath = Path.Combine(baseDir, "profiles", targetFramework, $"{profile}.editorconfig");

        if (!File.Exists(profilePath))
            throw new ArgumentException(
                $"Profile '{profile}' not found for {targetFramework}",
                nameof(profile));

        return File.ReadAllText(profilePath);
    }

    public static IReadOnlyList<string> GetAvailableProfiles() => AvailableProfiles;
}
```

**Step 4: Wire profile into WorkspaceAnalyzer**

In `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`:
- Add `string profile = "default"` parameter
- Load profile content via `ProfileProvider.GetProfileContent(targetFramework, profile)`
- Apply to compilation via `AnalyzerConfigOptionsProvider`

This requires creating an `AnalyzerConfigOptionsProvider` from the `.editorconfig` content. Research the exact Roslyn API during implementation — it may require `AnalyzerConfig.Parse()` and constructing a provider from the parsed config.

**Step 5: Run all tests**

Run: `dotnet test -v quiet`
Expected: All tests PASS.

**Step 6: Commit**

```bash
git add src/Parlance.Analyzers.Upstream/ProfileProvider.cs \
        src/Parlance.Analyzers.Upstream/profiles/ \
        src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs \
        tests/Parlance.Analyzers.Upstream.Tests/ProfileProviderTests.cs
git commit -m "Add profile system with .editorconfig loading and default profiles"
```

---

## Task 10: End-to-End Integration Testing

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
    var doc = System.Text.Json.JsonDocument.Parse(stdout);
    Assert.True(doc.RootElement.TryGetProperty("summary", out _));
}

[Fact]
public async Task Analyze_ExistingParlTests_StillPass()
{
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

## Task 11: Clean Up and Final Verification

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
