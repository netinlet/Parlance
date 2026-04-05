using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class CallHierarchyTool
{
    [McpServerTool(Name = "call-hierarchy", ReadOnly = true)]
    [Description("Returns callers (incoming calls) and callees (outgoing calls) for a method, one level deep.")]
    public static async Task<CallHierarchyResult> GetCallHierarchy(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<CallHierarchyTool> logger, string methodName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "call-hierarchy");

        if (holder.LoadFailure is { } failure)
            return CallHierarchyResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return CallHierarchyResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(methodName, SymbolFilter.Member, ct: ct);
        var methodSymbols = symbols.Where(s => s.Symbol is IMethodSymbol).ToImmutableList();

        if (methodSymbols.IsEmpty)
            return CallHierarchyResult.NotFound(methodName);

        if (methodSymbols.Count > 1 && !methodName.Contains('.'))
            return CallHierarchyResult.Ambiguous(methodName, methodSymbols.Select(s => s.ToCandidate()).ToImmutableList());

        var targetSymbol = methodSymbols[0].Symbol as IMethodSymbol;

        // === CALLERS (incoming) ===
        var callersBuilder = ImmutableList.CreateBuilder<HierarchyEntry>();
        var references = await query.FindReferencesAsync(targetSymbol!, ct);
        var seenCallers = new HashSet<string>();

        foreach (var refSymbol in references)
        {
            foreach (var location in refSymbol.Locations)
            {
                if (!location.Location.IsInSource) continue;

                var tree = location.Location.SourceTree;
                if (tree is null) continue;

                var root = await tree.GetRootAsync(ct);
                var node = root.FindNode(location.Location.SourceSpan);

                // Check if this reference is inside an invocation
                var isInvocation = node.AncestorsAndSelf().Any(n => n is InvocationExpressionSyntax);
                if (!isInvocation) continue;

                // Find the containing method
                var containingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var containingType = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

                var span = location.Location.GetLineSpan();
                var callerKey = $"{containingMethod?.Identifier.Text}:{span.Path}:{span.StartLinePosition.Line}";
                if (!seenCallers.Add(callerKey)) continue;

                callersBuilder.Add(new HierarchyEntry(
                    MethodName: containingMethod?.Identifier.Text ?? "<unknown>",
                    ContainingType: containingType?.Identifier.Text ?? "<unknown>",
                    FilePath: span.Path,
                    Line: span.StartLinePosition.Line + 1));
            }
        }

        // === CALLEES (outgoing) ===
        var calleesBuilder = ImmutableList.CreateBuilder<HierarchyEntry>();

        var targetLocation = targetSymbol!.Locations.FirstOrDefault();
        if (targetLocation is { IsInSource: true, SourceTree: not null })
        {
            var tree = targetLocation.SourceTree;
            var semanticModel = await query.GetSemanticModelAsync(tree.FilePath, ct);

            if (semanticModel is not null)
            {
                var root = await tree.GetRootAsync(ct);
                var methodNode = root.FindNode(targetLocation.SourceSpan)
                    .AncestorsAndSelf()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();

                if (methodNode is not null)
                {
                    var seen = new HashSet<string>();
                    foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                        var calledSymbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                        if (calledSymbol is not IMethodSymbol calledMethod) continue;

                        var key = calledMethod.ToDisplayString();
                        if (!seen.Add(key)) continue;

                        var callSpan = invocation.GetLocation().GetLineSpan();
                        calleesBuilder.Add(new HierarchyEntry(
                            MethodName: calledMethod.Name,
                            ContainingType: calledMethod.ContainingType?.Name ?? "<unknown>",
                            FilePath: callSpan.Path,
                            Line: callSpan.StartLinePosition.Line + 1));
                    }
                }
            }
        }

        return CallHierarchyResult.Found(
            targetSymbol.ToDisplayString(), callersBuilder.ToImmutable(), calleesBuilder.ToImmutable());
    }
}

public sealed record CallHierarchyResult(
    string Status, string? TargetMethod, ImmutableList<HierarchyEntry> Callers,
    ImmutableList<HierarchyEntry> Callees, ImmutableList<SymbolCandidate> Candidates, string? Message)
{
    public static CallHierarchyResult NotFound(string methodName) => new(
        "not_found", methodName, [], [], [], $"Method '{methodName}' not found");
    public static CallHierarchyResult NotLoaded() => new(
        "not_loaded", null, [], [], [], "Workspace is still loading");
    public static CallHierarchyResult LoadFailed(string message) => new(
        "load_failed", null, [], [], [], message);
    public static CallHierarchyResult Ambiguous(string methodName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", methodName, [], [], candidates,
        $"Multiple methods match '{methodName}'. Use a fully qualified name to disambiguate.");
    public static CallHierarchyResult Found(
        string targetMethod, ImmutableList<HierarchyEntry> callers, ImmutableList<HierarchyEntry> callees) => new(
        "found", targetMethod, callers, callees, [], null);
}

public sealed record HierarchyEntry(
    string MethodName, string ContainingType, string? FilePath, int Line);
