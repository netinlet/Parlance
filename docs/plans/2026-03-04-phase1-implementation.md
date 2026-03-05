# Phase 1: Core Engine + Custom Rules — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build the Parlance analysis engine so that given a C# source string, it returns structured diagnostics with rationale and an idiomatic score.

**Architecture:** Two projects — `Parlance.Abstractions` (language-agnostic interfaces/records) and `Parlance.CSharp` (Roslyn-based engine with 5 custom PARL rules). The engine parses source, builds an in-memory compilation against net10 reference assemblies, runs analyzers, enriches diagnostics with rationale, and computes a weighted score.

**Tech Stack:** .NET 10, Roslyn (`Microsoft.CodeAnalysis.CSharp`), xUnit, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`

**Design doc:** `docs/plans/2026-03-04-phase1-core-engine-design.md`

**Code style:** Seal everything by default. Use positional record syntax. Use `ImmutableDictionary` for computed-once data. Modern C# syntax throughout. YAGNI.

---

### Task 1: Solution and Project Scaffolding

**Files:**
- Create: `Parlance.sln`
- Create: `src/Parlance.Abstractions/Parlance.Abstractions.csproj`
- Create: `src/Parlance.CSharp/Parlance.CSharp.csproj`
- Create: `tests/Parlance.CSharp.Tests/Parlance.CSharp.Tests.csproj`
- Create: `.gitignore`

**Step 1: Create solution and projects**

```bash
cd /mnt/wsl/PHYSICALDRIVE0p1/doug/devroot/netinlet/parlance

# Create .gitignore
dotnet new gitignore

# Create solution
dotnet new sln -n Parlance

# Create projects
dotnet new classlib -n Parlance.Abstractions -o src/Parlance.Abstractions -f net10.0
dotnet new classlib -n Parlance.CSharp -o src/Parlance.CSharp -f net10.0
dotnet new xunit -n Parlance.CSharp.Tests -o tests/Parlance.CSharp.Tests -f net10.0

# Add projects to solution
dotnet sln add src/Parlance.Abstractions/Parlance.Abstractions.csproj
dotnet sln add src/Parlance.CSharp/Parlance.CSharp.csproj
dotnet sln add tests/Parlance.CSharp.Tests/Parlance.CSharp.Tests.csproj

# Add project references
dotnet add src/Parlance.CSharp/Parlance.CSharp.csproj reference src/Parlance.Abstractions/Parlance.Abstractions.csproj
dotnet add tests/Parlance.CSharp.Tests/Parlance.CSharp.Tests.csproj reference src/Parlance.CSharp/Parlance.CSharp.csproj
```

**Step 2: Add NuGet packages**

```bash
# Roslyn APIs for the engine
dotnet add src/Parlance.CSharp/Parlance.CSharp.csproj package Microsoft.CodeAnalysis.CSharp

# Roslyn analyzer testing for tests
dotnet add tests/Parlance.CSharp.Tests/Parlance.CSharp.Tests.csproj package Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit
```

**Step 3: Delete template placeholder files**

Delete `Class1.cs` from both `Parlance.Abstractions` and `Parlance.CSharp`. Delete `UnitTest1.cs` from `Parlance.CSharp.Tests`.

**Step 4: Verify it builds**

Run: `dotnet build Parlance.sln`
Expected: Build succeeded with 0 errors (warnings OK from empty projects)

**Step 5: Commit**

```bash
git add -A
git commit -m "Scaffold solution with Abstractions, CSharp engine, and test projects"
```

---

### Task 2: Parlance.Abstractions — Core Types

**Files:**
- Create: `src/Parlance.Abstractions/IAnalysisEngine.cs`
- Create: `src/Parlance.Abstractions/AnalysisOptions.cs`
- Create: `src/Parlance.Abstractions/AnalysisResult.cs`
- Create: `src/Parlance.Abstractions/Diagnostic.cs`
- Create: `src/Parlance.Abstractions/DiagnosticSeverity.cs`
- Create: `src/Parlance.Abstractions/Location.cs`
- Create: `src/Parlance.Abstractions/AnalysisSummary.cs`

**Step 1: Write all abstraction types**

`src/Parlance.Abstractions/IAnalysisEngine.cs`:
```csharp
namespace Parlance.Abstractions;

public interface IAnalysisEngine
{
    string Language { get; }

    Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default);
}
```

`src/Parlance.Abstractions/AnalysisOptions.cs`:
```csharp
namespace Parlance.Abstractions;

public sealed record AnalysisOptions(
    string[] SuppressRules,
    int? MaxDiagnostics = null,
    bool IncludeFixSuggestions = true)
{
    public AnalysisOptions() : this(SuppressRules: []) { }
}
```

`src/Parlance.Abstractions/AnalysisResult.cs`:
```csharp
namespace Parlance.Abstractions;

public sealed record AnalysisResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    AnalysisSummary Summary);
```

`src/Parlance.Abstractions/Diagnostic.cs`:
```csharp
namespace Parlance.Abstractions;

public sealed record Diagnostic(
    string RuleId,
    string Category,
    DiagnosticSeverity Severity,
    string Message,
    Location Location,
    string? Rationale = null,
    string? SuggestedFix = null);
```

`src/Parlance.Abstractions/DiagnosticSeverity.cs`:
```csharp
namespace Parlance.Abstractions;

public enum DiagnosticSeverity { Error, Warning, Suggestion, Silent }
```

`src/Parlance.Abstractions/Location.cs`:
```csharp
namespace Parlance.Abstractions;

public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn);
```

`src/Parlance.Abstractions/AnalysisSummary.cs`:
```csharp
using System.Collections.Immutable;

namespace Parlance.Abstractions;

public sealed record AnalysisSummary(
    int TotalDiagnostics,
    int Errors,
    int Warnings,
    int Suggestions,
    ImmutableDictionary<string, int> ByCategory,
    double IdiomaticScore);
```

**Step 2: Verify it builds**

Run: `dotnet build src/Parlance.Abstractions/Parlance.Abstractions.csproj`
Expected: Build succeeded, 0 errors

**Step 3: Commit**

```bash
git add src/Parlance.Abstractions/
git commit -m "Add Parlance.Abstractions core types"
```

---

### Task 3: IdiomaticScoreCalculator

TDD — tests first, then implementation.

**Files:**
- Create: `src/Parlance.CSharp/IdiomaticScoreCalculator.cs`
- Create: `tests/Parlance.CSharp.Tests/IdiomaticScoreCalculatorTests.cs`

**Step 1: Write the failing tests**

`tests/Parlance.CSharp.Tests/IdiomaticScoreCalculatorTests.cs`:
```csharp
using Parlance.Abstractions;

namespace Parlance.CSharp.Tests;

public sealed class IdiomaticScoreCalculatorTests
{
    private static readonly Location DummyLocation = new(1, 1, 1, 1);

    [Fact]
    public void NoDiagnostics_Returns100()
    {
        var result = IdiomaticScoreCalculator.Calculate([]);

        Assert.Equal(100, result.IdiomaticScore);
        Assert.Equal(0, result.TotalDiagnostics);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Warnings);
        Assert.Equal(0, result.Suggestions);
        Assert.True(result.ByCategory.IsEmpty);
    }

    [Fact]
    public void SingleError_Deducts10()
    {
        var diagnostics = new[]
        {
            new Diagnostic("PARL0001", "Modernization", DiagnosticSeverity.Error,
                "test", DummyLocation)
        };

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(90, result.IdiomaticScore);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.ByCategory["Modernization"]);
    }

    [Fact]
    public void SingleWarning_Deducts5()
    {
        var diagnostics = new[]
        {
            new Diagnostic("PARL0004", "PatternMatching", DiagnosticSeverity.Warning,
                "test", DummyLocation)
        };

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(95, result.IdiomaticScore);
        Assert.Equal(1, result.Warnings);
    }

    [Fact]
    public void SingleSuggestion_Deducts2()
    {
        var diagnostics = new[]
        {
            new Diagnostic("PARL0005", "PatternMatching", DiagnosticSeverity.Suggestion,
                "test", DummyLocation)
        };

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(98, result.IdiomaticScore);
        Assert.Equal(1, result.Suggestions);
    }

    [Fact]
    public void MixedSeverities_CorrectDeductions()
    {
        var diagnostics = new[]
        {
            new Diagnostic("PARL0001", "Modernization", DiagnosticSeverity.Error,
                "test", DummyLocation),
            new Diagnostic("PARL0004", "PatternMatching", DiagnosticSeverity.Warning,
                "test", DummyLocation),
            new Diagnostic("PARL0005", "PatternMatching", DiagnosticSeverity.Suggestion,
                "test", DummyLocation),
        };

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        // 100 - 10 - 5 - 2 = 83
        Assert.Equal(83, result.IdiomaticScore);
        Assert.Equal(3, result.TotalDiagnostics);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.Warnings);
        Assert.Equal(1, result.Suggestions);
        Assert.Equal(1, result.ByCategory["Modernization"]);
        Assert.Equal(2, result.ByCategory["PatternMatching"]);
    }

    [Fact]
    public void ScoreFloorsAtZero()
    {
        var diagnostics = Enumerable.Range(0, 20)
            .Select(i => new Diagnostic($"PARL{i:D4}", "Test", DiagnosticSeverity.Error,
                "test", DummyLocation))
            .ToArray();

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        // 20 errors * -10 = -200, but floor is 0
        Assert.Equal(0, result.IdiomaticScore);
    }

    [Fact]
    public void SilentSeverity_NoDeduction()
    {
        var diagnostics = new[]
        {
            new Diagnostic("PARL0001", "Modernization", DiagnosticSeverity.Silent,
                "test", DummyLocation)
        };

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(100, result.IdiomaticScore);
        Assert.Equal(1, result.TotalDiagnostics);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --no-restore`
Expected: FAIL — `IdiomaticScoreCalculator` does not exist

**Step 3: Write the implementation**

`src/Parlance.CSharp/IdiomaticScoreCalculator.cs`:
```csharp
using System.Collections.Immutable;
using Parlance.Abstractions;

namespace Parlance.CSharp;

internal static class IdiomaticScoreCalculator
{
    public static AnalysisSummary Calculate(IReadOnlyList<Diagnostic> diagnostics)
    {
        var errors = 0;
        var warnings = 0;
        var suggestions = 0;
        var byCategory = new Dictionary<string, int>();

        foreach (var d in diagnostics)
        {
            switch (d.Severity)
            {
                case DiagnosticSeverity.Error:
                    errors++;
                    break;
                case DiagnosticSeverity.Warning:
                    warnings++;
                    break;
                case DiagnosticSeverity.Suggestion:
                    suggestions++;
                    break;
            }

            if (!byCategory.TryAdd(d.Category, 1))
                byCategory[d.Category]++;
        }

        var deduction = errors * 10 + warnings * 5 + suggestions * 2;
        var score = Math.Max(0, 100 - deduction);

        return new AnalysisSummary(
            TotalDiagnostics: diagnostics.Count,
            Errors: errors,
            Warnings: warnings,
            Suggestions: suggestions,
            ByCategory: byCategory.ToImmutableDictionary(),
            IdiomaticScore: score);
    }
}
```

Note: `IdiomaticScoreCalculator` is `internal` but the test project needs to see it. Add `InternalsVisibleTo` to `Parlance.CSharp.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Parlance.CSharp.Tests" />
</ItemGroup>
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests/ -v minimal`
Expected: All 7 tests pass

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/ tests/Parlance.CSharp.Tests/
git commit -m "Add IdiomaticScoreCalculator with tests"
```

---

### Task 4: CompilationFactory

**Files:**
- Create: `src/Parlance.CSharp/CompilationFactory.cs`

No dedicated unit tests — this is infrastructure that gets tested through the integration tests in Task 8. The factory builds a `CSharpCompilation` from a source string using net10 reference assemblies resolved from the installed SDK.

**Step 1: Write the implementation**

`src/Parlance.CSharp/CompilationFactory.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Parlance.CSharp;

internal static class CompilationFactory
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static CSharpCompilation Create(SyntaxTree tree)
    {
        return CSharpCompilation.Create(
            assemblyName: "ParlanceAnalysis",
            syntaxTrees: [tree],
            references: References.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        // Resolve the reference assemblies directory from the runtime
        var assemblyDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var referenceNames = new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Collections.Immutable.dll",
            "System.Linq.dll",
            "System.Console.dll",
            "System.Threading.dll",
            "System.Threading.Tasks.dll",
            "System.ComponentModel.dll",
            "System.ObjectModel.dll",
            "System.Private.CoreLib.dll",
            "netstandard.dll",
        };

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var name in referenceNames)
        {
            var path = Path.Combine(assemblyDir, name);
            if (File.Exists(path))
                builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/Parlance.CSharp/Parlance.CSharp.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Parlance.CSharp/CompilationFactory.cs
git commit -m "Add CompilationFactory for in-memory compilation against net10 refs"
```

---

### Task 5: DiagnosticEnricher

**Files:**
- Create: `src/Parlance.CSharp/DiagnosticEnricher.cs`

No dedicated unit tests — tested through integration tests in Task 8. This maps Roslyn `Microsoft.CodeAnalysis.Diagnostic` instances to `Parlance.Abstractions.Diagnostic` records with rationale and suggested fix text.

**Step 1: Write the implementation**

`src/Parlance.CSharp/DiagnosticEnricher.cs`:
```csharp
using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using ParlanceDiagnostic = Parlance.Abstractions.Diagnostic;
using ParlanceSeverity = Parlance.Abstractions.DiagnosticSeverity;
using ParlanceLocation = Parlance.Abstractions.Location;

namespace Parlance.CSharp;

internal static class DiagnosticEnricher
{
    private static readonly FrozenDictionary<string, RuleMetadata> Metadata =
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
                "Remove the constructor parameter and add the 'required' modifier to the property."),
            ["PARL0004"] = new(
                "PatternMatching",
                "Pattern matching with 'is' (C# 7+) combines type checking and variable declaration in one expression. It is more concise than separate 'is' check followed by a cast, avoids the double type-check, and is the idiomatic modern C# approach.",
                "Use 'if (obj is Type name)' instead of separate is-check and cast."),
            ["PARL0005"] = new(
                "PatternMatching",
                "Switch expressions (C# 8+) are more concise than switch statements when every branch returns a value. They enforce exhaustiveness and make the data-flow intent clearer.",
                "Convert the switch statement to a switch expression."),
        }.ToFrozenDictionary();

    public static IReadOnlyList<ParlanceDiagnostic> Enrich(
        IReadOnlyList<RoslynDiagnostic> diagnostics)
    {
        var result = new List<ParlanceDiagnostic>(diagnostics.Count);

        foreach (var d in diagnostics)
        {
            var lineSpan = d.Location.GetLineSpan();
            var start = lineSpan.StartLinePosition;
            var end = lineSpan.EndLinePosition;

            var location = new ParlanceLocation(
                Line: start.Line + 1,       // Roslyn is 0-based, we use 1-based
                Column: start.Character + 1,
                EndLine: end.Line + 1,
                EndColumn: end.Character + 1);

            var severity = d.Severity switch
            {
                DiagnosticSeverity.Error => ParlanceSeverity.Error,
                DiagnosticSeverity.Warning => ParlanceSeverity.Warning,
                DiagnosticSeverity.Info => ParlanceSeverity.Suggestion,
                DiagnosticSeverity.Hidden => ParlanceSeverity.Silent,
                _ => ParlanceSeverity.Silent,
            };

            var hasMetadata = Metadata.TryGetValue(d.Id, out var meta);

            result.Add(new ParlanceDiagnostic(
                RuleId: d.Id,
                Category: hasMetadata ? meta.Category : d.Descriptor.Category,
                Severity: severity,
                Message: d.GetMessage(),
                Location: location,
                Rationale: hasMetadata ? meta.Rationale : null,
                SuggestedFix: hasMetadata ? meta.SuggestedFix : null));
        }

        return result;
    }

    private sealed record RuleMetadata(
        string Category,
        string Rationale,
        string SuggestedFix);
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/Parlance.CSharp/Parlance.CSharp.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Parlance.CSharp/DiagnosticEnricher.cs
git commit -m "Add DiagnosticEnricher to map Roslyn diagnostics to Parlance model"
```

---

### Task 6: PARL0004 — Use Pattern Matching Over Is+Cast

Starting with PARL0004 because it is the simplest analyzer (pure syntax analysis, no language-version gating) and will validate the full pipeline before tackling the more complex modernization rules.

**Files:**
- Create: `src/Parlance.CSharp/Rules/PARL0004_UsePatternMatchingOverIsCast.cs`
- Create: `tests/Parlance.CSharp.Tests/Rules/PARL0004Tests.cs`

**Step 1: Write the failing test**

`tests/Parlance.CSharp.Tests/Rules/PARL0004Tests.cs`:
```csharp
using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Rules.PARL0004_UsePatternMatchingOverIsCast,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0004Tests
{
    [Fact]
    public async Task Flags_IsThenCast()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    if ({|#0:obj is string|})
                    {
                        var s = (string)obj;
                    }
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0004")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_WhenAlreadyUsingPatternMatching()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    if (obj is string s)
                    {
                        System.Console.WriteLine(s);
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_IsCheckWithoutCast()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    if (obj is string)
                    {
                        System.Console.WriteLine("it's a string");
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0004" --no-restore`
Expected: FAIL — `PARL0004_UsePatternMatchingOverIsCast` does not exist

**Step 3: Write the analyzer**

`src/Parlance.CSharp/Rules/PARL0004_UsePatternMatchingOverIsCast.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0004_UsePatternMatchingOverIsCast : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0004";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use pattern matching instead of 'is' followed by cast",
        messageFormat: "Use pattern matching 'is {0} name' instead of 'is' check followed by cast",
        category: "PatternMatching",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Pattern matching combines type checking and variable declaration in one expression, avoiding the redundant cast.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // Look for: if (expr is TypeName)
        if (ifStatement.Condition is not BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.IsExpression
            } isExpression)
        {
            return;
        }

        // Get the expression being checked and the type
        var checkedExpr = isExpression.Left;
        var targetType = isExpression.Right as TypeSyntax;
        if (targetType is null) return;

        // Look for a cast to the same type in the if-body
        var body = ifStatement.Statement;
        if (!ContainsCastOf(body, checkedExpr, targetType, context.SemanticModel))
            return;

        context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            Rule,
            isExpression.GetLocation(),
            targetType.ToString()));
    }

    private static bool ContainsCastOf(
        SyntaxNode body,
        ExpressionSyntax checkedExpr,
        TypeSyntax targetType,
        SemanticModel model)
    {
        var checkedSymbol = model.GetSymbolInfo(checkedExpr).Symbol;
        var targetTypeSymbol = model.GetTypeInfo(targetType).Type;

        if (checkedSymbol is null || targetTypeSymbol is null)
            return false;

        foreach (var cast in body.DescendantNodes().OfType<CastExpressionSyntax>())
        {
            var castTypeSymbol = model.GetTypeInfo(cast.Type).Type;
            var castExprSymbol = model.GetSymbolInfo(cast.Expression).Symbol;

            if (SymbolEqualityComparer.Default.Equals(castTypeSymbol, targetTypeSymbol) &&
                SymbolEqualityComparer.Default.Equals(castExprSymbol, checkedSymbol))
            {
                return true;
            }
        }

        return false;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0004" -v minimal`
Expected: All 3 tests pass

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/Rules/ tests/Parlance.CSharp.Tests/Rules/
git commit -m "Add PARL0004: Use pattern matching over is+cast"
```

---

### Task 7: PARL0005 — Use Switch Expression

**Files:**
- Create: `src/Parlance.CSharp/Rules/PARL0005_UseSwitchExpression.cs`
- Create: `tests/Parlance.CSharp.Tests/Rules/PARL0005Tests.cs`

**Step 1: Write the failing test**

`tests/Parlance.CSharp.Tests/Rules/PARL0005Tests.cs`:
```csharp
using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Rules.PARL0005_UseSwitchExpression,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0005Tests
{
    [Fact]
    public async Task Flags_SwitchStatementThatReturnsFromEveryBranch()
    {
        var source = """
            class C
            {
                string M(int x)
                {
                    {|#0:switch (x)
                    {
                        case 1:
                            return "one";
                        case 2:
                            return "two";
                        default:
                            return "other";
                    }|}
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0005")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_SwitchWithSideEffects()
    {
        var source = """
            class C
            {
                void M(int x)
                {
                    switch (x)
                    {
                        case 1:
                            System.Console.WriteLine("one");
                            break;
                        case 2:
                            System.Console.WriteLine("two");
                            break;
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_SwitchMissingDefaultReturn()
    {
        var source = """
            class C
            {
                string M(int x)
                {
                    switch (x)
                    {
                        case 1:
                            return "one";
                        case 2:
                            return "two";
                    }
                    return "fallback";
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0005" --no-restore`
Expected: FAIL — `PARL0005_UseSwitchExpression` does not exist

**Step 3: Write the analyzer**

`src/Parlance.CSharp/Rules/PARL0005_UseSwitchExpression.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0005_UseSwitchExpression : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0005";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use switch expression instead of switch statement",
        messageFormat: "This switch statement can be converted to a switch expression",
        category: "PatternMatching",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Switch expressions are more concise than switch statements when every branch returns a value. They enforce exhaustiveness and make data-flow intent clearer.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeSwitchStatement, SyntaxKind.SwitchStatement);
    }

    private static void AnalyzeSwitchStatement(SyntaxNodeAnalysisContext context)
    {
        var switchStatement = (SwitchStatementSyntax)context.Node;

        if (switchStatement.Sections.Count == 0)
            return;

        var hasDefault = false;

        foreach (var section in switchStatement.Sections)
        {
            if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
                hasDefault = true;

            if (!SectionOnlyReturns(section))
                return;
        }

        // Only flag if there's a default — otherwise it's not exhaustive
        if (!hasDefault)
            return;

        context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            Rule,
            switchStatement.GetLocation()));
    }

    private static bool SectionOnlyReturns(SwitchSectionSyntax section)
    {
        // A convertible section has exactly one meaningful statement: a return
        var statements = section.Statements;

        // Filter out empty statements
        var meaningful = statements
            .Where(s => s is not EmptyStatementSyntax)
            .ToList();

        return meaningful.Count == 1 && meaningful[0] is ReturnStatementSyntax;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0005" -v minimal`
Expected: All 3 tests pass

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/Rules/PARL0005_UseSwitchExpression.cs tests/Parlance.CSharp.Tests/Rules/PARL0005Tests.cs
git commit -m "Add PARL0005: Use switch expression over switch statement"
```

---

### Task 8: PARL0001 — Prefer Primary Constructors

**Files:**
- Create: `src/Parlance.CSharp/Rules/PARL0001_PreferPrimaryConstructors.cs`
- Create: `tests/Parlance.CSharp.Tests/Rules/PARL0001Tests.cs`

**Step 1: Write the failing test**

`tests/Parlance.CSharp.Tests/Rules/PARL0001Tests.cs`:
```csharp
using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Rules.PARL0001_PreferPrimaryConstructors,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0001Tests
{
    [Fact]
    public async Task Flags_ConstructorThatOnlyAssignsFields()
    {
        var source = """
            class {|#0:C|}
            {
                private readonly string _name;
                private readonly int _age;

                public C(string name, int age)
                {
                    _name = name;
                    _age = age;
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_ConstructorWithLogic()
    {
        var source = """
            class C
            {
                private readonly string _name;

                public C(string name)
                {
                    if (name is null) throw new System.ArgumentNullException(nameof(name));
                    _name = name;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyUsesPrimaryConstructor()
    {
        var source = """
            class C(string name, int age)
            {
                public string Name => name;
                public int Age => age;
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_MultipleConstructors()
    {
        var source = """
            class C
            {
                private readonly string _name;

                public C(string name) { _name = name; }
                public C() { _name = "default"; }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0001" --no-restore`
Expected: FAIL — class does not exist

**Step 3: Write the analyzer**

`src/Parlance.CSharp/Rules/PARL0001_PreferPrimaryConstructors.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0001_PreferPrimaryConstructors : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer primary constructor",
        messageFormat: "Type '{0}' can use a primary constructor",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Primary constructors (C# 12+) combine type declaration and constructor into a single concise form when the constructor only assigns parameters to fields or properties.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        // Skip if already using primary constructor
        if (typeDecl.ParameterList is not null)
            return;

        // Get all constructors
        var constructors = typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        // Must have exactly one constructor
        if (constructors.Count != 1)
            return;

        var ctor = constructors[0];

        // Must have parameters
        if (ctor.ParameterList.Parameters.Count == 0)
            return;

        // Must have a body (not expression-bodied for simplicity)
        if (ctor.Body is null)
            return;

        // Every statement must be a simple assignment: _field = param or this.Prop = param
        foreach (var statement in ctor.Body.Statements)
        {
            if (!IsSimpleAssignment(statement, ctor.ParameterList, context.SemanticModel))
                return;
        }

        // Number of assignments must match number of parameters
        if (ctor.Body.Statements.Count != ctor.ParameterList.Parameters.Count)
            return;

        context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            Rule,
            typeDecl.Identifier.GetLocation(),
            typeDecl.Identifier.Text));
    }

    private static bool IsSimpleAssignment(
        StatementSyntax statement,
        ParameterListSyntax parameters,
        SemanticModel model)
    {
        // Must be: something = something;
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                } assignment
            })
        {
            return false;
        }

        // RHS must be a parameter reference
        if (assignment.Right is not IdentifierNameSyntax rhsIdentifier)
            return false;

        var rhsSymbol = model.GetSymbolInfo(rhsIdentifier).Symbol;
        if (rhsSymbol is not IParameterSymbol)
            return false;

        // LHS must be a field or property of the containing type
        var lhsSymbol = model.GetSymbolInfo(assignment.Left).Symbol;
        return lhsSymbol is IFieldSymbol or IPropertySymbol;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0001" -v minimal`
Expected: All 4 tests pass

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/Rules/PARL0001_PreferPrimaryConstructors.cs tests/Parlance.CSharp.Tests/Rules/PARL0001Tests.cs
git commit -m "Add PARL0001: Prefer primary constructors"
```

---

### Task 9: PARL0002 — Prefer Collection Expressions

**Files:**
- Create: `src/Parlance.CSharp/Rules/PARL0002_PreferCollectionExpressions.cs`
- Create: `tests/Parlance.CSharp.Tests/Rules/PARL0002Tests.cs`

**Step 1: Write the failing test**

`tests/Parlance.CSharp.Tests/Rules/PARL0002Tests.cs`:
```csharp
using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Rules.PARL0002_PreferCollectionExpressions,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0002Tests
{
    [Fact]
    public async Task Flags_NewListWithInitializer()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    var list = {|#0:new List<int> { 1, 2, 3 }|};
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_NewArrayWithInitializer()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = {|#0:new int[] { 1, 2, 3 }|};
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_ArrayEmpty()
    {
        var source = """
            using System;
            class C
            {
                void M()
                {
                    var arr = {|#0:Array.Empty<int>()|};
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_NewListWithoutInitializer()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    var list = new List<int>();
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0002" --no-restore`
Expected: FAIL

**Step 3: Write the analyzer**

`src/Parlance.CSharp/Rules/PARL0002_PreferCollectionExpressions.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0002_PreferCollectionExpressions : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer collection expression",
        messageFormat: "Use a collection expression instead of '{0}'",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Collection expressions (C# 12+) provide a unified, concise syntax for creating collections and let the compiler choose the optimal type.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        // Flag: new List<T> { ... } or new HashSet<T> { ... } etc. with initializer
        if (creation.Initializer is null)
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return;

        // Check if it implements IEnumerable (collection type)
        if (!ImplementsIEnumerable(namedType))
            return;

        context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            Rule,
            creation.GetLocation(),
            creation.Type.ToString()));
    }

    private static void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ArrayCreationExpressionSyntax)context.Node;

        // Flag: new int[] { 1, 2, 3 }
        if (creation.Initializer is null)
            return;

        context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            Rule,
            creation.GetLocation(),
            creation.Type.ToString()));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Flag: Array.Empty<T>()
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
        if (symbol is not IMethodSymbol method)
            return;

        if (method.Name == "Empty" &&
            method.ContainingType.SpecialType == SpecialType.System_Array &&
            method.IsGenericMethod)
        {
            context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                "Array.Empty<T>()"));
        }
    }

    private static bool ImplementsIEnumerable(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0002" -v minimal`
Expected: All 4 tests pass

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/Rules/PARL0002_PreferCollectionExpressions.cs tests/Parlance.CSharp.Tests/Rules/PARL0002Tests.cs
git commit -m "Add PARL0002: Prefer collection expressions"
```

---

### Task 10: PARL0003 — Prefer Required Properties

**Files:**
- Create: `src/Parlance.CSharp/Rules/PARL0003_PreferRequiredProperties.cs`
- Create: `tests/Parlance.CSharp.Tests/Rules/PARL0003Tests.cs`

**Step 1: Write the failing test**

`tests/Parlance.CSharp.Tests/Rules/PARL0003Tests.cs`:
```csharp
using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Rules.PARL0003_PreferRequiredProperties,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0003Tests
{
    [Fact]
    public async Task Flags_ConstructorAssigningToPublicSetProperties()
    {
        var source = """
            class {|#0:C|}
            {
                public string Name { get; set; }
                public int Age { get; set; }

                public C(string name, int age)
                {
                    Name = name;
                    Age = age;
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_ConstructorWithLogicBeyondAssignment()
    {
        var source = """
            class C
            {
                public string Name { get; set; }

                public C(string name)
                {
                    Name = name ?? throw new System.ArgumentNullException(nameof(name));
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyRequired()
    {
        var source = """
            class C
            {
                public required string Name { get; set; }
                public required int Age { get; set; }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_PrivateSetters()
    {
        var source = """
            class C
            {
                public string Name { get; private set; }

                public C(string name)
                {
                    Name = name;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0003" --no-restore`
Expected: FAIL

**Step 3: Write the analyzer**

`src/Parlance.CSharp/Rules/PARL0003_PreferRequiredProperties.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0003_PreferRequiredProperties : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer required properties",
        messageFormat: "Type '{0}' can use required properties instead of constructor initialization",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The 'required' modifier (C# 11+) enforces that callers set a property during initialization. This is clearer than constructor-only initialization for simple DTOs.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        var constructors = typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        if (constructors.Count != 1)
            return;

        var ctor = constructors[0];
        if (ctor.ParameterList.Parameters.Count == 0 || ctor.Body is null)
            return;

        // Every statement must be a simple assignment to a public settable property
        if (ctor.Body.Statements.Count != ctor.ParameterList.Parameters.Count)
            return;

        foreach (var statement in ctor.Body.Statements)
        {
            if (!IsAssignmentToPublicSettableProperty(statement, context.SemanticModel))
                return;
        }

        context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
            Rule,
            typeDecl.Identifier.GetLocation(),
            typeDecl.Identifier.Text));
    }

    private static bool IsAssignmentToPublicSettableProperty(
        StatementSyntax statement,
        SemanticModel model)
    {
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                } assignment
            })
        {
            return false;
        }

        // RHS must be a simple identifier (parameter reference)
        if (assignment.Right is not IdentifierNameSyntax rhsId)
            return false;

        var rhsSymbol = model.GetSymbolInfo(rhsId).Symbol;
        if (rhsSymbol is not IParameterSymbol)
            return false;

        // LHS must be a public property with a public setter
        var lhsSymbol = model.GetSymbolInfo(assignment.Left).Symbol;
        if (lhsSymbol is not IPropertySymbol property)
            return false;

        if (property.DeclaredAccessibility != Accessibility.Public)
            return false;

        if (property.SetMethod is null)
            return false;

        if (property.SetMethod.DeclaredAccessibility != Accessibility.Public)
            return false;

        return true;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests/ --filter "PARL0003" -v minimal`
Expected: All 4 tests pass

**Step 5: Commit**

```bash
git add src/Parlance.CSharp/Rules/PARL0003_PreferRequiredProperties.cs tests/Parlance.CSharp.Tests/Rules/PARL0003Tests.cs
git commit -m "Add PARL0003: Prefer required properties"
```

---

### Task 11: CSharpAnalysisEngine

Wires everything together: parse → compile → analyze → enrich → score.

**Files:**
- Create: `src/Parlance.CSharp/CSharpAnalysisEngine.cs`

**Step 1: Write the implementation**

`src/Parlance.CSharp/CSharpAnalysisEngine.cs`:
```csharp
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Abstractions;
using Parlance.CSharp.Rules;

namespace Parlance.CSharp;

public sealed class CSharpAnalysisEngine : IAnalysisEngine
{
    public string Language { get; } = "csharp";

    private static readonly DiagnosticAnalyzer[] Analyzers =
    [
        new PARL0001_PreferPrimaryConstructors(),
        new PARL0002_PreferCollectionExpressions(),
        new PARL0003_PreferRequiredProperties(),
        new PARL0004_UsePatternMatchingOverIsCast(),
        new PARL0005_UseSwitchExpression(),
    ];

    public async Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AnalysisOptions();

        var tree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: ct);
        var compilation = CompilationFactory.Create(tree);

        var analyzersToRun = options.SuppressRules.Length > 0
            ? Analyzers.Where(a => !a.SupportedDiagnostics.Any(d => options.SuppressRules.Contains(d.Id))).ToArray()
            : Analyzers;

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzersToRun.ToImmutableArray(),
            cancellationToken: ct);

        var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        // Filter out suppressed rules (belt and suspenders)
        var filtered = roslynDiagnostics
            .Where(d => !options.SuppressRules.Contains(d.Id))
            .ToList();

        var enriched = DiagnosticEnricher.Enrich(filtered);

        // Apply MaxDiagnostics cap
        if (options.MaxDiagnostics.HasValue && enriched.Count > options.MaxDiagnostics.Value)
            enriched = enriched.Take(options.MaxDiagnostics.Value).ToList();

        var summary = IdiomaticScoreCalculator.Calculate(enriched);

        return new AnalysisResult(enriched, summary);
    }
}
```

Note: needs `using System.Collections.Immutable;` for `ToImmutableArray()`. The `Analyzers` array uses `ToImmutableArray()` extension which requires the import.

**Step 2: Verify it builds**

Run: `dotnet build src/Parlance.CSharp/Parlance.CSharp.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Parlance.CSharp/CSharpAnalysisEngine.cs
git commit -m "Add CSharpAnalysisEngine wiring parse-compile-analyze-enrich-score pipeline"
```

---

### Task 12: Integration Tests

End-to-end tests that exercise the full `CSharpAnalysisEngine` pipeline.

**Files:**
- Create: `tests/Parlance.CSharp.Tests/CSharpAnalysisEngineTests.cs`

**Step 1: Write the tests**

`tests/Parlance.CSharp.Tests/CSharpAnalysisEngineTests.cs`:
```csharp
using Parlance.Abstractions;

namespace Parlance.CSharp.Tests;

public sealed class CSharpAnalysisEngineTests
{
    private readonly CSharpAnalysisEngine _engine = new();

    [Fact]
    public async Task CleanCode_Returns100()
    {
        var source = """
            class C
            {
                public void M()
                {
                    System.Console.WriteLine("hello");
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        Assert.Equal(100, result.Summary.IdiomaticScore);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task IsCastPattern_ReturnsDiagnosticWithRationale()
    {
        var source = """
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
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal("PARL0004", diag.RuleId);
        Assert.Equal("PatternMatching", diag.Category);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.NotNull(diag.Rationale);
        Assert.NotNull(diag.SuggestedFix);
        Assert.True(diag.Location.Line > 0);
    }

    [Fact]
    public async Task MultipleIssues_CorrectScoreAndCategories()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M(object obj)
                {
                    var list = new List<int> { 1, 2, 3 };
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        Assert.True(result.Diagnostics.Count >= 2);
        Assert.True(result.Summary.IdiomaticScore < 100);
        Assert.True(result.Summary.ByCategory.ContainsKey("PatternMatching"));
        Assert.True(result.Summary.ByCategory.ContainsKey("Modernization"));
    }

    [Fact]
    public async Task SuppressRules_FiltersOut()
    {
        var source = """
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
            """;

        var options = new AnalysisOptions(SuppressRules: ["PARL0004"]);
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARL0004");
    }

    [Fact]
    public async Task MaxDiagnostics_CapsOutput()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M(object obj)
                {
                    var a = new List<int> { 1 };
                    var b = new List<int> { 2 };
                    var c = new List<int> { 3 };
                    var d = new List<int> { 4 };
                    var e = new List<int> { 5 };
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: [], MaxDiagnostics: 2);
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.True(result.Diagnostics.Count <= 2);
    }

    [Fact]
    public async Task Language_IsCSharp()
    {
        Assert.Equal("csharp", _engine.Language);
    }
}
```

**Step 2: Run all tests**

Run: `dotnet test Parlance.sln -v minimal`
Expected: All tests pass (score calculator tests + per-rule tests + integration tests)

**Step 3: Commit**

```bash
git add tests/Parlance.CSharp.Tests/CSharpAnalysisEngineTests.cs
git commit -m "Add integration tests for CSharpAnalysisEngine"
```

---

### Task 13: Final Verification

**Step 1: Clean build from scratch**

```bash
dotnet clean Parlance.sln
dotnet build Parlance.sln
```

Expected: Build succeeded, 0 errors

**Step 2: Run full test suite**

```bash
dotnet test Parlance.sln -v normal
```

Expected: All tests pass

**Step 3: Final commit (if any formatting/cleanup needed)**

Only commit if changes were needed. Otherwise, Phase 1 is complete.
