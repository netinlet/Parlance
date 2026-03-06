using Microsoft.CodeAnalysis.Testing;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL9001_UseSimpleUsingDeclaration,
    Parlance.CSharp.Analyzers.Fixes.PARL9001_UseSimpleUsingDeclarationFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Fixes;

public sealed class PARL9001FixTests
{
    [Fact]
    public async Task Fixes_SimpleUsing()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    {|#0:using|} (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using var stream = new MemoryStream();
                    stream.WriteByte(1);
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task Fixes_NestedUsings()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    {|#0:using|} (var stream = new MemoryStream())
                    using (var reader = new StreamReader(stream))
                    {
                        reader.ReadToEnd();
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using var stream = new MemoryStream();
                    using var reader = new StreamReader(stream);
                    reader.ReadToEnd();
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Fact]
    public async Task Preserves_Comments()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    // Open the stream
                    {|#0:using|} (var stream = new MemoryStream())
                    {
                        // Write data
                        stream.WriteByte(1);
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    // Open the stream
                    using var stream = new MemoryStream();
                    // Write data
                    stream.WriteByte(1);
                }
            }
            """;

        var expected = VerifyCodeFix.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await VerifyCodeFix.VerifyCodeFixAsync(source, expected, fixedSource);
    }
}
