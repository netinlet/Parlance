using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL9003_UseDefaultLiteral,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL9003Tests
{
    [Fact]
    public async Task Flags_DefaultExpressionWithExplicitVariableType()
    {
        var source = """
            class C
            {
                void M()
                {
                    int x = {|#0:default(int)|};
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("int");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_DefaultExpressionInParameterDefault()
    {
        var source = """
            using System.Threading;
            class C
            {
                void M(CancellationToken ct = {|#0:default(CancellationToken)|})
                {
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("CancellationToken");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_DefaultExpressionInFieldInitializer()
    {
        var source = """
            class C
            {
                private int _x = {|#0:default(int)|};
            }
            """;

        var expected = Verify.Diagnostic("PARL9003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("int");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_VarDeclaration()
    {
        // var x = default(int) — can't simplify because var needs the type
        var source = """
            class C
            {
                void M()
                {
                    var x = default(int);
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyDefaultLiteral()
    {
        var source = """
            class C
            {
                void M()
                {
                    int x = default;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Flags_DefaultInReturnStatement()
    {
        // Return type is known from method signature
        var source = """
            class C
            {
                int M()
                {
                    return {|#0:default(int)|};
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("int");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_DefaultInTernary()
    {
        var source = """
            class C
            {
                void M(bool b)
                {
                    int x = b ? 1 : {|#0:default(int)|};
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("int");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_DefaultInOverloadedMethodArgument()
    {
        // Ambiguous overloads — removing the type could change resolution
        var source = """
            class C
            {
                void M()
                {
                    Process(default(int));
                }

                void Process(int x) { }
                void Process(string x) { }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
