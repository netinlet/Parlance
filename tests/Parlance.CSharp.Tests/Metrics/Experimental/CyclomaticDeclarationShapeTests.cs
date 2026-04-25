using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parlance.CSharp.Analyzers.Metrics.Experimental;

namespace Parlance.CSharp.Tests.Metrics.Experimental;

// Verifies CFG-based cyclomatic complexity works for every declaration shape the metric
// claims to support. Each body has exactly one predicate, so expected complexity is 2 in
// all cases. A CFG failure surfaces as SkippedReason != null, which is what we probe for.
// Since the metric has no syntactic fallback, these shapes MUST work via CFG or the
// declaration type is effectively unsupported.
public sealed class CyclomaticDeclarationShapeTests
{
    [Fact]
    public void Method_BlockBody()
    {
        var source = """
            class C
            {
                int M(int x) { if (x > 0) return 1; return 0; }
            }
            """;
        AssertComplexity<MethodDeclarationSyntax>(source, expected: 2, m => m.Identifier.ValueText == "M");
    }

    [Fact]
    public void Method_ExpressionBody()
    {
        var source = """
            class C
            {
                int M(int x) => x > 0 ? 1 : 0;
            }
            """;
        AssertComplexity<MethodDeclarationSyntax>(source, expected: 2, m => m.Identifier.ValueText == "M");
    }

    [Fact]
    public void Constructor_BlockBody()
    {
        var source = """
            class C
            {
                int _f;
                public C(int x) { if (x > 0) _f = 1; }
            }
            """;
        AssertComplexity<ConstructorDeclarationSyntax>(source, expected: 2, _ => true);
    }

    [Fact]
    public void Destructor()
    {
        var source = """
            class C
            {
                int _f;
                ~C() { if (_f > 0) _f = 0; }
            }
            """;
        AssertComplexity<DestructorDeclarationSyntax>(source, expected: 2, _ => true);
    }

    [Fact]
    public void BinaryOperator_ExpressionBody()
    {
        var source = """
            class C
            {
                public static int operator +(C a, C b) => a is null ? 0 : 1;
            }
            """;
        AssertComplexity<OperatorDeclarationSyntax>(source, expected: 2, _ => true);
    }

    [Fact]
    public void ConversionOperator_ExpressionBody()
    {
        var source = """
            class C
            {
                public static implicit operator int(C c) => c is null ? 0 : 1;
            }
            """;
        AssertComplexity<ConversionOperatorDeclarationSyntax>(source, expected: 2, _ => true);
    }

    [Fact]
    public void PropertyAccessor_Get_BlockBody()
    {
        var source = """
            class C
            {
                int _f;
                int P { get { if (_f > 0) return _f; return 0; } }
            }
            """;
        AssertAccessor(source, "P", SyntaxKind.GetAccessorDeclaration, expected: 2);
    }

    [Fact]
    public void PropertyAccessor_Get_ExpressionBody()
    {
        var source = """
            class C
            {
                int _f;
                int P { get => _f > 0 ? _f : 0; }
            }
            """;
        AssertAccessor(source, "P", SyntaxKind.GetAccessorDeclaration, expected: 2);
    }

    [Fact]
    public void PropertyAccessor_Set_BlockBody()
    {
        var source = """
            class C
            {
                int _f;
                int P { get => _f; set { if (value > 0) _f = value; } }
            }
            """;
        AssertAccessor(source, "P", SyntaxKind.SetAccessorDeclaration, expected: 2);
    }

    [Fact]
    public void PropertyAccessor_Init_BlockBody()
    {
        var source = """
            class C
            {
                int _f;
                public int P { get => _f; init { if (value > 0) _f = value; } }
            }
            """;
        AssertAccessor(source, "P", SyntaxKind.InitAccessorDeclaration, expected: 2);
    }

    [Fact]
    public void IndexerAccessor_Get_BlockBody()
    {
        var source = """
            class C
            {
                int[] _arr = new int[10];
                int this[int i] { get { if (i > 0) return _arr[i]; return 0; } }
            }
            """;
        AssertIndexerAccessor(source, SyntaxKind.GetAccessorDeclaration, expected: 2);
    }

    [Fact]
    public void ExpressionBodiedProperty()
    {
        var source = """
            class C
            {
                int _f;
                int P => _f > 0 ? _f : 0;
            }
            """;
        AssertComplexity<PropertyDeclarationSyntax>(source, expected: 2, p => p.Identifier.ValueText == "P");
    }

    [Fact]
    public void ExpressionBodiedIndexer()
    {
        var source = """
            class C
            {
                int[] _arr = new int[10];
                int this[int i] => i > 0 ? _arr[i] : 0;
            }
            """;
        AssertComplexity<IndexerDeclarationSyntax>(source, expected: 2, _ => true);
    }

    [Fact]
    public void LocalFunction_BlockBody()
    {
        var source = """
            class C
            {
                void Host()
                {
                    int Local(int x) { if (x > 0) return 1; return 0; }
                    Local(1);
                }
            }
            """;
        AssertComplexity<LocalFunctionStatementSyntax>(source, expected: 2, l => l.Identifier.ValueText == "Local");
    }

    [Fact]
    public void LocalFunction_ExpressionBody()
    {
        var source = """
            class C
            {
                void Host()
                {
                    int Local(int x) => x > 0 ? 1 : 0;
                    Local(1);
                }
            }
            """;
        AssertComplexity<LocalFunctionStatementSyntax>(source, expected: 2, l => l.Identifier.ValueText == "Local");
    }

    private static void AssertComplexity<T>(string source, int expected, Func<T, bool> selector)
        where T : SyntaxNode
    {
        var (tree, semanticModel) = Compile(source);
        var declaration = tree.GetRoot().DescendantNodes().OfType<T>().Single(selector);

        var actual = CyclomaticComplexityMetric.Calculate(declaration, semanticModel);

        Assert.Null(actual.SkippedReason);
        Assert.Equal(expected, actual.Complexity);
        Assert.True(actual.NodeCount > 0, $"NodeCount should be > 0 on CFG path; got {actual.NodeCount}");
    }

    private static void AssertAccessor(string source, string propertyName, SyntaxKind accessorKind, int expected)
    {
        var (tree, semanticModel) = Compile(source);
        var property = tree.GetRoot().DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == propertyName);
        var accessor = property.AccessorList!.Accessors.Single(a => a.IsKind(accessorKind));

        var actual = CyclomaticComplexityMetric.Calculate(accessor, semanticModel);

        Assert.Null(actual.SkippedReason);
        Assert.Equal(expected, actual.Complexity);
        Assert.True(actual.NodeCount > 0, $"NodeCount should be > 0 on CFG path; got {actual.NodeCount}");
    }

    private static void AssertIndexerAccessor(string source, SyntaxKind accessorKind, int expected)
    {
        var (tree, semanticModel) = Compile(source);
        var indexer = tree.GetRoot().DescendantNodes().OfType<IndexerDeclarationSyntax>().Single();
        var accessor = indexer.AccessorList!.Accessors.Single(a => a.IsKind(accessorKind));

        var actual = CyclomaticComplexityMetric.Calculate(accessor, semanticModel);

        Assert.Null(actual.SkippedReason);
        Assert.Equal(expected, actual.Complexity);
        Assert.True(actual.NodeCount > 0, $"NodeCount should be > 0 on CFG path; got {actual.NodeCount}");
    }

    private static (SyntaxTree Tree, SemanticModel SemanticModel) Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        var compilation = CSharpCompilation.Create(
            "Tests",
            [tree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (tree, compilation.GetSemanticModel(tree));
    }
}
