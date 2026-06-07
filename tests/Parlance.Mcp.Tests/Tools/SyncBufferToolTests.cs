using Parlance.CSharp.Workspace;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class SyncBufferToolTests
{
    [Fact]
    public void NotLoaded_ReturnsNotLoadedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        var result = SyncBufferTool.SyncBuffer(holder, "/x.cs", "text").GetAwaiter().GetResult();
        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void Disposed_ReturnsNotLoadedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.Dispose();
        var result = SyncBufferTool.SyncBuffer(holder, "/x.cs", "text").GetAwaiter().GetResult();
        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void LoadFailed_ReturnsLoadFailedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));
        var result = SyncBufferTool.SyncBuffer(holder, "/x.cs", "text").GetAwaiter().GetResult();
        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public void CloseBuffer_NotLoaded_ReturnsNotLoadedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        var result = SyncBufferTool.CloseBuffer(holder, "/x.cs").GetAwaiter().GetResult();
        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void CloseBuffer_LoadFailed_ReturnsLoadFailedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));
        var result = SyncBufferTool.CloseBuffer(holder, "/x.cs").GetAwaiter().GetResult();
        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
