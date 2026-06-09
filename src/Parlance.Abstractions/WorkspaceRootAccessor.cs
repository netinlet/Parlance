namespace Parlance.Abstractions;

/// <summary>
/// Holds the active workspace root for the serialization boundary. Populated once the session loads
/// (the JSON converter is registered at startup, before the root is known). One workspace per process.
/// </summary>
public sealed class WorkspaceRootAccessor
{
    // Backed by a volatile string: the accessor's job is thread-safe publication of the one root
    // (set on the load thread, read on request threads), and a readonly struct can't be volatile.
    // The RepoPath surface keeps the type honest end-to-end at the serialization boundary.
    private volatile string _root = string.Empty;

    public RepoPath Root
    {
        get => new(_root);
        set => _root = value.Absolute ?? string.Empty;
    }
}
