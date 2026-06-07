namespace Parlance.Abstractions;

/// <summary>
/// Holds the active workspace root for the serialization boundary. Populated once the session loads
/// (the JSON converter is registered at startup, before the root is known). One workspace per process.
/// </summary>
public sealed class WorkspaceRootAccessor
{
    private volatile string _root = "";

    public string Root
    {
        get => _root;
        set => _root = value ?? "";
    }
}
