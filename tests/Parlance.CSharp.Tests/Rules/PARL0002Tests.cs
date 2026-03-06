using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL0002_PreferCollectionExpressions,
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
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("List<int>");

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
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("int[]");

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
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("Array.Empty<T>()");

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
