using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class DescribeTypeTool
{
    [McpServerTool(Name = "describe-type", ReadOnly = true)]
    [Description("Get a type's members, signatures, base types, and interfaces. " +
                 "Works for both source-defined types and types from NuGet packages. " +
                 "Use this first for understanding a type's shape. " +
                 "Use a fully qualified name to disambiguate (e.g., 'Parlance.Abstractions.Diagnostic').")]
    public static Task<DescribeTypeResult> DescribeType(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        string typeName, CancellationToken ct) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(DescribeTypeResult.NotLoaded()),
            loaded: session => RunAsync(query, session, typeName, ct),
            loadFailed: failure => Task.FromResult(DescribeTypeResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(DescribeTypeResult.NotLoaded()));

    private static async Task<DescribeTypeResult> RunAsync(
        WorkspaceQueryService query, CSharpWorkspaceSession session, string typeName, CancellationToken ct)
    {
        var symbols = await query.FindSymbolsAsync(typeName, SymbolFilter.Type, ct: ct);

        if (symbols.IsEmpty)
            return DescribeTypeResult.NotFound(typeName);

        if (symbols.Count > 1 && !typeName.Contains('.'))
            return DescribeTypeResult.Ambiguous(typeName, symbols.Select(s => s.ToCandidate()).ToImmutableList());

        var resolved = symbols[0];
        if (resolved.Symbol is not INamedTypeSymbol type)
            return DescribeTypeResult.NotFound(typeName);

        var members = type.GetMembers()
            .Where(m => m.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.Internal)
            .Where(m => m is not IMethodSymbol ms || ms.MethodKind is MethodKind.Ordinary or MethodKind.Constructor)
            .Select(m => new MemberEntry(
                m.Kind.ToString(), m.DeclaredAccessibility.ToString(),
                m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                m.IsStatic))
            .ToImmutableList();

        var baseTypes = new List<string>();
        var current = type.BaseType;
        while (current is not null && current.SpecialType is not SpecialType.System_Object)
        {
            baseTypes.Add(current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            current = current.BaseType;
        }

        var interfaces = type.AllInterfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToImmutableList();

        return DescribeTypeResult.Found(
            type.ToDisplayString(), type.TypeKind.ToString(),
            type.DeclaredAccessibility.ToString(), type.IsSealed, type.IsAbstract, type.IsStatic,
            resolved.Project.Name,
            type.Locations.FirstOrDefault()?.GetLineSpan().Path,
            type.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1,
            [.. baseTypes], interfaces, members)
            with
        { SnapshotVersion = session.SnapshotVersion };
    }
}

public sealed record DescribeTypeResult(
    string Status, string? FullyQualifiedName, string? Kind,
    string? Accessibility, bool IsSealed, bool IsAbstract, bool IsStatic,
    string? ProjectName, RepoPath? FilePath, int? Line,
    ImmutableList<string> BaseTypes, ImmutableList<string> Interfaces,
    ImmutableList<MemberEntry> Members, ImmutableList<SymbolCandidate> Candidates,
    string? Message)
{
    public long SnapshotVersion { get; init; }

    public static DescribeTypeResult NotFound(string typeName) => new(
        "not_found", null, null, null, false, false, false,
        null, default(RepoPath?), null, [], [], [], [], $"Type '{typeName}' not found in the workspace");

    public static DescribeTypeResult NotLoaded() => new(
        "not_loaded", null, null, null, false, false, false,
        null, default(RepoPath?), null, [], [], [], [], "Workspace is still loading");

    public static DescribeTypeResult LoadFailed(string message) => new(
        "load_failed", null, null, null, false, false, false,
        null, default(RepoPath?), null, [], [], [], [], message);

    public static DescribeTypeResult Ambiguous(string typeName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", null, null, null, false, false, false,
        null, default(RepoPath?), null, [], [], [], candidates,
        $"Multiple types match '{typeName}'. Use a fully qualified name to disambiguate.");

    public static DescribeTypeResult Found(
        string fullyQualifiedName, string kind, string accessibility,
        bool isSealed, bool isAbstract, bool isStatic, string projectName,
        string? filePath, int? line,
        ImmutableList<string> baseTypes, ImmutableList<string> interfaces,
        ImmutableList<MemberEntry> members) => new(
        "found", fullyQualifiedName, kind, accessibility, isSealed, isAbstract, isStatic,
        projectName, RepoPath.OrNull(filePath), line, baseTypes, interfaces, members, [], null);
}

public sealed record MemberEntry(
    string Kind, string Accessibility, string Signature, bool IsStatic);
