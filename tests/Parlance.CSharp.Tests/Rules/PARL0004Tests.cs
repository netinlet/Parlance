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

    [Fact]
    public async Task NoFlag_WhenCheckedExpressionIsMethodCall()
    {
        // Method calls can have side effects — collapsing two calls into one
        // changes behavior, so PARL0004 should not flag this
        var source = """
            class C
            {
                private int _count;
                object Next() => (++_count % 2 == 0) ? "ok" : (object)42;

                void M()
                {
                    if (Next() is string)
                    {
                        var s = (string)Next();
                        System.Console.WriteLine(s);
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Flags_WhenCheckedExpressionIsLocalVariable()
    {
        // Local variables are safe to read twice — no side effects
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
    public async Task NoFlag_WhenCheckedExpressionIsIndexer()
    {
        // Indexers can have side effects
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M(Dictionary<string, object> dict)
                {
                    if (dict["key"] is string)
                    {
                        var s = (string)dict["key"];
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
