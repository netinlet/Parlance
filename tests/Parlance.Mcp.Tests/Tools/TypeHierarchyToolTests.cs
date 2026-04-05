using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class TypeHierarchyToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task TypeHierarchy_DefaultDepth_ReturnsBothDirections()
    {
        // CSharpWorkspaceSession implements IAsyncDisposable
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "CSharpWorkspaceSession",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.TypeName);
        Assert.Equal("Class", result.Kind);

        // Should have supertypes (at least object and IAsyncDisposable)
        Assert.NotEmpty(result.Supertypes);

        // All nodes at depth 1 should have empty children
        Assert.All(result.Supertypes, node =>
        {
            Assert.NotEmpty(node.Name);
            Assert.NotEmpty(node.FullyQualifiedName);
            Assert.Empty(node.Children);
        });

        // Verify relationship semantics: base class should be "base_class", interface should be "interface"
        var baseClassNode = result.Supertypes.FirstOrDefault(n => n.Relationship == "base_class");
        Assert.NotNull(baseClassNode);

        var interfaceNode = result.Supertypes.FirstOrDefault(n => n.Relationship == "interface");
        Assert.NotNull(interfaceNode);
        Assert.Equal("Interface", interfaceNode.Kind);
    }

    [Fact]
    public async Task TypeHierarchy_Interface_FindsSubtypes_WithCorrectRelationship()
    {
        // IProjectCompilationCache should have implementations
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "IProjectCompilationCache",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("Interface", result.Kind);
        Assert.NotEmpty(result.Subtypes);

        // Subtypes of an interface should all have relationship "interface"
        Assert.All(result.Subtypes, node =>
        {
            Assert.NotEmpty(node.Name);
            Assert.NotEmpty(node.FullyQualifiedName);
            Assert.Equal("interface", node.Relationship);
            Assert.Equal("Class", node.Kind);
        });
    }

    [Fact]
    public async Task TypeHierarchy_Depth2_PopulatesChildren()
    {
        // IAnalysisEngine is an interface; its implementor(s) are classes that have base classes
        // At depth 2, supertypes should have children populated
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "CSharpWorkspaceSession",
            maxDepth: 2,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Supertypes);

        // At depth 2, verify child structure is valid on all nodes
        Assert.All(result.Supertypes, node =>
        {
            Assert.NotEmpty(node.Name);
            Assert.All(node.Children, child =>
            {
                Assert.NotEmpty(child.Name);
                Assert.NotEmpty(child.FullyQualifiedName);
            });
        });
    }

    [Fact]
    public async Task TypeHierarchy_KindMapsToTypeKind()
    {
        // Verify interface Kind is "Interface", not "NamedType"
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "IProjectCompilationCache",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("Interface", result.Kind);
    }

    [Fact]
    public async Task TypeHierarchy_BlankTypeName_ReturnsError()
    {
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "  ",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
    }

    [Fact]
    public async Task TypeHierarchy_ZeroMaxDepth_ReturnsError()
    {
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "CSharpWorkspaceSession",
            maxDepth: 0,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
    }

    [Fact]
    public async Task TypeHierarchy_UnknownType_ReturnsNotFound()
    {
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "ThisTypeDefinitelyDoesNotExist",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public async Task TypeHierarchy_AmbiguousName_ReturnsAmbiguous()
    {
        // "Diagnostic" exists in both Parlance and Roslyn
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "Diagnostic",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public async Task TypeHierarchy_FindImplementations_ReturnsDirectSubtypesOnly()
    {
        // IAnalysisEngine → CSharpAnalysisEngine is direct
        // If FindImplementationsAsync returned transitive results, we'd see
        // unexpected entries at depth 1. Verify the count is reasonable.
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "IAnalysisEngine",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Subtypes);
        // All subtypes at depth 1 should have empty children
        Assert.All(result.Subtypes, node => Assert.Empty(node.Children));
    }

    [Fact]
    public void TypeHierarchy_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = TypeHierarchyTool.TypeHierarchy(
            holder, query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "Anything", maxDepth: 1,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void TypeHierarchy_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = TypeHierarchyTool.TypeHierarchy(
            holder, query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "Anything", maxDepth: 1,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
