using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetTypeAtTool
{
    [McpServerTool(Name = "get-type-at", ReadOnly = true)]
    [Description("Resolve the type of an expression at a given file position. " +
                 "Use 1-based line and column numbers (editor convention). " +
                 "Particularly useful for resolving 'var' declarations to their concrete types.")]
    public static async Task<GetTypeAtResult> GetTypeAt(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<GetTypeAtTool> logger, string filePath, int line, int column, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "get-type-at");

        if (holder.LoadFailure is { } failure)
            return GetTypeAtResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetTypeAtResult.NotLoaded();

        // Convert from 1-based (editor) to 0-based (Roslyn)
        var zeroLine = line - 1;
        var zeroCol = column - 1;

        var semanticModel = await query.GetSemanticModelAsync(filePath, ct);
        if (semanticModel is null)
            return GetTypeAtResult.NotFound(filePath);

        var text = await semanticModel.SyntaxTree.GetTextAsync(ct);
        if (zeroLine < 0 || zeroLine >= text.Lines.Count)
            return GetTypeAtResult.NotFound(filePath);

        var position = text.Lines.GetPosition(new LinePosition(zeroLine, Math.Max(0, zeroCol)));
        var root = await semanticModel.SyntaxTree.GetRootAsync(ct);
        var token = root.FindToken(position);
        var node = token.Parent;

        if (node is null)
            return GetTypeAtResult.NotFound(filePath);

        // Check for var inference
        bool isInferred = false;
        ITypeSymbol? typeSymbol = null;

        // Walk up to find VariableDeclarationSyntax with var
        var varDecl = node.AncestorsAndSelf()
            .OfType<VariableDeclarationSyntax>()
            .FirstOrDefault();

        if (varDecl?.Type is IdentifierNameSyntax { IsVar: true })
        {
            isInferred = true;
            var initializer = varDecl.Variables.FirstOrDefault()?.Initializer?.Value;
            if (initializer is not null)
            {
                var typeInfo = semanticModel.GetTypeInfo(initializer, ct);
                typeSymbol = typeInfo.Type;
            }
        }

        if (typeSymbol is null)
        {
            // Try GetTypeInfo for expressions
            var typeInfo2 = semanticModel.GetTypeInfo(node, ct);
            typeSymbol = typeInfo2.Type;
        }

        if (typeSymbol is null)
        {
            // Fall back to GetSymbolInfo / GetDeclaredSymbol
            var symbolInfo = semanticModel.GetSymbolInfo(node, ct);
            var symbol = symbolInfo.Symbol
                ?? symbolInfo.CandidateSymbols.FirstOrDefault()
                ?? semanticModel.GetDeclaredSymbol(node, ct);

            if (symbol is ITypeSymbol ts) typeSymbol = ts;
            else if (symbol is not null)
            {
                // For non-type symbols (method, property, field), return their type
                typeSymbol = symbol switch
                {
                    IMethodSymbol m => m.ReturnType,
                    IPropertySymbol p => p.Type,
                    IFieldSymbol f => f.Type,
                    ILocalSymbol l => l.Type,
                    IParameterSymbol pa => pa.Type,
                    _ => null
                };

                if (typeSymbol is null)
                {
                    // Return info about the non-type symbol itself
                    return new GetTypeAtResult(
                        Status: "found",
                        TypeName: symbol.Name,
                        FullyQualifiedName: symbol.ToDisplayString(),
                        Kind: symbol.Kind.ToString(),
                        IsInferred: false,
                        SourceText: text.Lines[zeroLine].ToString().Trim(),
                        Message: null);
                }
            }
        }

        if (typeSymbol is null)
            return GetTypeAtResult.NotFound(filePath);

        var sourceLine = zeroLine < text.Lines.Count ? text.Lines[zeroLine].ToString().Trim() : null;

        return new GetTypeAtResult(
            Status: "found",
            TypeName: typeSymbol.Name,
            FullyQualifiedName: typeSymbol.ToDisplayString(),
            Kind: typeSymbol.TypeKind.ToString(),
            IsInferred: isInferred,
            SourceText: sourceLine,
            Message: null);
    }
}

public sealed record GetTypeAtResult(
    string Status, string? TypeName, string? FullyQualifiedName,
    string? Kind, bool IsInferred, string? SourceText, string? Message)
{
    public static GetTypeAtResult NotFound(string filePath) => new(
        "not_found", null, null, null, false, null, $"File '{filePath}' not found in workspace or position out of range");
    public static GetTypeAtResult NotLoaded() => new(
        "not_loaded", null, null, null, false, null, "Workspace is still loading");
    public static GetTypeAtResult LoadFailed(string message) => new(
        "load_failed", null, null, null, false, null, message);
}
