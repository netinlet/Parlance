using System.Collections.Immutable;

namespace Parlance.CSharp.Workspace;

public sealed record TypeHierarchyResult(
    ImmutableList<HierarchyNode> Supertypes,
    ImmutableList<HierarchyNode> Subtypes,
    bool Truncated);

public sealed record HierarchyNode(
    string Name, string FullyQualifiedName, string Kind,
    string Relationship, string? FilePath, int? Line,
    ImmutableList<HierarchyNode> Children);
