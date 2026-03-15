using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceModelTests
{
    [Fact]
    public void WorkspaceMode_HasReportAndServer()
    {
        Assert.Equal(0, (int)WorkspaceMode.Report);
        Assert.Equal(1, (int)WorkspaceMode.Server);
    }

    [Fact]
    public void WorkspaceDiagnosticSeverity_HasExpectedValues()
    {
        Assert.Equal(0, (int)WorkspaceDiagnosticSeverity.Error);
        Assert.Equal(1, (int)WorkspaceDiagnosticSeverity.Warning);
        Assert.Equal(2, (int)WorkspaceDiagnosticSeverity.Info);
    }

    [Fact]
    public void ProjectLoadStatus_HasLoadedAndFailed()
    {
        Assert.Equal(0, (int)ProjectLoadStatus.Loaded);
        Assert.Equal(1, (int)ProjectLoadStatus.Failed);
    }

    [Fact]
    public void WorkspaceLoadStatus_HasLoadedDegradedFailed()
    {
        Assert.Equal(0, (int)WorkspaceLoadStatus.Loaded);
        Assert.Equal(1, (int)WorkspaceLoadStatus.Degraded);
        Assert.Equal(2, (int)WorkspaceLoadStatus.Failed);
    }

    [Fact]
    public void WorkspaceProjectKey_Default_HasEmptyGuid()
    {
        var key = default(WorkspaceProjectKey);
        Assert.Equal(Guid.Empty, key.Value);
    }

    [Fact]
    public void WorkspaceProjectKey_Equality_SameGuid()
    {
        var guid = Guid.NewGuid();
        var key1 = new WorkspaceProjectKey(guid);
        var key2 = new WorkspaceProjectKey(guid);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void WorkspaceProjectKey_Inequality_DifferentGuid()
    {
        var key1 = new WorkspaceProjectKey(Guid.NewGuid());
        var key2 = new WorkspaceProjectKey(Guid.NewGuid());
        Assert.NotEqual(key1, key2);
    }
}
