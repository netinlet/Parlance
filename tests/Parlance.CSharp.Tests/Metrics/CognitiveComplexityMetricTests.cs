using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parlance.CSharp.Analyzers.Metrics;

namespace Parlance.CSharp.Tests.Metrics;

public sealed class CognitiveComplexityMetricTests
{
    [Theory]
    [MemberData(nameof(UpstreamCases))]
    public void Calculates_UpstreamFixtureCases(string scenario, string source, string declarationName, int expected)
    {
        var (node, semanticModel, methodSymbol) = GetDeclaration(source, declarationName);

        var actual = CognitiveComplexityMetric.Calculate(node, semanticModel, methodSymbol);

        Assert.Equal(expected, actual.Score);
    }

    [Theory]
    [MemberData(nameof(ParlanceCases))]
    public void Calculates_ParlanceSpecificCases(string scenario, string source, string declarationName, int expected)
    {
        var (node, semanticModel, methodSymbol) = GetDeclaration(source, declarationName);

        var actual = CognitiveComplexityMetric.Calculate(node, semanticModel, methodSymbol);

        Assert.Equal(expected, actual.Score);
    }

    [Fact]
    public void IncompleteSyntax_DoesNotThrow()
    {
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

        var (node, semanticModel, methodSymbol) = GetDeclaration(source, "M");
        var exception = Record.Exception(() => CognitiveComplexityMetric.Calculate(node, semanticModel, methodSymbol));

        Assert.Null(exception);
    }

    public static TheoryData<string, string, string, int> UpstreamCases => new()
    {
        { "Condition_IfElse", """
            class C
            {
                void M(bool b)
                {
                    if (b)
                    {
                    }
                    else
                    {
                    }
                }
            }
            """, "M", 2 },
        { "Condition_NestedIfElseIfElse", """
            class C
            {
                void M(bool b)
                {
                    if (true)
                    {
                        if (b)
                        {
                        }
                        else if (!b)
                        {
                        }
                        else
                        {
                        }
                    }
                }
            }
            """, "M", 5 },
        // Upstream (JetBrains) scores this as 16 because goto is flat +1 there.
        // Parlance adopts Sonar's hybrid nesting+1 for goto (see T4.5 in the
        // complexity-metrics hardening plan), so the deeply-nested goto here
        // scores +6 (nesting 5) instead of +1 — total 21 = 1+2+3+4+5+6.
        { "Looping_NestedLoopsAndGoto", """
            class C
            {
                void M()
                {
                MyLabel:
                    for (var i = 0; i < 100; i++)
                    {
                        foreach (var c in "")
                        {
                            while (true)
                            {
                                do
                                {
                                    if (true)
                                    {
                                        goto MyLabel;
                                    }
                                } while (false);
                            }
                        }
                    }
                }
            }
            """, "M", 21 },
        { "Looping_FlatLoopsAndGoto", """
            class C
            {
                void M()
                {
                    for (var i = 0; i < 100; i++)
                    {
                    }

                    foreach (var c in "")
                    {
                    }

                    while (true)
                    {
                    }

                    do
                    {
                    } while (false);

                MyLabel:
                    goto MyLabel;
                }
            }
            """, "M", 5 },
        { "Looping_ContinueAndBreak", """
            class C
            {
                void M()
                {
                    foreach (var c in "")
                    {
                        if (true)
                        {
                            continue;
                        }

                        if (false)
                        {
                            break;
                        }
                    }
                }
            }
            """, "M", 6 },
        { "LogicalOperators_FlatGroups", """
            class C
            {
                void M(bool a, bool b, bool c, bool d)
                {
                    var x = a || b || c;
                    var x1 = a && !b && c && d;
                    var x2 = !(a && b && c);
                }
            }
            """, "M", 3 },
        { "LogicalOperators_MixedGroupsInIf", """
            class C
            {
                void M(bool a, bool b, bool c, bool d, bool e, bool f)
                {
                    if (a && b && c || d || e && f)
                    {
                    }
                }
            }
            """, "M", 4 },
        { "Switch_SimpleStatement", """
            class C
            {
                string M(int number)
                {
                    switch (number)
                    {
                        case 1:
                            return "one";
                        case 2:
                            return "a couple";
                        default:
                            return "lots";
                    }
                }
            }
            """, "M", 1 },
        { "Switch_NestedIfInCase", """
            class C
            {
                string M(int number)
                {
                    switch (number)
                    {
                        case 1:
                            if (true)
                            {
                                return "one";
                            }

                            return "ONE";
                        default:
                            return "lots";
                    }
                }
            }
            """, "M", 3 },
        { "TryCatch_NestedTryAndCatch", """
            using System;

            class C
            {
                void M(bool a, bool b)
                {
                    try
                    {
                        if (a)
                        {
                            for (int i = 0; i < 10; i++)
                            {
                                while (b)
                                {
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        if (b)
                        {
                        }
                    }
                }
            }
            """, "M", 9 },
        { "TryCatch_CatchInsideIf", """
            using System;

            class C
            {
                void M()
                {
                    if (true)
                    {
                        try
                        {
                            throw new Exception("ErrorType1");
                        }
                        catch (IndexOutOfRangeException)
                        {
                        }
                    }
                }
            }
            """, "M", 3 },
        { "TryCatch_FilterAddsComplexity", """
            using System;

            class C
            {
                void M()
                {
                    if (true)
                    {
                        try
                        {
                            throw new Exception("ErrorType1");
                        }
                        catch (Exception ex) when (ex.Message == "ErrorType2")
                        {
                        }
                    }
                }
            }
            """, "M", 4 },
        { "TryCatch_NestedIfInsideCatch", """
            using System;

            class C
            {
                void M()
                {
                    if (true)
                    {
                        try
                        {
                            throw new Exception("ErrorType1");
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message == "ErrorType3")
                            {
                            }
                        }
                    }
                }
            }
            """, "M", 6 },
        { "Lambda_LambdaAddsNesting", """
            using System;

            class C
            {
                void M(bool b)
                {
                    Action action = () =>
                    {
                        if (b)
                        {
                            Console.WriteLine();
                        }
                    };
                }
            }
            """, "M", 2 },
        { "Lambda_AnonymousMethodAddsNesting", """
            using System;

            class C
            {
                void M(bool b)
                {
                    Action action = delegate
                    {
                        if (b)
                        {
                            Console.WriteLine();
                        }
                    };
                }
            }
            """, "M", 2 },
        { "Recursive_DirectCall", """
            class C
            {
                void M()
                {
                    M();
                }
            }
            """, "M", 1 },
        { "Recursive_CallInsideLogicalIf", """
            class C
            {
                bool M(bool a)
                {
                    if (a && M(a))
                    {
                        return true;
                    }

                    return false;
                }
            }
            """, "M", 3 },
        { "Recursive_CallInsideReturnLogicalGroup", """
            class C
            {
                bool M(bool a)
                {
                    return a && !M(a);
                }
            }
            """, "M", 2 },
        { "NullChecking_ExplicitIf", """
            class C
            {
                void M(object obj)
                {
                    string str = null;
                    if (obj != null)
                    {
                        str = obj.ToString();
                    }
                }
            }
            """, "M", 1 },
        { "NullChecking_NullConditionalAccess", """
            class C
            {
                void M(object obj)
                {
                    var str = obj?.ToString();
                }
            }
            """, "M", 0 },
    };

    public static TheoryData<string, string, string, int> ParlanceCases => new()
    {
        { "ExpressionBodiedMethod", """
            class C
            {
                int M(bool b) => b ? 1 : 0;
            }
            """, "M", 1 },
        { "ExpressionBodiedProperty", """
            class C
            {
                private bool _enabled;
                int M => _enabled ? 1 : 0;
            }
            """, "M", 1 },
        { "LocalFunction", """
            class C
            {
                void M(bool b)
                {
                    void Local()
                    {
                        if (b)
                        {
                        }
                    }

                    Local();
                }
            }
            """, "Local", 1 },
        // Scoring the containing method must include the local function's body at
        // nesting+1 (the local function declaration itself is a nesting boundary).
        // Expected score for M: `if (b)` inside Local visited at nesting=1 → 1 + 1 = 2.
        { "LocalFunction_ParentScoreIncludesNesting", """
            class C
            {
                void M(bool b)
                {
                    void Local()
                    {
                        if (b)
                        {
                        }
                    }

                    Local();
                }
            }
            """, "M", 2 },
        { "SwitchExpression", """
            class C
            {
                string M(int number) => number switch
                {
                    1 => "one",
                    _ => "many",
                };
            }
            """, "M", 1 },
        { "PatternMatching", """
            class C
            {
                string M(object value)
                {
                    if (value is int number && number > 0)
                    {
                        return "positive";
                    }

                    return "other";
                }
            }
            """, "M", 2 },
        // Pattern switch guards (`case X when Y:`) must score their logical operators.
        // Score: switch statement (+1) + `&&` in the `when` clause (+1)
        //      + break in the case (+1) + break in default (+1) = 4.
        // The `when` keyword itself does not add a standalone increment (mirrors the
        // switch-expression handling, where arm.WhenClause is visited but not +1). If
        // the `&&` had not been scored — i.e. the pre-fix behavior — the score would
        // have been 3, so this test guards against regression.
        { "SwitchStatement_CaseWhenGuard", """
            class C
            {
                void M(object obj)
                {
                    switch (obj)
                    {
                        case int n when n > 0 && n < 10:
                            break;
                        default:
                            break;
                    }
                }
            }
            """, "M", 4 },
        // Pattern `and` (a single group) mirrors `&&` (a single group) scoring.
        // Score: `if` at nesting 0 (+1) + one pattern-operator group (+1) = 2.
        { "BinaryPattern_AndGroup", """
            class C
            {
                void M(object obj)
                {
                    if (obj is int n and > 0)
                    {
                    }
                }
            }
            """, "M", 2 },
        // Pattern `or` mirrors `||` scoring: one group, one increment.
        // Score: `if` at nesting 0 (+1) + one pattern-operator group (+1) = 2.
        { "BinaryPattern_OrGroup", """
            class C
            {
                void M(int n)
                {
                    if (n is > 0 or < -10)
                    {
                    }
                }
            }
            """, "M", 2 },
        // Mixed pattern groups (`and` then `or`) are scored like mixed boolean
        // groups: the `and` run (+1) + the `or` run (+1) + the closing `and`
        // run (+1) = 3 groups; plus the enclosing `if` (+1) = 4.
        { "BinaryPattern_MixedGroups", """
            class C
            {
                void M(int n)
                {
                    if (n is > 0 and < 10 or > 100 and < 200)
                    {
                    }
                }
            }
            """, "M", 4 },
        // Static local functions are scored independently — the parent method
        // does not include a static local's body in its own score. Score for
        // M: only the outer `if` (+1). The nested `if` inside Local lives in
        // the static local's score, not M's.
        { "StaticLocalFunction_ParentScoreExcludesStaticLocalBody", """
            class C
            {
                void M(bool b)
                {
                    if (b)
                    {
                    }

                    static void Local()
                    {
                        if (true)
                        {
                            if (true)
                            {
                            }
                        }
                    }

                    Local();
                }
            }
            """, "M", 1 },
        // Scoring the static local function on its own: `if` at nesting 0 (+1)
        // + nested `if` at nesting 1 (+2 incl 1 for nesting) = 3. This mirrors
        // how PARL3001 would analyze the declaration when registered directly.
        { "StaticLocalFunction_ScoredIndependently", """
            class C
            {
                void M()
                {
                    static void Local()
                    {
                        if (true)
                        {
                            if (true)
                            {
                            }
                        }
                    }
                }
            }
            """, "Local", 3 },
        // With both kinds present, only the non-static local's body folds into
        // the parent (+1 for its `if` at nesting 1 → 1+1 = 2). The static
        // local's nested `if`s are excluded from M's score.
        // goto at nesting 0 still scores +1 — hybrid nesting+1 is nesting+1,
        // and 0 nesting means flat +1, same as a simple label jump.
        { "Goto_AtNestingZero", """
            class C
            {
                void M()
                {
                MyLabel:
                    goto MyLabel;
                }
            }
            """, "M", 1 },
        // goto inside two nested loops now scores +3 (nesting 2 + 1) instead
        // of the old flat +1. Direct guard on T4.5's Sonar-parity switch.
        { "Goto_InsideNestedLoops", """
            class C
            {
                void M()
                {
                MyLabel:
                    for (var i = 0; i < 10; i++)
                    {
                        foreach (var c in "")
                        {
                            goto MyLabel;
                        }
                    }
                }
            }
            """, "M", 6 },
        { "MixedLocalFunctions_ParentIncludesOnlyNonStatic", """
            class C
            {
                void M(bool b)
                {
                    void NonStatic()
                    {
                        if (b)
                        {
                        }
                    }

                    static void Static()
                    {
                        if (true)
                        {
                            if (true)
                            {
                            }
                        }
                    }

                    NonStatic();
                    Static();
                }
            }
            """, "M", 2 },
    };

    private static (SyntaxNode Node, SemanticModel SemanticModel, IMethodSymbol? MethodSymbol) GetDeclaration(
        string source,
        string declarationName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        var compilation = CSharpCompilation.Create(
            "Tests",
            [tree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var localFunction = root.DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .SingleOrDefault(x => x.Identifier.ValueText == declarationName);

        if (localFunction is not null)
        {
            return (GetBodyOrExpression(localFunction), semanticModel, semanticModel.GetDeclaredSymbol(localFunction));
        }

        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(x => x.Identifier.ValueText == declarationName);

        if (method is not null)
        {
            return (GetBodyOrExpression(method), semanticModel, semanticModel.GetDeclaredSymbol(method));
        }

        var property = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText == declarationName);

        return (GetBodyOrExpression(property), semanticModel, null);
    }

    private static SyntaxNode GetBodyOrExpression(BaseMethodDeclarationSyntax declaration)
    {
        return declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression ?? declaration;
    }

    private static SyntaxNode GetBodyOrExpression(MethodDeclarationSyntax declaration)
    {
        return declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression ?? declaration;
    }

    private static SyntaxNode GetBodyOrExpression(LocalFunctionStatementSyntax declaration)
    {
        return declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression ?? declaration;
    }

    private static SyntaxNode GetBodyOrExpression(PropertyDeclarationSyntax declaration)
    {
        return declaration.ExpressionBody?.Expression ?? declaration.AccessorList ?? (SyntaxNode)declaration;
    }
}
