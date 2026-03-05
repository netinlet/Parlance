using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Rules.PARL0003_PreferRequiredProperties,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL0003Tests
{
    [Fact]
    public async Task Flags_ConstructorAssigningToPublicSetProperties()
    {
        var source = """
            class {|#0:C|}
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

        var expected = Verify.Diagnostic("PARL0003")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithArguments("C");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_ConstructorWithLogicBeyondAssignment()
    {
        var source = """
            class C
            {
                public string Name { get; set; }

                public C(string name)
                {
                    Name = name ?? throw new System.ArgumentNullException(nameof(name));
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyRequired()
    {
        var source = """
            namespace System.Runtime.CompilerServices
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
                sealed class RequiredMemberAttribute : Attribute { }

                [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
                sealed class CompilerFeatureRequiredAttribute : Attribute
                {
                    public CompilerFeatureRequiredAttribute(string featureName) { }
                }
            }

            class C
            {
                public required string Name { get; set; }
                public required int Age { get; set; }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_PrivateSetters()
    {
        var source = """
            class C
            {
                public string Name { get; private set; }

                public C(string name)
                {
                    Name = name;
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }
}
