# Phase 2: CLI Tool Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `parlance`, a dotnet global tool that analyzes C# files and applies auto-fixes via a Roslyn workspace pipeline.

**Architecture:** The CLI uses `AdhocWorkspace` for both analysis and code fixes. Code fix providers live in `Parlance.CSharp.Analyzers` (netstandard2.0). The CLI project (`Parlance.Cli`, net10.0) consumes both the analyzers and workspace APIs. Output is formatted via `IOutputFormatter` with text and JSON implementations.

**Tech Stack:** System.CommandLine, Microsoft.CodeAnalysis.CSharp.Workspaces, xUnit

**Design doc:** `docs/plans/2026-03-06-phase2-cli-design.md`
**Analyzer dev guide:** `docs/analyzer-development-guide.md`

---

### Task 1: Scaffold Parlance.Cli Project

**Files:**
- Create: `src/Parlance.Cli/Parlance.Cli.csproj`
- Create: `src/Parlance.Cli/Program.cs`
- Modify: `Parlance.sln`

**Step 1: Create the project directory**

Run: `mkdir -p src/Parlance.Cli`

**Step 2: Create the csproj**

Create `src/Parlance.Cli/Parlance.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>parlance</ToolCommandName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.3" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.0.0" />
    <PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Parlance.Abstractions\Parlance.Abstractions.csproj" />
    <ProjectReference Include="..\Parlance.CSharp\Parlance.CSharp.csproj" />
    <ProjectReference Include="..\Parlance.CSharp.Analyzers\Parlance.CSharp.Analyzers.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Create minimal Program.cs**

Create `src/Parlance.Cli/Program.cs`:

```csharp
using System.CommandLine;

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
return await rootCommand.Parse(args).InvokeAsync();
```

**Step 4: Add to solution**

Run: `dotnet sln Parlance.sln add src/Parlance.Cli/Parlance.Cli.csproj --solution-folder src`

**Step 5: Verify it builds**

Run: `dotnet build`
Expected: Build succeeds with no errors.

**Step 6: Verify the tool runs**

Run: `dotnet run --project src/Parlance.Cli -- --help`
Expected: Shows help text with "Parlance" description.

**Step 7: Commit**

```bash
git add src/Parlance.Cli/ Parlance.sln
git commit -m "Scaffold Parlance.Cli dotnet global tool project"
```

---

### Task 2: Path Resolver

Resolves input arguments (files, directories, globs) into a list of `.cs` file paths.

**Files:**
- Create: `src/Parlance.Cli/PathResolver.cs`
- Test: `tests/Parlance.Cli.Tests/PathResolverTests.cs`
- Create: `tests/Parlance.Cli.Tests/Parlance.Cli.Tests.csproj`

**Step 1: Create the test project**

Create `tests/Parlance.Cli.Tests/Parlance.Cli.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Parlance.Cli\Parlance.Cli.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln Parlance.sln add tests/Parlance.Cli.Tests/Parlance.Cli.Tests.csproj --solution-folder tests`

**Step 2: Write failing tests**

Create `tests/Parlance.Cli.Tests/PathResolverTests.cs`:

```csharp
namespace Parlance.Cli.Tests;

public sealed class PathResolverTests : IDisposable
{
    private readonly string _tempDir;

    public PathResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Resolves_SingleFile()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C {}");

        var result = PathResolver.Resolve([file]);

        Assert.Single(result);
        Assert.Equal(file, result[0]);
    }

    [Fact]
    public void Resolves_Directory_RecursivelyFinds_CsFiles()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_tempDir, "A.cs"), "");
        File.WriteAllText(Path.Combine(sub, "B.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "");

        var result = PathResolver.Resolve([_tempDir]);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.EndsWith(".cs", f));
    }

    [Fact]
    public void Resolves_GlobPattern()
    {
        var sub = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "A.cs"), "");
        File.WriteAllText(Path.Combine(sub, "B.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "C.cs"), "");

        var pattern = Path.Combine(_tempDir, "src", "*.cs");
        var result = PathResolver.Resolve([pattern]);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Contains("src", f));
    }

    [Fact]
    public void Resolves_MultipleInputs()
    {
        var file = Path.Combine(_tempDir, "A.cs");
        var sub = Path.Combine(_tempDir, "dir");
        Directory.CreateDirectory(sub);
        File.WriteAllText(file, "");
        File.WriteAllText(Path.Combine(sub, "B.cs"), "");

        var result = PathResolver.Resolve([file, sub]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Returns_Empty_ForNoMatches()
    {
        var result = PathResolver.Resolve([Path.Combine(_tempDir, "*.cs")]);

        Assert.Empty(result);
    }

    [Fact]
    public void Deduplicates_Files()
    {
        var file = Path.Combine(_tempDir, "A.cs");
        File.WriteAllText(file, "");

        var result = PathResolver.Resolve([file, file, _tempDir]);

        Assert.Single(result);
    }
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Cli.Tests --verbosity quiet`
Expected: FAIL — `PathResolver` does not exist.

**Step 4: Implement PathResolver**

Create `src/Parlance.Cli/PathResolver.cs`:

```csharp
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Parlance.Cli;

internal static class PathResolver
{
    public static IReadOnlyList<string> Resolve(string[] inputs)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                files.Add(Path.GetFullPath(input));
                continue;
            }

            if (Directory.Exists(input))
            {
                foreach (var cs in Directory.EnumerateFiles(input, "*.cs", SearchOption.AllDirectories))
                    files.Add(Path.GetFullPath(cs));
                continue;
            }

            // Treat as glob pattern
            var directory = Path.GetDirectoryName(input);
            var pattern = Path.GetFileName(input);

            if (string.IsNullOrEmpty(directory))
                directory = Directory.GetCurrentDirectory();

            if (!Directory.Exists(directory))
                continue;

            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(directory)));
            foreach (var match in result.Files)
                files.Add(Path.GetFullPath(Path.Combine(directory, match.Path)));
        }

        return [.. files.Order(StringComparer.OrdinalIgnoreCase)];
    }
}
```

`Microsoft.Extensions.FileSystemGlobbing` (version 10.0.3) is already in `Parlance.Cli.csproj` from Task 1.

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Cli.Tests --verbosity quiet`
Expected: All 6 tests pass.

**Step 6: Commit**

```bash
git add src/Parlance.Cli/PathResolver.cs tests/Parlance.Cli.Tests/
git commit -m "Add PathResolver for file, directory, and glob input resolution"
```

---

### Task 3: Code Fix Provider for PARL9001 (Using Declarations)

This is the simpler of the two fixes. The transformation is mechanical: remove parentheses and braces from `using (var x = y) { body }`, producing `using var x = y;` followed by the body statements.

**Reference:** `docs/analyzer-development-guide.md` — sections on code-fix guidelines, trivia preservation, batch behavior.

**Files:**
- Modify: `src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj` (add Workspaces dependency)
- Create: `src/Parlance.CSharp.Analyzers/Fixes/PARL9001_UseSimpleUsingDeclarationFix.cs`
- Create: `tests/Parlance.CSharp.Tests/Fixes/PARL9001FixTests.cs`

**Step 1: Add Workspaces dependency to analyzer project**

Modify `src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj` to add:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.12.0" PrivateAssets="all" />
```

This must match the existing `Microsoft.CodeAnalysis.CSharp` version (4.12.0). Verify the project still builds.

Run: `dotnet build src/Parlance.CSharp.Analyzers`

**Step 2: Add CodeFix testing package to test project**

Modify `tests/Parlance.CSharp.Tests/Parlance.CSharp.Tests.csproj` to add:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.XUnit" Version="1.1.2" />
```

Run: `dotnet build tests/Parlance.CSharp.Tests`

**Step 3: Write failing fix tests**

Create `tests/Parlance.CSharp.Tests/Fixes/PARL9001FixTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL9001_UseSimpleUsingDeclaration,
    Parlance.CSharp.Analyzers.Fixes.PARL9001_UseSimpleUsingDeclarationFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Fixes;

public sealed class PARL9001FixTests
{
    [Fact]
    public async Task Fixes_SimpleUsing()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    {|#0:using|} (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using var stream = new MemoryStream();
                    stream.WriteByte(1);
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task Fixes_NestedUsings()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    {|#0:using|} (var stream = new MemoryStream())
                    using (var reader = new StreamReader(stream))
                    {
                        reader.ReadToEnd();
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using var stream = new MemoryStream();
                    using var reader = new StreamReader(stream);
                    reader.ReadToEnd();
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task Preserves_Comments()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    // Open the stream
                    {|#0:using|} (var stream = new MemoryStream())
                    {
                        // Write data
                        stream.WriteByte(1);
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    // Open the stream
                    using var stream = new MemoryStream();
                    // Write data
                    stream.WriteByte(1);
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }
}
```

**Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests --filter "FullyQualifiedName~PARL9001Fix" --verbosity quiet`
Expected: FAIL — `PARL9001_UseSimpleUsingDeclarationFix` does not exist.

**Step 5: Implement the code fix provider**

Create `src/Parlance.CSharp.Analyzers/Fixes/PARL9001_UseSimpleUsingDeclarationFix.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Parlance.CSharp.Analyzers.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class PARL9001_UseSimpleUsingDeclarationFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["PARL9001"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var usingKeyword = root.FindToken(diagnosticSpan.Start);
        if (usingKeyword.Parent is not UsingStatementSyntax usingStatement)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to using declaration",
                createChangedDocument: ct => ConvertToUsingDeclarationAsync(context.Document, usingStatement, ct),
                equivalenceKey: "PARL9001_Fix"),
            diagnostic);
    }

    private static async Task<Document> ConvertToUsingDeclarationAsync(
        Document document, UsingStatementSyntax outerUsing, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        // Collect the chain of nested usings and the innermost body
        var usings = new List<UsingStatementSyntax>();
        var current = outerUsing;
        while (current is not null)
        {
            usings.Add(current);
            current = current.Statement as UsingStatementSyntax;
        }

        // The innermost body is the Statement of the last using in the chain
        var innermostBody = usings[^1].Statement;

        // Build replacement statements: using declarations + body statements
        var newStatements = new List<StatementSyntax>();

        foreach (var u in usings)
        {
            if (u.Declaration is null) continue;

            var usingDecl = SyntaxFactory.LocalDeclarationStatement(
                awaitKeyword: default,
                usingKeyword: SyntaxFactory.Token(SyntaxKind.UsingKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space),
                modifiers: default,
                declaration: u.Declaration,
                semicolonToken: SyntaxFactory.Token(SyntaxKind.SemicolonToken));

            // Preserve leading trivia from the original using keyword
            if (u == outerUsing)
            {
                usingDecl = usingDecl.WithLeadingTrivia(outerUsing.GetLeadingTrivia());
            }

            newStatements.Add(usingDecl.WithAdditionalAnnotations(Formatter.Annotation));
        }

        // Add body statements
        if (innermostBody is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
                newStatements.Add(stmt);
        }
        else
        {
            newStatements.Add(innermostBody);
        }

        var newRoot = root.ReplaceNode(outerUsing,
            newStatements.Select(s => s.WithAdditionalAnnotations(Formatter.Annotation)));

        return document.WithSyntaxRoot(newRoot);
    }
}
```

**Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests --filter "FullyQualifiedName~PARL9001Fix" --verbosity quiet`
Expected: All 3 tests pass.

**Step 7: Run all existing tests to verify no regressions**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass (68 existing + 3 new).

**Step 8: Commit**

```bash
git add src/Parlance.CSharp.Analyzers/ tests/Parlance.CSharp.Tests/
git commit -m "Add code fix provider for PARL9001 (using declarations)"
```

---

### Task 4: Code Fix Provider for PARL0004 (Pattern Matching)

Transforms `if (x is Type) { var y = (Type)x; ... }` to `if (x is Type y) { ... }` by replacing the is-expression with a declaration pattern and removing the cast variable declaration.

**Files:**
- Create: `src/Parlance.CSharp.Analyzers/Fixes/PARL0004_UsePatternMatchingOverIsCastFix.cs`
- Create: `tests/Parlance.CSharp.Tests/Fixes/PARL0004FixTests.cs`

**Step 1: Write failing fix tests**

Create `tests/Parlance.CSharp.Tests/Fixes/PARL0004FixTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL0004_UsePatternMatchingOverIsCast,
    Parlance.CSharp.Analyzers.Fixes.PARL0004_UsePatternMatchingOverIsCastFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Fixes;

public sealed class PARL0004FixTests
{
    [Fact]
    public async Task Fixes_IsThenCast_SimpleCase()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    if ({|#0:obj is string|})
                    {
                        var s = (string)obj;
                        System.Console.WriteLine(s);
                    }
                }
            }
            """;

        var fixedSource = """
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

        var expected = VerifyCodeFix.Diagnostic("PARL0004")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithArguments("string");

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task Preserves_Comments()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    // Check the type
                    if ({|#0:obj is string|})
                    {
                        var s = (string)obj;
                        // Use the value
                        System.Console.WriteLine(s);
                    }
                }
            }
            """;

        var fixedSource = """
            class C
            {
                void M(object obj)
                {
                    // Check the type
                    if (obj is string s)
                    {
                        // Use the value
                        System.Console.WriteLine(s);
                    }
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL0004")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithArguments("string");

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.CSharp.Tests --filter "FullyQualifiedName~PARL0004Fix" --verbosity quiet`
Expected: FAIL — `PARL0004_UsePatternMatchingOverIsCastFix` does not exist.

**Step 3: Implement the code fix provider**

Create `src/Parlance.CSharp.Analyzers/Fixes/PARL0004_UsePatternMatchingOverIsCastFix.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Parlance.CSharp.Analyzers.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class PARL0004_UsePatternMatchingOverIsCastFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["PARL0004"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        if (node is not BinaryExpressionSyntax isExpression)
            return;
        if (isExpression.Parent is not IfStatementSyntax ifStatement)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use pattern matching",
                createChangedDocument: ct => UsePatternMatchingAsync(context.Document, ifStatement, isExpression, ct),
                equivalenceKey: "PARL0004_Fix"),
            diagnostic);
    }

    private static async Task<Document> UsePatternMatchingAsync(
        Document document,
        IfStatementSyntax ifStatement,
        BinaryExpressionSyntax isExpression,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        var model = await document.GetSemanticModelAsync(ct);
        if (root is null || model is null) return document;

        var checkedExpr = isExpression.Left;
        var targetType = isExpression.Right as TypeSyntax;
        if (targetType is null) return document;

        var body = ifStatement.Statement;
        if (body is not BlockSyntax block) return document;

        // Find the cast declaration: var name = (Type)expr;
        var checkedSymbol = model.GetSymbolInfo(checkedExpr).Symbol;
        var targetTypeSymbol = model.GetTypeInfo(targetType).Type;
        if (checkedSymbol is null || targetTypeSymbol is null) return document;

        LocalDeclarationStatementSyntax? castDeclaration = null;
        string? variableName = null;

        foreach (var stmt in block.Statements.OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in stmt.Declaration.Variables)
            {
                if (variable.Initializer?.Value is CastExpressionSyntax cast)
                {
                    var castTypeSymbol = model.GetTypeInfo(cast.Type).Type;
                    var castExprSymbol = model.GetSymbolInfo(cast.Expression).Symbol;

                    if (SymbolEqualityComparer.Default.Equals(castTypeSymbol, targetTypeSymbol) &&
                        SymbolEqualityComparer.Default.Equals(castExprSymbol, checkedSymbol))
                    {
                        castDeclaration = stmt;
                        variableName = variable.Identifier.Text;
                        break;
                    }
                }
            }
            if (castDeclaration is not null) break;
        }

        if (castDeclaration is null || variableName is null) return document;

        // Build: if (expr is Type name)
        var declarationPattern = SyntaxFactory.IsPatternExpression(
            checkedExpr,
            SyntaxFactory.DeclarationPattern(
                targetType.WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.SingleVariableDesignation(
                    SyntaxFactory.Identifier(variableName))));

        // Remove the cast declaration from the block
        var newStatements = block.Statements.Remove(castDeclaration);
        var newBlock = block.WithStatements(newStatements);

        // Replace the if statement
        var newIf = ifStatement
            .WithCondition(declarationPattern.WithTriviaFrom(ifStatement.Condition))
            .WithStatement(newBlock)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(ifStatement, newIf);
        return document.WithSyntaxRoot(newRoot);
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.CSharp.Tests --filter "FullyQualifiedName~PARL0004Fix" --verbosity quiet`
Expected: All 2 tests pass.

**Step 5: Run all tests**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass.

**Step 6: Commit**

```bash
git add src/Parlance.CSharp.Analyzers/Fixes/ tests/Parlance.CSharp.Tests/Fixes/
git commit -m "Add code fix provider for PARL0004 (pattern matching over is+cast)"
```

---

### Task 5: Output Formatters

**Files:**
- Create: `src/Parlance.Cli/Formatting/IOutputFormatter.cs`
- Create: `src/Parlance.Cli/Formatting/TextFormatter.cs`
- Create: `src/Parlance.Cli/Formatting/JsonFormatter.cs`
- Create: `src/Parlance.Cli/Formatting/AnalysisOutput.cs`
- Create: `tests/Parlance.Cli.Tests/Formatting/TextFormatterTests.cs`
- Create: `tests/Parlance.Cli.Tests/Formatting/JsonFormatterTests.cs`

**Step 1: Create the output model**

Create `src/Parlance.Cli/Formatting/AnalysisOutput.cs`:

```csharp
using Parlance.Abstractions;

namespace Parlance.Cli.Formatting;

internal sealed record AnalysisOutput(
    IReadOnlyList<FileDiagnostic> Diagnostics,
    AnalysisSummary Summary,
    int FilesAnalyzed);

internal sealed record FileDiagnostic(
    string FilePath,
    Diagnostic Diagnostic);
```

**Step 2: Create the formatter interface**

Create `src/Parlance.Cli/Formatting/IOutputFormatter.cs`:

```csharp
namespace Parlance.Cli.Formatting;

internal interface IOutputFormatter
{
    string Format(AnalysisOutput output);
}
```

**Step 3: Write failing TextFormatter tests**

Create `tests/Parlance.Cli.Tests/Formatting/TextFormatterTests.cs`:

```csharp
using System.Collections.Immutable;
using Parlance.Abstractions;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Tests.Formatting;

public sealed class TextFormatterTests
{
    [Fact]
    public void Formats_DiagnosticsAndSummary()
    {
        var output = new AnalysisOutput(
            Diagnostics:
            [
                new FileDiagnostic("src/Example.cs", new Diagnostic(
                    "PARL0004", "PatternMatching", DiagnosticSeverity.Warning,
                    "Use pattern matching", new Location(12, 5, 12, 38),
                    Rationale: "Pattern matching is safer",
                    SuggestedFix: "if (x is Foo y)")),
            ],
            Summary: new AnalysisSummary(1, 0, 1, 0,
                ImmutableDictionary<string, int>.Empty.Add("PatternMatching", 1), 95),
            FilesAnalyzed: 1);

        var result = new TextFormatter().Format(output);

        Assert.Contains("src/Example.cs(12,5)", result);
        Assert.Contains("warning PARL0004", result);
        Assert.Contains("Use pattern matching", result);
        Assert.Contains("Rationale:", result);
        Assert.Contains("Idiomatic Score: 95/100", result);
    }

    [Fact]
    public void Formats_CleanOutput_WhenNoDiagnostics()
    {
        var output = new AnalysisOutput(
            Diagnostics: [],
            Summary: new AnalysisSummary(0, 0, 0, 0,
                ImmutableDictionary<string, int>.Empty, 100),
            FilesAnalyzed: 3);

        var result = new TextFormatter().Format(output);

        Assert.Contains("Idiomatic Score: 100/100", result);
        Assert.Contains("Files analyzed: 3", result);
    }
}
```

**Step 4: Implement TextFormatter**

Create `src/Parlance.Cli/Formatting/TextFormatter.cs`:

```csharp
using System.Text;
using Parlance.Abstractions;

namespace Parlance.Cli.Formatting;

internal sealed class TextFormatter : IOutputFormatter
{
    public string Format(AnalysisOutput output)
    {
        var sb = new StringBuilder();

        foreach (var fd in output.Diagnostics)
        {
            var d = fd.Diagnostic;
            var severity = d.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Suggestion => "suggestion",
                _ => "info",
            };

            sb.AppendLine($"{fd.FilePath}({d.Location.Line},{d.Location.Column}): {severity} {d.RuleId}: {d.Message}");

            if (d.Rationale is not null)
                sb.AppendLine($"  Rationale: {d.Rationale}");
            if (d.SuggestedFix is not null)
                sb.AppendLine($"  Suggested: {d.SuggestedFix}");

            sb.AppendLine();
        }

        sb.AppendLine(new string('\u2500', 40));
        sb.AppendLine($"  Files analyzed: {output.FilesAnalyzed}");
        sb.AppendLine($"  Diagnostics: {output.Summary.TotalDiagnostics} ({output.Summary.Errors} errors, {output.Summary.Warnings} warnings, {output.Summary.Suggestions} suggestions)");
        sb.AppendLine($"  Idiomatic Score: {output.Summary.IdiomaticScore:0}/100");

        return sb.ToString();
    }
}
```

**Step 5: Run text formatter tests**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~TextFormatter" --verbosity quiet`
Expected: All 2 tests pass.

**Step 6: Write failing JsonFormatter tests**

Create `tests/Parlance.Cli.Tests/Formatting/JsonFormatterTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Text.Json;
using Parlance.Abstractions;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Tests.Formatting;

public sealed class JsonFormatterTests
{
    [Fact]
    public void Produces_ValidJson()
    {
        var output = new AnalysisOutput(
            Diagnostics:
            [
                new FileDiagnostic("src/Example.cs", new Diagnostic(
                    "PARL0004", "PatternMatching", DiagnosticSeverity.Warning,
                    "Use pattern matching", new Location(12, 5, 12, 38))),
            ],
            Summary: new AnalysisSummary(1, 0, 1, 0,
                ImmutableDictionary<string, int>.Empty.Add("PatternMatching", 1), 95),
            FilesAnalyzed: 1);

        var result = new JsonFormatter().Format(output);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal("PARL0004", doc.RootElement.GetProperty("diagnostics")[0].GetProperty("ruleId").GetString());
        Assert.Equal(95, doc.RootElement.GetProperty("summary").GetProperty("idiomaticScore").GetDouble());
        Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("filesAnalyzed").GetInt32());
    }

    [Fact]
    public void Uses_CamelCase_PropertyNames()
    {
        var output = new AnalysisOutput(
            Diagnostics: [],
            Summary: new AnalysisSummary(0, 0, 0, 0,
                ImmutableDictionary<string, int>.Empty, 100),
            FilesAnalyzed: 0);

        var result = new JsonFormatter().Format(output);

        Assert.Contains("\"diagnostics\"", result);
        Assert.Contains("\"idiomaticScore\"", result);
        Assert.Contains("\"filesAnalyzed\"", result);
        Assert.DoesNotContain("\"Diagnostics\"", result);
    }
}
```

**Step 7: Implement JsonFormatter**

Create `src/Parlance.Cli/Formatting/JsonFormatter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Parlance.Abstractions;

namespace Parlance.Cli.Formatting;

internal sealed class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Format(AnalysisOutput output)
    {
        var json = new
        {
            diagnostics = output.Diagnostics.Select(fd => new
            {
                ruleId = fd.Diagnostic.RuleId,
                category = fd.Diagnostic.Category,
                severity = fd.Diagnostic.Severity.ToString(),
                message = fd.Diagnostic.Message,
                location = new
                {
                    path = fd.FilePath,
                    line = fd.Diagnostic.Location.Line,
                    column = fd.Diagnostic.Location.Column,
                    endLine = fd.Diagnostic.Location.EndLine,
                    endColumn = fd.Diagnostic.Location.EndColumn,
                },
                rationale = fd.Diagnostic.Rationale,
                suggestedFix = fd.Diagnostic.SuggestedFix,
            }),
            summary = new
            {
                filesAnalyzed = output.FilesAnalyzed,
                totalDiagnostics = output.Summary.TotalDiagnostics,
                errors = output.Summary.Errors,
                warnings = output.Summary.Warnings,
                suggestions = output.Summary.Suggestions,
                byCategory = output.Summary.ByCategory,
                idiomaticScore = output.Summary.IdiomaticScore,
            },
        };

        return JsonSerializer.Serialize(json, Options);
    }
}
```

**Step 8: Run all formatter tests**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~Formatter" --verbosity quiet`
Expected: All 4 tests pass.

**Step 9: Commit**

```bash
git add src/Parlance.Cli/Formatting/ tests/Parlance.Cli.Tests/Formatting/
git commit -m "Add text and JSON output formatters"
```

---

### Task 6: Workspace Analysis Pipeline

The core pipeline that creates an `AdhocWorkspace`, loads files, runs analyzers, and returns results.

**Files:**
- Create: `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`
- Modify: `src/Parlance.CSharp/CompilationFactory.cs` (extract reference resolution to internal method)
- Create: `tests/Parlance.Cli.Tests/Analysis/WorkspaceAnalyzerTests.cs`

**Step 1: Make reference assembly loading accessible from CLI**

The `CompilationFactory.LoadReferences()` is currently private. Extract it to an internal static method so the CLI workspace can reuse it. The CLI project already references `Parlance.CSharp`, but needs `InternalsVisibleTo`.

Modify `src/Parlance.CSharp/Parlance.CSharp.csproj` to add within the existing `InternalsVisibleTo` ItemGroup:

```xml
<InternalsVisibleTo Include="Parlance.Cli" />
```

Modify `src/Parlance.CSharp/CompilationFactory.cs` — change `LoadReferences()` from private to internal:

```csharp
internal static ImmutableArray<MetadataReference> LoadReferences()
```

Run: `dotnet build`

**Step 2: Write failing tests**

Create `tests/Parlance.Cli.Tests/Analysis/WorkspaceAnalyzerTests.cs`:

```csharp
using Parlance.Cli.Analysis;

namespace Parlance.Cli.Tests.Analysis;

public sealed class WorkspaceAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public WorkspaceAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Analyzes_FileWithDiagnostic()
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

        var result = await WorkspaceAnalyzer.AnalyzeAsync([file]);

        Assert.True(result.Diagnostics.Count > 0);
        Assert.Contains(result.Diagnostics, d => d.Diagnostic.RuleId == "PARL0004");
        Assert.Equal(1, result.FilesAnalyzed);
    }

    [Fact]
    public async Task Analyzes_CleanFile_ScoresHigh()
    {
        var file = Path.Combine(_tempDir, "Clean.cs");
        File.WriteAllText(file, """
            class C
            {
                void M() { }
            }
            """);

        var result = await WorkspaceAnalyzer.AnalyzeAsync([file]);

        Assert.Equal(100, result.Summary.IdiomaticScore);
    }

    [Fact]
    public async Task Respects_SuppressRules()
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

        var result = await WorkspaceAnalyzer.AnalyzeAsync([file], suppressRules: ["PARL0004"]);

        Assert.DoesNotContain(result.Diagnostics, d => d.Diagnostic.RuleId == "PARL0004");
    }
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~WorkspaceAnalyzer" --verbosity quiet`
Expected: FAIL — `WorkspaceAnalyzer` does not exist.

**Step 4: Implement WorkspaceAnalyzer**

Create `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Cli.Formatting;
using Parlance.CSharp;
using Parlance.CSharp.Analyzers.Rules;

namespace Parlance.Cli.Analysis;

internal static class WorkspaceAnalyzer
{
    private static readonly DiagnosticAnalyzer[] AllAnalyzers =
    [
        new PARL0001_PreferPrimaryConstructors(),
        new PARL0002_PreferCollectionExpressions(),
        new PARL0003_PreferRequiredProperties(),
        new PARL0004_UsePatternMatchingOverIsCast(),
        new PARL0005_UseSwitchExpression(),
        new PARL9001_UseSimpleUsingDeclaration(),
        new PARL9002_UseImplicitObjectCreation(),
        new PARL9003_UseDefaultLiteral(),
    ];

    public static async Task<AnalysisOutput> AnalyzeAsync(
        IReadOnlyList<string> filePaths,
        string[]? suppressRules = null,
        int? maxDiagnostics = null,
        string? languageVersion = null,
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

        var analyzers = suppressRules.Length > 0
            ? AllAnalyzers.Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id))).ToArray()
            : AllAnalyzers;

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers.ToImmutableArray(), cancellationToken: ct);

        var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        var filtered = roslynDiagnostics
            .Where(d => !suppressRules.Contains(d.Id))
            .ToList();

        var enriched = DiagnosticEnricher.Enrich(filtered);
        var summary = IdiomaticScoreCalculator.Calculate(enriched);

        if (maxDiagnostics.HasValue && enriched.Count > maxDiagnostics.Value)
            enriched = enriched.Take(maxDiagnostics.Value).ToList();

        // Map diagnostics to file paths
        var fileDiagnostics = new List<FileDiagnostic>();
        for (var i = 0; i < enriched.Count; i++)
        {
            var roslynDiag = filtered[Math.Min(i, filtered.Count - 1)];
            var filePath = roslynDiag.Location.SourceTree is not null &&
                           pathMap.TryGetValue(roslynDiag.Location.SourceTree, out var p)
                ? p
                : "unknown";
            fileDiagnostics.Add(new FileDiagnostic(filePath, enriched[i]));
        }

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

Note: `DiagnosticEnricher` and `IdiomaticScoreCalculator` are `internal` in `Parlance.CSharp`. The `InternalsVisibleTo` added in Step 1 makes them accessible.

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~WorkspaceAnalyzer" --verbosity quiet`
Expected: All 3 tests pass.

**Step 6: Run all tests**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/Parlance.CSharp/CompilationFactory.cs src/Parlance.CSharp/Parlance.CSharp.csproj src/Parlance.Cli/Analysis/ tests/Parlance.Cli.Tests/Analysis/
git commit -m "Add workspace-based analysis pipeline for CLI"
```

---

### Task 7: Analyze Command

Wire up the `analyze` command with all options.

**Files:**
- Modify: `src/Parlance.Cli/Program.cs`
- Create: `src/Parlance.Cli/Commands/AnalyzeCommand.cs`

**Step 1: Create the analyze command**

Create `src/Parlance.Cli/Commands/AnalyzeCommand.cs`:

```csharp
using System.CommandLine;
using Parlance.Cli.Analysis;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Commands;

internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Arity = ArgumentArity.OneOrMore };
        pathsArg.Description = "Files, directories, or glob patterns to analyze";

        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";

        var failBelowOption = new Option<int?>("--fail-below") { Description = "Exit with code 1 if score is below threshold (0-100)" };
        var suppressOption = new Option<string[]>("--suppress") { Description = "Rule IDs to suppress" };
        suppressOption.DefaultValueFactory = _ => Array.Empty<string>();

        var maxDiagOption = new Option<int?>("--max-diagnostics") { Description = "Maximum number of diagnostics to report" };
        var langVersionOption = new Option<string?>("--language-version") { Description = "C# language version (default: Latest)" };

        var command = new Command("analyze", "Analyze C# source files for idiomatic patterns");
        command.Add(pathsArg);
        command.Add(formatOption);
        command.Add(failBelowOption);
        command.Add(suppressOption);
        command.Add(maxDiagOption);
        command.Add(langVersionOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var paths = parseResult.GetValue(pathsArg)!;
            var format = parseResult.GetValue(formatOption)!;
            var failBelow = parseResult.GetValue(failBelowOption);
            var suppress = parseResult.GetValue(suppressOption)!;
            var maxDiag = parseResult.GetValue(maxDiagOption);
            var langVersion = parseResult.GetValue(langVersionOption);

            var files = PathResolver.Resolve(paths);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("No .cs files found matching the specified paths.");
                Environment.ExitCode = 2;
                return;
            }

            var result = await WorkspaceAnalyzer.AnalyzeAsync(
                files, suppress, maxDiag, langVersion, ct);

            IOutputFormatter formatter = format.ToLowerInvariant() switch
            {
                "json" => new JsonFormatter(),
                _ => new TextFormatter(),
            };

            Console.Write(formatter.Format(result));

            if (failBelow.HasValue && result.Summary.IdiomaticScore < failBelow.Value)
            {
                Environment.ExitCode = 1;
            }
        });

        return command;
    }
}
```

**Step 2: Wire into Program.cs**

Replace `src/Parlance.Cli/Program.cs`:

```csharp
using System.CommandLine;
using Parlance.Cli.Commands;

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
rootCommand.Add(AnalyzeCommand.Create());
return await rootCommand.Parse(args).InvokeAsync();
```

**Step 3: Build and smoke test**

Run: `dotnet build src/Parlance.Cli`
Expected: Build succeeds.

Run: `dotnet run --project src/Parlance.Cli -- analyze --help`
Expected: Shows analyze command help with all options.

**Step 4: Manual smoke test with a real file**

Run: `dotnet run --project src/Parlance.Cli -- analyze src/Parlance.CSharp/CSharpAnalysisEngine.cs`
Expected: Shows analysis output with diagnostics (if any) and summary with score.

**Step 5: Commit**

```bash
git add src/Parlance.Cli/Commands/ src/Parlance.Cli/Program.cs
git commit -m "Add analyze command with text/JSON output and --fail-below"
```

---

### Task 8: Fix Command

**Files:**
- Create: `src/Parlance.Cli/Analysis/WorkspaceFixer.cs`
- Create: `src/Parlance.Cli/Commands/FixCommand.cs`
- Modify: `src/Parlance.Cli/Program.cs`
- Create: `tests/Parlance.Cli.Tests/Analysis/WorkspaceFixerTests.cs`

**Step 1: Write failing tests for WorkspaceFixer**

Create `tests/Parlance.Cli.Tests/Analysis/WorkspaceFixerTests.cs`:

```csharp
using Parlance.Cli.Analysis;

namespace Parlance.Cli.Tests.Analysis;

public sealed class WorkspaceFixerTests : IDisposable
{
    private readonly string _tempDir;

    public WorkspaceFixerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Fixes_PARL9001_UsingStatement()
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

        var result = await WorkspaceFixer.FixAsync([file]);

        Assert.Single(result.FixedFiles);
        Assert.Contains("using var stream", result.FixedFiles[0].NewContent);
        Assert.DoesNotContain("using (var stream", result.FixedFiles[0].NewContent);
    }

    [Fact]
    public async Task Fixes_PARL0004_PatternMatching()
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
                        System.Console.WriteLine(s);
                    }
                }
            }
            """);

        var result = await WorkspaceFixer.FixAsync([file]);

        Assert.Single(result.FixedFiles);
        Assert.Contains("obj is string s", result.FixedFiles[0].NewContent);
    }

    [Fact]
    public async Task Returns_Empty_WhenNoFixesAvailable()
    {
        var file = Path.Combine(_tempDir, "Clean.cs");
        File.WriteAllText(file, """
            class C
            {
                void M() { }
            }
            """);

        var result = await WorkspaceFixer.FixAsync([file]);

        Assert.Empty(result.FixedFiles);
    }

    [Fact]
    public async Task Apply_WritesFiles()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        var original = """
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
            """;
        File.WriteAllText(file, original);

        var result = await WorkspaceFixer.FixAsync([file]);
        WorkspaceFixer.ApplyFixes(result);

        var written = File.ReadAllText(file);
        Assert.Contains("using var stream", written);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~WorkspaceFixer" --verbosity quiet`
Expected: FAIL — `WorkspaceFixer` does not exist.

**Step 3: Implement WorkspaceFixer**

Create `src/Parlance.Cli/Analysis/WorkspaceFixer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.CSharp;
using Parlance.CSharp.Analyzers.Fixes;
using Parlance.CSharp.Analyzers.Rules;

namespace Parlance.Cli.Analysis;

internal sealed record FixResult(IReadOnlyList<FixedFile> FixedFiles);

internal sealed record FixedFile(string FilePath, string OriginalContent, string NewContent);

internal static class WorkspaceFixer
{
    private static readonly DiagnosticAnalyzer[] FixableAnalyzers =
    [
        new PARL0004_UsePatternMatchingOverIsCast(),
        new PARL9001_UseSimpleUsingDeclaration(),
    ];

    private static readonly CodeFixProvider[] FixProviders =
    [
        new PARL0004_UsePatternMatchingOverIsCastFix(),
        new PARL9001_UseSimpleUsingDeclarationFix(),
    ];

    public static async Task<FixResult> FixAsync(
        IReadOnlyList<string> filePaths,
        string[]? suppressRules = null,
        string? languageVersion = null,
        CancellationToken ct = default)
    {
        suppressRules ??= [];

        var parseOptions = new CSharpParseOptions(
            languageVersion is not null && LanguageVersionFacts.TryParse(languageVersion, out var lv)
                ? lv
                : LanguageVersion.Latest);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId, VersionStamp.Default, "ParlanceFixTarget", "ParlanceFixTarget",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: parseOptions,
            metadataReferences: CompilationFactory.LoadReferences());

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        var documentPaths = new Dictionary<DocumentId, string>();
        var originalContents = new Dictionary<string, string>();

        foreach (var path in filePaths)
        {
            var content = await File.ReadAllTextAsync(path, ct);
            originalContents[path] = content;

            var docId = DocumentId.CreateNewId(projectId);
            solution = solution.AddDocument(docId, Path.GetFileName(path),
                content, filePath: path);
            documentPaths[docId] = path;
        }

        workspace.TryApplyChanges(solution);

        var project = workspace.CurrentSolution.GetProject(projectId)!;
        var compilation = (await project.GetCompilationAsync(ct))!;

        var analyzers = suppressRules.Length > 0
            ? FixableAnalyzers.Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id))).ToArray()
            : FixableAnalyzers;

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers.ToImmutableArray(), cancellationToken: ct);

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);
        var filtered = diagnostics
            .Where(d => !suppressRules.Contains(d.Id))
            .ToList();

        // Apply fixes one diagnostic at a time
        var currentSolution = workspace.CurrentSolution;

        foreach (var diagnostic in filtered)
        {
            var fixProvider = FixProviders.FirstOrDefault(fp =>
                fp.FixableDiagnosticIds.Contains(diagnostic.Id));

            if (fixProvider is null) continue;

            var tree = diagnostic.Location.SourceTree;
            if (tree is null) continue;

            var docId = currentSolution.GetDocumentIdsWithFilePath(tree.FilePath).FirstOrDefault();
            if (docId is null) continue;

            var document = currentSolution.GetDocument(docId);
            if (document is null) continue;

            // Re-get the diagnostic on the current document since solution may have changed
            var currentCompilation = (await document.Project.GetCompilationAsync(ct))!;
            var currentDiagnostics = await currentCompilation
                .WithAnalyzers(analyzers.ToImmutableArray(), cancellationToken: ct)
                .GetAnalyzerDiagnosticsAsync(ct);

            var matchingDiag = currentDiagnostics.FirstOrDefault(d =>
                d.Id == diagnostic.Id &&
                d.Location.SourceTree?.FilePath == tree.FilePath &&
                d.Location.SourceSpan == diagnostic.Location.SourceSpan);

            if (matchingDiag is null) continue;

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, matchingDiag,
                (action, _) => actions.Add(action), ct);

            await fixProvider.RegisterCodeFixesAsync(context);

            if (actions.Count == 0) continue;

            var operations = await actions[0].GetOperationsAsync(ct);
            foreach (var op in operations)
            {
                if (op is ApplyChangesOperation applyOp)
                    currentSolution = applyOp.ChangedSolution;
            }
        }

        // Diff original vs fixed
        var fixedFiles = new List<FixedFile>();
        foreach (var (docId, path) in documentPaths)
        {
            var doc = currentSolution.GetDocument(docId);
            if (doc is null) continue;

            var newText = (await doc.GetTextAsync(ct)).ToString();
            if (originalContents.TryGetValue(path, out var original) && original != newText)
            {
                fixedFiles.Add(new FixedFile(path, original, newText));
            }
        }

        return new FixResult(fixedFiles);
    }

    public static void ApplyFixes(FixResult result)
    {
        foreach (var file in result.FixedFiles)
        {
            File.WriteAllText(file.FilePath, file.NewContent);
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~WorkspaceFixer" --verbosity quiet`
Expected: All 4 tests pass.

**Step 5: Create the fix command**

Create `src/Parlance.Cli/Commands/FixCommand.cs`:

```csharp
using System.CommandLine;
using Parlance.Cli.Analysis;

namespace Parlance.Cli.Commands;

internal static class FixCommand
{
    public static Command Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Arity = ArgumentArity.OneOrMore };
        pathsArg.Description = "Files, directories, or glob patterns to fix";

        var applyOption = new Option<bool>("--apply") { Description = "Apply fixes to files (default is dry-run)" };
        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";

        var suppressOption = new Option<string[]>("--suppress") { Description = "Rule IDs to suppress" };
        suppressOption.DefaultValueFactory = _ => Array.Empty<string>();

        var langVersionOption = new Option<string?>("--language-version") { Description = "C# language version (default: Latest)" };

        var command = new Command("fix", "Apply auto-fixes to C# source files");
        command.Add(pathsArg);
        command.Add(applyOption);
        command.Add(formatOption);
        command.Add(suppressOption);
        command.Add(langVersionOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var paths = parseResult.GetValue(pathsArg)!;
            var apply = parseResult.GetValue(applyOption);
            var suppress = parseResult.GetValue(suppressOption)!;
            var langVersion = parseResult.GetValue(langVersionOption);

            var files = PathResolver.Resolve(paths);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("No .cs files found matching the specified paths.");
                Environment.ExitCode = 2;
                return;
            }

            var result = await WorkspaceFixer.FixAsync(files, suppress, langVersion, ct);

            if (result.FixedFiles.Count == 0)
            {
                Console.WriteLine("No auto-fixes available.");
                return;
            }

            foreach (var file in result.FixedFiles)
            {
                Console.WriteLine($"--- {file.FilePath}");
                if (!apply)
                {
                    Console.WriteLine($"+++ {file.FilePath} (fixed)");
                    Console.WriteLine(file.NewContent);
                }
            }

            if (apply)
            {
                WorkspaceFixer.ApplyFixes(result);
                Console.WriteLine($"Applied fixes to {result.FixedFiles.Count} file(s).");
            }
            else
            {
                Console.WriteLine($"{result.FixedFiles.Count} file(s) would be modified. Use --apply to write changes.");
            }
        });

        return command;
    }
}
```

**Step 6: Wire into Program.cs**

Modify `src/Parlance.Cli/Program.cs` to add:

```csharp
rootCommand.Add(FixCommand.Create());
```

**Step 7: Build and smoke test**

Run: `dotnet build src/Parlance.Cli`
Run: `dotnet run --project src/Parlance.Cli -- fix --help`
Expected: Shows fix command help with all options.

**Step 8: Run all tests**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass.

**Step 9: Commit**

```bash
git add src/Parlance.Cli/ tests/Parlance.Cli.Tests/
git commit -m "Add fix command with workspace-based code fix pipeline"
```

---

### Task 9: Rules Command

**Files:**
- Create: `src/Parlance.Cli/Commands/RulesCommand.cs`
- Modify: `src/Parlance.Cli/Program.cs`

**Step 1: Create the rules command**

Create `src/Parlance.Cli/Commands/RulesCommand.cs`:

```csharp
using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.CSharp.Analyzers.Rules;

namespace Parlance.Cli.Commands;

internal static class RulesCommand
{
    private sealed record RuleInfo(
        string Id,
        string Title,
        string Category,
        string DefaultSeverity,
        bool HasFix);

    private static readonly string[] FixableRuleIds = ["PARL0004", "PARL9001"];

    private static readonly DiagnosticAnalyzer[] AllAnalyzers =
    [
        new PARL0001_PreferPrimaryConstructors(),
        new PARL0002_PreferCollectionExpressions(),
        new PARL0003_PreferRequiredProperties(),
        new PARL0004_UsePatternMatchingOverIsCast(),
        new PARL0005_UseSwitchExpression(),
        new PARL9001_UseSimpleUsingDeclaration(),
        new PARL9002_UseImplicitObjectCreation(),
        new PARL9003_UseDefaultLiteral(),
    ];

    public static Command Create()
    {
        var categoryOption = new Option<string?>("--category") { Description = "Filter by category" };
        var severityOption = new Option<string?>("--severity") { Description = "Filter by severity (Error, Warning, Suggestion)" };
        var fixableOption = new Option<bool>("--fixable") { Description = "Show only rules with auto-fixes" };
        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";

        var command = new Command("rules", "List available analysis rules");
        command.Add(categoryOption);
        command.Add(severityOption);
        command.Add(fixableOption);
        command.Add(formatOption);

        command.SetAction((parseResult, _) =>
        {
            var category = parseResult.GetValue(categoryOption);
            var severity = parseResult.GetValue(severityOption);
            var fixable = parseResult.GetValue(fixableOption);
            var format = parseResult.GetValue(formatOption)!;

            var rules = GetRules();

            if (category is not null)
                rules = rules.Where(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (severity is not null)
                rules = rules.Where(r => r.DefaultSeverity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fixable)
                rules = rules.Where(r => r.HasFix).ToList();

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                });
                Console.Write(json);
            }
            else
            {
                Console.WriteLine($"{"ID",-12} {"Severity",-12} {"Category",-20} {"Fix",-5} {"Title"}");
                Console.WriteLine(new string('-', 80));
                foreach (var rule in rules)
                {
                    Console.WriteLine($"{rule.Id,-12} {rule.DefaultSeverity,-12} {rule.Category,-20} {(rule.HasFix ? "Yes" : ""),-5} {rule.Title}");
                }
                Console.WriteLine();
                Console.WriteLine($"{rules.Count} rule(s)");
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static List<RuleInfo> GetRules()
    {
        var rules = new List<RuleInfo>();

        foreach (var analyzer in AllAnalyzers)
        {
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                var severity = descriptor.DefaultSeverity switch
                {
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Error => "Error",
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => "Warning",
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Info => "Suggestion",
                    _ => "Silent",
                };

                rules.Add(new RuleInfo(
                    descriptor.Id,
                    descriptor.Title.ToString(),
                    descriptor.Category,
                    severity,
                    FixableRuleIds.Contains(descriptor.Id)));
            }
        }

        return rules.OrderBy(r => r.Id).ToList();
    }
}
```

**Step 2: Wire into Program.cs**

Modify `src/Parlance.Cli/Program.cs` to add:

```csharp
rootCommand.Add(RulesCommand.Create());
```

Final `Program.cs`:

```csharp
using System.CommandLine;
using Parlance.Cli.Commands;

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
rootCommand.Add(AnalyzeCommand.Create());
rootCommand.Add(FixCommand.Create());
rootCommand.Add(RulesCommand.Create());
return await rootCommand.Parse(args).InvokeAsync();
```

**Step 3: Build and smoke test**

Run: `dotnet build src/Parlance.Cli`
Run: `dotnet run --project src/Parlance.Cli -- rules`
Expected: Table listing all 8 rules with IDs, severities, categories, and fix availability.

Run: `dotnet run --project src/Parlance.Cli -- rules --fixable`
Expected: Only PARL0004 and PARL9001.

**Step 4: Run all tests**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add src/Parlance.Cli/Commands/RulesCommand.cs src/Parlance.Cli/Program.cs
git commit -m "Add rules command with category, severity, and fixable filters"
```

---

### Task 10: CLI Integration Tests

End-to-end tests that invoke the built CLI binary and assert on output and exit codes.

**Files:**
- Create: `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`

**Step 1: Write integration tests**

Create `tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;

namespace Parlance.Cli.Tests.Integration;

public sealed class CliIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cliProject;

    public CliIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Navigate to the CLI project for dotnet run
        var testDir = AppContext.BaseDirectory;
        _cliProject = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
            "src", "Parlance.Cli", "Parlance.Cli.csproj"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{_cliProject}\" -- {string.Join(' ', args)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public async Task Analyze_SingleFile_ShowsDiagnostics()
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
        Assert.Contains("Idiomatic Score:", stdout);
    }

    [Fact]
    public async Task Analyze_JsonFormat_ReturnsValidJson()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C { void M() { } }");

        var (exitCode, stdout, _) = await RunCliAsync("analyze", file, "--format", "json");

        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.NotNull(doc.RootElement.GetProperty("summary"));
    }

    [Fact]
    public async Task Analyze_FailBelow_ExitCode1()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            class C
            {
                void M(object obj)
                {
                    if (obj is string) { var s = (string)obj; }
                }
            }
            """);

        var (exitCode, _, _) = await RunCliAsync("analyze", file, "--fail-below", "100");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Analyze_NoFiles_ExitCode2()
    {
        var (exitCode, _, stderr) = await RunCliAsync("analyze", Path.Combine(_tempDir, "nonexistent"));

        Assert.Equal(2, exitCode);
        Assert.Contains("No .cs files found", stderr);
    }

    [Fact]
    public async Task Fix_DryRun_DoesNotModify()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        var original = """
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
            """;
        File.WriteAllText(file, original);

        var (exitCode, stdout, _) = await RunCliAsync("fix", file);

        Assert.Equal(0, exitCode);
        Assert.Contains("would be modified", stdout);
        Assert.Equal(original, File.ReadAllText(file));
    }

    [Fact]
    public async Task Fix_Apply_ModifiesFile()
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

        var (exitCode, stdout, _) = await RunCliAsync("fix", file, "--apply");

        Assert.Equal(0, exitCode);
        Assert.Contains("Applied fixes", stdout);

        var modified = File.ReadAllText(file);
        Assert.Contains("using var stream", modified);
    }

    [Fact]
    public async Task Rules_ShowsAllRules()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules");

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0001", stdout);
        Assert.Contains("PARL0004", stdout);
        Assert.Contains("PARL9001", stdout);
    }

    [Fact]
    public async Task Rules_Fixable_ShowsOnlyFixableRules()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules", "--fixable");

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0004", stdout);
        Assert.Contains("PARL9001", stdout);
        Assert.DoesNotContain("PARL0001", stdout);
    }
}
```

**Step 2: Run integration tests**

Run: `dotnet test tests/Parlance.Cli.Tests --filter "FullyQualifiedName~Integration" --verbosity normal`
Expected: All 8 integration tests pass. These may take longer since each spawns a `dotnet run` process.

Note: If tests are slow due to `dotnet run` compilation on each invocation, consider building once first and running the compiled binary directly. Adjust `RunCliAsync` to use the built exe path if needed.

**Step 3: Run all tests**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass.

**Step 4: Commit**

```bash
git add tests/Parlance.Cli.Tests/Integration/
git commit -m "Add CLI integration tests for analyze, fix, and rules commands"
```

---

### Task 11: Final Verification and Cleanup

**Step 1: Run full test suite**

Run: `dotnet test --verbosity normal`
Expected: All tests pass with no warnings relevant to our code.

**Step 2: Verify tool packaging**

Run: `dotnet pack src/Parlance.Cli -o ./nupkg`
Run: `dotnet tool install --global --add-source ./nupkg parlance` (or use `--tool-path`)
Run: `parlance --help`
Run: `parlance analyze src/`
Run: `parlance rules`
Run: `parlance fix src/ --apply` (be careful — this modifies source files, test on a copy)

Expected: Tool installs and all commands work as expected.

**Step 3: Clean up the tool**

Run: `dotnet tool uninstall -g parlance`
Run: `rm -rf ./nupkg`

**Step 4: Run dotnet format**

Run: `dotnet format`

**Step 5: Final commit if any formatting changes**

```bash
git add -A
git commit -m "Format code with dotnet format"
```

**Step 6: Run all tests one final time**

Run: `dotnet test --verbosity quiet`
Expected: All tests pass.
