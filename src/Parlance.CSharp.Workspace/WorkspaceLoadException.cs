namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceLoadException(
    string message,
    string workspacePath,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string WorkspacePath { get; } = workspacePath;
}
