using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace;

/// <summary>A symbol paired with the project it was resolved from.</summary>
public sealed record ResolvedSymbol(ISymbol Symbol, Project Project);
