using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class OutlineFileTool
{
    [McpServerTool(Name = "outline-file", ReadOnly = true)]
    [Description("Returns the structural outline of a C# file — types, members, and signatures — without method bodies. " +
                 "Use absolute file paths.")]
    public static async Task<OutlineFileResult> OutlineFile(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ToolAnalytics analytics, string filePath, CancellationToken ct)
    {
        using var _ = analytics.TimeToolCall("outline-file", new { filePath });

        if (holder.LoadFailure is { } failure)
            return OutlineFileResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return OutlineFileResult.NotLoaded();

        var semanticModel = await query.GetSemanticModelAsync(filePath, ct);
        if (semanticModel is null)
            return OutlineFileResult.NotFound(filePath);

        var root = await semanticModel.SyntaxTree.GetRootAsync(ct);

        var types = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(t => t.Parent is not BaseTypeDeclarationSyntax)
            .Select(typeDecl =>
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct);
                var typeName = typeSymbol?.Name ?? typeDecl.Identifier.Text;
                var typeKind = typeDecl switch
                {
                    ClassDeclarationSyntax => "class",
                    StructDeclarationSyntax => "struct",
                    RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructKeyword) ? "record struct" : "record",
                    InterfaceDeclarationSyntax => "interface",
                    EnumDeclarationSyntax => "enum",
                    _ => "type"
                };
                var typeAccess = typeSymbol?.DeclaredAccessibility.ToString() ?? "Unknown";
                var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                var memberDecls = typeDecl is TypeDeclarationSyntax typeDeclSyntax
                    ? (IEnumerable<MemberDeclarationSyntax>)typeDeclSyntax.Members
                    : [];

                var members = memberDecls
                    .Select(memberDecl =>
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(memberDecl, ct);
                        if (symbol is null) return null;

                        var sig = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        return new OutlineMember(
                            symbol.Name,
                            symbol.Kind.ToString(),
                            symbol.DeclaredAccessibility.ToString(),
                            sig,
                            symbol.IsStatic,
                            memberDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                    })
                    .OfType<OutlineMember>()
                    .ToImmutableList();

                return new OutlineType(typeName, typeKind, typeAccess, line, members);
            })
            .ToImmutableList();

        return OutlineFileResult.Found(filePath, types);
    }
}

public sealed record OutlineFileResult(string Status, string? FilePath, ImmutableList<OutlineType> Types, string? Message)
{
    public static OutlineFileResult NotFound(string filePath) => new(
        "not_found", filePath, [], $"File '{filePath}' not found in workspace");
    public static OutlineFileResult NotLoaded() => new(
        "not_loaded", null, [], "Workspace is still loading");
    public static OutlineFileResult LoadFailed(string message) => new(
        "load_failed", null, [], message);
    public static OutlineFileResult Found(string filePath, ImmutableList<OutlineType> types) => new(
        "found", filePath, types, null);
}

public sealed record OutlineType(
    string Name, string Kind, string Accessibility, int Line,
    ImmutableList<OutlineMember> Members);

public sealed record OutlineMember(
    string Name, string Kind, string Accessibility, string Signature,
    bool IsStatic, int Line);
