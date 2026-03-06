using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL9002_UseImplicitObjectCreation,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL9002Tests
{
    [Fact]
    public async Task Flags_ExplicitTypeInVariableDeclaration()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    List<int> list = new {|#0:List<int>|}();
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_FieldDeclaration()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                private List<int> _list = new {|#0:List<int>|}();
            }
            """;

        var expected = Verify.Diagnostic("PARL9002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_VarDeclaration()
    {
        // var x = new List<int>() — can't convert because var needs the type
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
    public async Task NoFlag_DifferentTypes()
    {
        // List<int> list = new SortedList<int>() — types don't match
        // (won't compile, but the analyzer should not flag it)
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    IList<int> list = new List<int>();
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyImplicit()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    List<int> list = new();
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Flags_PropertyInitializer()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                public List<int> Items { get; set; } = new {|#0:List<int>|}();
            }
            """;

        var expected = Verify.Diagnostic("PARL9002")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_MethodArgument()
    {
        // Type is not spatially apparent at the call site
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M()
                {
                    Process(new List<int>());
                }

                void Process(IEnumerable<int> items) { }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_ReturnStatement()
    {
        // Return type is not spatially adjacent
        var source = """
            using System.Collections.Generic;
            class C
            {
                List<int> M()
                {
                    return new List<int>();
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
