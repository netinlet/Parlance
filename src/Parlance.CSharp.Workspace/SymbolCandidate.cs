using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace;

/// <summary>Lightweight view for presenting ambiguous matches to callers.</summary>
public sealed record SymbolCandidate(
    string DisplayName, string FullyQualifiedName, string Kind,
    string ProjectName, string? FilePath, int? Line)
{
    public static SymbolCandidate From(ResolvedSymbol resolved) => new(
        resolved.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        resolved.Symbol.ToDisplayString(), resolved.Symbol.Kind.ToString(), resolved.Project.Name,
        resolved.Symbol.Locations.FirstOrDefault()?.GetLineSpan().Path,
        resolved.Symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line);
}
