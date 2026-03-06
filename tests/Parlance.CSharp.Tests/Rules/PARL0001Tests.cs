using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL0001_PreferPrimaryConstructors,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0001Tests
{
    [Fact]
    public async Task Flags_ConstructorThatOnlyAssignsFields()
    {
        var source = """
            class {|#0:C|}
            {
                private readonly string _name;
                private readonly int _age;

                public C(string name, int age)
                {
                    _name = name;
                    _age = age;
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("C");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_ConstructorWithLogic()
    {
        var source = """
            class C
            {
                private readonly string _name;

                public C(string name)
                {
                    if (name is null) throw new System.ArgumentNullException(nameof(name));
                    _name = name;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyUsesPrimaryConstructor()
    {
        var source = """
            class C(string name, int age)
            {
                public string Name => name;
                public int Age => age;
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_MultipleConstructors()
    {
        var source = """
            class C
            {
                private readonly string _name;

                public C(string name) { _name = name; }
                public C() { _name = "default"; }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
