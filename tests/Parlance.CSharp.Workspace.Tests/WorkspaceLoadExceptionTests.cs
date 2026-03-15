using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceLoadExceptionTests
{
    [Fact]
    public void Construction_SetsMessageAndPath()
    {
        var ex = new WorkspaceLoadException("Load failed", "/path/to/Solution.sln");

        Assert.Equal("Load failed", ex.Message);
        Assert.Equal("/path/to/Solution.sln", ex.WorkspacePath);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Construction_WithInnerException()
    {
        var inner = new FileNotFoundException("Not found");
        var ex = new WorkspaceLoadException("Load failed", "/path/to/Solution.sln", inner);

        Assert.Equal("Load failed", ex.Message);
        Assert.Equal("/path/to/Solution.sln", ex.WorkspacePath);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsException()
    {
        var ex = new WorkspaceLoadException("fail", "/path");
        Assert.IsAssignableFrom<Exception>(ex);
    }
}
