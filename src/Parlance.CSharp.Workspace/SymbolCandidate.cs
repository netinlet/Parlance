using Microsoft.CodeAnalysis;
using Parlance.Abstractions;

namespace Parlance.CSharp.Workspace;

/// <summary>Lightweight view for presenting ambiguous matches to callers.</summary>
public sealed record SymbolCandidate(
    string FullyQualifiedName, string Kind,
    string ProjectName, RepoPath? FilePath, int? Line)
{
    public static SymbolCandidate From(ResolvedSymbol resolved)
    {
        var path = resolved.Symbol.Locations.FirstOrDefault()?.GetLineSpan().Path;
        return new(
            resolved.Symbol.ToDisplayString(), resolved.Symbol.Kind.ToString(), resolved.Project.Name,
            RepoPath.OrNull(path),
            resolved.Symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1);
    }
}
