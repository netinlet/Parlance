using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL0002_PreferCollectionExpressions,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0002Tests
{
    [Fact]
    public async Task Flags_NewListWithInitializer_ExplicitType()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    List<int> list = {|#0:new List<int> { 1, 2, 3 }|};
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
    public async Task Flags_NewArrayWithInitializer_ExplicitType()
    {
        var source = """
            class C
            {
                void M()
                {
                    int[] arr = {|#0:new int[] { 1, 2, 3 }|};
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
    public async Task Flags_ArrayEmpty_ExplicitType()
    {
        var source = """
            using System;
            class C
            {
                void M()
                {
                    int[] arr = {|#0:Array.Empty<int>()|};
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

    [Fact]
    public async Task NoFlag_VarNewListWithInitializer()
    {
        // Bug fix: var list = [1, 2, 3] is illegal — collection expressions
        // have no natural type. Must not flag when target type is var.
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    var list = new List<int> { 1, 2, 3 };
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_VarNewArrayWithInitializer()
    {
        // Bug fix: var arr = [1, 2, 3] is illegal
        var source = """
            class C
            {
                void M()
                {
                    var arr = new int[] { 1, 2, 3 };
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_VarArrayEmpty()
    {
        // Bug fix: var arr = [] is illegal
        var source = """
            using System;
            class C
            {
                void M()
                {
                    var arr = Array.Empty<int>();
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Flags_FieldInitializer()
    {
        // Field has explicit type — collection expression is valid
        var source = """
            using System.Collections.Generic;
            class C
            {
                private List<int> _items = {|#0:new List<int> { 1, 2 }|};
            }
            """;

        var expected = Verify.Diagnostic("PARL0002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("List<int>");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }
}
