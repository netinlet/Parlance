using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceOpenOptionsTests
{
    [Fact]
    public void Default_IsReportMode_NoFileWatching()
    {
        var options = new WorkspaceOpenOptions();

        Assert.Equal(WorkspaceMode.Report, options.Mode);
        Assert.False(options.FileWatchingEnabled);
    }

    [Fact]
    public void ReportMode_FileWatchingNull_ReturnsFalse()
    {
        var options = new WorkspaceOpenOptions(Mode: WorkspaceMode.Report);

        Assert.False(options.FileWatchingEnabled);
    }

    [Fact]
    public void ReportMode_FileWatchingTrue_Throws()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Report,
            EnableFileWatching: true);

        Assert.Throws<ArgumentException>(() => options.FileWatchingEnabled);
    }

    [Fact]
    public void ServerMode_FileWatchingNull_DefaultsTrue()
    {
        var options = new WorkspaceOpenOptions(Mode: WorkspaceMode.Server);

        Assert.True(options.FileWatchingEnabled);
    }

    [Fact]
    public void ServerMode_FileWatchingFalse_ReturnsFalse()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Server,
            EnableFileWatching: false);

        Assert.False(options.FileWatchingEnabled);
    }

    [Fact]
    public void ServerMode_FileWatchingTrue_ReturnsTrue()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Server,
            EnableFileWatching: true);

        Assert.True(options.FileWatchingEnabled);
    }

    [Fact]
    public void ReportMode_FileWatchingExplicitlyFalse_ReturnsFalse()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Report,
            EnableFileWatching: false);

        Assert.False(options.FileWatchingEnabled);
    }
}
