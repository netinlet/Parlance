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
