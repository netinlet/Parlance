using Microsoft.Extensions.Logging.Abstractions;

namespace Parlance.CSharp.Workspace.Tests.Integration;

/// <summary>
/// Loads <c>Parlance.slnx</c> through MSBuildWorkspace once per test class and shares
/// the resulting read-only session across every test method in that class. A single
/// MSBuild solution load costs several seconds; before this fixture every test method
/// paid that cost independently.
/// </summary>
/// <remarks>
/// <para>
/// Consume via <c>IClassFixture&lt;WorkspaceFixture&gt;</c> rather than a collection
/// fixture: xUnit runs the methods within a class sequentially but runs separate
/// classes in parallel. A per-class fixture therefore keeps cross-class parallelism
/// while still collapsing each class's many loads into one. A single shared collection
/// would instead serialize every consuming class, cancelling out the saving.
/// </para>
/// <para>
/// Only read-only tests may use this fixture. Tests that mutate workspace state
/// (buffer sync/overlay, file watching, refresh) or that exercise solution/project
/// loading itself must keep their own isolated session.
/// </para>
/// </remarks>
public sealed class WorkspaceFixture : IAsyncLifetime
{
    public CSharpWorkspaceSession Session { get; private set; } = null!;
    public WorkspaceSessionHolder Holder { get; private set; } = null!;
    public WorkspaceQueryService Query { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        Session = Assert.IsType<WorkspaceLoadResult.Success>(
            await CSharpWorkspaceSession.TryOpenSolutionAsync(solutionPath)).Session;
        Holder = new WorkspaceSessionHolder();
        Holder.SetSession(Session);
        Query = new WorkspaceQueryService(Holder, NullLogger<WorkspaceQueryService>.Instance);
    }

    public async Task DisposeAsync() => await Session.DisposeAsync();
}
