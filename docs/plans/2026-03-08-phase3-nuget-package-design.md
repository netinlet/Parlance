# Phase 3: NuGet Analyzer Packages — Design

## Overview

Ship two NuGet packages that bring Parlance analyzers into the IDE (Visual Studio, Rider, VS Code). One package contains the custom PARL rules, the other bundles those with upstream analyzer dependencies and a curated .editorconfig profile.

## Packages

| Package ID | Purpose | Contents |
|---|---|---|
| `Parlance.CSharp.Analyzers` | Custom PARL rules for IDE integration | 8 analyzers + 2 code fixers |
| `Parlance.CSharp` | Batteries-included bundle | Depends on custom analyzers + upstream packages + curated profile |

## Package Structure

### Parlance.CSharp.Analyzers.nupkg

```
analyzers/
  dotnet/
    cs/
      Parlance.CSharp.Analyzers.dll
build/
  Parlance.CSharp.Analyzers.props
```

The `.props` file ensures the DLL is treated as an analyzer-only reference (not a compile-time lib dependency). The project already targets `netstandard2.0` for maximum IDE compatibility.

### Parlance.CSharp.nupkg

```
build/
  Parlance.CSharp.props
  Parlance.CSharp.targets
buildTransitive/
  Parlance.CSharp.props
  Parlance.CSharp.targets
content/
  parlance-default.editorconfig
```

**Dependencies (declared as PackageReference):**
- `Parlance.CSharp.Analyzers` (same version)
- `Microsoft.CodeAnalysis.NetAnalyzers`
- `Roslynator.Analyzers`

The `.props` file injects the curated .editorconfig profile settings. The `buildTransitive/` folder ensures settings apply even when the package is referenced transitively. The `content/` folder includes a reference `.editorconfig` that users can copy to their repo root.

## Shared Versioning

A `Directory.Build.props` at the repo root provides:

- `<Version>0.1.0</Version>`
- `<Authors>`, `<Company>`, `<RepositoryUrl>`, `<License>` — shared across all packages
- Individual projects opt in to packaging via their own `.csproj`

Starting at 0.1.0 (pre-release) since the ruleset may evolve before 1.0.

## New Project: Parlance.CSharp.Package

A new project (or `.nuspec`) is needed for the bundle package since the existing `Parlance.CSharp` project is the analysis engine library (net10.0) used by the CLI. The bundle NuGet package is a different artifact — it's a metapackage that references upstream analyzers and ships a .props file.

Options:
- **Rename existing project** — The current `Parlance.CSharp` becomes `Parlance.CSharp.Engine` and the package ID `Parlance.CSharp` is used for the bundle.
- **New project** — Create `src/Parlance.CSharp.Package/` as a packaging-only project that produces the bundle .nupkg.

Decision: **New project** — avoids churn in existing project references and test configurations.

## .props File Behavior

### Parlance.CSharp.Analyzers.props

Minimal — just marks the assembly as an analyzer:

```xml
<Project>
  <ItemGroup>
    <Analyzer Include="$(MSBuildThisFileDirectory)../analyzers/dotnet/cs/Parlance.CSharp.Analyzers.dll" />
  </ItemGroup>
</Project>
```

### Parlance.CSharp.props

Injects curated severity configuration for upstream rules. This is the "profile" — default severities for NetAnalyzers and Roslynator rules that align with the Parlance philosophy:

```xml
<Project>
  <PropertyGroup>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

The actual rule severity configuration ships as an .editorconfig in the content folder, since .editorconfig is the standard mechanism for configuring Roslyn analyzer severities and IDEs natively support it.

## Testing Strategy

### Automated Integration Test

A new test class `NuGetPackageIntegrationTests` that:

1. Runs `dotnet pack` on both package projects
2. Creates a temporary .NET project referencing each package from a local NuGet feed
3. Writes a C# source file containing known violations (e.g., `is` + cast → triggers PARL0004)
4. Runs `dotnet build` and captures MSBuild output
5. Asserts that expected PARL diagnostics appear in build warnings
6. For the bundle: asserts that upstream analyzer diagnostics also appear

### Manual Smoke Test

After automated tests pass, manually verify in one IDE:
- Add package reference to a real project
- Confirm squiggly underlines appear for PARL rules
- Confirm code fix suggestions appear for PARL0004 and PARL9001

## Out of Scope

- New analyzers (PARL0006+) — added incrementally in future releases
- Publishing to NuGet.org — local pack and test only for now
- Multi-IDE matrix testing — automated build verification + single IDE smoke test
- SARIF output format — deferred
