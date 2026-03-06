using Microsoft.CodeAnalysis.Testing;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Parlance.CSharp.Analyzers.Rules.PARL9001_UseSimpleUsingDeclaration,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Parlance.CSharp.Tests.Rules;

public sealed class PARL9001Tests
{
    [Fact]
    public async Task Flags_UsingStatementWithDeclaration()
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

        var expected = Verify.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_NestedUsingStatements()
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

        var expected = Verify.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFlag_UsingStatementWithoutDeclaration()
    {
        // using (expression) — no variable declaration, can't convert
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M(IDisposable d)
                {
                    using (d)
                    {
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_AlreadyUsingDeclaration()
    {
        var source = """
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

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_UsingFollowedByCodeDependingOnDisposal()
    {
        // After conversion, disposal would happen later — not safe if
        // subsequent code depends on the resource being disposed first.
        var source = """
            using System;
            using System.IO;
            class C
            {
                int M()
                {
                    using (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                    return ComputeAfterDisposal();
                }

                int ComputeAfterDisposal() => 42;
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFlag_InnerUsingWithoutDeclaration()
    {
        // Nested using where inner one has no declaration — can't convert group
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M(IDisposable d)
                {
                    using (var stream = new MemoryStream())
                    using (d)
                    {
                    }
                }
            }
            """;

        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Flags_UsingAsLastStatementInBlock()
    {
        var source = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    var x = 1;
                    {|#0:using|} (var stream = new MemoryStream())
                    {
                        stream.WriteByte((byte)x);
                    }
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Flags_UsingFollowedByReturn()
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
                    return;
                }
            }
            """;

        var expected = Verify.Diagnostic("PARL9001")
            .WithLocation(0)
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }
}
