using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceOpenOptions(
    WorkspaceMode Mode = WorkspaceMode.Report,
    bool? EnableFileWatching = null,
    ILoggerFactory? LoggerFactory = null)
{
    public bool FileWatchingEnabled => Mode switch
    {
        WorkspaceMode.Report => EnableFileWatching == true
            ? throw new ArgumentException("File watching is not supported in Report mode")
            : false,
        WorkspaceMode.Server => EnableFileWatching ?? true,
        _ => false
    };
}
