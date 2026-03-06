using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL0004_UsePatternMatchingOverIsCast,
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
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithArguments("string");

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
