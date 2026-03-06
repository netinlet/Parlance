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

    [Fact]
    public async Task NoFlag_AssignsToMemberAccessOnAnotherObject()
    {
        // Bug fix: other.Name = name assigns to a property on another object's
        // member, not a direct member of the containing type. Must not flag.
        var source = """
            class Other
            {
                public string Name { get; set; }
            }

            class C
            {
                private Other _other = new Other();

                public C(string name)
                {
                    _other.Name = name;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AssignsToMemberOfDifferentInstance()
    {
        // Bug fix: assigning to a static or member of a different object
        // should not be flagged even if it matches the simple assignment pattern
        var source = """
            class C
            {
                public static string GlobalName;

                public C(string name)
                {
                    GlobalName = name;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AllPublicSettableProperties()
    {
        // Bug fix: when ALL assignments are to public settable properties,
        // PARL0003 (required properties) is the better suggestion. PARL0001
        // should defer to avoid contradictory diagnostics.
        var source = """
            class C
            {
                public string Name { get; set; }
                public int Age { get; set; }

                public C(string name, int age)
                {
                    Name = name;
                    Age = age;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Flags_MixOfFieldsAndProperties()
    {
        // When there's a mix of fields and properties, primary constructor
        // is still the right suggestion (PARL0003 won't fire because not
        // all are public settable properties)
        var source = """
            class {|#0:C|}
            {
                private readonly string _name;
                public int Age { get; set; }

                public C(string name, int age)
                {
                    _name = name;
                    Age = age;
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL0001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("C");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }
}
