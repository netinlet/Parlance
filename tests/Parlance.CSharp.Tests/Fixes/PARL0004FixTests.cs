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
