// tests/Parlance.Mcp.Tests/Tools/AllPathFieldsAreRepoPathTests.cs
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AllPathFieldsAreRepoPathTests
{
    // Caller-echo input paths intentionally kept as string (see Task 10).
    // If DecompileType surfaces a Path-named field, it's an assembly path — add it here.
    private static readonly HashSet<string> Exceptions =
    [
        "GetCodeFixesResult.FilePath",
        "GetRefactoringsResult.FilePath",
        "OutlineFileResult.FilePath",
    ];

    [Fact]
    public void EveryRepoPathLikeField_IsRepoPath()
    {
        var types = typeof(SearchSymbolsResult).Assembly.GetTypes()
            .Where(t => t.Namespace == "Parlance.Mcp.Tools")
            .Concat([typeof(SymbolCandidate), typeof(HierarchyNode)])
            .Where(t => t.IsClass && !t.IsAbstract);

        var offenders = new List<string>();
        foreach (var type in types)
        foreach (var prop in type.GetProperties())
        {
            if (prop.Name is not ("FilePath" or "File" or "Path" or "SolutionPath")) continue;
            if (Exceptions.Contains($"{type.Name}.{prop.Name}")) continue;
            if (prop.PropertyType != typeof(RepoPath) && prop.PropertyType != typeof(RepoPath?))
                offenders.Add($"{type.Name}.{prop.Name} : {prop.PropertyType.Name}");
        }

        Assert.True(offenders.Count == 0, "Non-RepoPath path fields: " + string.Join(", ", offenders));
    }
}
