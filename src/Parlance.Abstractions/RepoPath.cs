namespace Parlance.Abstractions;

/// <summary>
/// An absolute filesystem path that knows how to present itself workspace-relative.
/// Construction keeps the absolute form (used internally for disk/Roslyn lookups); the JSON
/// boundary writes <see cref="Relative"/> against the active workspace root. Replaces the
/// repeated absolute-path strings across tool DTOs (CLAUDE.md "value objects over primitives").
/// </summary>
public readonly record struct RepoPath(string Absolute)
{
    public string Relative(RepoPath root) =>
        string.IsNullOrEmpty(Absolute) || string.IsNullOrEmpty(root.Absolute)
            ? Absolute ?? string.Empty
            : Path.GetRelativePath(root.Absolute, Absolute);

    /// <summary>The repo root that owns a solution/project file: the directory containing it.
    /// The single home for that derivation — full-paths the input first so a bare or relative
    /// solution name still yields a usable absolute root (an empty root makes every
    /// <see cref="Relative"/> leak the absolute path).</summary>
    public static RepoPath Containing(string path) =>
        string.IsNullOrEmpty(path)
            ? new RepoPath(path ?? string.Empty)
            : new RepoPath(Path.GetDirectoryName(Path.GetFullPath(path)) ?? path);

    public static implicit operator RepoPath(string absolute) => new(absolute);

    public override string ToString() => Absolute;
}

/// <summary>String → <see cref="RepoPath"/> conversion for DTO/tool construction sites.</summary>
public static class RepoPathExtensions
{
    /// <summary>A <see cref="RepoPath"/> for a non-empty path, or <c>null</c> for a null/empty one.
    /// Use at construction sites instead of the implicit operator, which would wrap an empty string
    /// into a present-but-empty value.</summary>
    public static RepoPath? ToRepoPath(this string? absolute) =>
        string.IsNullOrEmpty(absolute) ? (RepoPath?)null : new RepoPath(absolute!);
}
