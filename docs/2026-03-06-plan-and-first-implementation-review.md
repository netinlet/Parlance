# Plan and First Implementation Review

Date: 2026-03-06
Branch reviewed: `development`
HEAD reviewed: `0a0bb47`

## Scope

Review of:

- `docs/plans/2026-03-04-phase1-core-engine-design.md`
- `docs/plans/2026-03-04-phase1-implementation.md`
- Current first-round implementation in `src/` and `tests/`

Focus:

- Whether the plan or implementation appears to "make up" Roslyn or C# behavior
- Whether analyzer behavior matches the documented intent
- Whether primary-source documentation supports the recommendations

## Verification Performed

- Read the design and implementation plan
- Reviewed the current analyzer and engine code in `src/`
- Reviewed the current tests in `tests/`
- Ran `dotnet test Parlance.sln`
  - Result: Passed, 31/31
- Verified risky language and tooling assumptions against Microsoft Learn primary sources

## Findings

### 1. PARL0002 reports `Array.Empty<T>()` as convertible to a collection expression, which is not generally valid

Severity: High

Files:

- `src/Parlance.CSharp.Analyzers/Rules/PARL0002_PreferCollectionExpressions.cs:76`
- `tests/Parlance.CSharp.Tests/Rules/PARL0002Tests.cs:54`

The analyzer reports every `Array.Empty<T>()` invocation as a collection-expression candidate, and the tests lock in:

```csharp
var arr = Array.Empty<int>();
```

as a positive diagnostic case.

That recommendation is not generally valid. Collection expressions are target-typed; they do not have a natural type. In practice, `var arr = [];` is not allowed, so the current analyzer is teaching a transformation that does not work in the form being implied.

Conclusion: this rule currently contains a made-up recommendation.

### 2. PARL0001 does not verify that assignments target members of the containing type

Severity: High

Files:

- `src/Parlance.CSharp.Analyzers/Rules/PARL0001_PreferPrimaryConstructors.cs:59`
- `src/Parlance.CSharp.Analyzers/Rules/PARL0001_PreferPrimaryConstructors.cs:97`

The implementation comment says the left-hand side must be a field or property of the containing type, but the actual code only checks symbol kind:

- `IFieldSymbol`
- `IPropertySymbol`

It does not verify ownership.

That means code such as:

```csharp
public C(string name)
{
    _other.Name = name;
}
```

can be treated as a primary-constructor candidate even though it is not equivalent to assigning constructor parameters into the current type's own state.

Conclusion: the analyzer overclaims convertibility and needs a containing-type check.

### 3. PARL0003 has the same ownership bug and an unsafe suggested fix

Severity: High

Files:

- `src/Parlance.CSharp.Analyzers/Rules/PARL0003_PreferRequiredProperties.cs:85`
- `src/Parlance.CSharp/DiagnosticEnricher.cs:25`

Like PARL0001, PARL0003 accepts any public settable property symbol and does not verify that the property belongs to the containing type.

In addition, the suggested fix text says:

- remove the constructor parameter
- add `required` to the property

That is not a behavior-preserving recommendation in the general case. C# `required` members change construction requirements for callers. Existing call sites using `new T(...)` may need to move to object initialization or rely on constructors marked with `SetsRequiredMembers`.

Conclusion: both the detection logic and the fix/rationale are too broad.

### 4. CompilationFactory does not implement the documented "net10 reference assemblies from the SDK" design

Severity: Medium

Files:

- `docs/plans/2026-03-04-phase1-core-engine-design.md:94`
- `docs/plans/2026-03-04-phase1-core-engine-design.md:110`
- `src/Parlance.CSharp/CompilationFactory.cs:22`

The plan says the engine compiles against .NET 10 reference assemblies resolved from the installed SDK.

The implementation instead reads `TRUSTED_PLATFORM_ASSEMBLIES` from the host runtime and builds references from those runtime assemblies, including `System.Private.CoreLib`.

This is materially different:

- it is host-runtime dependent
- it is not clearly tied to the intended target pack
- it uses implementation assemblies rather than the documented targeting-pack reference assemblies

Conclusion: the implementation does not match the architectural claim in the plan.

### 5. The plan says PARL0001-0003 are language-version-aware, but the engine does not model language version at all

Severity: Medium

Files:

- `docs/plans/2026-03-04-phase1-core-engine-design.md:131`
- `src/Parlance.Abstractions/AnalysisOptions.cs:3`
- `src/Parlance.CSharp/CSharpAnalysisEngine.cs:31`

The design says PARL0001-0003 should be suppressed for older C# versions.

However:

- `AnalysisOptions` has no language-version input
- `ParseText` is called without explicit parse options
- the analyzers do not inspect parse options or the effective language version

So the current system has no way to uphold the documented minimum-language-version behavior.

Conclusion: this portion of the plan is not implemented.

## Additional Notes

- The design already acknowledges that PARL0001 and PARL0003 can produce contradictory guidance for the same type.
- That conflict still exists in the current implementation because both analyzers are always loaded and no arbitration is performed.
- The current test suite passing does not reduce the concern here; some tests currently reinforce the incorrect assumptions.

### Follow-up guidance from the local Denace Roslyn articles

Reading the converted Denace series in `docs/denace.dev/` did not change the findings above, but it does reinforce a few practical next steps for this codebase:

- before creating a custom rule, check whether a built-in Roslyn/IDE analyzer already covers the scenario; custom diagnostics carry ongoing maintenance cost
- use `Microsoft.CodeAnalysis.Testing` as the main verification surface and keep adding negative/regression cases whenever a false positive or unsafe recommendation is found
- when code fixes are added later, treat them as stateless workspace components and plan for `FixAll`, cancellation, and `additionalLocations` up front instead of bolting them on afterward

## Overall Assessment

There are real issues here. The concerns about "made up" behavior are justified.

Most importantly:

- PARL0002 currently promotes at least one transformation that is not generally valid
- PARL0001 and PARL0003 both over-detect because they do not verify containing-type ownership
- the plan's claims about reference assemblies and language-version awareness are not actually implemented

## Sources

Primary references consulted:

- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-12.0/collection-expressions
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/required
- https://learn.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies
- https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors
- `docs/denace.dev/exploring-roslyn-dotnet-compiler-platform-sdk.md`
- `docs/denace.dev/getting-started-with-roslyn-analyzers.md`
- `docs/denace.dev/fixing-mistakes-with-roslyn-code-fixes.md`
- `docs/denace.dev/testing-roslyn-analyzers-and-code-fixes.md`
