# Phase 3: NuGet Analyzer Packages — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship two NuGet packages — `Parlance.CSharp.Analyzers` (custom PARL rules) and `Parlance.CSharp` (bundle with upstream deps + curated profile) — that light up in IDEs via standard NuGet analyzer package conventions.

**Architecture:** The existing `Parlance.CSharp.Analyzers` project (netstandard2.0) gets NuGet packaging metadata and the correct `analyzers/dotnet/cs/` folder structure via MSBuild properties. A new `Parlance.CSharp.Package` project produces the bundle metapackage that declares dependencies on upstream analyzers and ships a curated `.editorconfig` profile. Both packages share version `0.1.0` via a root `Directory.Build.props`.

**Tech Stack:** MSBuild NuGet packaging (no .nuspec files), xUnit integration tests, `dotnet pack`, `dotnet build` with local NuGet feed

---

### Task 1: Create Directory.Build.props for Shared Versioning

**Files:**
- Create: `Directory.Build.props`

**Step 1: Create the file**

```xml
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <Authors>NetInlet</Authors>
    <Company>NetInlet</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/netinlet/parlance</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
  </PropertyGroup>
</Project>
```

**Step 2: Verify the solution still builds**

Run: `dotnet build Parlance.sln`
Expected: Build succeeds. All existing projects pick up the shared properties.

**Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "Add Directory.Build.props with shared version 0.1.0 and package metadata"
```

---

### Task 2: Configure Parlance.CSharp.Analyzers as NuGet Analyzer Package

**Files:**
- Modify: `src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj`
- Create: `src/Parlance.CSharp.Analyzers/build/Parlance.CSharp.Analyzers.props`

**Context:** A NuGet analyzer package must place DLLs in `analyzers/dotnet/cs/` inside the .nupkg. MSBuild provides the `<DevelopmentDependency>` and `<SuppressDependenciesWhenPacking>` properties for this. The `.props` file in `build/` ensures the DLL is loaded as an analyzer (not a compile reference) by consuming projects.

**Step 1: Create the build props file**

Create `src/Parlance.CSharp.Analyzers/build/Parlance.CSharp.Analyzers.props`:

```xml
<Project>
  <!-- This file is intentionally empty. The analyzer DLL is automatically picked up
       from the analyzers/dotnet/cs/ folder in the NuGet package. This .props file
       exists as a convention placeholder for future build-time configuration. -->
</Project>
```

**Step 2: Update the .csproj with packaging configuration**

Modify `src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj` — add packaging properties and NuGet analyzer folder structure:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <NoWarn>RS2008</NoWarn>
  </PropertyGroup>

  <!-- NuGet analyzer package configuration -->
  <PropertyGroup>
    <PackageId>Parlance.CSharp.Analyzers</PackageId>
    <Description>Custom Roslyn analyzers for idiomatic modern C# — pattern matching, primary constructors, collection expressions, and more.</Description>
    <PackageTags>analyzers;roslyn;csharp;code-quality;idiomatic</PackageTags>
    <DevelopmentDependency>true</DevelopmentDependency>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoPackageAnalysis>true</NoPackageAnalysis>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.12.0" PrivateAssets="all" />
  </ItemGroup>

  <!-- Pack the analyzer DLL into analyzers/dotnet/cs/ -->
  <ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll"
          Pack="true"
          PackagePath="analyzers/dotnet/cs"
          Visible="false" />
  </ItemGroup>

  <!-- Pack the build props file -->
  <ItemGroup>
    <None Include="build\Parlance.CSharp.Analyzers.props"
          Pack="true"
          PackagePath="build"
          Visible="false" />
  </ItemGroup>

</Project>
```

**Step 3: Build and pack**

Run: `dotnet build src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj -c Release`
Expected: Build succeeds.

Run: `dotnet pack src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj -c Release --output ./artifacts`
Expected: `Parlance.CSharp.Analyzers.0.1.0.nupkg` created in `./artifacts/`.

**Step 4: Verify package contents**

Run: `unzip -l ./artifacts/Parlance.CSharp.Analyzers.0.1.0.nupkg | grep -E "(analyzers|build)/"`
Expected output should include:
- `analyzers/dotnet/cs/Parlance.CSharp.Analyzers.dll`
- `build/Parlance.CSharp.Analyzers.props`

No `lib/` folder should be present (the DLL is analyzer-only, not a compile reference).

**Step 5: Verify full solution still builds**

Run: `dotnet build Parlance.sln`
Expected: Build succeeds. Existing projects that reference `Parlance.CSharp.Analyzers` via `<ProjectReference>` are unaffected.

**Step 6: Commit**

```bash
git add src/Parlance.CSharp.Analyzers/ artifacts/
git commit -m "Configure Parlance.CSharp.Analyzers as NuGet analyzer package"
```

Note: Do NOT commit `artifacts/` — add it to `.gitignore` if not already ignored.

---

### Task 3: Create Bundle Metapackage Project

**Files:**
- Create: `src/Parlance.CSharp.Package/Parlance.CSharp.Package.csproj`
- Create: `src/Parlance.CSharp.Package/build/Parlance.CSharp.props`
- Create: `src/Parlance.CSharp.Package/build/Parlance.CSharp.targets`
- Create: `src/Parlance.CSharp.Package/buildTransitive/Parlance.CSharp.props`
- Create: `src/Parlance.CSharp.Package/buildTransitive/Parlance.CSharp.targets`
- Create: `src/Parlance.CSharp.Package/content/parlance-default.editorconfig`
- Modify: `Parlance.sln` (add new project)

**Context:** This project produces a NuGet package with `PackageId=Parlance.CSharp` (different from the project folder name). It's a packaging-only project — no source code. It declares dependencies on `Parlance.CSharp.Analyzers`, `Microsoft.CodeAnalysis.NetAnalyzers`, and `Roslynator.Analyzers`. The `.props` file enables `EnforceCodeStyleInBuild`. The `content/` folder ships a reference `.editorconfig`.

**Step 1: Create the .csproj**

Create `src/Parlance.CSharp.Package/Parlance.CSharp.Package.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Parlance.CSharp</PackageId>
    <Description>Batteries-included C# code quality package — Parlance custom analyzers plus curated upstream analyzers (NetAnalyzers, Roslynator) with a default .editorconfig profile.</Description>
    <PackageTags>analyzers;roslyn;csharp;code-quality;idiomatic;bundle</PackageTags>
    <DevelopmentDependency>true</DevelopmentDependency>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoPackageAnalysis>true</NoPackageAnalysis>
    <NoBuild>true</NoBuild>
    <NoDefaultExcludes>true</NoDefaultExcludes>
  </PropertyGroup>

  <!-- Dependencies that consumers will get -->
  <ItemGroup>
    <PackageReference Include="Parlance.CSharp.Analyzers" Version="$(Version)" />
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.101">
      <PrivateAssets>none</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0">
      <PrivateAssets>none</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <!-- Pack build props/targets -->
  <ItemGroup>
    <None Include="build\**"
          Pack="true"
          PackagePath="build"
          Visible="false" />
    <None Include="buildTransitive\**"
          Pack="true"
          PackagePath="buildTransitive"
          Visible="false" />
    <None Include="content\**"
          Pack="true"
          PackagePath="content"
          Visible="false" />
  </ItemGroup>

</Project>
```

**Step 2: Create build/Parlance.CSharp.props**

```xml
<Project>
  <PropertyGroup>
    <EnforceCodeStyleInBuild Condition="'$(EnforceCodeStyleInBuild)' == ''">true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

**Step 3: Create build/Parlance.CSharp.targets**

```xml
<Project>
  <!-- Reserved for future build-time targets (e.g., score reporting). -->
</Project>
```

**Step 4: Create buildTransitive/ copies**

Copy `build/Parlance.CSharp.props` to `buildTransitive/Parlance.CSharp.props` (identical content).
Copy `build/Parlance.CSharp.targets` to `buildTransitive/Parlance.CSharp.targets` (identical content).

These ensure the settings apply to projects that reference this package transitively (e.g., through a shared library).

**Step 5: Create content/parlance-default.editorconfig**

This is a reference `.editorconfig` users can copy to their repo root. Use the existing profile content from `src/Parlance.Analyzers.Upstream/profiles/net10.0/default.editorconfig` as the base, expanded with concrete rule configurations:

```ini
# Parlance Default Profile
# Copy this file to your repository root as .editorconfig
# Balanced — warnings for bugs, suggestions for idioms

root = true

[*.cs]

# === Parlance Custom Rules (PARL) ===
dotnet_diagnostic.PARL0001.severity = suggestion
dotnet_diagnostic.PARL0002.severity = suggestion
dotnet_diagnostic.PARL0003.severity = suggestion
dotnet_diagnostic.PARL0004.severity = warning
dotnet_diagnostic.PARL0005.severity = suggestion
dotnet_diagnostic.PARL9001.severity = suggestion
dotnet_diagnostic.PARL9002.severity = suggestion
dotnet_diagnostic.PARL9003.severity = suggestion

# === Key CA Rule Overrides ===
dotnet_diagnostic.CA1822.severity = suggestion
dotnet_diagnostic.CA1050.severity = warning
dotnet_diagnostic.CA2007.severity = suggestion

# === Key IDE Rule Overrides ===
dotnet_diagnostic.IDE0003.severity = suggestion
dotnet_diagnostic.IDE0058.severity = silent

# === Key Roslynator Rule Overrides ===
dotnet_diagnostic.RCS1003.severity = suggestion
dotnet_diagnostic.RCS1036.severity = suggestion
```

**Step 6: Add the project to the solution**

Run: `dotnet sln Parlance.sln add src/Parlance.CSharp.Package/Parlance.CSharp.Package.csproj --solution-folder src`

**Step 7: Pack the bundle package**

Run: `dotnet pack src/Parlance.CSharp.Package/Parlance.CSharp.Package.csproj -c Release --output ./artifacts`
Expected: `Parlance.CSharp.0.1.0.nupkg` created in `./artifacts/`.

**Step 8: Verify package contents**

Run: `unzip -l ./artifacts/Parlance.CSharp.0.1.0.nupkg | grep -E "(build|content)/"`
Expected output should include:
- `build/Parlance.CSharp.props`
- `build/Parlance.CSharp.targets`
- `buildTransitive/Parlance.CSharp.props`
- `buildTransitive/Parlance.CSharp.targets`
- `content/parlance-default.editorconfig`

**Step 9: Verify dependency metadata in .nuspec**

Run: `unzip -p ./artifacts/Parlance.CSharp.0.1.0.nupkg Parlance.CSharp.nuspec | grep -A2 "<dependency"`
Expected: Should list dependencies on `Parlance.CSharp.Analyzers`, `Microsoft.CodeAnalysis.NetAnalyzers`, `Roslynator.Analyzers`.

**Step 10: Commit**

```bash
git add src/Parlance.CSharp.Package/ Parlance.sln
git commit -m "Add Parlance.CSharp bundle metapackage with upstream deps and curated profile"
```

---

### Task 4: Add .gitignore Entry for Artifacts

**Files:**
- Modify or Create: `.gitignore`

**Step 1: Add artifacts directory to .gitignore**

Add the following line to `.gitignore` (create the file if it doesn't exist):

```
artifacts/
```

**Step 2: Commit**

```bash
git add .gitignore
git commit -m "Add artifacts/ to .gitignore"
```

---

### Task 5: Write Integration Tests for Analyzer Package

**Files:**
- Create: `tests/Parlance.Package.Tests/Parlance.Package.Tests.csproj`
- Create: `tests/Parlance.Package.Tests/AnalyzerPackageIntegrationTests.cs`
- Modify: `Parlance.sln` (add test project)

**Context:** These tests verify that the NuGet packages work correctly end-to-end: pack → restore → build → diagnostics fire. They use `dotnet pack` to create local packages, then create temporary .NET projects that reference them via a local NuGet feed, then run `dotnet build` and check for expected diagnostics in the build output.

**Step 1: Create the test project**

Create `tests/Parlance.Package.Tests/Parlance.Package.Tests.csproj`:

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

</Project>
```

**Step 2: Add to solution**

Run: `dotnet sln Parlance.sln add tests/Parlance.Package.Tests/Parlance.Package.Tests.csproj --solution-folder tests`

**Step 3: Write the failing test skeleton**

Create `tests/Parlance.Package.Tests/AnalyzerPackageIntegrationTests.cs`:

```csharp
using System.Diagnostics;

namespace Parlance.Package.Tests;

public sealed class AnalyzerPackageIntegrationTests : IAsyncLifetime
{
    private string _artifactsDir = null!;
    private string _tempDir = null!;
    private string _nugetConfigPath = null!;

    public async Task InitializeAsync()
    {
        // Find the repo root (walk up from test assembly location)
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Parlance.sln")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);
        var repoRoot = dir!;

        _artifactsDir = Path.Combine(repoRoot, "artifacts", "test-packages");
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-pkg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_artifactsDir);

        // Pack both packages into local feed
        await RunDotnet($"pack {Path.Combine(repoRoot, "src", "Parlance.CSharp.Analyzers", "Parlance.CSharp.Analyzers.csproj")} -c Release --output \"{_artifactsDir}\"");
        await RunDotnet($"pack {Path.Combine(repoRoot, "src", "Parlance.CSharp.Package", "Parlance.CSharp.Package.csproj")} -c Release --output \"{_artifactsDir}\"");

        // Create a NuGet.config that points to local feed
        _nugetConfigPath = Path.Combine(_tempDir, "NuGet.config");
        await File.WriteAllTextAsync(_nugetConfigPath, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{_artifactsDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AnalyzerPackage_ReportsParl0004_WhenIsFollowedByCast()
    {
        var projectDir = Path.Combine(_tempDir, "analyzer-test");
        Directory.CreateDirectory(projectDir);

        // Create a minimal .csproj referencing the analyzer package
        await File.WriteAllTextAsync(Path.Combine(projectDir, "Test.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Parlance.CSharp.Analyzers" Version="0.1.0" />
              </ItemGroup>
            </Project>
            """);

        // Write C# code that triggers PARL0004
        await File.WriteAllTextAsync(Path.Combine(projectDir, "Code.cs"), """
            public class Example
            {
                public void Method(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                        System.Console.WriteLine(s);
                    }
                }
            }
            """);

        var output = await RunDotnet($"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore -v normal", allowFailure: true, restoreFirst: projectDir);

        Assert.Contains("PARL0004", output);
    }

    [Fact]
    public async Task BundlePackage_RestoresUpstreamDependencies()
    {
        var projectDir = Path.Combine(_tempDir, "bundle-test");
        Directory.CreateDirectory(projectDir);

        await File.WriteAllTextAsync(Path.Combine(projectDir, "Test.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Parlance.CSharp" Version="0.1.0" />
              </ItemGroup>
            </Project>
            """);

        // Write code that triggers both a PARL rule and a CA rule
        await File.WriteAllTextAsync(Path.Combine(projectDir, "Code.cs"), """
            public class Example
            {
                public void Method(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                        System.Console.WriteLine(s);
                    }
                }
            }
            """);

        var output = await RunDotnet($"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore -v normal", allowFailure: true, restoreFirst: projectDir);

        // PARL analyzer from Parlance.CSharp.Analyzers dependency
        Assert.Contains("PARL0004", output);
    }

    [Fact]
    public async Task AnalyzerPackage_HasNoLibFolder()
    {
        // Verify the .nupkg has no lib/ folder (analyzer-only package)
        var nupkgPath = Directory.GetFiles(_artifactsDir, "Parlance.CSharp.Analyzers.*.nupkg").FirstOrDefault();
        Assert.NotNull(nupkgPath);

        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath!);
        var hasLib = archive.Entries.Any(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase));
        var hasAnalyzers = archive.Entries.Any(e => e.FullName.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase));

        Assert.False(hasLib, "Analyzer package should not contain a lib/ folder");
        Assert.True(hasAnalyzers, "Analyzer package should contain analyzers/dotnet/cs/ folder");
    }

    [Fact]
    public async Task BundlePackage_ContainsBuildProps()
    {
        var nupkgPath = Directory.GetFiles(_artifactsDir, "Parlance.CSharp.0.1.0.nupkg").FirstOrDefault();
        Assert.NotNull(nupkgPath);

        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath!);
        var entries = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e == "build/Parlance.CSharp.props");
        Assert.Contains(entries, e => e == "build/Parlance.CSharp.targets");
        Assert.Contains(entries, e => e == "buildTransitive/Parlance.CSharp.props");
        Assert.Contains(entries, e => e == "buildTransitive/Parlance.CSharp.targets");
        Assert.Contains(entries, e => e.StartsWith("content/") && e.EndsWith(".editorconfig"));
    }

    private async Task<string> RunDotnet(string arguments, bool allowFailure = false, string? restoreFirst = null)
    {
        if (restoreFirst is not null)
        {
            // Restore with our custom NuGet.config
            await RunDotnet($"restore \"{restoreFirst}\" --configfile \"{_nugetConfigPath}\"");
        }

        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combined = stdout + Environment.NewLine + stderr;

        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {arguments} failed with exit code {process.ExitCode}:\n{combined}");

        return combined;
    }
}
```

**Step 4: Run the tests to verify they fail (packages not yet built by CI)**

Run: `dotnet test tests/Parlance.Package.Tests/ --no-build`
Expected: Fails — project hasn't been built yet.

Run: `dotnet build tests/Parlance.Package.Tests/`
Expected: Build succeeds (no project references to fail).

Run: `dotnet test tests/Parlance.Package.Tests/ -v normal`
Expected: Tests run. The `InitializeAsync` packs the projects, tests validate the packages.

**Step 5: Commit**

```bash
git add tests/Parlance.Package.Tests/ Parlance.sln
git commit -m "Add integration tests for NuGet analyzer package structure and diagnostics"
```

---

### Task 6: Run Full Test Suite and Fix Issues

**Files:**
- Potentially modify any files from previous tasks

**Step 1: Run existing tests to verify no regressions**

Run: `dotnet test Parlance.sln -v normal`
Expected: All existing tests (73 analyzer + 25 CLI + upstream tests) pass. New integration tests pass.

**Step 2: Fix any issues**

If any tests fail, diagnose and fix. Common issues:
- `Directory.Build.props` may conflict with existing project settings — check that `<Version>` doesn't break non-packable projects (test projects have `<IsPackable>false</IsPackable>`)
- Package dependency resolution — the bundle's `<PackageReference Include="Parlance.CSharp.Analyzers" Version="$(Version)" />` needs the analyzer package to be in the local feed before the bundle can be packed

**Step 3: Commit any fixes**

```bash
git add -u
git commit -m "Fix issues found during full test suite run"
```

---

### Task 7: Verify End-to-End Package Experience

**Files:** None (manual verification)

**Step 1: Clean pack both packages**

Run:
```bash
rm -rf artifacts/
dotnet pack src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj -c Release --output ./artifacts
dotnet pack src/Parlance.CSharp.Package/Parlance.CSharp.Package.csproj -c Release --output ./artifacts
```

**Step 2: Verify package contents**

Run: `dotnet nuget locals all --list` (for reference)

Run:
```bash
unzip -l artifacts/Parlance.CSharp.Analyzers.0.1.0.nupkg
unzip -l artifacts/Parlance.CSharp.0.1.0.nupkg
```

Verify:
- `Parlance.CSharp.Analyzers.0.1.0.nupkg` contains `analyzers/dotnet/cs/Parlance.CSharp.Analyzers.dll`, `build/Parlance.CSharp.Analyzers.props`, no `lib/` folder
- `Parlance.CSharp.0.1.0.nupkg` contains `build/`, `buildTransitive/`, `content/` folders with expected files

**Step 3: Manual IDE smoke test (user performs)**

Create a throwaway project outside the Parlance repo:

```bash
mkdir /tmp/parlance-smoke && cd /tmp/parlance-smoke
dotnet new console
```

Add a `NuGet.config` pointing to the `artifacts/` folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local" value="/path/to/parlance/artifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Add the package reference:

```bash
dotnet add package Parlance.CSharp.Analyzers --version 0.1.0
```

Write code with a known PARL0004 violation, open in IDE, verify squiggly underline appears.

**Step 4: Final commit (if any cleanup needed)**

```bash
git add -u
git commit -m "Phase 3 complete: NuGet analyzer packages ready for local testing"
```
