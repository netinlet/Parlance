using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parlance.CSharp.Analyzers.Metrics.Experimental;

namespace Parlance.CSharp.Tests.Metrics.Experimental;

public sealed class CyclomaticComplexityMetricTests
{
    [Theory]
    [MemberData(nameof(UpstreamCases))]
    public void Calculates_UpstreamFixtureCases(string scenario, string source, string declarationName, int expected)
    {
        var (declaration, semanticModel) = GetDeclaration(source, declarationName);

        var actual = CyclomaticComplexityMetric.Calculate(declaration, semanticModel);

        Assert.Null(actual.SkippedReason);
        Assert.Equal(expected, actual.Complexity);
        Assert.True(actual.EdgeCount >= 0);
        Assert.True(actual.NodeCount > 0);
    }

    [Theory]
    [MemberData(nameof(ParlanceCases))]
    public void Calculates_ParlanceSpecificCases(string scenario, string source, string declarationName, int expected)
    {
        var (declaration, semanticModel) = GetDeclaration(source, declarationName);

        var actual = CyclomaticComplexityMetric.Calculate(declaration, semanticModel);

        Assert.Null(actual.SkippedReason);
        Assert.Equal(expected, actual.Complexity);
    }

    [Fact]
    public void InvalidSyntax_DoesNotThrow()
    {
        // The cyclomatic metric has no syntactic fallback: when Roslyn cannot build a
        // CFG we return a Skipped result rather than approximate the number with a
        // different algorithm. This test verifies we handle broken-but-recoverable
        // input without crashing. Either a Skipped result (CFG unavailable) or a
        // valid CFG-derived complexity is acceptable.
        const string source = """
            class C
            {
                void M(bool b)
                {
                    if (b)
                    {
                }
            }
            """;

        var (declaration, semanticModel) = GetDeclaration(source, "M");

        var actual = CyclomaticComplexityMetric.Calculate(declaration, semanticModel);

        if (actual.SkippedReason is null)
        {
            Assert.True(actual.Complexity >= 1);
            Assert.True(actual.NodeCount > 0);
        }
    }

    public static TheoryData<string, string, string, int> UpstreamCases => new()
    {
        { "ComplexMethod_DefaultSettingsMethod", ComplexMethodSource, "SomeComplexMethod", 12 },
        { "ComplexMethod_ModifiedThresholdMethodScore", ComplexMethodSource, "SomeComplexMethod", 12 },
        { "ManySequentialIfs_HighComplexity", ManySequentialIfsSource, "ManySequentialIfs", 84 },
        { "ManyDeclarations_LowComplexity", ManyDeclarationsSource, "ManyDeclarations", 1 },
        { "BoolAssignments_DoNotIncreaseComplexity", BoolAssignmentsSource, "Thing", 1 },
    };

    public static TheoryData<string, string, string, int> ParlanceCases => new()
    {
        { "ExpressionBodiedMethod", """
            class C
            {
                int M(bool b) => b ? 1 : 0;
            }
            """, "M", 2 },
        // CFG counts the implicit "no match" branch even when a discard arm is present,
        // giving complexity 4 (three predicate blocks + 1) rather than the naive syntactic
        // count of 3 (two non-discard arms + 1). The CFG answer matches CA1502's IL-level view.
        { "SwitchExpression", """
            class C
            {
                string M(int number) => number switch
                {
                    1 => "one",
                    2 => "two",
                    _ => "many",
                };
            }
            """, "M", 4 },
        { "Accessor", """
            class C
            {
                private int _value;
                int M
                {
                    get
                    {
                        if (_value > 0)
                        {
                            return _value;
                        }

                        return 0;
                    }
                }
            }
            """, "M.get", 2 },
    };

    // Adapted from the JetBrains ReSharper Cyclomatic Complexity PowerToy fixture
    // `ComplexMethodWithDefaultSettings.cs` (Apache-2.0). Branch structure and
    // literal names are preserved so the Parlance score (12) can be verified
    // against the upstream gold. Attribution and full lineage in
    // `THIRD_PARTY_NOTICES.md` and in `docs/research/2026-04-16-parl3002-quality-gates.md`.
    private const string ComplexMethodSource = """
        class C
        {
            public bool SomeComplexMethod(int age, string name, bool isAdmin)
            {
                bool result = false;
                bool value = false;

                if (name == "Sarah")
                {
                    if (age < 20 || age > 100)
                    {
                        if (isAdmin)
                        {
                            result = true;
                            value = true;
                        }
                        else if (age == 42)
                        {
                            result = false;
                        }
                        else
                        {
                            result = true;
                        }
                    }
                }
                else if (name == "Gentry" && isAdmin)
                {
                    result = false;
                }
                else
                {
                    if (age == 50)
                    {
                        if (isAdmin)
                        {
                            if (name == "Amrit")
                            {
                                result = false;
                            }
                            else if (name == "Jane")
                            {
                                result = true;
                            }
                            else
                            {
                                result = false;
                            }
                        }
                    }
                }

                return result;
            }
        }
        """;

    private static readonly string ManySequentialIfsSource = BuildManySequentialIfsSource();
    private static readonly string ManyDeclarationsSource = BuildManyDeclarationsSource();

    private const string BoolAssignmentsSource = """
        public class Foo
        {
            public bool Blah { get; set; }

            public void Thing(bool val)
            {
                Blah = val;
                Blah = val;
                Blah = val;
                Blah = val;
                Blah = val;
                Blah = val;
                Blah = val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
                Blah = !val;
            }
        }
        """;

    private static (SyntaxNode Declaration, SemanticModel SemanticModel) GetDeclaration(string source, string declarationName)
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

        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        if (declarationName.EndsWith(".get", StringComparison.Ordinal))
        {
            var propertyName = declarationName[..^4];
            var property = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Single(x => x.Identifier.ValueText == propertyName);
            var getter = property.AccessorList!.Accessors.Single(x => x.IsKind(SyntaxKind.GetAccessorDeclaration));
            return (getter, semanticModel);
        }

        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText == declarationName);

        return (method, semanticModel);
    }

    private static string BuildManySequentialIfsSource()
    {
        var statements = string.Join(Environment.NewLine, Enumerable.Range(1, 83).Select(i => $$"""
                    var t{{i}} = obj as string;
                    if (t{{i}} != null) { }
        """));

        return """
            public class C
            {
                public static void ManySequentialIfs(object obj)
                {
            """
            + statements
            + """

                }
            }
            """;
    }

    private static string BuildManyDeclarationsSource()
    {
        var statements = string.Join(Environment.NewLine, Enumerable.Range(1, 85).Select(i => $"        var t{i} = obj as string;"));

        return """
            public class C
            {
                public static void ManyDeclarations(object obj)
                {
            """
            + statements
            + """

                }
            }
            """;
    }
}
