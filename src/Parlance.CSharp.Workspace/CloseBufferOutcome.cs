namespace Parlance.CSharp.Workspace;

/// <summary>Result of <see cref="CSharpWorkspaceSession.CloseBufferAsync"/>.</summary>
public enum CloseBufferOutcome
{
    /// <summary>No live overlay existed for the path; nothing to do.</summary>
    NotOpen,

    /// <summary>Overlay dropped and the document reverted to its on-disk contents.</summary>
    Closed,

    /// <summary>The buffer was open but the disk file is gone, so it cannot be reverted. The overlay
    /// is left in place (buffer stays open) rather than leaking phantom unsaved text into the solution.</summary>
    RevertUnavailable
}
